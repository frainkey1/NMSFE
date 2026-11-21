using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
    TextBox? _shipName, _shipSeed, _shipDamage, _shipShield, _shipDrive, _shipManeuver;
    TextBlock? _shipPrimaryLabel;
    Button? _shipPrimaryButton;
    ComboBox? _shipType, _shipClass;
    CheckBox? _shipOldColour;
    Action? _refreshShipList;
    bool _shipDetailsLoading, _shipDetailsChanged;

    bool ShipDetailsPending => (_inventoryPage==MainPageCatalog.Starships || _inventoryPage==MainPageCatalog.Corvettes) && _selectedShip != null && !_showArchive && _shipDetailsChanged;

    void RefreshPrimaryShipLabel()
    {
        if (_shipPrimaryLabel != null)
            _shipPrimaryLabel.Text = UiStrings.Format("editor.primary_ship_label", PrimaryShipIndex() + 1);
    }

    void RefreshShipPrimaryButton()
    {
        if (_shipPrimaryButton != null)
            _shipPrimaryButton.IsEnabled = !_showArchive && _selectedShip is { Occupied: true } ship && ship.Index != PrimaryShipIndex();
    }

    UIElement BuildStarshipDetails()
    {
        var card = new Border { Style=(Style)FindResource("Card"), Padding=new Thickness(8), Margin=new Thickness(0,4,0,5) };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text="Ship Details", FontSize=15, FontWeight=FontWeights.SemiBold, Margin=new Thickness(2,0,0,4) });
        var grid = new Grid();
        for(int i=0;i<4;i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i%2==0 ? new GridLength(94) : new GridLength(1,GridUnitType.Star) });
        void Pair(int row,int pair,string label,FrameworkElement value)
        {
            while(grid.RowDefinitions.Count<=row) grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var l=new TextBlock{Text=UiStrings.LocalizeText(label),Style=(Style)FindResource("Label"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,3,6,3)};
            Grid.SetRow(l,row);Grid.SetColumn(l,pair*2);grid.Children.Add(l);
            value.Margin=new Thickness(2,3,6,3);Grid.SetRow(value,row);Grid.SetColumn(value,pair*2+1);grid.Children.Add(value);
        }
        _shipName=new TextBox();Pair(0,0,"Name",_shipName);
        _shipType=new ComboBox{ItemsSource=StarshipLogic.GetShipTypeItems()};Pair(0,1,"Type",_shipType);
        _shipClass=new ComboBox{ItemsSource=StarshipLogic.ShipClasses};Pair(1,0,"Class",_shipClass);
        var seedPanel=new DockPanel();_shipSeed=new TextBox();seedPanel.Children.Add(_shipSeed);
        var gen=new Button{Content="Generate",Margin=new Thickness(4,0,0,0)};gen.Click+=(_,_)=>_shipSeed.Text="0x"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8));DockPanel.SetDock(gen,Dock.Right);seedPanel.Children.Add(gen);
        Pair(1,1,"Seed",seedPanel);
        _shipDamage=new TextBox();Pair(2,0,"Damage",_shipDamage);
        _shipShield=new TextBox();Pair(2,1,"Shield",_shipShield);
        _shipDrive=new TextBox();Pair(3,0,"Hyperdrive",_shipDrive);
        _shipManeuver=new TextBox();Pair(3,1,"Maneuverability",_shipManeuver);
        _shipOldColour=new CheckBox{Content=UiStrings.LocalizeText("Use Old Colour"),VerticalAlignment=VerticalAlignment.Center};Pair(4,0,"",_shipOldColour);
        foreach(var field in new[]{_shipName,_shipSeed,_shipDamage,_shipShield,_shipDrive,_shipManeuver})
            field!.TextChanged+=(_,_)=>{if(!_shipDetailsLoading)_shipDetailsChanged=true;};
        _shipType.SelectionChanged+=(_,_)=>{if(!_shipDetailsLoading)_shipDetailsChanged=true;};
        _shipClass.SelectionChanged+=(_,_)=>{if(!_shipDetailsLoading)_shipDetailsChanged=true;};
        _shipOldColour.Checked+=(_,_)=>{if(!_shipDetailsLoading)_shipDetailsChanged=true;};
        _shipOldColour.Unchecked+=(_,_)=>{if(!_shipDetailsLoading)_shipDetailsChanged=true;};
        root.Children.Add(grid);
        var actions=new StackPanel{Margin=new Thickness(0,4,0,0)};
        var primaryActions=new WrapPanel();var managementActions=new WrapPanel();
        Button AddAction(WrapPanel host,string text,Action action,bool primary=false){var b=new Button{Content=text,Margin=new Thickness(2)};if(primary)b.Style=(Style)FindResource("Primary");b.Click+=(_,_)=>action();host.Children.Add(b);return b;}
        AddAction(primaryActions,"Apply Ship Changes",()=>{if(CommitShipDetails()){RefreshShipDetails();_refreshShipList?.Invoke();}},true);
        _shipPrimaryButton=AddAction(primaryActions,"Make Primary",MakeShipPrimary); AddAction(primaryActions,"Delete",DeleteShip); AddAction(primaryActions,"Export JSON",ExportShip); AddAction(primaryActions,"Import JSON",ImportShip);
        AddAction(managementActions,"Move to Archive",MoveShipToArchive); AddAction(managementActions,"Import from Archive",ImportShipFromArchive);
        if(_inventoryPage!=MainPageCatalog.Corvettes)AddAction(managementActions,"Customisation JSON",OpenShipCustomisation);
        actions.Children.Add(primaryActions);actions.Children.Add(managementActions);
        root.Children.Add(actions);card.Child=root;RefreshShipDetails();return card;
    }

    void RefreshShipDetails()
    {
        if(_shipName==null)return;
        _shipDetailsLoading=true;
        if(_selectedShip==null)
        {
            _shipName.Text=_shipSeed!.Text=_shipDamage!.Text=_shipShield!.Text=_shipDrive!.Text=_shipManeuver!.Text="";
            _shipType!.SelectedIndex=_shipClass!.SelectedIndex=-1;_shipOldColour!.IsChecked=false;
            _shipDetailsChanged=false;_shipDetailsLoading=false;RefreshShipPrimaryButton();return;
        }
        var d=StarshipLogic.LoadShipData(_selectedShip.Ownership,_session.Player,_selectedShip.Index);
        _shipName.Text=d.Name;var typeItems=_shipType!.ItemsSource as IEnumerable<StarshipLogic.ShipTypeItem>;var typeIndex=typeItems?.ToList().FindIndex(x=>x.InternalName==d.ShipTypeName)??-1;_shipType.SelectedIndex=typeIndex;
        _shipClass!.SelectedIndex=d.ClassIndex;_shipSeed!.Text=d.Seed;
        static string StatText(string? text,double value)=>string.IsNullOrWhiteSpace(text)?value.ToString("G",CultureInfo.InvariantCulture):text;
        _shipDamage!.Text=StatText(d.DamageText,d.Damage);_shipShield!.Text=StatText(d.ShieldText,d.Shield);_shipDrive!.Text=StatText(d.HyperdriveText,d.Hyperdrive);_shipManeuver!.Text=StatText(d.ManeuverText,d.Maneuver);_shipOldColour!.IsChecked=d.UseOldColours;
        bool enabled=!_showArchive;foreach(var c in new Control[]{_shipName,_shipType,_shipClass,_shipSeed,_shipDamage,_shipShield,_shipDrive,_shipManeuver,_shipOldColour})c.IsEnabled=enabled;
        _shipDetailsChanged=false;_shipDetailsLoading=false;RefreshShipPrimaryButton();
    }

    bool CommitShipDetails()
    {
        if(!ShipDetailsPending || _selectedShip==null || _showArchive)return true;
        try
        {
            var old=StarshipLogic.LoadShipData(_selectedShip.Ownership,_session.Player,_selectedShip.Index);
            double N(string? s,double fallback,string label)=>string.IsNullOrWhiteSpace(s)?fallback:double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&n>=0?n:throw new ArgumentException(UiStrings.Format("starship.pass95.finite_template",label));
            var type=_shipType!.SelectedItem as StarshipLogic.ShipTypeItem;
            if(type==null||_shipClass!.SelectedIndex<0)throw new ArgumentException("Choose a ship type and class.");
            string seed=string.IsNullOrWhiteSpace(_shipSeed!.Text)?old.Seed:_shipSeed.Text;
            if(SeedHelper.NormalizeSeed(seed)==null)throw new ArgumentException("Enter a valid hexadecimal ship seed.");
            string StatValue(string? current,string? oldText,double oldValue)=>string.IsNullOrWhiteSpace(current)?(string.IsNullOrWhiteSpace(oldText)?oldValue.ToString("G",CultureInfo.InvariantCulture):oldText):current;
            StarshipLogic.SaveShipData(_selectedShip.Ownership,_session.Player!,new StarshipLogic.ShipSaveValues{
                Name=_shipName!.Text,SelectedTypeName=type.InternalName,CustomFilename=type.CustomFilename,ClassIndex=_shipClass.SelectedIndex,OriginalClassIndex=old.ClassIndex,
                Seed=seed,Damage=N(_shipDamage!.Text,old.Damage,"Damage"),Shield=N(_shipShield!.Text,old.Shield,"Shield"),Hyperdrive=N(_shipDrive!.Text,old.Hyperdrive,"Hyperdrive"),Maneuver=N(_shipManeuver!.Text,old.Maneuver,"Maneuverability"),
                DamageText=StatValue(_shipDamage.Text,old.DamageText,old.Damage),ShieldText=StatValue(_shipShield.Text,old.ShieldText,old.Shield),HyperdriveText=StatValue(_shipDrive.Text,old.HyperdriveText,old.Hyperdrive),ManeuverText=StatValue(_shipManeuver.Text,old.ManeuverText,old.Maneuver),UseOldColours=_shipOldColour!.IsChecked==true,
                ShipIndex=_selectedShip.Index,PrimaryShipIndex=PrimaryShipIndex(),
                RawStatValues=new(){["^SHIP_DAMAGE"]=old.Damage,["^SHIP_SHIELD"]=old.Shield,["^SHIP_HYPERDRIVE"]=old.Hyperdrive,["^SHIP_AGILE"]=old.Maneuver}});
            _session.MarkDirty();_shipDetailsChanged=false;Status.Text=UiStrings.LocalizeText("Ship changes applied in memory. Press Save to keep them.");return true;
        }catch(Exception ex){Error(ex);return false;}
    }

    int PrimaryShipIndex(){try{return _session.Player?.GetInt("PrimaryShip")??0;}catch{return 0;}}
    void MakeShipPrimary(){if(_selectedShip is not { Occupied:true } ship||_showArchive||!CommitShipDetails())return;RawNumberGuard.SetInt(_session.Player,"PrimaryShip",ship.Index);_session.MarkDirty();RefreshPrimaryShipLabel();RefreshShipPrimaryButton();Status.Text=UiStrings.LocalizeText("Primary ship changed in memory.");}
    void DeleteShip()
    {
        if(_selectedShip==null||_showArchive||!CommitShipDetails())return;
        if(MessageBox.Show(this,UiStrings.LocalizeText("Delete this ship from the save?"),UiStrings.LocalizeText("Delete ship"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        StarshipLogic.DeleteShipData(_selectedShip.Ownership);StarshipLogic.ResetShipCustomisation(_session.Player?.GetArray("CharacterCustomisationData"),_selectedShip.Index);
        StarshipLogic.InvalidateCorvetteBase(_session.Player?.GetArray("PersistentPlayerBases"),_selectedShip.Index);_session.MarkDirty();_selectedShip=null;_refreshShipList?.Invoke();RefreshShipDetails();RenderExosuitInventory();
    }
    void ExportShip()
    {
        if(_selectedShip==null||!CommitShipDetails())return;var d=new SaveFileDialog{Filter=UiStrings.LocalizeText("Ship JSON|*.json"),FileName="ship.json"};
        if(d.ShowDialog(this)==true)try{File.WriteAllText(d.FileName,_selectedShip.Ownership.ToFormattedString());Status.Text=UiStrings.LocalizeText("Exported ship JSON.");}catch(Exception ex){Error(ex);}
    }
    void ImportShip()
    {
        if(_selectedShip==null||_showArchive||!CommitShipDetails())return;var d=new OpenFileDialog{Filter=UiStrings.LocalizeText("Ship JSON|*.json|All files|*.*")};if(d.ShowDialog(this)!=true)return;
        try{var imported=JsonObject.ImportFromFile(d.FileName);if(MessageBox.Show(this,UiStrings.LocalizeText("Replace the selected ship with this JSON data?"),UiStrings.LocalizeText("Import ship"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            foreach(var n in imported.Names())_selectedShip.Ownership.Set(n,imported.Get(n));_session.MarkDirty();RefreshShipDetails();_refreshShipList?.Invoke();RenderExosuitInventory();
        }catch(Exception ex){Error(ex);}
    }
    void MoveShipToArchive()
    {
        if(_selectedShip==null||_showArchive||!CommitShipDetails())return;
        var owned=_session.Player?.GetArray("ShipOwnership");var archive=_session.Player?.GetArray("ArchivedShipOwnership");if(owned==null||archive==null)return;
        int targetIndex=StarshipLogic.FindEmptyArchivedShipSlot(archive);if(targetIndex<0){Error(new InvalidOperationException("The ship archive is full."));return;}
        var target=archive.GetObject(targetIndex);if(target==null)return;
        StarshipLogic.MoveShipToArchive(_selectedShip.Ownership,_selectedShip.Index,target,_session.Player?.GetArray("CharacterCustomisationData"),StarshipLogic.GetShipUsesLegacyColours(_session.Player,_selectedShip.Index));
        _session.MarkDirty();_selectedShip=null;_refreshShipList?.Invoke();RefreshShipDetails();RenderExosuitInventory();
    }
    void ImportShipFromArchive()
    {
        if(_selectedShip==null||!_showArchive)return;var archive=_session.Player?.GetArray("ArchivedShipOwnership");var owned=_session.Player?.GetArray("ShipOwnership");if(archive==null||owned==null)return;
        int targetIndex=StarshipLogic.FindEmptySlot(owned);if(targetIndex<0){Error(new InvalidOperationException("There is no empty owned ship slot."));return;}
        StarshipLogic.ImportShipFromArchive(archive.GetObject(_selectedShip.Index)!,owned.GetObject(targetIndex)!,targetIndex,_session.Player?.GetArray("CharacterCustomisationData"),_session.Player);
        _session.MarkDirty();_selectedShip=null;_refreshShipList?.Invoke();RefreshShipDetails();RenderExosuitInventory();
    }
    void OpenShipCustomisation(){if(_selectedShip==null)return;int i=StarshipLogic.ShipIndexToCcdIndex(_selectedShip.Index);if(i>=0)NavigateToRawJsonPath("PlayerStateData","CharacterCustomisationData",$"[{i}]");}
}

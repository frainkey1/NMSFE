using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using NMSE.Data;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

internal sealed partial class DomainPageView
{
 TextBox? _fName,_fHome,_fModel,_fCrewSeed,_fHyper,_fFleet; ComboBox? _fType,_fClass,_fRace;
 string _freighterInventoryKey="FreighterInventory"; TextBox? _freighterAmount,_freighterMaximum,_freighterDamage; SlotView? _selectedFreighterSlot;
 internal bool FreighterInventoryPending => _selectedFreighterSlot?.Data is {} s && _freighterAmount!=null && (_freighterAmount.Text!=s.GetInt("Amount").ToString(CultureInfo.InvariantCulture)||_freighterMaximum?.Text!=s.GetInt("MaxAmount").ToString(CultureInfo.InvariantCulture)||_freighterDamage?.Text!=s.GetDouble("DamageFactor").ToString("R",CultureInfo.InvariantCulture));
 internal bool FreighterDetailsPending => _section.Path=="CurrentFreighter" && _fName!=null && _session.Player is {} p && FreighterLogic.LoadFreighterData(p) is var d && (_fName.Text!=d.EffectiveName || (_fType?.SelectedItem as FreighterLogic.FreighterTypeItem)?.InternalName!=d.TypeDisplayName || _fClass?.SelectedIndex!=d.ClassIndex || _fHome?.Text!=d.HomeSeed || _fModel?.Text!=d.ModelSeed || _fHyper?.Text!=d.HyperdriveText || _fFleet?.Text!=d.FleetCoordinationText || _fCrewSeed?.Text!=(p.GetObject("CurrentFreighterNPC")?.GetArray("Seed")?.Get(1)?.ToString()??"") || (_fRace?.SelectedItem as FreighterLogic.CrewRaceItem)?.InternalName!=FreighterLogic.NpcResourceToRace.GetValueOrDefault(p.GetObject("CurrentFreighterNPC")?.GetString("Filename")??""));
 UIElement BuildFreighterPage()
 {
  if(_session.Player is not {} p)return new TextBlock{Text="Freighter data is unavailable."};
  if(string.IsNullOrWhiteSpace(p.GetObject("CurrentFreighter")?.GetString("Filename")))
  {
   _fName=null;
   _selectedFreighterSlot=null;
   return new TextBlock{Text=UiStrings.LocalizeText("No freighter is assigned in this save context."),Margin=new Thickness(8)};
  }
  var root=new StackPanel();
  root.Children.Add(BuildFreighterDetailsCard());
  var tabs=new WrapPanel{Margin=new Thickness(0,6,0,3)};
  var inventoryTabs=new Dictionary<string,Button>();
  var links=new WrapPanel{Margin=new Thickness(0,2,0,5)};
  var body=new Grid{Height=570,MinWidth=820};
  body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  body.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
  var tiles=new UniformGrid{MinWidth=540,VerticalAlignment=VerticalAlignment.Top};
  var gridScroll=new ScrollViewer{Content=tiles,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
  var resizeControls=new ContentControl();
  var gridCard=new Border{Style=PageStyle("Card"),Child=gridScroll,Margin=new Thickness(0,0,6,0)};
  body.Children.Add(gridCard);
  var inspector=new StackPanel();
  var detail=new StackPanel();
  var heading=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(9),Margin=new Thickness(0,0,0,10)};
  heading.SetResourceReference(Border.BackgroundProperty,"AccentSurface");
  var headingText=new TextBlock{Text="╱╱  Slot Details",FontSize=17,FontWeight=FontWeights.SemiBold};
  headingText.SetResourceReference(TextBlock.ForegroundProperty,"OnAccent");heading.Child=headingText;detail.Children.Add(heading);
  var icon=new Image{Height=88,Margin=new Thickness(0,0,0,9)};
  detail.Children.Add(icon);
  var itemName=new TextBlock{Text=UiStrings.Get("inventory.select_slot"),FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,12)};
  detail.Children.Add(itemName);
  TextBox DetailField(string label){detail.Children.Add(new TextBlock{Text=label,Style=PageStyle("Label")});var box=new TextBox();detail.Children.Add(box);return box;}
  var amount=_freighterAmount=DetailField("Amount");var maximum=_freighterMaximum=DetailField("Maximum");var damage=_freighterDamage=DetailField("Damage (0–1)");
  var applySlot=new Button{Content="Apply Changes",Style=PageStyle("Primary"),Margin=new Thickness(3,14,3,3),IsEnabled=false};
  detail.Children.Add(applySlot);
  inspector.Children.Add(new Border{Style=PageStyle("Card"),Child=detail});
  var picker=new ContentControl();inspector.Children.Add(picker);
  var detailScroll=new ScrollViewer{Content=inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  Grid.SetColumn(detailScroll,1);body.Children.Add(detailScroll);
  var inventoryHeader=new Grid{MinWidth=820};inventoryHeader.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});inventoryHeader.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
  var inventoryHeaderLeft=new StackPanel();inventoryHeaderLeft.Children.Add(tabs);inventoryHeaderLeft.Children.Add(links);inventoryHeader.Children.Add(inventoryHeaderLeft);
  resizeControls.Content=_owner.BuildInventoryResizeControls(p.GetObject(_freighterInventoryKey),15,13,CommitChanges,RenderInventory,compact:true);
  Grid.SetColumn(resizeControls,1);inventoryHeader.Children.Add(resizeControls);
  root.Children.Add(inventoryHeader);root.Children.Add(body);

  bool CommitChanges()
  {
   if(!CommitFreighterInventorySlot())return false;
   return CommitFreighterDetails();
  }
  void SelectSlot(SlotView? slot,bool commit=true)
  {
   if(commit&&!CommitChanges())return;
   _selectedFreighterSlot=slot;
   itemName.Text=slot?.Name??UiStrings.Get("inventory.select_slot");
   icon.Source=slot?.Icon;icon.Visibility=icon.Source==null?Visibility.Collapsed:Visibility.Visible;
   var data=slot?.Data;
   amount.Text=data?.GetInt("Amount").ToString(CultureInfo.InvariantCulture)??"";
   maximum.Text=data?.GetInt("MaxAmount").ToString(CultureInfo.InvariantCulture)??"";
   damage.Text=data?.GetDouble("DamageFactor").ToString("R",CultureInfo.InvariantCulture)??"";
   amount.IsEnabled=maximum.IsEnabled=damage.IsEnabled=data!=null;
   applySlot.IsEnabled=data!=null;
   picker.Content=BuildPickerForSlot(slot);
  }
  Border BuildPickerForSlot(SlotView? slot)
  {
   var inventory=slot?.Inventory;
   bool technology=_freighterInventoryKey.EndsWith("_TechOnly",StringComparison.Ordinal);
   var kind=new InventoryKind("Freighter",technology?"Freighter":"FreighterCargo",technology,true);
   return _owner.BuildItemPicker(slot,kind,CommitChanges,RenderInventory);
  }
  void RenderInventory()
  {
   var inventory=p.GetObject(_freighterInventoryKey);
   resizeControls.Content=_owner.BuildInventoryResizeControls(inventory,15,13,CommitChanges,RenderInventory,compact:true);
   var previous=_selectedFreighterSlot;
   _selectedFreighterSlot=null;
   tiles.Children.Clear();
   if(inventory==null){tiles.Columns=1;tiles.Children.Add(new TextBlock{Text="This inventory is unavailable.",Margin=new Thickness(8)});SelectSlot(null,false);return;}
   var (items,width,height)=_owner.MakeSlotModels(inventory);tiles.Columns=width;
   bool technology=_freighterInventoryKey.EndsWith("_TechOnly",StringComparison.Ordinal);
   var kind=new InventoryKind("Freighter",technology?"Freighter":"FreighterCargo",technology,true);
   foreach(var item in items)
   {
    var contents=new StackPanel{Height=72};
    var label=new TextBlock{Text=item.Name,Width=51,FontSize=9,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=item.LabelForeground};
    contents.Children.Add(new Border{Child=label,Background=item.LabelBackground,Padding=new Thickness(2),CornerRadius=new CornerRadius(4)});
    contents.Children.Add(new Image{Source=item.Icon,Height=39,Margin=new Thickness(2)});
    contents.Children.Add(new TextBlock{Text=item.AmountLabel,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center});
    var tile=new Button{Content=contents,Padding=new Thickness(0),Margin=new Thickness(2),Background=item.Background,BorderBrush=item.Border,ToolTip=item.Name};
    tile.Click+=(_,_)=>SelectSlot(item);
    _owner.WireInventoryTile(tile,item,kind,CommitChanges,RenderInventory,()=>SelectSlot(item),()=>((Border)picker.Content).BringIntoView());
    tiles.Children.Add(tile);
   }

   SelectSlot(previous!=null&&ReferenceEquals(previous.Inventory,inventory)?items.FirstOrDefault(x=>x.X==previous.X&&x.Y==previous.Y):null,false);
  }
  void SelectInventoryTab(string key)
  {
   if(!CommitChanges())return;
   _freighterInventoryKey=key;_selectedFreighterSlot=null;
   foreach(var pair in inventoryTabs)
   {
    bool selected=pair.Key==key;
    pair.Value.SetResourceReference(Control.BackgroundProperty,selected?"AccentSurface":"Surface");
    pair.Value.SetResourceReference(Control.ForegroundProperty,selected?"OnAccent":"Text");
   }
   links.Children.Clear();
   string property=key;
   links.Children.Add(_owner.JsonRedirectButton(key=="FreighterInventory"?"↗ JSON · Cargo":"↗ JSON · Technology","Open this freighter inventory in Raw JSON Editor",()=>_owner.NavigateToRawJsonPath("PlayerStateData",property)));
   RenderInventory();
  }
  foreach(var (label,key) in new[]{("Cargo","FreighterInventory"),("Technology","FreighterInventory_TechOnly")})
  {
   var button=new Button{Content=label,MinWidth=115,Margin=new Thickness(2)};
   inventoryTabs[key]=button;button.Click+=(_,_)=>SelectInventoryTab(key);tabs.Children.Add(button);
  }
  amount.TextChanged+=(_,_)=>applySlot.IsEnabled=_selectedFreighterSlot?.Data!=null;
  maximum.TextChanged+=(_,_)=>applySlot.IsEnabled=_selectedFreighterSlot?.Data!=null;
  damage.TextChanged+=(_,_)=>applySlot.IsEnabled=_selectedFreighterSlot?.Data!=null;
  applySlot.Click+=(_,_)=>{if(CommitFreighterInventorySlot()){RenderInventory();_owner.PageStatus("Freighter inventory changes applied in memory. Press Save to keep them.");}};
  SelectInventoryTab(_freighterInventoryKey);
  return new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
 bool CommitFreighterInventorySlot()
 {
  if(!FreighterInventoryPending||_selectedFreighterSlot?.Data is not {} slot)return true;
  try
  {
   if(!int.TryParse(_freighterAmount?.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int amount)||
      !int.TryParse(_freighterMaximum?.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int maximum)||
      !double.TryParse(_freighterDamage?.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double damage)||
      !double.IsFinite(damage)||damage<0)throw new ArgumentException("Enter valid numeric slot values.");
   _session.ApplyInventorySlot(_selectedFreighterSlot.Inventory,slot,amount,maximum,damage);return true;
  }
  catch(Exception ex){_owner.PageError(ex);return false;}
 }

 UIElement BuildFreighterDetailsCard()
 {
  if(_session.Player is not {} p)return new TextBlock{Text="Freighter data is unavailable."};
  var d=FreighterLogic.LoadFreighterData(p);var card=new Border{Style=PageStyle("Card"),Padding=new Thickness(10)};var root=new StackPanel();
  var header=new DockPanel();header.Children.Add(new TextBlock{Text="Freighter",FontSize=17,FontWeight=FontWeights.SemiBold});var json=_owner.JsonRedirectButton("↗ JSON","Open current freighter data in Raw JSON Editor",()=>_owner.NavigateToRawJsonPath("PlayerStateData","CurrentFreighter"));DockPanel.SetDock(json,Dock.Right);header.Children.Add(json);root.Children.Add(header);
  var g=new Grid{Margin=new Thickness(0,8,0,2)};g.ColumnDefinitions.Add(new(){Width=new GridLength(145)});g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new(){Width=new GridLength(145)});g.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  void Pair(int r,int c,string label,FrameworkElement control){while(g.RowDefinitions.Count<=r)g.RowDefinitions.Add(new(){Height=GridLength.Auto});var l=new TextBlock{Text=label,Style=PageStyle("Label"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3)};Grid.SetRow(l,r);Grid.SetColumn(l,c);g.Children.Add(l);control.Margin=new Thickness(3);Grid.SetRow(control,r);Grid.SetColumn(control,c+1);g.Children.Add(control);}
  _fName=new(){Text=d.EffectiveName};Pair(0,0,"Name",_fName);var typeItems=FreighterLogic.GetFreighterTypeItems();_fType=new(){ItemsSource=typeItems,SelectedItem=typeItems.FirstOrDefault(x=>x.InternalName==d.TypeDisplayName)};Pair(0,2,"Type",_fType);
  _fClass=new(){ItemsSource=FreighterLogic.FreighterClasses,SelectedIndex=d.ClassIndex};Pair(1,0,"Class",_fClass);_fHyper=new(){Text=d.HyperdriveText};Pair(1,2,"Hyperdrive",_fHyper);
  _fHome=new(){Text=d.HomeSeed};Pair(2,0,"Home Seed",_fHome);var npc=p.GetObject("CurrentFreighterNPC");var raceItems=FreighterLogic.GetCrewRaceItems();_fRace=new(){ItemsSource=raceItems,SelectedItem=raceItems.FirstOrDefault(x=>x.InternalName==d.CrewRaceDisplayName)};Pair(2,2,"Crew Race",_fRace);
  _fFleet=new(){Text=d.FleetCoordinationText};Pair(3,0,"Fleet Coordination",_fFleet);_fModel=new(){Text=d.ModelSeed};Pair(3,2,"Model Seed",_fModel);
  _fCrewSeed=new(){Text=npc?.GetArray("Seed")?.Get(1)?.ToString()??""};Pair(4,0,"Crew Seed",_fCrewSeed);Pair(4,2,"Base Items",new TextBox{Text=d.BaseItemCount<0?"N/A":d.BaseItemCount.ToString(CultureInfo.InvariantCulture),IsReadOnly=true});root.Children.Add(g);
  var actions=new WrapPanel{Margin=new Thickness(0,6,0,0)};
  void Act(string text,Action action,bool primary=false){var b=new Button{Content=text,Margin=new Thickness(2)};if(primary)b.Style=PageStyle("Primary");b.Click+=(_,_)=>action();actions.Children.Add(b);}
  Act("Generate Home Seed",()=>_fHome.Text="0x"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));Act("Generate Model Seed",()=>_fModel.Text="0x"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));Act("Generate Crew Seed",()=>_fCrewSeed.Text="0x"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
  Act("Apply Freighter Changes",()=>{if(CommitFreighterDetails())_owner.PageStatus("Freighter changes applied in memory. Press Save to keep them.");},true);Act("Export Freighter Base",ExportFreighterBase);Act("Import Freighter Base",ImportFreighterBase);root.Children.Add(actions);card.Child=root;return card;
 }
 internal bool CommitFreighterDetails()
 {
  if(!FreighterDetailsPending||_session.Player is not {} p)return true;
  try{
   double N(string? s,string label)=>double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&n>=0?n:throw new ArgumentException(string.Format(UiStrings.LocalizeText("{0} must be a finite, non-negative number."),label));
   var old=FreighterLogic.LoadFreighterData(p);var type=_fType!.SelectedItem as FreighterLogic.FreighterTypeItem;if(type==null||_fClass!.SelectedIndex<0)throw new ArgumentException("Choose a freighter type and class.");
   if(SeedHelper.NormalizeSeed(_fHome!.Text)==null||SeedHelper.NormalizeSeed(_fModel!.Text)==null)throw new ArgumentException("Enter valid hexadecimal seeds.");
   FreighterLogic.SaveFreighterData(p,new FreighterLogic.FreighterSaveValues{Name=_fName!.Text==old.EffectiveName?old.Name:_fName.Text,SelectedTypeName=type.InternalName,ClassIndex=_fClass.SelectedIndex,OriginalClassIndex=old.ClassIndex,HomeSeed=_fHome.Text,ModelSeed=_fModel.Text,Hyperdrive=N(_fHyper!.Text,"Hyperdrive"),FleetCoordination=N(_fFleet!.Text,"Fleet coordination"),HyperdriveText=_fHyper.Text,FleetCoordinationText=_fFleet.Text,RawStatValues=new(){["^FREI_HYPERDRIVE"]=old.Hyperdrive,["^FREI_FLEET"]=old.FleetCoordination}});
   var npc=p.GetObject("CurrentFreighterNPC");var race=(_fRace!.SelectedItem as FreighterLogic.CrewRaceItem)?.InternalName;if(npc!=null&&race!=null&&FreighterLogic.RaceToNpcResource.TryGetValue(race,out var file))npc.Set("Filename",file);var seed=npc?.GetArray("Seed");var normalized=SeedHelper.NormalizeSeed(_fCrewSeed!.Text);if(seed!=null&&seed.Length>1&&normalized!=null)seed.Set(1,normalized);
   _session.MarkDirty();return true;
  }catch(Exception ex){_owner.PageError(ex);return false;}
 }
 void ExportFreighterBase(){var b=_session.Player==null?null:FreighterLogic.FindFreighterBase(_session.Player);if(b==null){_owner.PageError(new InvalidOperationException("No Freighter Base is present in this save."));return;}var d=new SaveFileDialog{Filter=UiStrings.LocalizeText("Freighter Base JSON|*.json"),FileName="freighterbase.json"};if(d.ShowDialog(_owner)==true)try{File.WriteAllText(d.FileName,b.ToFormattedString());_owner.PageStatus("Freighter Base exported.");}catch(Exception ex){_owner.PageError(ex);}}
 void ImportFreighterBase(){if(_session.Player is not {} p)return;var b=FreighterLogic.FindFreighterBase(p);if(b==null){_owner.PageError(new InvalidOperationException("No Freighter Base is present to replace."));return;}var d=new OpenFileDialog{Filter=UiStrings.LocalizeText("Freighter Base JSON|*.json|All files|*.*")};if(d.ShowDialog(_owner)!=true)return;try{var v=JsonObject.ImportFromFile(d.FileName);if(v.GetArray("Objects")==null||v.GetObject("BaseType")?.GetString("PersistentBaseTypes")!="FreighterBase")throw new InvalidDataException("Selected JSON is not a Freighter Base.");if(MessageBox.Show(_owner,UiStrings.LocalizeText("Replace the saved Freighter Base with this JSON?"),UiStrings.LocalizeText("Import Freighter Base"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;foreach(var n in v.Names())b.Set(n,v.Get(n));_session.MarkDirty();_owner.PageStatus("Freighter Base imported in memory.");}catch(Exception ex){_owner.PageError(ex);}}
}

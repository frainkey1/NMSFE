using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;
public partial class MainWindow
{
 readonly List<(FieldEdit Field,Func<string> Read)> _generalFields=[];
 readonly List<TextBox> _coordinateFields=[];
 PlayerCoordinates? _coordinateBaseline;
 TextBox? _battleWarps;
 int _battleBaseline;
 StackPanel? _locationFields;
 WrapPanel? _currentGlyphs;
 bool PlayerGeneralPending=>_generalFields.Any(f=>f.Read()!=f.Field.Baseline)||PlayerCoordinatesChanged()||(_battleWarps!=null&&_battleWarps.Text!=_battleBaseline.ToString(CultureInfo.InvariantCulture));
 void ClearPlayerGeneralState(){_generalFields.Clear();_coordinateFields.Clear();_coordinateBaseline=null;_battleWarps=null;_locationFields=null;_currentGlyphs=null;}
 static string[] CoordinateTexts(PlayerCoordinates c)=>[c.Galaxy.ToString(CultureInfo.InvariantCulture),c.X.ToString(CultureInfo.InvariantCulture),c.Y.ToString(CultureInfo.InvariantCulture),c.Z.ToString(CultureInfo.InvariantCulture),c.System.ToString(CultureInfo.InvariantCulture),c.Planet.ToString(CultureInfo.InvariantCulture)];
 bool PlayerCoordinatesChanged(){if(_coordinateBaseline==null||_coordinateFields.Count!=6)return false;if(string.IsNullOrWhiteSpace(_coordinateFields[0].Text)&&_coordinateFields.Skip(1).All(f=>string.IsNullOrWhiteSpace(f.Text)))return false;var baseline=CoordinateTexts(_coordinateBaseline);return _coordinateFields.Skip(1).Where((f,i)=>f.Text!=baseline[i+1]).Any()||(!string.IsNullOrWhiteSpace(_coordinateFields[0].Text)&&_coordinateFields[0].Text!=baseline[0]);}
 TextBlock PlayerLabel(string text)=>new(){Text=text,Style=(Style)FindResource("Label"),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
 Grid PlayerRow(Panel panel,string label,FrameworkElement input)
 {
  input.MinHeight=32;input.VerticalAlignment=VerticalAlignment.Center;
  var row=new Grid{Margin=new Thickness(0,2,0,2),MinHeight=40};row.ColumnDefinitions.Add(new(){Width=new GridLength(135)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(PlayerLabel(label));Grid.SetColumn(input,1);row.Children.Add(input);panel.Children.Add(row);return row;
 }
 Border PlayerCard(string title,StackPanel panel,double width=420,TextBlock? footer=null)
 {
  panel.Children.Insert(0,new TextBlock{Text=title,Style=(Style)FindResource("Heading")});
  var content=new DockPanel();
  if(footer!=null){DockPanel.SetDock(footer,Dock.Bottom);content.Children.Add(footer);}
  content.Children.Add(panel);
  return new Border{Style=(Style)FindResource("Card"),Width=width,VerticalAlignment=VerticalAlignment.Stretch,Child=content};
 }
 Button PlayerButton(string text,Action action,bool primary=false)
 {
  var b=new Button{Content=text,HorizontalAlignment=HorizontalAlignment.Left};if(primary)b.Style=(Style)FindResource("Primary");b.Click+=(_,_)=>{if(_busy)return;try{action();}catch(Exception ex){Error(ex);}};return b;
 }
 void PlayerReadOnly(Panel body,string label,string? value)
 {
  string display=value switch{"Yes"=>UiStrings.Get("ui.player.value_yes"),"No"=>UiStrings.Get("ui.player.value_no"),_=>value??UiStrings.LocalizeText("Unavailable")};
  PlayerRow(body,label,new TextBox{Text=display,IsReadOnly=true,MinWidth=150});
 }
 sealed record GeneralChoice(string Value,string Label){public override string ToString()=>Label;}
 void PlayerExistingField(Panel body,string label,JsonObject? parent,string key,string[]? options=null,bool disabled=false,Func<string,string>? optionLabel=null)
 {
  if(parent==null||!parent.Contains(key)){PlayerReadOnly(body,label,UiStrings.LocalizeText("Unavailable in this save"));return;}
  var value=parent.Get(key);string baseline=SaveSession.FieldText(value);FrameworkElement input;Func<string> read;
  if(value is bool boolean){var box=new CheckBox{IsChecked=boolean,Margin=new Thickness(7),VerticalAlignment=VerticalAlignment.Center};input=box;read=()=>box.IsChecked==true?"true":"false";}
  else if(options!=null){var list=options.ToList();if(!list.Contains(baseline))list.Insert(0,baseline);var choices=list.Select(value=>new GeneralChoice(value,optionLabel?.Invoke(value)??value)).ToArray();var box=new ComboBox{ItemsSource=choices,SelectedValuePath=nameof(GeneralChoice.Value),SelectedValue=baseline,MinWidth=150};input=box;read=()=>box.SelectedValue as string??baseline;}
  else{var box=new TextBox{Text=baseline,MinWidth=150};input=box;read=()=>box.Text;}
  input.IsEnabled=!disabled;PlayerRow(body,label,input);_generalFields.Add((new(parent,key,baseline,baseline),read));
 }
 FrameworkElement BuildPlayerGeneralPage()
 {
  var root=new StackPanel();var cards=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left};root.Children.Add(cards);_playerPageBaseline=_session.ReadPlayer();var p=_session.Player!;var common=_session.Data.GetObject("CommonStateData");var draft=_playerPageBaseline;
  var vitals=new StackPanel();string[] names=[UiStrings.Get("player.health"),UiStrings.Get("player.shield"),UiStrings.Get("player.energy"),UiStrings.Get("player.units"),UiStrings.Get("player.nanites"),UiStrings.Get("player.quicksilver")];string[] values=[draft.Health,draft.Shield,draft.Energy,draft.Units,draft.Nanites,draft.Quicksilver];
  for(int i=0;i<6;i++)
  {
   int index=i;var box=new TextBox{Text=values[i],MinWidth=150};_playerFields.Add(box);var input=new DockPanel();
   if(i>=3){var max=PlayerButton("Max",()=>box.Text=uint.MaxValue.ToString(CultureInfo.InvariantCulture));DockPanel.SetDock(max,Dock.Right);input.Children.Add(max);}input.Children.Add(box);PlayerRow(vitals,names[i],input);
  }
  cards.Children.Add(PlayerCard(UiStrings.Get("player.section_statistics"),vitals,footer:PlayerLabel("Vitals 0–999999 · Currency 0–4294967295. Unchanged values retain their original data.")));
  var save=new StackPanel();string?[] texts=[draft.SaveName,draft.SaveSummary];for(int i=0;i<2;i++){var field=new TextBox{Text=texts[i]??"Unavailable",IsEnabled=texts[i]!=null,MinWidth=150};_playerFields.Add(field);PlayerRow(save,UiStrings.Get(i==0?"player.save_name":"player.save_summary"),field);}
  long seconds=0;try{seconds=common?.GetLong("TotalPlayTime")??0;}catch{}var elapsed=TimeSpan.FromSeconds(Math.Max(0,seconds));PlayerReadOnly(save,UiStrings.Get("player.total_play_time"),$"{(long)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s");
  PlayerExistingField(save,UiStrings.Get("player.third_person_camera"),common,"UsesThirdPersonCharacterCam");
  PlayerReadOnly(save,UiStrings.Get("player.last_save_date"),_session.SourcePath!=null?File.GetLastWriteTime(_session.SourcePath).ToString("g"):"Unavailable");
  var difficulty=p.GetObject("DifficultyState");foreach(var (labelKey,key) in new[]{("player.current_preset","Preset"),("player.easiest_used","EasiestUsedPreset"),("player.hardest_used","HardestUsedPreset")})PlayerExistingField(save,UiStrings.Get(labelKey),difficulty?.GetObject(key),"DifficultyPresetType",SaveSession.PlayerPresets,_session.PlayerExpedition);
  if(_session.PlayerExpedition)PlayerReadOnly(save,UiStrings.LocalizeText("Expedition"),p.Get("StartingSeasonNumber")?.ToString()??UiStrings.LocalizeText("Active"));
  PlayerReadOnly(save,UiStrings.Get("player.account_name"),_session.PlayerAccountDisplayName);cards.Children.Add(PlayerCard(UiStrings.Get("player.section_save_info"),save));
  var location=new StackPanel();_locationFields=new StackPanel();location.Children.Add(_locationFields);_currentGlyphs=new WrapPanel();location.Children.Add(_currentGlyphs);RefreshPlayerLocation();
  string PlayerStateLabel(string value){int index=Array.IndexOf(CoordinateHelper.PlayerStates,value);return index>=0&&index<CoordinateHelper.PlayerStateLocKeys.Length?UiStrings.Get(CoordinateHelper.PlayerStateLocKeys[index]):value;}
  PlayerExistingField(location,UiStrings.Get("player.label_player_state"),_session.Data.GetObject("SpawnStateData"),"LastKnownPlayerState",CoordinateHelper.PlayerStates,optionLabel:PlayerStateLabel);
  PlayerExistingField(location,"Portal interference",p,"OnOtherSideOfPortal");PlayerExistingField(location,"Purple-system warping",p,"HasDiscoveredPurpleSystems");cards.Children.Add(PlayerCard("Current coordinates",location,420));
  var coordinate=new StackPanel();_coordinateBaseline=_session.ReadPlayerCoordinates();string[] coordLabels=["Galaxy (1–256)","Voxel X (−2048–2047)","Voxel Y (−128–127)","Voxel Z (−2048–2047)","System (0–4095)","Planet (0–15)"];
  TextBox? galaxyNameField=null;if(_coordinateBaseline!=null){for(int i=0;i<6;i++){var field=new TextBox{Text=string.Empty,MinWidth=150};_coordinateFields.Add(field);FrameworkElement coordinateInput=field;if(i==0){var galaxyHint=new TextBlock{Text="Enter a galaxy from 1 to 256",Foreground=Brushes.Gray,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};var galaxyInput=new Grid();galaxyInput.Children.Add(field);galaxyInput.Children.Add(galaxyHint);void UpdateGalaxyHint(){galaxyHint.Visibility=string.IsNullOrEmpty(field.Text)?Visibility.Visible:Visibility.Collapsed;}field.TextChanged+=(_,_)=>UpdateGalaxyHint();UpdateGalaxyHint();coordinateInput=galaxyInput;}PlayerRow(coordinate,coordLabels[i],coordinateInput);if(i==0){var galaxyName=new TextBox{IsReadOnly=true,MinWidth=150};galaxyNameField=galaxyName;void UpdateGalaxyName(){galaxyName.Text=string.IsNullOrWhiteSpace(field.Text)?string.Empty:int.TryParse(field.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int galaxy)&&galaxy>=1&&galaxy<=256?GalaxyDatabase.GetGalaxyDisplayName(galaxy-1):string.Empty;}field.TextChanged+=(_,_)=>UpdateGalaxyName();UpdateGalaxyName();PlayerRow(coordinate,"Galaxy name",galaxyName);}}coordinate.Children.Add(PlayerButton("Apply coordinates",()=>{if(CommitPlayerPage()){RefreshPlayerLocation();Status.Text=UiStrings.LocalizeText("Coordinates applied in memory. Press Save to keep them.");}}));}
  else coordinate.Children.Add(PlayerLabel("No player address is present in this save."));
  var portal=new TextBox{MaxLength=12,MinWidth=180,CharacterCasing=CharacterCasing.Upper};PlayerRow(coordinate,"Portal code (hex)",portal);var galaxyField=_coordinateFields.Count>0?_coordinateFields[0]:null;int? lastRolledGalaxy=null;DispatcherTimer? galaxyDebounce=null;void ClearPortalConverter(){portal.Clear();for(int i=1;i<_coordinateFields.Count;i++)_coordinateFields[i].Clear();lastRolledGalaxy=null;}void RollPortalForGalaxy(int galaxyNumber,bool confirm){string code=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));var c=SaveSession.ConvertPlayerPortal(code,galaxyNumber);string galaxyName=GalaxyDatabase.GetGalaxyDisplayName(galaxyNumber-1);if(confirm&&MessageBox.Show(this,string.Format(UiStrings.LocalizeText("Use portal {0} in {1}? Changes remain in memory until Save."),code,galaxyName),UiStrings.LocalizeText("Coordinate roulette"),MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;lastRolledGalaxy=galaxyNumber;SetPlayerCoordinateDraft(c);if(galaxyField!=null)galaxyField.Text=galaxyNumber.ToString(CultureInfo.InvariantCulture);if(galaxyNameField!=null)galaxyNameField.Text=galaxyName;portal.Text=code;if(!confirm)Status.Text=string.Format(UiStrings.LocalizeText("Coordinate roulette generated for {0}. Press Apply coordinates to update your location."),galaxyName);}void CommitGalaxyRoulette(){galaxyDebounce?.Stop();if(galaxyField==null||!int.TryParse(galaxyField.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int galaxyNumber)||galaxyNumber<1||galaxyNumber>256||lastRolledGalaxy==galaxyNumber)return;RollPortalForGalaxy(galaxyNumber,false);}if(galaxyField!=null){galaxyDebounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};galaxyDebounce.Tick+=(_,_)=>CommitGalaxyRoulette();galaxyField.TextChanged+=(_,_)=>{galaxyDebounce.Stop();if(string.IsNullOrWhiteSpace(galaxyField.Text)){galaxyDebounce.Stop();ClearPortalConverter();}else galaxyDebounce.Start();};galaxyField.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){CommitGalaxyRoulette();e.Handled=true;}};galaxyField.LostKeyboardFocus+=(_,e)=>{if(e.NewFocus is Button b&&Equals(b.Content,"Coordinate roulette"))return;CommitGalaxyRoulette();};}var glyphs=new WrapPanel();
  for(int i=0;i<16;i++){char digit="0123456789ABCDEF"[i];var b=new Button{Content=PlayerGlyph(digit),ToolTip=string.Format(UiStrings.LocalizeText("Glyph {0} · {1}"),i+1,digit),Padding=new Thickness(2),Margin=new Thickness(2)};b.Click+=(_,_)=>{if(portal.Text.Length<12)portal.Text+=digit;};glyphs.Children.Add(b);}coordinate.Children.Add(glyphs);
  coordinate.Children.Add(PlayerButton("Convert to coordinates",()=>{int galaxy=_coordinateFields.Count>0&&int.TryParse(_coordinateFields[0].Text,out int g)?g:(_coordinateBaseline?.Galaxy??1);SetPlayerCoordinateDraft(SaveSession.ConvertPlayerPortal(portal.Text,galaxy));}));
  coordinate.Children.Add(PlayerButton("Coordinate roulette",()=>RollPortalForGalaxy(Random.Shared.Next(1,257),true)));cards.Children.Add(PlayerCard("Change coordinates & portal converter",coordinate,420));
  var battle=new StackPanel();_battleBaseline=_session.PlayerWarpsUntilBattle;_battleWarps=new TextBox{Text=_battleBaseline.ToString(CultureInfo.InvariantCulture),IsEnabled=p.Contains("WarpsLastSpaceBattle")};PlayerRow(battle,"Warps to next",_battleWarps);var remaining=TimeSpan.FromSeconds(_session.PlayerSecondsUntilBattle);PlayerReadOnly(battle,"Time to next",$"{remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s");
  battle.Children.Add(PlayerButton("Trigger battle on next warp",()=>{if(!CommitPlayerPage())return;_session.TriggerPlayerBattle();_battleBaseline=_session.PlayerWarpsUntilBattle;_battleWarps.Text=_battleBaseline.ToString(CultureInfo.InvariantCulture);Status.Text=UiStrings.LocalizeText("Space battle trigger applied in memory. Press Save to keep it.");}));cards.Children.Add(PlayerCard("Space battle",battle));
  string OutfitDisplayName(string name)=>name.ToLowerInvariant() switch{"direfit"=>UiStrings.Get("player.outfit_name_direfit"),"direfitcape"=>UiStrings.Get("player.outfit_name_direfitcape"),_=>name};
  var outfits=new StackPanel();var outfitArray=p.GetArray("Outfits");var outfitNames=p.GetArray("OutfitNames");var outfit=new ComboBox{MinWidth=240};var choices=new List<string>();if(outfitArray!=null)for(int i=0;i<outfitArray.Length;i++)choices.Add(outfitNames!=null&&i<outfitNames.Length&&outfitNames.Get(i) is string n&&!string.IsNullOrWhiteSpace(n)?OutfitDisplayName(n):string.Format(UiStrings.Get("player.outfit_default_name"),i+1));outfit.ItemsSource=choices;outfit.SelectedIndex=choices.Count>0?0:-1;outfits.Children.Add(outfit);var outfitActions=new WrapPanel{IsEnabled=choices.Count>0};

  outfitActions.Children.Add(PlayerButton(UiStrings.Get("player.button_export_outfit"),()=>{if(!CommitPlayerPage())return;var d=new SaveFileDialog{Filter=UiStrings.Get("ui.player.outfit_export_filter"),FileName=UiStrings.Get("ui.player.outfit_filename")};if(d.ShowDialog(this)==true)OutfitLogic.ExportOutfit(outfitArray!.GetObject(outfit.SelectedIndex)!,d.FileName);}));
  outfitActions.Children.Add(PlayerButton(UiStrings.Get("player.button_import_outfit"),()=>{if(!CommitPlayerPage())return;var d=new OpenFileDialog{Filter=UiStrings.Get("ui.player.outfit_import_filter")};if(d.ShowDialog(this)==true){_session.ImportPlayerOutfit(outfit.SelectedIndex,d.FileName);Status.Text=UiStrings.Get("player.untranslated_outfit_imported");}}));
  outfitActions.Children.Add(PlayerButton(UiStrings.Get("player.button_apply_outfit"),()=>{if(!CommitPlayerPage())return;_session.ApplyPlayerOutfit(outfit.SelectedIndex);Status.Text=UiStrings.Get("player.untranslated_outfit_applied");}));outfits.Children.Add(outfitActions);cards.Children.Add(PlayerCard(UiStrings.Get("player.card_outfits"),outfits));
  root.Children.Add(PlayerButton("✓ Apply player edits",()=>{if(CommitPlayerPage()){RefreshPlayerLocation();Status.Text=UiStrings.LocalizeText("Player changes applied in memory. Press Save to keep them.");}},true));
  root.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=BuildPlayerSaveUtilities()});
  return new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
 }
 internal FrameworkElement PlayerGlyph(char hex)
 {
  int index="0123456789ABCDEF".IndexOf(char.ToUpperInvariant(hex))+1;return new Border{Background=new SolidColorBrush(Color.FromRgb(40,55,57)),CornerRadius=new CornerRadius(4),Child=new Image{Source=LoadIcon($"UI-GLYPH{index}.PNG"),Width=27,Height=27,ToolTip=hex.ToString()}};
 }
 void RefreshPlayerLocation()
 {
  if(_locationFields==null)return;_locationFields.Children.Clear();foreach(var pair in _session.ReadPlayerLocationInfo())PlayerReadOnly(_locationFields,pair.Key,pair.Value);
  if(_currentGlyphs!=null){_currentGlyphs.Children.Clear();foreach(char hex in _session.PlayerPortalCode)_currentGlyphs.Children.Add(PlayerGlyph(hex));}
 }
 void SetPlayerCoordinateDraft(PlayerCoordinates c){if(_coordinateFields.Count!=6)throw new InvalidOperationException("No editable coordinate address.");var texts=CoordinateTexts(c);for(int i=0;i<6;i++)_coordinateFields[i].Text=texts[i];}
 bool CommitPlayerGeneral(bool validateOnly=false)
 {
  if(_generalFields.Count==0&&_coordinateFields.Count==0&&_battleWarps==null)return true;
  var edits=_generalFields.Select(f=>f.Field with{Text=f.Read()}).ToArray();
  PlayerCoordinates? coordinates=null;if(PlayerCoordinatesChanged())
  {
   var nums=_coordinateFields.Select((f,i)=>i==0&&string.IsNullOrWhiteSpace(f.Text)?_coordinateBaseline!.Galaxy:int.TryParse(f.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:throw new ArgumentException("Coordinates must be whole numbers.")).ToArray();coordinates=new(nums[0],nums[1],nums[2],nums[3],nums[4],nums[5]);
   if(nums[0]<1||nums[0]>256||nums[1]< -2048||nums[1]>2047||nums[2]< -128||nums[2]>127||nums[3]< -2048||nums[3]>2047||nums[4]<0||nums[4]>4095||nums[5]<0||nums[5]>15)throw new ArgumentException("Coordinates are outside the displayed ranges.");
  }
  int? warps=null;if(_battleWarps!=null&&_battleWarps.Text!=_battleBaseline.ToString(CultureInfo.InvariantCulture)){if(!int.TryParse(_battleWarps.Text,out int n)||n<0||n>999)throw new ArgumentException("Warps must be a whole number from 0 to 999.");warps=n;}
  _session.ApplyPlayerGeneralFields(edits,true);if(coordinates!=null)_session.ApplyPlayerCoordinates(coordinates,true);if(warps.HasValue)_session.SetPlayerBattleWarps(warps.Value,true);if(validateOnly)return true;
  _session.ApplyPlayerGeneralFields(edits);if(coordinates!=null){_session.ApplyPlayerCoordinates(coordinates);_coordinateBaseline=coordinates;}if(warps.HasValue){_session.SetPlayerBattleWarps(warps.Value);_battleBaseline=warps.Value;}
  for(int i=0;i<_generalFields.Count;i++){var f=_generalFields[i];_generalFields[i]=(f.Field with{Baseline=f.Read()},f.Read);}return true;
 }
}

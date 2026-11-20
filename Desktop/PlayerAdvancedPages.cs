using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;

public partial class MainWindow
{
 PlayerAdvancedDraft? _advancedDraft;
 readonly List<Action<PlayerAdvancedDraft>> _advancedEdits=[];
 bool _advancedPending;
 CheckBox? _advancedConsent;
 bool PlayerAdvancedPending=>_advancedPending||(_advancedDraft?.Changed??false);
 PlayerAdvancedDraft Advanced=>_advancedDraft??=_session.ReadPlayerAdvanced();
 void ClearPlayerAdvancedState(){_advancedDraft=null;_advancedEdits.Clear();_advancedPending=false;_advancedConsent=null;}
 static string AdvancedLabel(string key)
 {
  string? textKey=key switch
  {
   "CachedPlayerCommunityTeam"=>"ui.player.cached_team",
   "UseTeamShipSeeds"=>"player.untranslated_use_team_seeds",
   "UseTeamShipPalettes"=>"player.untranslated_use_team_palettes",
   "UseCommunityTeamPalettes"=>"player.untranslated_use_community_palettes",
   "NeverAllowShipPurchases"=>"player.untranslated_never_ship_purchases",
   "AllowOnlyCorvetteShipPurchases"=>"player.untranslated_only_corvette_purchases",
   "NeverAllowCorvettePurchases"=>"player.untranslated_never_corvette_purchases",
   "AllowSaveContextCorvetteTransfer"=>"player.untranslated_allow_corvette_transfer",
   "AllowSaveContextShipTransfer"=>"player.untranslated_allow_ship_transfer",
   "AllowSaveContextMultitoolTransfer"=>"player.untranslated_allow_multitool_transfer",
   "OnlyCorvettesSpawnWhenPlayerTeleports"=>"player.untranslated_only_corvettes_spawn",
   "OnlyCorvetteLauncherCanBeRepaired"=>"player.untranslated_corvette_launcher_repair",
   "BlockShipPurchasesUntilMilestoneWithSeedComplete"=>"player.untranslated_block_ship_milestone",
   "BlockShipRepairUntilMilestoneWithSeedComplete"=>"player.untranslated_block_repair_milestone",
   "WeaponSeed"=>"player.untranslated_weapon_seed",
   "ShipSeed"=>"player.untranslated_ship_seed",
   "ForcePlayerRace"=>"player.untranslated_force_race",
   "PersistentPOI"=>"player.untranslated_persistent_poi",
   "IntroSequencePOI"=>"player.untranslated_intro_poi",
   "StartWithIntroQuizID"=>"player.untranslated_intro_quiz",
   _=>null
  };
  return textKey!=null?UiStrings.Get(textKey):UiStrings.LocalizeText(Regex.Replace(key,"(?<=[a-z0-9])([A-Z])"," $1").Replace("POI","POI").Replace("ID","ID"));
 }
 TextBlock AdvancedText(string text,string style="Label")=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Style=(Style)FindResource(style)};
 Border AdvancedCard(string title,StackPanel body){body.Children.Insert(0,AdvancedText(title,"Heading"));return new Border{Style=(Style)FindResource("Card"),Child=body};}
 static ScrollViewer AdvancedScroll(UIElement content)=>new(){Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
 FrameworkElement BuildPlayerHotkeysPage()
 {
  var root=new StackPanel();
  root.Children.Add(AdvancedText(UiStrings.Get("ui.hotkeys.title"),"Heading"));
  root.Children.Add(AdvancedText(UiStrings.Get("ui.hotkeys.hint")));
  var bar=new WrapPanel();
  var export=new Button{Content=UiStrings.Get("ui.hotkeys.export")};var import=new Button{Content=UiStrings.Get("ui.hotkeys.import")};
  export.Click+=(_,_)=>{try{var dialog=new SaveFileDialog{Filter=UiStrings.Get("ui.player.hotkey_filter_save"),FileName=UiStrings.Get("ui.player.hotkey_filename")};if(dialog.ShowDialog(this)==true)HotActionsLogic.BuildExport(Advanced.Player).ExportToFile(dialog.FileName);}catch(Exception ex){Error(ex);}};
  import.Click+=(_,_)=>{try{var dialog=new OpenFileDialog{Filter=UiStrings.Get("player.untranslated_hotkey_open_filter")};if(dialog.ShowDialog(this)!=true)return;var candidate=Advanced.Player.DeepClone();int count=HotActionsLogic.ApplyImport(candidate,JsonObject.ImportFromFile(dialog.FileName));if(count==0)throw new ArgumentException(UiStrings.Get("player.untranslated_hotkey_empty"));Advanced.Player.Set("HotActions",candidate.GetArray("HotActions")!.DeepClone());_advancedPending=true;Status.Text=string.Format(UiStrings.Get("player.untranslated_hotkey_imported"),count);var rebuilt=BuildPlayerHotkeysPage();root.Children.Clear();root.Children.Add(rebuilt);}catch(Exception ex){Error(ex);}};
  bar.Children.Add(import);bar.Children.Add(export);root.Children.Add(bar);
  var columns=new WrapPanel();root.Children.Add(columns);
  string[] names=[UiStrings.Get("ui.hotkeys.onfoot"),UiStrings.Get("ui.hotkeys.inship"),UiStrings.Get("ui.hotkeys.in_exocraft")];
  for(int c=0;c<3;c++){
   int context=c;var body=new StackPanel();var card=AdvancedCard(names[c],body);card.Width=300;card.MaxWidth=390;columns.Children.Add(card);
   for(int s=0;s<10;s++){
    int slot=s;var existing=Advanced.Player.GetArray("HotActions");var slots=existing!=null&&context<existing.Length?existing.GetObject(context)?.GetArray("KeyActions"):null;
    JsonObject? current=slots!=null&&slot<slots.Length?slots.Get(slot) as JsonObject:null;
    var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
    var label=AdvancedText("Ctrl+"+slot);row.Children.Add(label);
    var combo=new ComboBox{MinWidth=140,MaxDropDownHeight=350};Grid.SetColumn(combo,1);row.Children.Add(combo);
    foreach(var action in HotActionsLogic.Actions)combo.Items.Add(new AdvancedAction(action.Name,HotkeyActionLabel(action)));
    var name=HotActionsLogic.GetActionName(current);if(HotActionsLogic.FindAction(name)==null)combo.Items.Add(new AdvancedAction(name,string.Format(UiStrings.Get("ui.hotkeys.unknown"),name)));
    combo.SelectedItem=combo.Items.Cast<AdvancedAction>().First(x=>x.Name==name);
    var edit=new Button{Content="…",ToolTip=UiStrings.Get("ui.hotkeys.edit_parameters"),Padding=new Thickness(8,3,8,3)};Grid.SetColumn(edit,2);row.Children.Add(edit);
    void Refresh()=>edit.IsEnabled=HotActionsLogic.FindAction((combo.SelectedItem as AdvancedAction)?.Name)?.ParameterKind is {} kind&&kind!=HotActionParameterKind.None;
    Refresh();
    combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is not AdvancedAction a)return;HotActionsLogic.SetSlot(HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(Advanced.Player,context)).GetObject(slot),a.Name);_advancedPending=true;Refresh();};
    edit.Click+=(_,_)=>{if(combo.SelectedItem is AdvancedAction a)EditAdvancedHotkey(context,slot,a.Name);};
    body.Children.Add(row);
   }
  }
  return AdvancedScroll(root);
 }
 sealed record AdvancedAction(string Name,string Label){public override string ToString()=>Label;}
 sealed record AdvancedChoice(string Value,string Label){public override string ToString()=>Label;}

 string HotkeyActionLabel(HotActionInfo action)
 {
  string? direct=UiStrings.GetOrNull("ui.hotkeys.action."+action.Name);
  if(direct!=null)return direct;
  return UiStrings.LocalizeText(HotActionsLogic.GetDisplayName(action));
 }

 void EditAdvancedHotkey(int context,int slot,string action)
 {
  var info=HotActionsLogic.FindAction(action);if(info==null)return;
  var value=HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(Advanced.Player,context)).GetObject(slot);
  var content=new StackPanel{Margin=new Thickness(16)};var dialog=new Window{Title=HotkeyActionLabel(info),Owner=this,Width=410,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Content=content};
  TextBox Field(string label,string text){content.Children.Add(AdvancedText(label));var box=new TextBox{Text=text};content.Children.Add(box);return box;}
  var kind=info.ParameterKind;var index=HotActionsLogic.GetInventoryIndex(value);
  TextBox? number=null,x=null,y=null,id=null;
  if(kind is HotActionParameterKind.Index or HotActionParameterKind.InventoryItem or HotActionParameterKind.WeaponMode or HotActionParameterKind.CreatureFood)number=Field(UiStrings.Get(kind==HotActionParameterKind.InventoryItem?"ui.hotkeys.inventory":kind==HotActionParameterKind.CreatureFood?"ui.hotkeys.amount":kind==HotActionParameterKind.Index?"ui.hotkeys.index":"player.untranslated_hotkey_weapon"),HotActionsLogic.GetNumber(value).ToString(CultureInfo.InvariantCulture));
  if(kind==HotActionParameterKind.InventoryItem){x=Field(UiStrings.Get("ui.hotkeys.slotx"),index.X.ToString());y=Field(UiStrings.Get("ui.hotkeys.sloty"),index.Y.ToString());}
  if(kind is HotActionParameterKind.EmoteId or HotActionParameterKind.CreatureFood)id=Field(UiStrings.Get(kind==HotActionParameterKind.EmoteId?"ui.hotkeys.emote":"ui.hotkeys.food"),HotActionsLogic.GetId(value));
  var error=AdvancedText("");content.Children.Add(error);
  var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};var ok=new Button{Content=UiStrings.Get("ui.hotkeys.apply"),IsDefault=true};var cancel=new Button{Content=UiStrings.Get("player.untranslated_cancel"),IsCancel=true};buttons.Children.Add(ok);buttons.Children.Add(cancel);content.Children.Add(buttons);
  ok.Click+=(_,_)=>{try{int Read(TextBox? box,int fallback)=>box==null?fallback:int.Parse(box.Text,NumberStyles.Integer,CultureInfo.InvariantCulture);int n=Read(number,0),ix=Read(x,-1),iy=Read(y,-1);PlayerAdvancedDraft.ValidateHotSlot(action,n,ix,iy);HotActionsLogic.SetSlot(value,action,string.IsNullOrWhiteSpace(id?.Text)?"^":id.Text.Trim(),n,ix,iy);_advancedPending=true;dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
  dialog.ShowDialog();
 }
 FrameworkElement BuildPlayerMultiplayerPage()
 {
  _advancedEdits.Clear();
  var root=new StackPanel();root.Children.Add(AdvancedText(UiStrings.Get("ui.hotkeys.multiplayer"),"Heading"));
  root.Children.Add(AdvancedText(UiStrings.Get("ui.hotkeys.warning")));
  var consent=new CheckBox{Content=UiStrings.Get("ui.hotkeys.consent"),Margin=new Thickness(6,8,6,12)};root.Children.Add(consent);_advancedConsent=consent;
  var groups=new WrapPanel{IsEnabled=false};root.Children.Add(groups);consent.Checked+=(_,_)=>groups.IsEnabled=true;consent.Unchecked+=(_,_)=>groups.IsEnabled=false;
  var season=Advanced.Season;if(season==null){root.Children.Add(AdvancedText(UiStrings.Get("ui.hotkeys.no_season")));consent.IsEnabled=false;return AdvancedScroll(root);}
  StackPanel Group(string title){var body=new StackPanel();var card=AdvancedCard(title,body);card.Width=350;groups.Children.Add(card);return body;}
  var team=Group(UiStrings.Get("ui.hotkeys.team_config"));var restrictions=Group(UiStrings.Get("ui.hotkeys.purchases"));var setup=Group(UiStrings.Get("ui.hotkeys.player_setup"));
  void Boolean(StackPanel parent,string key){bool original=season.Contains(key)&&season.Get(key) is bool b&&b;var box=new CheckBox{Content=AdvancedText(AdvancedLabel(key)),IsChecked=original,Margin=new Thickness(4,5,4,5)};parent.Children.Add(box);box.Click+=(_,_)=>_advancedPending=true;_advancedEdits.Add(d=>{if(box.IsChecked!=original)d.Season!.Set(key,box.IsChecked==true);});}
  void Enum(StackPanel parent,string key,string sub,string[] choices){var obj=season.GetObject(key);string original=obj?.GetString(sub)??"";parent.Children.Add(AdvancedText(AdvancedLabel(key)));string OptionLabel(string value)=>key=="CachedPlayerCommunityTeam"?UiStrings.Get("ui.player.team_"+value.ToLowerInvariant()):key=="ForcePlayerRace"?UiStrings.Get("ui.player.race_"+value.ToLowerInvariant()):UiStrings.LocalizeText(value);var options=choices.Concat(choices.Contains(original)?[]:new[]{original}).Distinct().Select(value=>new AdvancedChoice(value,OptionLabel(value))).ToArray();var combo=new ComboBox{ItemsSource=options,SelectedValuePath=nameof(AdvancedChoice.Value),SelectedValue=original,IsEnabled=obj!=null};parent.Children.Add(combo);combo.SelectionChanged+=(_,_)=>_advancedPending=true;_advancedEdits.Add(d=>{if(combo.SelectedItem is AdvancedChoice selected&&selected.Value!=original)d.Season!.GetObject(key)!.Set(sub,selected.Value);});}

  void Seed(StackPanel parent,string key,int teamIndex=-1){JsonArray? Pair(JsonObject data)=>teamIndex<0?data.GetArray(key):data.GetArray(key)?.GetArray(teamIndex);var pair=Pair(season);string label=teamIndex<0?AdvancedLabel(key):string.Format(UiStrings.Get("ui.hotkeys.team_seed"),teamIndex+1);parent.Children.Add(AdvancedText(label));bool original=pair!=null&&pair.Length>0&&pair.Get(0) is bool b&&b;string old=pair!=null&&pair.Length>1?pair.Get(1)?.ToString()??"":"";var area=new StackPanel{IsEnabled=pair!=null&&pair.Length>=2};var active=new CheckBox{Content=UiStrings.Get("ui.hotkeys.active"),IsChecked=original,Margin=new Thickness(4)};var text=new TextBox{Text=old};var generate=new Button{Content=string.Format(UiStrings.Get("ui.hotkeys.generate"),label),HorizontalAlignment=HorizontalAlignment.Left};area.Children.Add(active);area.Children.Add(text);area.Children.Add(generate);parent.Children.Add(area);active.Click+=(_,_)=>_advancedPending=true;text.TextChanged+=(_,_)=>_advancedPending=true;generate.Click+=(_,_)=>text.Text=PlayerAdvancedDraft.GenerateSeed();_advancedEdits.Add(d=>{if(active.IsChecked!=original||text.Text!=old)PlayerAdvancedDraft.SetSeed(Pair(d.Season!)!,active.IsChecked==true,text.Text);});}
  Enum(team,"CachedPlayerCommunityTeam","CommunityTeam",PlayerAdvancedDraft.TeamValues);
  foreach(string key in PlayerAdvancedDraft.BooleanFields)Boolean(key.StartsWith("Use")?team:restrictions,key);
  for(int i=0;i<3;i++)Seed(team,"TeamShipSeeds",i);
  foreach(string key in PlayerAdvancedDraft.SeedFields)Seed(key.StartsWith("Block")?restrictions:setup,key);
  Enum(setup,"ForcePlayerRace","AlienRace",PlayerAdvancedDraft.RaceValues);
  foreach(string key in new[]{"PersistentPOI","IntroSequencePOI","StartWithIntroQuizID"}){string original=season.GetString(key)??"";setup.Children.Add(AdvancedText(AdvancedLabel(key)));var box=new TextBox{Text=original};setup.Children.Add(box);box.TextChanged+=(_,_)=>_advancedPending=true;_advancedEdits.Add(d=>{if(box.Text!=original)d.Season!.Set(key,box.Text);});}
  return AdvancedScroll(root);
 }
 bool CommitPlayerAdvancedPages()
 {
  if(_advancedDraft==null)return true;
  try{
   // Validate all pending controls on a detached candidate before applying any change.
   var candidate=new PlayerAdvancedDraft(Advanced.Player,Advanced.Season);
   foreach(var edit in _advancedEdits)edit(candidate);
   if(candidate.Season?.ToString()!=Advanced.Season?.ToString()&&_advancedConsent?.IsChecked!=true)throw new InvalidOperationException("Enable experimental multiplayer editing before applying these changes.");
   if(candidate.Season!=null&&Advanced.Season!=null)foreach(string key in candidate.Season.Names())Advanced.Season.Set(key,candidate.Season.Get(key));
   _session.ApplyPlayerAdvanced(Advanced);_advancedDraft=_session.ReadPlayerAdvanced();_advancedPending=false;
   return true;
  }catch(Exception ex){Error(ex);return false;}
 }
}

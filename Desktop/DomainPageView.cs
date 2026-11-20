using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NMSE.Models;
using NMSE.Next.Engine;
using NMSE.Data;
namespace NMSE.Next.Desktop;

internal sealed class RecordEntry(object parent,object key,int index, bool isVehicle = false, bool isSong = false, bool isFrigate = false, bool isBase = false, JsonObject? playerState = null, string? fallbackTitle = null, bool isCompanion = false):INotifyPropertyChanged
{
 internal object Parent=parent,Key=key;internal int Index=index;
 internal object? DirectValue;
 internal object? Value=>DirectValue??SaveSession.ReadField(Parent,Key);
 internal bool IsBase => isBase;
 public string Title => isSong && Value is JsonObject song ? (string.IsNullOrWhiteSpace(song.GetString("Name")) ? UiStrings.Format("domain.song_fallback", Index + 1) : song.GetString("Name")!) : isCompanion ? SectionCatalog.CompanionCaption(Value, Index) : isVehicle ? SectionCatalog.VehicleCaption(Value, Index) : isFrigate && Value is JsonObject frigate ? SectionCatalog.FrigateCaption(frigate, Index) : isBase && Value is JsonObject baseData ? SectionCatalog.BaseCaption(playerState, baseData, Index) : fallbackTitle ?? SectionCatalog.Caption(Value, Index);
 public event PropertyChangedEventHandler? PropertyChanged;
 internal void Refresh()=>PropertyChanged?.Invoke(this,new(nameof(Title)));
 public override string ToString()=>Title;
}
internal sealed partial class DomainPageView:Grid
{
 readonly MainWindow _owner;readonly SaveSession _session;readonly int _page;readonly SectionSpec[] _specs;
 readonly Grid _recordGrid=new();readonly ContentControl _catalogueView=new();
 readonly WrapPanel _tabs=new();readonly ListBox _list=new();readonly TextBox _search=new();readonly TextBlock _count=new();
 readonly StackPanel _form=new();readonly TextBlock _recordTitle=new();readonly Button _back=new();



 readonly List<(FieldEdit Target,Func<string> Read)> _fields=[];
 readonly Stack<(object? Value,object? Parent,object? Key,string Title,string[] Path)> _history=[];
 readonly List<RecordEntry> _entries=[];readonly ScrollViewer _details;
 readonly List<Func<bool>> _draftValidators=[];
 SectionSpec _section;RecordEntry? _entry;object? _node,_nodeParent,_nodeKey;JsonObject? _ship;
 int _selectedBaseNpcWorkerIndex;
 string[] _currentJsonSegments=[];
 CheckBox? _unlockAllCompanionSlots;
 bool _binding;string _query="";
 internal int FieldCount=>_fields.Count;
 internal int RecordCount=>_entries.Count;
 internal int SectionCount=>_specs.Length;
 internal bool HasPending=>_fields.Any(f=>f.Read()!=f.Target.Baseline)||VisitedStationDraftPending||FreighterDetailsPending||FreighterInventoryPending;
 Style PageStyle(string name)=>(Style)Application.Current.FindResource(name);
 public DomainPageView(MainWindow owner,SaveSession session,int page)
 {
  _owner=owner;_session=session;_page=page;_specs=SectionCatalog.Get(page);
  _section=page==MainPageCatalog.Fleet?_specs.FirstOrDefault(spec=>spec.Path=="CurrentFreighter")??_specs[0]:_specs[0];
  RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
  var heading=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,2,4,6)};heading.Children.Add(ConceptIcons.Make(owner.PageName(page),25));heading.Children.Add(new TextBlock{Text=owner.PageName(page),Style=PageStyle("Heading"),Margin=new Thickness(9,0,0,0)});Children.Add(heading);
  foreach(var spec in _specs)
  {
   var button=new Button{Content=SectionCatalog.DisplayName(spec),Tag=spec,Padding=new Thickness(10,6,10,6)};
   button.Click+=(_,_)=>{if(Commit()){_section=spec;ShowSection();}};_tabs.Children.Add(button);
  }
  var tabScroll=new ScrollViewer{Content=_tabs,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=108};SetRow(tabScroll,1);Children.Add(tabScroll);
  var main=_recordGrid;main.ColumnDefinitions.Add(new(){Width=new GridLength(215)});main.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});SetRow(main,2);Children.Add(main);SetRow(_catalogueView,2);Children.Add(_catalogueView);
  var left=new Grid();left.RowDefinitions.Add(new(){Height=GridLength.Auto});left.RowDefinitions.Add(new(){Height=GridLength.Auto});left.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
  _search.ToolTip=UiStrings.LocalizeText("Filter records");_search.TextChanged+=(_,_)=>{if(!_binding)Filter();};
  var searchRow=new DockPanel();var sectionJson=owner.JsonRedirectButton("↗ JSON","Open this section in Raw JSON Editor",()=>owner.NavigateToRawJsonPath(owner.RawPathForSection(_section)));DockPanel.SetDock(sectionJson,Dock.Right);searchRow.Children.Add(sectionJson);searchRow.Children.Add(_search);left.Children.Add(searchRow);
  _count.Margin=new Thickness(5,6,5,8);SetRow(_count,1);left.Children.Add(_count);
  _list.DisplayMemberPath="Title";_list.SetResourceReference(Control.BackgroundProperty,"Input");_list.SetResourceReference(Control.ForegroundProperty,"Text");_list.SetResourceReference(Control.BorderBrushProperty,"Edge");
  VirtualizingPanel.SetIsVirtualizing(_list,true);VirtualizingPanel.SetVirtualizationMode(_list,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(_list,true);
  _list.SelectionChanged+=(_,_)=>{if(_binding)return;if(!Commit()){_binding=true;_list.SelectedItem=_entry;_binding=false;return;}if(_list.SelectedItem is RecordEntry item)Select(item);};SetRow(_list,2);left.Children.Add(_list);
  var leftCard=new Border{Style=PageStyle("Card"),Padding=new Thickness(8),Child=left};main.Children.Add(leftCard);
  var right=new Grid();right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var banner=new DockPanel();_back.Content=UiStrings.LocalizeText("‹ Back");_back.Click+=(_,_)=>{if(!Commit()||_history.Count==0)return;var prior=_history.Pop();_currentJsonSegments=prior.Path;ShowNode(prior.Value,prior.Parent,prior.Key,prior.Title);};DockPanel.SetDock(_back,Dock.Left);banner.Children.Add(_back);
  var currentJson=owner.JsonRedirectButton("↗ JSON","Open the selected record or field in Raw JSON Editor",()=>owner.NavigateToRawJsonPath(_currentJsonSegments));DockPanel.SetDock(currentJson,Dock.Right);banner.Children.Add(currentJson);
  _recordTitle.FontSize=17;_recordTitle.FontWeight=FontWeights.SemiBold;_recordTitle.TextTrimming=TextTrimming.CharacterEllipsis;_recordTitle.Margin=new Thickness(7,2,2,2);banner.Children.Add(_recordTitle);right.Children.Add(banner);



  _details=new ScrollViewer{Content=_form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};SetRow(_details,2);right.Children.Add(_details);
  var actions=new WrapPanel();var apply=new Button{Content="✓ Apply changes",Style=PageStyle("Primary")};apply.Click+=(_,_)=>{if(Commit()){_owner.PageStatus("Changes applied in memory. Press Save to keep them.");if(_section.Path=="PetBattleTeam")RefreshBattle();}};actions.Children.Add(apply);var hint=new TextBlock{Text="Edits apply to existing fields only.",Margin=new Thickness(10,0,0,0),FontSize=11};hint.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");actions.Children.Add(hint);SetRow(actions,3);right.Children.Add(actions);
  var rightCard=new Border{Style=PageStyle("Card"),Child=right};SetColumn(rightCard,1);main.Children.Add(rightCard);ShowSection();
 }
 void ShowSection()
 {
  foreach(Button button in _tabs.Children){bool selected=Equals(button.Tag,_section);button.SetResourceReference(Control.BackgroundProperty,selected?"AccentSurface":"Surface");button.SetResourceReference(Control.ForegroundProperty,selected?"OnAccent":"Text");}
  _unlockAllCompanionSlots=null;_binding=true;_entries.Clear();_fields.Clear();_visitedStationDrafts.Clear();_draftValidators.Clear();_entry=null;_history.Clear();_ship=null;_selectedBaseNpcWorkerIndex=0;
  bool words=_section.Path=="KnownWordGroups";
  bool records=_section.Path is "KnownPortalRunes" or "TeleportEndpoints" or "FishingRecord" or "KnownRefinerRecipes";
  bool progress=SaveSession.IsCatalogueProgress(_section.Path);
  bool stationConstruction=_page==MainPageCatalog.SpaceStation && _section.Name=="Construction";
  bool stationVisited=_page==MainPageCatalog.SpaceStation && _section.Name=="Visited Stations";
  bool freighterDetails=_page==MainPageCatalog.Fleet && _section.Path=="CurrentFreighter";
  bool fleetFrigates=_page==MainPageCatalog.Fleet && _section.Path=="FleetFrigates";
  bool squadronPilots=_page==MainPageCatalog.Fleet && _section.Path=="SquadronPilots";
  bool catalogue=(words || records || progress || SaveSession.IsKnownCatalogue(_section.Path)) && _session.Player!=null && !stationConstruction;
  _recordGrid.Visibility=catalogue||stationConstruction||stationVisited||freighterDetails||fleetFrigates||squadronPilots?Visibility.Collapsed:Visibility.Visible;
  _catalogueView.Visibility=catalogue||stationConstruction||stationVisited||freighterDetails||fleetFrigates||squadronPilots?Visibility.Visible:Visibility.Collapsed;
  _catalogueView.Content=freighterDetails?BuildFreighterPage():fleetFrigates?BuildFrigatesPage():squadronPilots?BuildSquadronPage():stationConstruction?BuildStationConstruction():stationVisited?BuildVisitedStationsPage():catalogue?(words?BuildKnownWords():records?BuildCatalogueRecords(_section.Path):progress?BuildCatalogueProgress(_section.Path):BuildKnownCatalogue(_section.Path)):null;
  if(catalogue||stationConstruction||stationVisited||freighterDetails||fleetFrigates||squadronPilots){_currentJsonSegments=_owner.RawPathForSection(_section);_binding=false;return;}
  object? root=SectionCatalog.Resolve(_session.Data,_session.AccountData,_section);
  _currentJsonSegments=_owner.RawPathForSection(_section);
  if((_section.Path is "PetAccessoryCustomisation" or "PetBattleTeam") && Pets is {} pets)
  {
   for(int i=0;i<pets.Length;i++)
    if(pets.GetObject(i)?.GetString("CreatureID")?.TrimStart('^') is { Length: > 0 })
     _entries.Add(new(pets,i,i,isCompanion:true));
  }
  else if(root is JsonArray array)
  {
   bool baseRecords=_section.Path=="PersistentPlayerBases" && (_page==MainPageCatalog.Bases || _page==MainPageCatalog.Construction || (_page==MainPageCatalog.SpaceStation && _section.Name=="Owned Stations"));
   HashSet<int>? visibleBaseIndices=baseRecords
    ? SectionCatalog.BaseRecordIndices(array,_page==MainPageCatalog.Construction || _page==MainPageCatalog.SpaceStation).ToHashSet()
    : null;
   for(int i=0;i<array.Length;i++)
   {
    if(visibleBaseIndices is not null && !visibleBaseIndices.Contains(i))continue;
    _entries.Add(new(array, i, i,
     isVehicle:_section.Path=="VehicleOwnership", isSong:_page==MainPageCatalog.ByteBeats && _section.Name=="My songs", isCompanion:_section.Path is "Pets" or "Eggs",
     isFrigate:_page==MainPageCatalog.Fleet && _section.Path=="FleetFrigates", isBase:baseRecords,
     playerState:_session.Player));
   }
  }
  else if(root is JsonObject obj)
  {
   string? fallbackTitle=_page==MainPageCatalog.Fleet
    ? _section.Path is "FreighterInventory" or "FreighterInventory_TechOnly"
      ? SectionCatalog.FreighterInventoryCaption(_session.Player)
      : _section.Path=="CurrentFreighter"
       ? SectionCatalog.FreighterCaption(_session.Player,obj)
       : null
    : null;
   _entries.Add(new(obj,"",0,fallbackTitle:fallbackTitle){DirectValue=obj});
  }
  _search.Text="";_query="";_binding=false;Filter();
  if(_entries.Count==0)
  {
   _form.Children.Clear();_recordTitle.Text=_section.Name;_back.Visibility=Visibility.Collapsed;
   _form.Children.Add(new TextBlock{Text=UiStrings.LocalizeText(_section.Root=="Account"?_session.AccountLoadMessage??"No records in this account section.":"No records in this section of the loaded save."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6,18,6,18)});
  }
 }
 void Filter()
 {
  if(!Commit()){_binding=true;_search.Text=_query;_binding=false;return;}
  _query=_search.Text;var filtered=_entries.Where(x=>x.Title.Contains(_query,StringComparison.OrdinalIgnoreCase)).ToArray();_binding=true;_list.ItemsSource=filtered;_list.SelectedItem=filtered.FirstOrDefault();_binding=false;_count.Text=UiStrings.Format("domain.records_count",filtered.Length.ToString("N0"),_entries.Count.ToString("N0"));
  if(filtered.FirstOrDefault() is {} first)Select(first);
 }
 void Select(RecordEntry entry)
 {
  _entry=entry;_history.Clear();object? value=entry.Value;
  _currentJsonSegments=_owner.RawPathForSection(_section);
  if(entry.DirectValue==null&&entry.Parent is JsonArray)_currentJsonSegments=_currentJsonSegments.Append($"[{entry.Index}]").ToArray();
  _ship=_page==3?value as JsonObject:null;ShowNode(value,entry.DirectValue!=null?null:entry.Parent,entry.Key,entry.Title);
 }
 void Open(object? value,object? parent,object? key,string title)
 {
  if(!Commit())return;_history.Push((_node,_nodeParent,_nodeKey,_recordTitle.Text,_currentJsonSegments));
  string segment=key is int index?$"[{index}]":Convert.ToString(key,System.Globalization.CultureInfo.InvariantCulture)??"";
  _currentJsonSegments=_currentJsonSegments.Append(segment).ToArray();ShowNode(value,parent,key,title);
 }
 void ShowNode(object? value,object? parent,object? key,string title)
 {
  _node=value;_nodeParent=parent;_nodeKey=key;_fields.Clear();_draftValidators.Clear();_form.Children.Clear();_recordTitle.Text=title;_back.Visibility=_history.Count>0?Visibility.Visible:Visibility.Collapsed;_details.ScrollToTop();


  if(_section.Path=="Pets"&&value is JsonObject petRecord)
  {
   if(_section.Name=="Descriptors") ShowCompanionDescriptors(petRecord);
   else ShowCompanionStats(petRecord);
   return;
  }
  if(_section.Path == "PetAccessoryCustomisation" && _entry is {} companionEntry)
  {
   ShowCompanionAccessories(value as JsonObject, companionEntry.Index);
   return;
  }
  if(_section.Path == "PetBattleTeam" && value is JsonObject pet)
  {
   ShowBattle(pet);
   return;
  }
  if(_section.Path=="UnlockedPetSlots" && parent is JsonArray slots)
   AddUnlockAllCompanionSlots(slots);
  if(value is JsonObject obj)
  {
   if(obj.GetArray("Slots")!=null && obj.Get("Width")!=null){AddInventory(obj);return;}
   var names=obj.Names().OrderBy(n=>{int at=Array.IndexOf(_section.PreferredFields,n);return at<0?1000:at;}).ToArray();
   var card=new StackPanel();int count=0;
   if(_page==MainPageCatalog.Bases && _section.Path=="NPCWorkers")
   {
    var locationRow=new Grid{Margin=new Thickness(0,1,0,1)};
    locationRow.ColumnDefinitions.Add(new(){Width=new GridLength(165)});
    locationRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
    locationRow.Children.Add(new TextBlock{Text=UiStrings.LocalizeText("Assigned Base"),Style=PageStyle("Label"),TextWrapping=TextWrapping.Wrap});
    var location=new TextBox{Text=SectionCatalog.BaseWorkerLocationCaption(_session.Player,obj),IsReadOnly=true,MinWidth=160,ToolTip=UiStrings.LocalizeText("Resolved from this worker's saved base location")};
    SetColumn(location,1);locationRow.Children.Add(location);card.Children.Add(locationRow);
   }
   bool isBaseRecord=_entry is { IsBase:true } && (_page==MainPageCatalog.Bases || _page==MainPageCatalog.Construction);
   bool isByteBeatSong=_page==MainPageCatalog.ByteBeats&&_section.Name=="My songs";
   var baseNestedFields=isBaseRecord?new StackPanel():null;
   foreach(string name in names)
   {
    if(isByteBeatSong&&(name.Equals("Id",StringComparison.OrdinalIgnoreCase)
       || name.Equals("RequiredSpecialId",StringComparison.OrdinalIgnoreCase)
       || name.Equals("LocID",StringComparison.OrdinalIgnoreCase)))continue;
    if(_page==MainPageCatalog.Bases&&_section.Path=="NPCWorkers"
       && (name.Equals("ResourceElement",StringComparison.OrdinalIgnoreCase)
        || name.Equals("InteractionSeed",StringComparison.OrdinalIgnoreCase)
        || name.Equals("BaseOffset",StringComparison.OrdinalIgnoreCase)))continue;
    if(isBaseRecord && (name.Equals("Objects",StringComparison.OrdinalIgnoreCase)
       || name.Equals("Position",StringComparison.OrdinalIgnoreCase)
       || name.Equals("Forward",StringComparison.OrdinalIgnoreCase)
       || name.Contains("Screenshot",StringComparison.OrdinalIgnoreCase)))continue;
    object? field=obj.Get(name);
    if(isBaseRecord && field is JsonObject nestedBaseFields)
    {
     AddBaseNestedFields(baseNestedFields!,name,nestedBaseFields);
     continue;
    }
    if(field is JsonObject or JsonArray)
    {
     Panel? nestedHost=isByteBeatSong&&name.Equals("Data",StringComparison.OrdinalIgnoreCase)?card:baseNestedFields;
     AddNested(name,field,obj,name,nestedHost);
     continue;
    }
    AddField(card,obj,name,name,field);count++;
    if(_page==MainPageCatalog.Bases && _entry is { IsBase:true }
       && name.Equals("GalacticAddress",StringComparison.OrdinalIgnoreCase))
     card.Children.Add(BuildBaseActionButtons(obj));
   }
   if(baseNestedFields is { Children.Count: > 0 })
    card.Children.Add(baseNestedFields);
   if(count>0||baseNestedFields is { Children.Count: > 0 })_form.Children.Insert(0,new Border{Style=PageStyle("Card"),Padding=new Thickness(9),Child=card});
   if(_page==MainPageCatalog.Bases && _entry is { IsBase:true } baseEntry && ReferenceEquals(baseEntry.Value,obj))
    _form.Children.Insert(0,BuildBaseNpcCard(obj));
  }
  else if(value is JsonArray array)
  {
   var list=new ListBox{DisplayMemberPath="Title",MinHeight=120,MaxHeight=420};list.SetResourceReference(Control.BackgroundProperty,"Input");list.SetResourceReference(Control.ForegroundProperty,"Text");VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
   bool songArray=_page==MainPageCatalog.ByteBeats&&(_section.Name=="My songs"||(_section.Name=="ByteBeat library"&&Equals(_nodeKey,"MySongs")));var entries=Enumerable.Range(0,array.Length).Select(i=>new RecordEntry(array,i,i,false,songArray)).ToArray();list.ItemsSource=entries;
   list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is RecordEntry item)Open(item.Value,array,item.Key,item.Title);};_form.Children.Add(new TextBlock{Text=UiStrings.Format("domain.array_entries_hint",array.Length.ToString("N0")),Margin=new Thickness(5,10,5,10)});_form.Children.Add(list);
  }
  else if(parent is JsonObject or JsonArray){var card=new StackPanel();AddField(card,parent,key!,title,value);_form.Children.Add(new Border{Style=PageStyle("Card"),Child=card});}
 }
 void AddUnlockAllCompanionSlots(JsonArray slots)
 {
  var checkbox=new CheckBox { Content=UiStrings.Get("common.unlock_all"), Margin=new Thickness(7) };
  _unlockAllCompanionSlots=checkbox;
  UpdateUnlockAllCompanionSlots(slots);
  checkbox.Click+=(_,_) =>
  {
   try
   {
    var lockedSlots=Enumerable.Range(0,slots.Length)
     .Where(index=>slots.Get(index) is false)
     .Select(index=>new FieldEdit(slots,index,"true","false"));
    _session.ApplyFields(lockedSlots);
    foreach(var entry in _entries)entry.Refresh();
    if(_entry is {} selected)Select(selected);
    _owner.PageStatus("All companion slots unlocked in memory. Press Save to keep the changes.");
   }
   catch(Exception ex)
   {
    UpdateUnlockAllCompanionSlots(slots);
    _owner.PageError(ex);
   }
  };
  _form.Children.Add(new Border { Style=PageStyle("Card"), Child=checkbox });
 }
 void UpdateUnlockAllCompanionSlots(JsonArray slots)
 {
  if(_unlockAllCompanionSlots is not {} checkbox)return;
  bool allUnlocked=!Enumerable.Range(0,slots.Length).Any(index=>slots.Get(index) is false);
  checkbox.IsChecked=allUnlocked;
  checkbox.IsEnabled=!allUnlocked;
 }
 void AddBaseNestedFields(StackPanel host,string groupName,JsonObject nested)
 {
  var leaves=new List<(JsonObject Parent,string Key,string Path,object? Value)>();
  void Collect(JsonObject current,string prefix)
  {
   foreach(string key in current.Names())
   {
    object? value=current.Get(key);
    string path=string.IsNullOrEmpty(prefix)?Readable(key):prefix+" · "+Readable(key);
    if(value is JsonObject child)Collect(child,path);
    else if(value is JsonArray array)AddNested(path,array,current,key,host);
    else leaves.Add((current,key,path,value));
   }
  }
  Collect(nested,"");
  foreach(var leaf in leaves)
  {
   string label=leaves.Count==1?Readable(groupName):Readable(groupName)+" · "+leaf.Path;
   AddField(host,leaf.Parent,leaf.Key,label,leaf.Value);
  }
 }
 void AddNested(string name,object value,object parent,object key,Panel? host=null)
 {
  var card=new DockPanel();int count=value is JsonArray a?a.Length:((JsonObject)value).Length;
  var jsonPath=_currentJsonSegments.Append(Convert.ToString(key,System.Globalization.CultureInfo.InvariantCulture)??name).ToArray();
  var json=_owner.JsonRedirectButton("↗ JSON","Open this field in Raw JSON Editor",()=>_owner.NavigateToRawJsonPath(jsonPath));DockPanel.SetDock(json,Dock.Right);card.Children.Add(json);
  var open=new Button{Content=UiStrings.Format("domain.open_count",count.ToString("N0")),MinWidth=100};DockPanel.SetDock(open,Dock.Right);open.Click+=(_,_)=>Open(value,parent,key,Readable(name));card.Children.Add(open);card.Children.Add(new TextBlock{Text=UiStrings.LocalizeText(Readable(name)),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
  var nestedCard=new Border{Style=PageStyle("Card"),Padding=new Thickness(7),Child=card};
  (host??_form).Children.Add(nestedCard);
 }
 void AddField(Panel panel,object parent,object key,string label,object? value)
 {
  var row=new Grid{Margin=new Thickness(0,1,0,1)};row.ColumnDefinitions.Add(new(){Width=new GridLength(165)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.Children.Add(new TextBlock{Text=UiStrings.LocalizeText(Readable(label)),Style=PageStyle("Label"),TextWrapping=TextWrapping.Wrap});
  string baseline=SaveSession.FieldText(value);Func<string> read;FrameworkElement editor;
  if(value is bool b){var box=new CheckBox{IsChecked=b,Margin=new Thickness(7),VerticalAlignment=VerticalAlignment.Center};editor=box;read=()=>box.IsChecked==true?"true":"false";}
  else if(value is not (string or RawDouble or int or long or uint or ulong or double or float or decimal) && value!=null){editor=new TextBlock{Text=value.GetType().Name+" data",Margin=new Thickness(7)};read=()=>baseline;}
  else if(key is string property && property=="InventoryClass" && value is string)
  {var box=new ComboBox{ItemsSource=new[]{"C","B","A","S"},SelectedItem=baseline};editor=box;read=()=>box.SelectedItem as string??baseline;}
  else{var box=new TextBox{Text=baseline,MinWidth=160,ToolTip=label};editor=box;read=()=>box.Text;}
  SetColumn(editor,1);row.Children.Add(editor);panel.Children.Add(row);_fields.Add((new FieldEdit(parent,key,baseline,baseline),read));
 }
 (JsonObject Inventory, int X, int Y)? _selectedInventorySlot;
 void AddInventory(JsonObject inventory)
 {
  var (items,width,height)=_owner.MakeSlotModels(inventory);var tiles=new UniformGrid{Columns=width,MinWidth=540,VerticalAlignment=VerticalAlignment.Top};
  bool technology=(_nodeKey as string ?? _section.Path).Contains("TechOnly",StringComparison.Ordinal);
  var kind=_page switch
  {
   3 => new InventoryKind(SaveSession.ShipInventoryOwner(_ship),technology?"Ship":"ShipCargo",technology,true),
   4 => new InventoryKind("Freighter",technology?"Freighter":"FreighterCargo",technology,true),
   5 => new InventoryKind(SaveSession.VehicleInventoryOwner(inventory),"Vehicle", technology, technology),
   _ => new InventoryKind("Storage","Chest",false,false)
  };
  var inspector=new StackPanel { Width=230 };
  var details=new StackPanel();
  details.Children.Add(new TextBlock {Text="Slot Details",Style=PageStyle("Heading"),FontSize=17});
  var icon=new Image {Height=62,Visibility=Visibility.Collapsed};
  var name=new TextBlock {Text="Select an inventory slot",TextWrapping=TextWrapping.Wrap};
  var edit=new Button {Content="Edit slot details",IsEnabled=false};
  details.Children.Add(icon);details.Children.Add(name);details.Children.Add(edit);
  inspector.Children.Add(new Border {Style=PageStyle("Card"),Child=details});
  var picker=new ContentControl();inspector.Children.Add(picker);
  SlotView? selected=null;
  void Refresh()
  {
   double offset=_details.VerticalOffset;
   Repaint();
   _details.ScrollToVerticalOffset(offset);
  }
  void Select(SlotView? item)
  {
   selected=item;
   _selectedInventorySlot=item==null?null:(inventory,item.X,item.Y);
   name.Text=item?.Name??"Select an inventory slot";
   icon.Source=item?.Icon;icon.Visibility=icon.Source==null?Visibility.Collapsed:Visibility.Visible;
   edit.IsEnabled=item?.Data!=null;
   picker.Content=_owner.BuildItemPicker(item,kind,Commit,Refresh);
  }
  edit.Click+=(_,_)=>{if(selected?.Data is {} data)Open(data,null,null,selected.Name);};
  foreach(var item in items)
  {
   var body=new StackPanel{Height=70};var text=new TextBlock{Text=item.Name,Width=51,FontSize=9,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=item.LabelForeground};body.Children.Add(new Border{Child=text,Background=item.LabelBackground,CornerRadius=new CornerRadius(4),Padding=new Thickness(2)});body.Children.Add(new Image{Source=item.Icon,Height=38,Margin=new Thickness(2)});body.Children.Add(new TextBlock{Text=item.AmountLabel,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center});
   var tile=new Button{Content=body,Padding=new Thickness(0),Margin=new Thickness(2),Background=item.Background,BorderBrush=item.Border,ToolTip=item.Name};tile.Click+=(_,_)=>Select(item);
   _owner.WireInventoryTile(tile,item,kind,Commit,Refresh,()=>Select(item),()=>((Border)picker.Content).BringIntoView());tiles.Children.Add(tile);
  }
    Select(_selectedInventorySlot is {} previous && ReferenceEquals(previous.Inventory,inventory)
   ? items.FirstOrDefault(cell=>cell.X==previous.X && cell.Y==previous.Y) : null);
  var layout=new DockPanel();
  DockPanel.SetDock(inspector,Dock.Right);layout.Children.Add(inspector);
  layout.Children.Add(new ScrollViewer {Content=tiles,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
  _form.Children.Add(new TextBlock{Text=UiStrings.Format("domain.slots_count",width,height),Margin=new Thickness(5,9,5,9)});_form.Children.Add(layout);
  var settings=new Button{Content="Inventory settings and base stats",HorizontalAlignment=HorizontalAlignment.Left};settings.Click+=(_,_)=>
  {
   if(!Commit())return;
   _history.Push((_node,_nodeParent,_nodeKey,_recordTitle.Text,_currentJsonSegments));
   _node=inventory;_fields.Clear();_form.Children.Clear();
   var fields=new StackPanel();
   bool showStorageSizeLimits=_page==MainPageCatalog.Bases&&_section.Path is not ("PersistentPlayerBases" or "NPCWorkers");
   bool classAdded=false;
   var hiddenStorageFields=new HashSet<string>(StringComparer.OrdinalIgnoreCase)
   {"IsCool","NumSlotsFromTech","ValidSlotIndices","StackSizeGroup","BaseStatValues","SpecialSlots"};
   foreach(string n in inventory.Names())
   {
    if(n=="Slots")continue;
    if(showStorageSizeLimits&&hiddenStorageFields.Contains(n))
    {
     if(n.Equals("NumSlotsFromTech",StringComparison.OrdinalIgnoreCase)&&inventory.GetObject("Class") is {} classData)
     {
      AddField(fields,classData,"InventoryClass","Class",classData.Get("InventoryClass"));
      classAdded=true;
     }
     continue;
    }
    var v=inventory.Get(n);
    if(showStorageSizeLimits&&n.Equals("Class",StringComparison.OrdinalIgnoreCase))continue;
    if(v is JsonObject or JsonArray)AddNested(n,v,inventory,n);
    else
    {
     string label=showStorageSizeLimits&&n.Equals("Width",StringComparison.OrdinalIgnoreCase)?UiStrings.LocalizeText("Width (max=20)"):showStorageSizeLimits&&n.Equals("Height",StringComparison.OrdinalIgnoreCase)?UiStrings.LocalizeText("Height (max=20)"):n;
     AddField(fields,inventory,n,label,v);
    }
   }
   if(showStorageSizeLimits&&!classAdded&&inventory.GetObject("Class") is {} fallbackClass)
    AddField(fields,fallbackClass,"InventoryClass","Class",fallbackClass.Get("InventoryClass"));
   _form.Children.Insert(0,new Border{Style=PageStyle("Card"),Child=fields});
   _back.Visibility=Visibility.Visible;
  };
  _form.Children.Insert(0,settings);
 }
 Border BuildBaseNpcCard(JsonObject baseRecord)
 {
  var card=new StackPanel();
  card.Children.Add(new TextBlock{Text=UiStrings.LocalizeText("Base NPC"),Style=PageStyle("Heading"),FontSize=16,Margin=new Thickness(3,2,3,7)});
  var workers=BaseNpcOperations.GetHiredWorkers(_session.Player);
  if(workers.Count==0)
  {
   card.Children.Add(new TextBlock{Text=UiStrings.LocalizeText("No hired base workers found in this save."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
   card.Children.Add(BuildBaseNpcSummonButton(baseRecord));
   return new Border{Style=PageStyle("Card"),Padding=new Thickness(9),Child=card};
  }

  var selected=workers.FirstOrDefault(worker=>worker.Index==_selectedBaseNpcWorkerIndex)??workers[0];
  _selectedBaseNpcWorkerIndex=selected.Index;
  var workerPicker=new ComboBox{ItemsSource=workers,SelectedItem=selected,MinWidth=220,Margin=new Thickness(3,2,3,7)};
  card.Children.Add(workerPicker);

  var workerDetails=new StackPanel();
  card.Children.Add(workerDetails);
  var resource=selected.Data.GetObject("ResourceElement");
  object? rawFilename=resource?.Get("Filename");
  string filename=rawFilename as string??"";
  var raceCombo=new ComboBox{ItemsSource=BaseNpcOperations.Races,SelectedItem=BaseNpcOperations.Races.FirstOrDefault(race=>string.Equals(race.Filename,filename,StringComparison.OrdinalIgnoreCase)),MinWidth=220};
  var raceRow=new Grid{Margin=new Thickness(3,2,3,2)};
  raceRow.ColumnDefinitions.Add(new(){Width=new GridLength(130)});
  raceRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  raceRow.Children.Add(new TextBlock{Text="Race",Style=PageStyle("Label"),VerticalAlignment=VerticalAlignment.Center});
  SetColumn(raceCombo,1);raceRow.Children.Add(raceCombo);workerDetails.Children.Add(raceRow);

  if(resource is not null && rawFilename is string currentFilename)
  {
   string raceBaseline=SaveSession.FieldText(currentFilename);
   _fields.Add((new FieldEdit(resource,"Filename",raceBaseline,raceBaseline),()=>raceCombo.SelectedItem is BaseNpcRace race?race.Filename:raceBaseline));
   if(!string.IsNullOrWhiteSpace(currentFilename)&&BaseNpcOperations.GetRaceName(currentFilename) is null)
    workerDetails.Children.Add(new TextBlock{Text=UiStrings.LocalizeText("Saved race is unrecognized; it will stay unchanged unless you choose a race."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
  }
  else
  {
   raceCombo.IsEnabled=false;
   workerDetails.Children.Add(new TextBlock{Text=UiStrings.LocalizeText("Race data is unavailable for this worker."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
  }

  var seedArray=resource?.GetArray("Seed");
  if(seedArray is not null&&seedArray.Length>1)
  {
   string seedBaseline=SaveSession.FieldText(seedArray.Get(1));
   var seedBox=new TextBox{Text=seedBaseline,MinWidth=180};
   var generate=new Button{Content="Generate",Margin=new Thickness(6,0,0,0)};
   var seedRow=new Grid{Margin=new Thickness(3,2,3,2)};
   seedRow.ColumnDefinitions.Add(new(){Width=new GridLength(130)});
   seedRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
   seedRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
   seedRow.Children.Add(new TextBlock{Text="Seed",Style=PageStyle("Label"),VerticalAlignment=VerticalAlignment.Center});
   SetColumn(seedBox,1);seedRow.Children.Add(seedBox);SetColumn(generate,2);seedRow.Children.Add(generate);workerDetails.Children.Add(seedRow);
   string? NormalizeSeed(string text)=>NMSE.Core.Utilities.SeedHelper.NormalizeSeed(text);
   _fields.Add((new FieldEdit(seedArray,1,seedBaseline,seedBaseline),()=>NormalizeSeed(seedBox.Text)??seedBox.Text.Trim()));
   _draftValidators.Add(()=>seedBox.Text==seedBaseline||NormalizeSeed(seedBox.Text)is not null);
   generate.Click+=(_,_)=>seedBox.Text="0x"+Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
  }
  else
   workerDetails.Children.Add(new TextBlock{Text=UiStrings.LocalizeText("Seed data is unavailable for this worker."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});

  card.Children.Add(BuildBaseNpcSummonButton(baseRecord));

  workerPicker.SelectionChanged+=(_,_) =>
  {
   if(workerPicker.SelectedItem is not BaseNpcWorker next||next.Index==_selectedBaseNpcWorkerIndex)return;
   int previous=_selectedBaseNpcWorkerIndex;
   if(!Commit())
   {
    _binding=true;workerPicker.SelectedItem=workers.FirstOrDefault(worker=>worker.Index==previous);_binding=false;
    return;
   }
   _selectedBaseNpcWorkerIndex=next.Index;
   ShowNode(_node,_nodeParent,_nodeKey,_recordTitle.Text);
  };

  return new Border{Style=PageStyle("Card"),Padding=new Thickness(9),Child=card};
 }
 Button BuildBaseNpcSummonButton(JsonObject baseRecord)
 {
  var workers=BaseNpcOperations.GetHiredWorkers(_session.Player);
  var button=new Button
  {
   Content=UiStrings.LocalizeText("Summon NPC to Base"),
   Style=PageStyle("Primary"),
   Margin=new Thickness(3,6,3,6),
   HorizontalAlignment=HorizontalAlignment.Left,
   IsEnabled=workers.Count>0
  };
  button.Click+=(_,_) =>
  {
   var selected=workers.FirstOrDefault(worker=>worker.Index==_selectedBaseNpcWorkerIndex);
   if(selected is null)
   {
    _owner.PageError(new InvalidOperationException(UiStrings.LocalizeText("No hired base worker is available in this save.")));
    return;
   }
   string baseName=_entry?.Title??UiStrings.LocalizeText("selected base");
   if(MessageBox.Show(_owner,UiStrings.Format("domain.summon_confirm",selected.Name,baseName),UiStrings.LocalizeText("Summon NPC to Base"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   if(!Commit())return;
   try
   {
    _session.SummonBaseNpc(selected.Data,baseRecord);
    _owner.PageStatus(UiStrings.Format("domain.summon_status",selected.Name,baseName));
   }
   catch(Exception ex){_owner.PageError(ex);}
  };
  return button;
 }
 sealed record BaseObjectChoice(int Index,string Name,JsonObject Data)
 {
  public override string ToString()=>Name;
 }
 StackPanel BuildBaseActionButtons(JsonObject baseRecord)
 {
  var actions=new StackPanel{Margin=new Thickness(2,7,2,5)};
  bool canMoveOrClear=_session.CanMoveBaseComputerOrClearTerrain(baseRecord);
  var objects=baseRecord.GetArray("Objects");
  bool hasMoveTargets=objects is not null&&Enumerable.Range(0,objects.Length)
   .Select(objects.GetObject)
   .Any(item=>item is not null&&!string.Equals(item.GetString("ObjectID"),"^BASE_FLAG",StringComparison.OrdinalIgnoreCase));
  var primary=new WrapPanel();
  var cleanup=new WrapPanel();
  void Add(WrapPanel row,string label,Action click,bool enabled=true)
  {
   var button=new Button{Content=UiStrings.LocalizeText(label),Style=PageStyle("Primary"),Margin=new Thickness(3),IsEnabled=enabled};
   button.Click+=(_,_)=>{if(Commit())click();};
   row.Children.Add(button);
  }
  Add(primary,"Export",()=>ExportBaseFromUi(baseRecord));
  Add(primary,"Import",()=>ImportBaseFromUi(baseRecord,false));
  Add(primary,"Import as New",()=>ImportBaseFromUi(baseRecord,true));
  Add(primary,"Move Base Computer",()=>MoveBaseComputerFromUi(baseRecord),canMoveOrClear&&hasMoveTargets);
  Add(primary,"Delete Base",()=>DeleteBaseFromUi(baseRecord));
  Add(cleanup,"Clear Terrain Edits at Base",()=>ClearBaseTerrainFromUi(baseRecord),canMoveOrClear);
  Add(cleanup,"Clear All Terrain Edits In Save",ClearAllTerrainFromUi);
  Add(cleanup,"Clear All Terrain Edits Except Bases",ClearAllTerrainExceptBasesFromUi);
  actions.Children.Add(primary);actions.Children.Add(cleanup);
  return actions;
 }
 void ExportBaseFromUi(JsonObject baseRecord)
 {
  string name=_entry?.Title??"Base";
  foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
  var dialog=new Microsoft.Win32.SaveFileDialog
  {
   Title=UiStrings.LocalizeText("Export Base"),FileName=name+".nmsbase",DefaultExt=".nmsbase",AddExtension=true,OverwritePrompt=true,
   Filter=UiStrings.LocalizeText("No Man's Sky base (*.nmsbase)|*.nmsbase|JSON files (*.json)|*.json|All files (*.*)|*.*")
  };
  if(dialog.ShowDialog(_owner)!=true)return;
  try{_session.ExportBase(baseRecord,dialog.FileName);_owner.PageStatus(UiStrings.LocalizeText("Base exported with any matching terrain data."));}
  catch(Exception ex){_owner.PageError(ex);}
 }
 void ImportBaseFromUi(JsonObject baseRecord,bool asNew)
 {
  var dialog=new Microsoft.Win32.OpenFileDialog
  {
   Title=UiStrings.LocalizeText(asNew?"Import Base as New":"Import Base"),CheckFileExists=true,
   Filter=UiStrings.LocalizeText("No Man's Sky base (*.nmsbase)|*.nmsbase|JSON files (*.json)|*.json|Terrain sidecar (*.terrain.json)|*.terrain.json|All files (*.*)|*.*")
  };
  if(dialog.ShowDialog(_owner)!=true)return;
  int targetIndex=asNew?-1:_entry?.Index??-1;
  try
  {
   var inspection=_session.InspectBaseImport(dialog.FileName,targetIndex);
   if(!inspection.IsSupported)throw new InvalidDataException(UiStrings.LocalizeText("The selected file is not a supported base export."));
   if(inspection.InvalidTerrainSidecar)throw new InvalidDataException(UiStrings.LocalizeText("The terrain sidecar is invalid or does not match the selected base."));
   if(inspection.TypeMismatch)throw new InvalidOperationException(UiStrings.LocalizeText("The selected export is a different base type. Replace only accepts a matching type; use Import as New instead."));
   string action=UiStrings.LocalizeText(asNew?"import as a new base":"replace the selected base");
   if(MessageBox.Show(_owner,UiStrings.Format("domain.import_prompt",inspection.Name,action),UiStrings.LocalizeText("Import Base"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   if(inspection.MetadataMismatch&&MessageBox.Show(_owner,UiStrings.LocalizeText("The imported base has different owner, game mode, or difficulty metadata. Continue?"),UiStrings.LocalizeText("Base Metadata Warning"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   if(inspection.TerrainConflict&&MessageBox.Show(_owner,UiStrings.LocalizeText("Applying this terrain data may affect terrain edits shared with another base or already present at the destination. Continue?"),UiStrings.LocalizeText("Terrain Conflict"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   var result=_session.ImportBase(dialog.FileName,targetIndex);
   RefreshBaseSelection(result.BaseIndex);
   _owner.PageStatus(UiStrings.Format("domain.base_imported",result.TerrainBuffers.ToString("N0")));
  }
  catch(Exception ex){_owner.PageError(ex);}
 }
 void MoveBaseComputerFromUi(JsonObject baseRecord)
 {
  var objects=baseRecord.GetArray("Objects");
  if(objects is null)return;
  var candidates=Enumerable.Range(0,objects.Length)
   .Select(i=>(Index:i,Data:objects.GetObject(i)))
   .Where(item=>item.Data is not null&&!string.Equals(item.Data.GetString("ObjectID"),"^BASE_FLAG",StringComparison.OrdinalIgnoreCase))
   .Select(item=>new BaseObjectChoice(item.Index,
    (item.Data!.GetString("ObjectID")??UiStrings.LocalizeText("Base object"))+" · "+UiStrings.Format("domain.entry",item.Index+1),item.Data!)).ToArray();
  if(candidates.Length==0)return;
  var list=new ListBox{ItemsSource=candidates,MinHeight=280,MinWidth=440,Margin=new Thickness(8)};
  list.SelectedItem=candidates[0];
  var ok=new Button{Content=UiStrings.LocalizeText("Move Base Computer"),Style=PageStyle("Primary"),Margin=new Thickness(5),IsDefault=true,IsEnabled=true};
  var cancel=new Button{Content=UiStrings.LocalizeText("Cancel"),Margin=new Thickness(5),IsCancel=true};
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
  buttons.Children.Add(ok);buttons.Children.Add(cancel);
  var layout=new DockPanel();DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);layout.Children.Add(list);
  var window=new Window{Title=UiStrings.LocalizeText("Choose Base Computer Location"),Owner=_owner,Width=560,Height=430,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=layout};
  ok.Click+=(_,_)=>window.DialogResult=true;
  if(window.ShowDialog()!=true||list.SelectedItem is not BaseObjectChoice target)return;
  if(MessageBox.Show(_owner,UiStrings.Format("domain.base_move_confirm",target.Name),UiStrings.LocalizeText("Move Base Computer"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{_session.MoveBaseComputer(baseRecord,target.Data);_owner.PageStatus(UiStrings.LocalizeText("Base computer moved in memory. Press Save to keep the change."));ShowNode(_node,_nodeParent,_nodeKey,_recordTitle.Text);}
  catch(Exception ex){_owner.PageError(ex);}
 }
 void DeleteBaseFromUi(JsonObject baseRecord)
 {
  if(_entry is not {IsBase:true} entry)return;
  string name=entry.Title;
  if(MessageBox.Show(_owner,UiStrings.Format("domain.base_delete_confirm",name),UiStrings.LocalizeText("Delete Base"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{_session.DeleteBase(entry.Index);RefreshBaseSelection(Math.Min(entry.Index,_entries.Count-2));_owner.PageStatus(UiStrings.Format("domain.base_deleted",name));}
  catch(Exception ex){_owner.PageError(ex);}
 }
 void ClearBaseTerrainFromUi(JsonObject baseRecord)
 {
  string name=_entry?.Title??UiStrings.LocalizeText("this base");
  if(MessageBox.Show(_owner,UiStrings.Format("domain.clear_base_confirm",name),UiStrings.LocalizeText("Clear Base Terrain Edits"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{int count=_session.ClearBaseTerrainEdits(baseRecord);_owner.PageStatus(UiStrings.Format("domain.terrain_buffers_cleared",count.ToString("N0")));}
  catch(Exception ex){_owner.PageError(ex);}
 }
 void ClearAllTerrainFromUi()
 {
  if(MessageBox.Show(_owner,UiStrings.LocalizeText("Clear every terrain edit buffer in this save? This cannot be undone after saving."),UiStrings.LocalizeText("Clear All Terrain Edits"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{int count=_session.ClearAllTerrainEdits();_owner.PageStatus(UiStrings.Format("domain.terrain_buffers_cleared",count.ToString("N0")));}
  catch(Exception ex){_owner.PageError(ex);}
 }
 void ClearAllTerrainExceptBasesFromUi()
 {
  if(MessageBox.Show(_owner,UiStrings.LocalizeText("Clear terrain edits everywhere except at saved base locations? This cannot be undone after saving."),UiStrings.LocalizeText("Clear Terrain Edits Except Bases"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  try{int count=_session.ClearAllTerrainEditsExceptBases();_owner.PageStatus(UiStrings.Format("domain.nonbase_terrain_buffers_cleared",count.ToString("N0")));}
  catch(Exception ex){_owner.PageError(ex);}
 }
 void RefreshBaseSelection(int index)
 {
  ShowSection();
  var entry=_entries.FirstOrDefault(item=>item.Index==index)??_entries.FirstOrDefault();
  if(entry is null)return;
  _binding=true;_list.SelectedItem=entry;_binding=false;Select(entry);
 }
 internal bool Commit()
 {
  if(_draftValidators.Any(validate=>!validate()))
  {
   _owner.PageError(new ArgumentException(UiStrings.LocalizeText("Enter a valid hexadecimal NPC seed, up to 16 digits.")));
   return false;
  }
  if(_section.Path=="CurrentFreighter" && (!CommitFreighterInventorySlot()||!CommitFreighterDetails()))return false;
  if(!ValidateVisitedStationDrafts())return false;
  if(!HasPending)return true;
  try{_session.ApplyFields(_fields.Select(f=>f.Target with{Text=f.Read()}),_section.Root=="Account",_ship);ApplyVisitedStationDrafts();foreach(var entry in _entries)entry.Refresh();for(int i=0;i<_fields.Count;i++){var f=_fields[i];_fields[i]=(f.Target with{Baseline=f.Read()},f.Read);}if(_section.Path=="UnlockedPetSlots" && _nodeParent is JsonArray slots)UpdateUnlockAllCompanionSlots(slots);return true;}
  catch(Exception ex){_owner.PageError(ex);return false;}
 }
 internal void Repaint(){if(_catalogueView.Content==null && !HasPending)ShowNode(_node,_nodeParent,_nodeKey,_recordTitle.Text);}
 static string Readable(string name)=>System.Text.RegularExpressions.Regex.Replace(name,"([a-z0-9])([A-Z])","$1 $2").Replace('_',' ');
}

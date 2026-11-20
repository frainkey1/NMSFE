using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;
internal sealed record ExocraftStationEntry(JsonArray OwnerArray, JsonObject Station, int Index, string Title, string BaseName, JsonObject? BaseEntry, bool IsBaseStation);
public partial class MainWindow
{
 SaveSession? _exoSession;
 Grid? _exoContent;
 ScrollViewer? _exoPageScroll;
 int _inventoryPage = 1;
 bool _showingExocraft => _inventoryPage == 5;
 bool _showingStarships => _inventoryPage == MainPageCatalog.Starships;
 bool _showingCorvettes => _inventoryPage == MainPageCatalog.Corvettes;
 bool _showingShipPage => _showingStarships || _showingCorvettes;
 bool _showingSummoningStations;
 ExocraftStationEntry? _selectedStation;
 ListBox? _vehicleList;
 RecordEntry? _vehicle;
 string _vehicleInventoryKey = "Inventory";
 string _suitInventoryKey = "Inventory";
 JsonObject? CurrentExoInventory => _showingShipPage
  ? SelectedShipInventory()
  : (_showingExocraft ? _vehicle?.Value as JsonObject : _session.Player)?.GetObject(_exoInventoryKey);
 UniformGrid? _exoTiles;
 ContentControl? _exoResizeControls;
 TextBlock? _exoName;
 Image? _exoIcon;
 TextBox? _exoAmount,_exoMaximum,_exoDamage;
 SlotView? _exoSelected;
 Button? _exoApplyButton;
 ContentControl? _exoItemPicker;
 UniformGrid? _corvetteCacheTiles;
 ContentControl? _corvetteCachePicker;
 SlotView? _corvetteCacheSelected;
 Image? _corvetteCacheIcon;
 TextBlock? _corvetteCacheName;
 TextBox? _corvetteCacheAmount,_corvetteCacheMaximum,_corvetteCacheDamage;
 Button? _corvetteCacheApplyButton;
 static readonly InventoryKind CorvetteCacheKind = new("Corvette", "Chest", false, false, false);
 readonly Dictionary<string,Button> _exoTabButtons=[];
 string _exoInventoryKey="Inventory";
 bool ExosuitPageHasPendingChanges=>_exoSelected?.Data is {} s && _exoAmount!=null && (_exoAmount.Text!=Int(s,"Amount").ToString(CultureInfo.InvariantCulture)||_exoMaximum!.Text!=Int(s,"MaxAmount").ToString(CultureInfo.InvariantCulture)||_exoDamage!.Text!=Double(s,"DamageFactor").ToString("R",CultureInfo.InvariantCulture));
 bool CorvetteCacheHasPendingChanges=>_corvetteCacheSelected?.Data is {} s && _corvetteCacheAmount!=null && (_corvetteCacheAmount.Text!=Int(s,"Amount").ToString(CultureInfo.InvariantCulture)||_corvetteCacheMaximum!.Text!=Int(s,"MaxAmount").ToString(CultureInfo.InvariantCulture)||_corvetteCacheDamage!.Text!=Double(s,"DamageFactor").ToString("R",CultureInfo.InvariantCulture));
 void ClearExosuitPageState(){ClearShipInventoryState();_exoSession=null;_exoContent=null;_exoPageScroll=null;_vehicleList=null;_vehicle=null;_selectedStation=null;_showingSummoningStations=false;_suitInventoryKey=_vehicleInventoryKey="Inventory";_exoSelected=null;_exoTiles=null;_exoInventoryKey="Inventory";_exoTabButtons.Clear();}
 void ShowExosuitPage(int page)
 {
  if(_busy)return;
  if(!ReferenceEquals(_exoSession,_session)){ClearExosuitPageState();_exoSession=_session;}
  if (_inventoryPage != page)
  {
   _inventoryPage = page;
   _exoContent = null;
   _exoSelected = null;
   _exoTabButtons.Clear();
  }
 FrameworkElement BuildSummoningStationsView()
 {
  var root=new Grid { Margin=new Thickness(2,3,2,2) };
  root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(250) });
  root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
  var lists=new Grid();
  lists.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
  lists.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
  var individual=FindSummoningStations(baseStations:false);
  var built=FindSummoningStations(baseStations:true);
  ListBox MakeList(string caption,IReadOnlyList<ExocraftStationEntry> entries,int row)
  {
   var group=new Grid { Margin=new Thickness(2) };
   group.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   group.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
   group.Children.Add(new TextBlock { Text=caption,Style=(Style)FindResource("Heading"),FontSize=15,Margin=new Thickness(2,3,2,5) });
   var list=new ListBox { ItemsSource=entries,DisplayMemberPath=nameof(ExocraftStationEntry.Title) };
   list.SetResourceReference(Control.BackgroundProperty,"Input");list.SetResourceReference(Control.ForegroundProperty,"Text");list.SetResourceReference(Control.BorderBrushProperty,"Edge");
   Grid.SetRow(list,1);group.Children.Add(list);
   var card=new Border { Style=(Style)FindResource("Card"),Padding=new Thickness(5),Child=group };
   Grid.SetRow(card,row);lists.Children.Add(card);return list;
  }
  var individualList=MakeList(UiStrings.Get("exocraft.individual_stations"),individual,0);
  var baseList=MakeList(UiStrings.Get("exocraft.part_of_bases"),built,1);
  var details=new StackPanel { Margin=new Thickness(8) };
  var detailCard=new Border { Style=(Style)FindResource("Card"),Child=details };
  var detailScroll=new ScrollViewer{Content=detailCard,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
  Grid.SetColumn(detailScroll,1);root.Children.Add(detailScroll);
  void ShowStation(ExocraftStationEntry? entry)
  {
   _selectedStation=entry;details.Children.Clear();
   details.Children.Add(new TextBlock { Text=entry?.Title??UiStrings.Get("exocraft.no_station_selected"),Style=(Style)FindResource("Heading"),Margin=new Thickness(2,2,2,10) });
   if(entry==null)return;
   var obj=entry.Station;
   string objectId=obj.GetString("ObjectID")??"";
   string displayName=_session.Items.GetItem(objectId.TrimStart('^'))?.Name??objectId;
   string address=CoordinateHelper.NormalizeGalacticAddress(entry.IsBaseStation?entry.BaseEntry?.Get("GalacticAddress"):obj.Get("GalacticAddress"));
   string raw=address.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?address[2..]:address;
   string portal=raw.Length==14?string.Concat(raw.AsSpan(0,4),raw.AsSpan(6,8)):raw.Length==12?raw:"";
   int reality=raw.Length==14&&int.TryParse(raw.AsSpan(4,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var ri)?ri:-1;
   int vx=0,vy=0,vz=0,system=0,planet=0;
   if(portal.Length==12)CoordinateHelper.PortalCodeToVoxel(portal,out vx,out vy,out vz,out system,out planet);
   string timestamp=ReadStationTimestamp(obj);
   string position=FormatStationPosition(obj.GetArray("Position"));
   void Add(string label,string value)
   {
    var row=new Grid { Margin=new Thickness(2,2,2,5) };row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
    row.Children.Add(new TextBlock{Text=label,Style=(Style)FindResource("Label"),VerticalAlignment=VerticalAlignment.Center});
    var valueText=new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,4,2,4)};Grid.SetColumn(valueText,1);row.Children.Add(valueText);details.Children.Add(row);
   }
   Add(UiStrings.Get("exocraft.station_name"),displayName);Add(UiStrings.Get("exocraft.station_timestamp"),timestamp);Add(UiStrings.Get("exocraft.station_base_name"),entry.BaseName);Add(UiStrings.Get("exocraft.station_galactic_address"),address);
   if(!entry.IsBaseStation)Add(UiStrings.Get("exocraft.station_region_seed"),obj.Get("RegionSeed")?.ToString()??"");
   Add(UiStrings.Get("exocraft.station_position"),position);Add(UiStrings.Get("exocraft.station_galaxy"),reality>=0?$"{GalaxyDatabase.GetGalaxyDisplayName(reality)} ({GalaxyDatabase.GetGalaxyType(reality)})":UiStrings.Get("exocraft.station_no_galaxy_info"));
   Add(UiStrings.Get("exocraft.station_portal_code"),portal);
   if(portal.Length==12)
   {
    var glyphRow=new Grid { Margin=new Thickness(2,2,2,5) };glyphRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});glyphRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
    glyphRow.Children.Add(new TextBlock{Text=UiStrings.Get("player.portal_glyphs"),Style=(Style)FindResource("Label"),VerticalAlignment=VerticalAlignment.Center});
    var glyphs=new WrapPanel { Margin=new Thickness(6,4,2,4),VerticalAlignment=VerticalAlignment.Center };
    foreach(char glyph in portal)glyphs.Children.Add(PlayerGlyph(glyph));
    Grid.SetColumn(glyphs,1);glyphRow.Children.Add(glyphs);details.Children.Add(glyphRow);
   }
   Add(UiStrings.Get("exocraft.station_portal_code_dec"),portal.Length==12?CoordinateHelper.PortalHexToDec(portal):"");
   Add(UiStrings.Get("exocraft.station_signal_booster"),portal.Length==12?CoordinateHelper.VoxelToSignalBooster(vx,vy,vz,system):"");
   Add(UiStrings.Get("exocraft.station_voxel_x"),vx.ToString(CultureInfo.InvariantCulture));Add(UiStrings.Get("exocraft.station_voxel_y"),vy.ToString(CultureInfo.InvariantCulture));Add(UiStrings.Get("exocraft.station_voxel_z"),vz.ToString(CultureInfo.InvariantCulture));
   Add(UiStrings.Get("exocraft.station_solar_system"),system.ToString(CultureInfo.InvariantCulture));Add(UiStrings.Get("exocraft.station_planet"),planet.ToString(CultureInfo.InvariantCulture));
   var delete=new Button{Content=UiStrings.Get("exocraft.delete_station"),Margin=new Thickness(2,10,2,2),HorizontalAlignment=HorizontalAlignment.Left};
   delete.Click+=(_,_)=>DeleteStation(entry);details.Children.Add(delete);
  }
  individualList.SelectionChanged+=(_,_)=>{if(individualList.SelectedItem is ExocraftStationEntry entry){baseList.SelectedItem=null;ShowStation(entry);}else if(baseList.SelectedItem==null)ShowStation(null);};
  baseList.SelectionChanged+=(_,_)=>{if(baseList.SelectedItem is ExocraftStationEntry entry){individualList.SelectedItem=null;ShowStation(entry);}else if(individualList.SelectedItem==null)ShowStation(null);};
  if(_selectedStation is {} selected)
  {
   var match=individual.Concat(built).FirstOrDefault(e=>ReferenceEquals(e.Station,selected.Station));
   if(match!=null){if(match.IsBaseStation)baseList.SelectedItem=match;else individualList.SelectedItem=match;}
  }
  if(_selectedStation==null)ShowStation(null);
  root.Children.Add(lists);
  return root;
 }
 List<ExocraftStationEntry> FindSummoningStations(bool baseStations)
 {
  var result=new List<ExocraftStationEntry>();
  if(_session.Player is not {} player)return result;
  if(!baseStations)
  {
   var array=player.GetArray("BaseBuildingObjects");if(array==null)return result;
   for(int i=0;i<array.Length;i++)if(array.GetObject(i) is {} obj && (obj.GetString("ObjectID")??"").Contains("GARAGE",StringComparison.OrdinalIgnoreCase))
   {string id=obj.GetString("ObjectID")??"";result.Add(new(array,obj,i,StationName(id),UiStrings.Get("exocraft.station_not_in_base"),null,false));}
   return result;
  }
  var bases=player.GetArray("PersistentPlayerBases");if(bases==null)return result;
  for(int bi=0;bi<bases.Length;bi++)
  {
   if(bases.GetObject(bi) is not {} baseEntry || baseEntry.GetArray("Objects") is not {} objects)continue;
   string baseName=baseEntry.GetString("Name")??$"Base {bi+1}";
   for(int oi=0;oi<objects.Length;oi++)if(objects.GetObject(oi) is {} obj && (obj.GetString("ObjectID")??"").Contains("GARAGE",StringComparison.OrdinalIgnoreCase))
   {string id=obj.GetString("ObjectID")??"";result.Add(new(objects,obj,oi,StationName(id),baseName,baseEntry,true));}
  }
  return result;
 }
 string StationName(string objectId)=>_session.Items.GetItem(objectId.TrimStart('^'))?.Name??objectId;
 static string ReadStationTimestamp(JsonObject obj)
 {
  try{long value=obj.GetLong("Timestamp");return value>0?DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime.ToString("g",CultureInfo.CurrentCulture):UiStrings.Get("exocraft.station_unknown");}catch{return UiStrings.Get("exocraft.station_unknown");}
 }
 static string FormatStationPosition(JsonArray? position)
 {
  if(position==null||position.Length<3)return UiStrings.Get("exocraft.station_unknown");
  try{return $"X: {position.GetDouble(0).ToString("F2",CultureInfo.CurrentCulture)}, Y: {position.GetDouble(1).ToString("F2",CultureInfo.CurrentCulture)}, Z: {position.GetDouble(2).ToString("F2",CultureInfo.CurrentCulture)}";}catch{return UiStrings.Get("exocraft.station_unknown");}
 }
 void DeleteStation(ExocraftStationEntry entry)
 {
  if(MessageBox.Show(this,UiStrings.Get("exocraft.delete_station_confirm"),UiStrings.Get("exocraft.delete_station_title"),MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
  if(entry.Index<0||entry.Index>=entry.OwnerArray.Length||!ReferenceEquals(entry.OwnerArray.GetObject(entry.Index),entry.Station))return;
  entry.OwnerArray.RemoveAt(entry.Index);_session.MarkDirty();_selectedStation=null;_exoContent=null;ShowExosuitPage(5);Status.Text=UiStrings.Get("exocraft.station_deleted");
 }
  _exoInventoryKey = page switch { MainPageCatalog.Starships => _shipInventoryKey, MainPageCatalog.Corvettes => _corvetteInventoryKey, 5 => _vehicleInventoryKey, _ => _suitInventoryKey };
  if(_exoContent==null)
  {
   _exoContent=new Grid{MinWidth=820};_exoContent.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});_exoContent.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});_exoContent.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});_exoContent.ColumnDefinitions.Add(new ColumnDefinition());_exoContent.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(260)});
   var top = new StackPanel();
   string pageName = page==5?UiStrings.Get("exocraft.title"):PageName(page);
   var title = new StackPanel { Orientation = Orientation.Horizontal };
   title.Children.Add(ConceptIcons.Make(pageName, 25));
   title.Children.Add(new TextBlock { Text = " " + pageName, Style = (Style)FindResource("Heading") });
   if (page == 3)
   {
    _shipPrimaryLabel = new TextBlock { FontSize = 13, FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 2, 0, 0) };
    RefreshPrimaryShipLabel();
    title.Children.Add(_shipPrimaryLabel);
   }
   top.Children.Add(title);
   WrapPanel? inventoryTabs=null;
   if(page==MainPageCatalog.Starships || page==MainPageCatalog.Corvettes)top.Children.Add(BuildStarshipDetails());
   if(page==5)
   {
    var sectionTabs=new WrapPanel { Margin=new Thickness(0,2,0,3) };
    foreach(var (label,stations) in new[]{(UiStrings.Get("exocraft.tab_exocrafts"),false),(UiStrings.Get("exocraft.tab_stations"),true)})
    {
     var button=new Button{Content=label,MinWidth=115};
     bool selected=_showingSummoningStations==stations;
     button.SetResourceReference(Control.BackgroundProperty,selected?"AccentSurface":"Surface");
     button.SetResourceReference(Control.ForegroundProperty,selected?"OnAccent":"Text");
     button.Click+=(_,_)=>{if(_busy||!CommitShipDetails()||!CommitExosuitPage())return;_showingSummoningStations=stations;_selectedStation=null;_exoContent=null;ShowExosuitPage(5);};
     sectionTabs.Children.Add(button);
    }
    top.Children.Add(sectionTabs);
   }
   if(page!=5 || !_showingSummoningStations)
   {
    inventoryTabs=new WrapPanel();
    foreach(var (label,key) in new[]{("Cargo","Inventory"),("Technology","Inventory_TechOnly")})
    {
     var button=new Button{Content=label,MinWidth=115};_exoTabButtons[key]=button;button.Click+=(_,_)=>{if(!_busy && CommitShipDetails() && CommitExosuitPage()){_showShipCustomisation=false;_showCorvetteCustomisation=false;_exoInventoryKey=key;if(page==MainPageCatalog.Starships)_shipInventoryKey=key;else if(page==MainPageCatalog.Corvettes)_corvetteInventoryKey=key;else if(page==5)_vehicleInventoryKey=key;else _suitInventoryKey=key;_exoSelected=null;_exoContent=null;ShowExosuitPage(page);}};inventoryTabs.Children.Add(button);
    }
    if(page==MainPageCatalog.Starships || page==MainPageCatalog.Corvettes)
    {
     var customButton=new Button{Content=page==MainPageCatalog.Corvettes?UiStrings.Get("ui.exosuit.tab_cache"):UiStrings.Get("starship.tab_customisation"),MinWidth=125};
     customButton.SetResourceReference(Control.BackgroundProperty,(page==MainPageCatalog.Starships?_showShipCustomisation:_showCorvetteCustomisation)?"AccentSurface":"Surface");
     customButton.SetResourceReference(Control.ForegroundProperty,(page==MainPageCatalog.Starships?_showShipCustomisation:_showCorvetteCustomisation)?"OnAccent":"Text");
     customButton.Click+=(_,_)=>{if(!_busy && !_showingShipCustomisation && CommitShipDetails() && CommitExosuitPage()){if(page==MainPageCatalog.Starships)_showShipCustomisation=true;else _showCorvetteCustomisation=true;_exoSelected=null;_exoContent=null;ShowExosuitPage(page);}};
     inventoryTabs.Children.Add(customButton);
    }
   }
   var jsonLinks=new WrapPanel{Margin=new Thickness(0,3,0,5)};
   void AddJsonLink(string label,string tooltip,Func<string[]> path)
   {
    string localizedTooltip=UiStrings.LocalizeText(tooltip);
    if(localizedTooltip==tooltip)localizedTooltip=UiStrings.Get("exosuit.raw_json_tooltip");
    jsonLinks.Children.Add(JsonRedirectButton(UiStrings.LocalizeText(label),localizedTooltip,()=>NavigateToRawJsonPath(path())));
   }
   if(page==1)
   {
    AddJsonLink("↗ JSON · Inventory","Open in Raw JSON Editor",()=>["PlayerStateData","Inventory"]);
   }
   else if(page==MainPageCatalog.Starships)
   {
    AddJsonLink("↗ JSON · Ships","Open in Raw JSON Editor",()=>["PlayerStateData",_showArchive?"ArchivedShipOwnership":"ShipOwnership"]);
    if(_showShipCustomisation)AddJsonLink("↗ JSON · Customisation","Open in Raw JSON Editor",SelectedShipCustomisationJsonPath);
    else AddJsonLink("↗ JSON · Inventory","Open in Raw JSON Editor",()=>SelectedShipJsonPath(_exoInventoryKey));
    AddJsonLink("↗ JSON · Resource","Open in Raw JSON Editor",()=>SelectedShipJsonPath("Resource"));
   }
   else if(page==MainPageCatalog.Corvettes)
   {
    AddJsonLink("↗ JSON · Ships", "Open in Raw JSON Editor", () => ["PlayerStateData", _showArchive ? "ArchivedShipOwnership" : "ShipOwnership"]);
    AddJsonLink("↗ JSON · Assembled parts", "Open in Raw JSON Editor", SelectedCorvetteBaseJsonPath);
    AddJsonLink("↗ JSON · Inventory", "Open in Raw JSON Editor", () => SelectedShipJsonPath(_exoInventoryKey));
    if (_showCorvetteCustomisation)
     AddJsonLink("↗ JSON · Cache", "Open in Raw JSON Editor", () => ["PlayerStateData", "CorvetteStorageInventory"]);
    AddJsonLink("↗ JSON · Resource", "Open in Raw JSON Editor", () => SelectedShipJsonPath("Resource"));
   }
   else
   {
    AddJsonLink("↗ JSON · Vehicles","Open in Raw JSON Editor",()=>["PlayerStateData","VehicleOwnership"]);
    AddJsonLink("↗ JSON · Inventory","Open in Raw JSON Editor",()=>_vehicle==null?["PlayerStateData","VehicleOwnership"]:["PlayerStateData","VehicleOwnership",$"[{_vehicle.Index}]",_exoInventoryKey]);
    AddJsonLink("↗ JSON · Base objects","Open in Raw JSON Editor",()=>["PlayerStateData","BaseBuildingObjects"]);
    AddJsonLink("↗ JSON · Bases","Open in Raw JSON Editor",()=>["PlayerStateData","PersistentPlayerBases"]);
   }
   if(page!=5 || !_showingSummoningStations)
   {
    var inventoryHeaderLeft=new StackPanel{Margin=new Thickness(0,0,0,3)};if(inventoryTabs!=null)inventoryHeaderLeft.Children.Add(inventoryTabs);inventoryHeaderLeft.Children.Add(jsonLinks);
    bool hasInventoryGrid=!(page==MainPageCatalog.Starships&&_showShipCustomisation)&&!(page==MainPageCatalog.Corvettes&&_showCorvetteCustomisation);
    Grid.SetRow(inventoryHeaderLeft,1);_exoContent.Children.Add(inventoryHeaderLeft);
    if(hasInventoryGrid || (page==MainPageCatalog.Corvettes&&_showCorvetteCustomisation)){_exoResizeControls=new ContentControl { HorizontalContentAlignment=HorizontalAlignment.Stretch, Margin=page==MainPageCatalog.Corvettes&&_showCorvetteCustomisation?new Thickness(-18,0,0,0):new Thickness(0) };Grid.SetRow(_exoResizeControls,1);Grid.SetColumn(_exoResizeControls,1);_exoContent.Children.Add(_exoResizeControls);}
   }
   Grid.SetColumnSpan(top,2);Grid.SetRow(top,0);_exoContent.Children.Add(top);
   if(page==5 && _showingSummoningStations)
   {
    var stations=BuildSummoningStationsView();Grid.SetRow(stations,1);Grid.SetRowSpan(stations,2);Grid.SetColumnSpan(stations,2);_exoContent.Children.Add(stations);
   }
   else if((page==MainPageCatalog.Starships || page==MainPageCatalog.Corvettes) && _showingShipCustomisation)
   {
    _exoTiles=null;_exoIcon=null;_exoName=null;_exoAmount=null;_exoMaximum=null;_exoDamage=null;_exoApplyButton=null;_exoItemPicker=null;
    UIElement customisation=page==MainPageCatalog.Corvettes?BuildCorvetteCacheView():BuildStarshipCustomisationView();Grid.SetRow(customisation,2);Grid.SetColumnSpan(customisation,2);_exoContent.Children.Add(customisation);
   }
   else
   {
   var gridScroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};_exoTiles=new UniformGrid{Columns=10,VerticalAlignment=VerticalAlignment.Top,MinWidth=540};gridScroll.Content=_exoTiles;
   var gridBody=new StackPanel();gridBody.Children.Add(gridScroll);
   var gridCard=new Border{Style=(Style)FindResource("Card"),Child=gridBody};Grid.SetRow(gridCard,2);_exoContent.Children.Add(gridCard);
   var detail=new StackPanel();
   var heading=new Border{CornerRadius=new CornerRadius(7),Padding=new Thickness(9),Margin=new Thickness(0,0,0,12)};heading.SetResourceReference(Border.BackgroundProperty,"AccentSurface");var headerText=new TextBlock{Text="╱╱  "+UiStrings.Get("exosuit.slot_details"),FontSize=17,FontWeight=FontWeights.SemiBold};headerText.SetResourceReference(TextBlock.ForegroundProperty,"OnAccent");heading.Child=headerText;detail.Children.Add(heading);
   _exoIcon=new Image{Height=88,Margin=new Thickness(0,0,0,9)};detail.Children.Add(_exoIcon);_exoName=new TextBlock{Text="Select an inventory slot",FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,0,3,14)};detail.Children.Add(_exoName);
   TextBox Field(string label){detail.Children.Add(new TextBlock{Text=label,Style=(Style)FindResource("Label")});var box=new TextBox();detail.Children.Add(box);return box;}
   _exoAmount=Field("Amount");_exoMaximum=Field("Maximum");_exoDamage=Field("Damage (0–1)");
   var apply=new Button{Content="Apply Changes",Style=(Style)FindResource("Primary"),Margin=new Thickness(3,16,3,3),IsEnabled=false};_exoApplyButton=apply;apply.Click+=(_,_)=>{if(CommitExosuitPage()){RenderExosuitInventory();Status.Text=UiStrings.LocalizeText("Slot changes applied in memory. Press Save to keep them.");}};detail.Children.Add(apply);
   var inspector=new StackPanel();
   inspector.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=detail});
   _exoItemPicker=new ContentControl();inspector.Children.Add(_exoItemPicker);
   var detailCard=new ScrollViewer{Content=inspector,Width=260,MaxWidth=260,HorizontalAlignment=HorizontalAlignment.Left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(detailCard,2);Grid.SetColumn(detailCard,1);_exoContent.Children.Add(detailCard);
   }
  }
  if (_exoContent.Parent is Panel oldPanel) oldPanel.Children.Remove(_exoContent);
  if(_exoContent.Parent is ScrollViewer old)old.Content=null;
  FrameworkElement content = _exoContent;
  if ((_showingExocraft && !_showingSummoningStations) || _showingShipPage)
  {
   var layout = new Grid();
   layout.ColumnDefinitions.Add(new ColumnDefinition { Width = _showingShipPage ? GridLength.Auto : new GridLength(108) });
   layout.ColumnDefinitions.Add(new ColumnDefinition());
   if (_showingShipPage) layout.Children.Add(BuildShipSelector());
   else layout.Children.Add(BuildVehicleSelector());
   Grid.SetColumn(_exoContent, 1);
   layout.Children.Add(_exoContent);
   content = layout;
  }
  else Grid.SetColumn(_exoContent, 0);
  _exoPageScroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=content};
  MigrationPage.Child=_exoPageScroll;
  UpdateExosuitContentHeight();
  MigrationPage.SizeChanged-=ExosuitResized;MigrationPage.SizeChanged+=ExosuitResized;
  MigrationPage.Visibility=Visibility.Visible;Workspace.Visibility=Visibility.Collapsed;RenderExosuitInventory();
  var activeScroll=_exoPageScroll;
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>
  {
   if(ReferenceEquals(MigrationPage.Child,activeScroll))
   {
    UpdateExosuitContentHeight();
    ResetExosuitPageScroll();
   }
  });
 }
 string[] SelectedShipJsonPath(string property)
 {
  if(_selectedShip==null)return ["PlayerStateData",_showArchive?"ArchivedShipOwnership":"ShipOwnership"];
  var ships=_session.Player?.GetArray(_showArchive?"ArchivedShipOwnership":"ShipOwnership");
  if(ships==null)return ["PlayerStateData",_showArchive?"ArchivedShipOwnership":"ShipOwnership"];
  for(int i=0;i<ships.Length;i++)
  {
   var record=ships.GetObject(i);var ownership=_showArchive?record?.GetObject("Ownership"):record;
   if(!ReferenceEquals(ownership,_selectedShip.Ownership))continue;
   return _showArchive?["PlayerStateData","ArchivedShipOwnership",$"[{i}]","Ownership",property]:["PlayerStateData","ShipOwnership",$"[{i}]",property];
  }
  return ["PlayerStateData",_showArchive?"ArchivedShipOwnership":"ShipOwnership"];
 }
 Border BuildVehicleSelector()
 {
   if (_vehicleList == null)
   {
    var vehicles = _session.Player?.GetArray("VehicleOwnership");
    var entries = vehicles == null ? Array.Empty<RecordEntry>() :
     Enumerable.Range(0, vehicles.Length).Select(i => new RecordEntry(vehicles, i, i, true)).ToArray();
    _vehicleList = new ListBox { ItemsSource = entries, DisplayMemberPath = nameof(RecordEntry.Title) };
    _vehicleList.SetResourceReference(Control.BackgroundProperty, "Input");
    _vehicleList.SetResourceReference(Control.ForegroundProperty, "Text");
    _vehicleList.SetResourceReference(Control.BorderBrushProperty, "Edge");
    _vehicle = entries.FirstOrDefault();
    _vehicleList.SelectedItem = _vehicle;
    _vehicleList.SelectionChanged += (_, _) =>
    {
     if (ReferenceEquals(_vehicleList.SelectedItem, _vehicle)) return;
     if (!CommitExosuitPage()) { _vehicleList.SelectedItem = _vehicle; return; }
     _vehicle = _vehicleList.SelectedItem as RecordEntry;
     _exoSelected = null;
     RenderExosuitInventory();
    };
   }
   if (_vehicleList.Parent is Panel oldPanel) oldPanel.Children.Remove(_vehicleList);
   var selector = new DockPanel();
   var header = new Border { Padding = new Thickness(6), Margin = new Thickness(0,0,0,4), CornerRadius = new CornerRadius(5) };
   header.SetResourceReference(Border.BackgroundProperty, "AccentSurface");
   var label = new TextBlock { Text = "Name", FontWeight = FontWeights.SemiBold };
   label.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
   header.Child = label;
   DockPanel.SetDock(header, Dock.Top);
   selector.Children.Add(header);
   selector.Children.Add(_vehicleList);
   return new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(5), Child = selector };
 }
 void ExosuitResized(object sender,SizeChangedEventArgs e){UpdateExosuitContentHeight(e.NewSize.Height);}
 void UpdateExosuitContentHeight(double availableHeight=0)
 {
  if(_exoContent==null)return;
  if(availableHeight<=0)availableHeight=MigrationPage.ActualHeight;
  if(availableHeight<=0)return;
  double contentHeight=Math.Max(250,availableHeight-MigrationPage.Padding.Top-MigrationPage.Padding.Bottom-14);
   _exoContent.Height=contentHeight+(_showingShipPage?420:0);
 }
 void ResetExosuitPageScroll()
 {
  var scroll=_exoPageScroll;
  if(scroll==null)return;
  scroll.ScrollToTop();
  Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>
  {
   if(ReferenceEquals(MigrationPage.Child,scroll))scroll.ScrollToTop();
  });
 }
 JsonObject? CorvetteCacheInventory => _session.Player?.GetObject("CorvetteStorageInventory");
 static bool IsCorvettePart(GameItem item) =>
  !string.IsNullOrWhiteSpace(item.BuildableShipTechID)
  && !string.Equals(item.Id, "B_TUR_F", StringComparison.OrdinalIgnoreCase);
 FrameworkElement BuildCorvetteCacheView()
 {
  var root=new Grid { Margin=new Thickness(2,3,2,2) };
  root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
  root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(320) });
  _corvetteCacheTiles=new UniformGrid { Columns=10, VerticalAlignment=VerticalAlignment.Top, MinWidth=540 };
  var scroll=new ScrollViewer { Content=_corvetteCacheTiles, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto };
  var inventoryCard=new Border { Style=(Style)FindResource("Card"), Child=scroll };
  root.Children.Add(inventoryCard);
  var details=new StackPanel();
  var slotDetails=new StackPanel();
  slotDetails.Children.Add(new TextBlock { Text=UiStrings.Get("exosuit.slot_details"), Style=(Style)FindResource("Heading"), Margin=new Thickness(4,2,4,8) });
  _corvetteCacheIcon=new Image { Height=82, Margin=new Thickness(0,0,0,8) };
  slotDetails.Children.Add(_corvetteCacheIcon);
  _corvetteCacheName=new TextBlock { Text="Select a cache slot", FontSize=15, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(3,0,3,10) };
  slotDetails.Children.Add(_corvetteCacheName);
  TextBox Field(string label){slotDetails.Children.Add(new TextBlock { Text=label, Style=(Style)FindResource("Label") });var box=new TextBox();slotDetails.Children.Add(box);return box;}
  _corvetteCacheAmount=Field("Amount");_corvetteCacheMaximum=Field("Maximum");_corvetteCacheDamage=Field("Damage (0–1)");
  _corvetteCacheApplyButton=new Button { Content="Apply Changes", Style=(Style)FindResource("Primary"), Margin=new Thickness(3,14,3,3), IsEnabled=false };
  _corvetteCacheApplyButton.Click+=(_,_)=>{if(CommitCorvetteCachePage()){RenderCorvetteCache();Status.Text=UiStrings.LocalizeText("Slot changes applied in memory. Press Save to keep them.");}};
  slotDetails.Children.Add(_corvetteCacheApplyButton);
  var slotDetailsCard=new Border { Style=(Style)FindResource("Card"), Child=slotDetails, Margin=new Thickness(0,0,0,8) };
  details.Children.Add(slotDetailsCard);
  _corvetteCachePicker=new ContentControl { Content=BuildItemPicker(null, CorvetteCacheKind, CommitCorvetteCachePage, RenderCorvetteCache, IsCorvettePart) };
  details.Children.Add(_corvetteCachePicker);
  var detailScroll=new ScrollViewer { Content=details, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
  var detailCard=new Border { Style=(Style)FindResource("Card"), Child=detailScroll, Margin=new Thickness(8,0,0,0) };
  Grid.SetColumn(detailCard,1);
  root.Children.Add(detailCard);
  RenderCorvetteCache();
  return root;
 }
 void RenderCorvetteCache()
 {
  if(_corvetteCacheTiles==null)return;
  var previous=_corvetteCacheSelected;
  _corvetteCacheTiles.Children.Clear();
  var inventory=CorvetteCacheInventory;
  if(_exoResizeControls!=null)
   _exoResizeControls.Content=BuildInventoryResizeControls(inventory,20,20,()=>CommitShipDetails()&&CommitCorvetteCachePage(),RenderCorvetteCache,compact:true);
  if(inventory==null)
  {
   _corvetteCachePicker!.Content=new TextBlock { Text=UiStrings.Get("exosuit.cache_unavailable"), TextWrapping=TextWrapping.Wrap };
   ClearCorvetteCacheSelection();
   return;
  }
  _corvetteCachePicker!.Content=BuildItemPicker(null, CorvetteCacheKind, CommitCorvetteCachePage, RenderCorvetteCache, IsCorvettePart);
  var (items,width,_)=BuildSlotModels(inventory);
  _corvetteCacheTiles.Columns=width;
  foreach(var item in items)
  {
   var body=new StackPanel { Height=72 };
   var name=new TextBlock { Text=item.Name, Width=51, FontSize=9, TextTrimming=TextTrimming.CharacterEllipsis, Foreground=item.LabelForeground };
   body.Children.Add(new Border { Child=name, Background=item.LabelBackground, Padding=new Thickness(2), CornerRadius=new CornerRadius(4) });
   body.Children.Add(new Image { Source=item.Icon, Height=39, Margin=new Thickness(2) });
   body.Children.Add(new TextBlock { Text=item.AmountLabel, FontSize=10, HorizontalAlignment=HorizontalAlignment.Center });
   var button=new Button { Content=body, Background=item.Background, BorderBrush=item.Border, Margin=new Thickness(2), Padding=new Thickness(0), ToolTip=item.Name, Tag=item };
   button.Click+=(_,_)=>{if(!_busy&&CommitCorvetteCachePage())SelectCorvetteCacheSlot(item);};
   WireInventoryTile(button,item,CorvetteCacheKind,CommitCorvetteCachePage,RenderCorvetteCache,()=>{if(CommitCorvetteCachePage())SelectCorvetteCacheSlot(item);},()=>_corvetteCachePicker!.BringIntoView());
   _corvetteCacheTiles.Children.Add(button);
  }
  if(previous!=null && ReferenceEquals(previous.Inventory,inventory) && items.FirstOrDefault(x=>x.X==previous.X && x.Y==previous.Y) is {} retained)
   SelectCorvetteCacheSlot(retained);
  else ClearCorvetteCacheSelection();
 }
 void ClearCorvetteCacheSelection()
 {
  _corvetteCacheSelected=null;
  if(_corvetteCacheName!=null)_corvetteCacheName.Text="Select a cache slot";
  if(_corvetteCacheIcon!=null)_corvetteCacheIcon.Source=null;
  if(_corvetteCacheAmount!=null)_corvetteCacheAmount.Text=_corvetteCacheMaximum!.Text=_corvetteCacheDamage!.Text="";
  if(_corvetteCacheAmount!=null)_corvetteCacheAmount.IsEnabled=_corvetteCacheMaximum!.IsEnabled=_corvetteCacheDamage!.IsEnabled=false;
  if(_corvetteCacheApplyButton!=null)_corvetteCacheApplyButton.IsEnabled=false;
 }
 void SelectCorvetteCacheSlot(SlotView slot)
 {
  _corvetteCacheSelected=slot;
  if(slot.Data is {} data)
  {
   _corvetteCacheName!.Text=slot.Name;_corvetteCacheIcon!.Source=slot.Icon;
   _corvetteCacheAmount!.Text=Int(data,"Amount").ToString(CultureInfo.InvariantCulture);
   _corvetteCacheMaximum!.Text=Int(data,"MaxAmount").ToString(CultureInfo.InvariantCulture);
   _corvetteCacheDamage!.Text=Double(data,"DamageFactor").ToString("R",CultureInfo.InvariantCulture);
   _corvetteCacheAmount.IsEnabled=_corvetteCacheMaximum!.IsEnabled=_corvetteCacheDamage!.IsEnabled=true;
   _corvetteCacheApplyButton!.IsEnabled=true;
  }
  else
  {
   _corvetteCacheName!.Text=UiStrings.Get("inventory.empty_slot");_corvetteCacheIcon!.Source=null;
   _corvetteCacheAmount!.Text=_corvetteCacheMaximum!.Text=_corvetteCacheDamage!.Text="";
   _corvetteCacheAmount.IsEnabled=_corvetteCacheMaximum!.IsEnabled=_corvetteCacheDamage!.IsEnabled=false;
   _corvetteCacheApplyButton!.IsEnabled=false;
  }
  _corvetteCachePicker!.Content=BuildItemPicker(slot,CorvetteCacheKind,CommitCorvetteCachePage,RenderCorvetteCache,IsCorvettePart);
 }
 bool CommitCorvetteCachePage()
 {
  if(_busy)return false;if(!CorvetteCacheHasPendingChanges)return true;
  try
  {
   if(_corvetteCacheSelected?.Data is not {} slot)return true;
   if(!int.TryParse(_corvetteCacheAmount!.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int amount)||!int.TryParse(_corvetteCacheMaximum!.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int maximum)||!double.TryParse(_corvetteCacheDamage!.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double damage))throw new ArgumentException("Enter valid numeric slot values.");
   _session.ApplyInventorySlot(CorvetteCacheInventory,slot,amount,maximum,damage);return true;
  }
  catch(Exception ex){Error(ex);return false;}
 }
 void RenderExosuitInventory()
 {
  if((_showingShipPage&&_showingShipCustomisation)||(_showingExocraft&&_showingSummoningStations))return;
  if(_exoTiles==null)return;
  foreach(var pair in _exoTabButtons){pair.Value.SetResourceReference(Control.BackgroundProperty,pair.Key==_exoInventoryKey?"AccentSurface":"Surface");pair.Value.SetResourceReference(Control.ForegroundProperty,pair.Key==_exoInventoryKey?"OnAccent":"Text");}
  var previous=_exoSelected;_exoTiles.Children.Clear();
  var inventory=CurrentExoInventory;
  if(_exoResizeControls!=null){bool technology=_exoInventoryKey.EndsWith("_TechOnly",StringComparison.Ordinal);_exoResizeControls.Content=BuildInventoryResizeControls(inventory,10,technology?6:12,()=>CommitShipDetails()&&CommitExosuitPage(),RenderExosuitInventory,compact:true);}
  _exoItemPicker!.Content=BuildItemPicker(null,ExosuitInventoryKind,CommitExosuitPage,RenderExosuitInventory);
  if(inventory==null){_exoName!.Text="This inventory is unavailable.";_exoIcon!.Source=null;_exoSelected=null;_exoAmount!.Text=_exoMaximum!.Text=_exoDamage!.Text="";_exoAmount.IsEnabled=_exoMaximum.IsEnabled=_exoDamage.IsEnabled=_exoApplyButton!.IsEnabled=false;return;}
  var (items,width,height)=BuildSlotModels(inventory);_exoTiles.Columns=width;
  foreach(var item in items)
  {
   var body=new StackPanel{Height=72};var name=new TextBlock{Text=item.Name,Width=51,FontSize=9,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=item.LabelForeground};body.Children.Add(new Border{Child=name,Background=item.LabelBackground,Padding=new Thickness(2),CornerRadius=new CornerRadius(4)});body.Children.Add(new Image{Source=item.Icon,Height=39,Margin=new Thickness(2)});body.Children.Add(new TextBlock{Text=item.AmountLabel,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center});
   var button=new Button{Content=body,Background=item.Background,BorderBrush=item.Border,Margin=new Thickness(2),Padding=new Thickness(0),ToolTip=item.Name,Tag=item};
   button.Click+=(_,_)=>{if(!_busy && CommitExosuitPage())SelectExosuitSlot(item);};
   WireInventoryTile(button,item,ExosuitInventoryKind,CommitExosuitPage,RenderExosuitInventory,
    () => {if(CommitExosuitPage())SelectExosuitSlot(item);}, () => ((Border)_exoItemPicker!.Content).BringIntoView());
   _exoTiles.Children.Add(button);
  }
  if(previous!=null && ReferenceEquals(previous.Inventory,inventory) && items.FirstOrDefault(x=>x.X==previous.X && x.Y==previous.Y) is {} retained)SelectExosuitSlot(retained);
  else{_exoSelected=null;_exoName!.Text="Select an inventory slot";_exoIcon!.Source=null;_exoAmount!.Text=_exoMaximum!.Text=_exoDamage!.Text="";if(_exoApplyButton!=null)_exoApplyButton.IsEnabled=false;}
 }
 void SelectExosuitSlot(SlotView slot)
 {
  _exoSelected=slot;_exoItemPicker!.Content=BuildItemPicker(slot,ExosuitInventoryKind,CommitExosuitPage,RenderExosuitInventory);_exoName!.Text=slot.Name;_exoIcon!.Source=slot.Icon;var s=slot.Data;
  _exoAmount!.Text=Int(s,"Amount").ToString(CultureInfo.InvariantCulture);_exoMaximum!.Text=Int(s,"MaxAmount").ToString(CultureInfo.InvariantCulture);_exoDamage!.Text=Double(s,"DamageFactor").ToString("R",CultureInfo.InvariantCulture);
  _exoAmount.IsEnabled=_exoMaximum.IsEnabled=_exoDamage.IsEnabled=s!=null;if(_exoApplyButton!=null)_exoApplyButton.IsEnabled=s!=null;
 }
 bool CommitExosuitPage()
 {
  if(_busy)return false;if(!ExosuitPageHasPendingChanges)return true;
  try
  {
   if(_exoSelected?.Data is not {} slot)return true;
   if(!int.TryParse(_exoAmount!.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int amount)||!int.TryParse(_exoMaximum!.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int maximum)||!double.TryParse(_exoDamage!.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double damage))throw new ArgumentException("Enter valid numeric slot values.");
   _session.ApplyInventorySlot(CurrentExoInventory,slot,amount,maximum,damage);return true;
  }
  catch(Exception ex){Error(ex);return false;}
 }
}

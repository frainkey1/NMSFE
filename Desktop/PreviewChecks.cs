using System.IO;
using NMSE.Next.Engine;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace NMSE.Next.Desktop;
public partial class MainWindow
{
 async Task VerifyPreview()
 {
  string output=Path.Combine(Path.GetDirectoryName(_session.ResourceRoot)!,"Artifacts","Preview10");Directory.CreateDirectory(output);
  try
  {
   if(_activeTool<0)throw new Exception("Real save did not load into WPF editor.");
   int checks=0;
   NavigateToRawJsonPath("PlayerStateData");
   if(_rawTree==null||!_utilityPages.TryGetValue(MainPageCatalog.RawJsonEditor,out var rawPage)||!ReferenceEquals(MigrationPage.Child,rawPage))
    throw new Exception("JSON redirect did not open the Raw JSON Editor page.");
   checks++;
   Navigate(new Button{Tag=MainPageCatalog.MultiTools},new RoutedEventArgs());
   double previousMinHeight=Slots.MinHeight;
   Slots.MinHeight=1200;
   try
   {
    UpdateLayout();
    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    DependencyObject? parent=Slots;
    while(parent!=null && parent is not ScrollViewer)parent=VisualTreeHelper.GetParent(parent);
    if(parent is not ScrollViewer gridScroll || gridScroll.ScrollableHeight<=0 ||
       gridScroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible)
     throw new Exception("Overflowing multi-tool inventory has no vertical scrollbar.");
    gridScroll.ScrollToBottom();
    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    if(gridScroll.VerticalOffset<gridScroll.ScrollableHeight-1)
     throw new Exception("The multi-tool scrollbar cannot reach the bottom of the grid.");
    gridScroll.ScrollToTop();
   }
   finally
   {
    Slots.MinHeight=previousMinHeight;
   }
   checks++;
   var allianceFixture=new AlliancePageView(new AlliancePageData("Fixture data", [new AllianceLeaderboardRow(1,"Read-only alliance",987654)], [], []));
   if(!allianceFixture.LeaderboardGrid.IsReadOnly || allianceFixture.LeaderboardGrid.CanUserAddRows ||
      allianceFixture.LeaderboardGrid.CanUserDeleteRows || allianceFixture.LeaderboardGrid.ItemsSource is not IEnumerable<AllianceLeaderboardRow> rows ||
      rows.Single().Score!=987654)
    throw new Exception("Alliance leaderboard values are editable or fixture row binding failed.");
   checks++;
   var originalSource=_session.SourcePath!;var sourceHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(originalSource));
   var initial=_session.ReadTool(_activeTool);
   ToolName.Text="WPF binding check";ApplyTool(this,new RoutedEventArgs());
   if(_session.ReadTool(_activeTool).Name!="WPF binding check")throw new Exception("Tool UI edits do not reach engine");
   _session.Load(_session.SourcePath!);ShowTool(_session.PrimaryIndex);RefreshSelector();checks++;
   bool priorTheme=Themes.Dark;_busy=true;ToggleTheme(this,new RoutedEventArgs());_busy=false;
   if(Themes.Dark!=priorTheme)throw new Exception("Theme switch bypasses busy guard");checks++;

   foreach(bool dark in new[]{false,true})
   {
    Themes.Apply(dark);_moreNavigation=false;BuildNavigationV2();ApplyActionIcons();
    if(_navigationButtons.Skip(10).Any(b=>b.Visibility!=Visibility.Collapsed))throw new Exception("More pages are expanded before More is pressed");
    var more=Navigation.Children.OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="More");more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(_navigationButtons.Skip(10).Any(b=>b.Visibility!=Visibility.Visible))throw new Exception("More does not expand pages inline");checks+=2;
    _moreNavigation=false;BuildNavigationV2();
    ToolSelector.SelectedItem=_session.Tools.First(t=>t.Index==0);RenderInventory();
    var states=((IEnumerable<SlotView>)Slots.ItemsSource).ToArray();
    if(!states.Any(s=>s.Supercharged)||!states.Any(s=>s.Activated&&!s.Supercharged))throw new Exception("Mixed regular/supercharged tool is not displayed distinctly");checks++;

    foreach(var size in new[]{new Size(800,500),new Size(1050,610),new Size(1230,670)})
    {
     Width=size.Width;Height=size.Height;UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
     var uniform=FindVisual<System.Windows.Controls.Primitives.UniformGrid>(Slots)??throw new Exception("Inventory grid missing");
     if(uniform.Columns!=10)throw new Exception("Actual inventory column count lost");
     if(ToolPage.ActualWidth>Workspace.ViewportWidth+1 && Workspace.ScrollableWidth<=0)throw new Exception("Overflow does not expose horizontal scroll");
     if(Workspace.ActualHeight<100 || _navigationButtons.Count!=MainPageCatalog.Names.Length)throw new Exception("Workspace/navigation invalid");
     Workspace.ScrollToRightEnd();UpdateLayout();
     if(Workspace.HorizontalOffset+Workspace.ViewportWidth<Workspace.ExtentWidth-1)throw new Exception("Rightmost content cannot be reached");
     Workspace.ScrollToLeftEnd();
     DependencyObject? parent=Slots;while(parent!=null && parent is not ScrollViewer)parent=VisualTreeHelper.GetParent(parent);
     if(parent is ScrollViewer inventoryScroll){inventoryScroll.ScrollToBottom();UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(inventoryScroll.VerticalOffset+inventoryScroll.ViewportHeight<inventoryScroll.ExtentHeight-1)throw new Exception("Final inventory rows cannot be reached");inventoryScroll.ScrollToTop();}
     checks+=5;
    }
    var occupied=((IEnumerable<SlotView>)Slots.ItemsSource).First(x=>x.Data!=null);
    SelectSlot(new Button{Tag=occupied},new RoutedEventArgs());
    if(DetailIcon.Source==null || string.IsNullOrWhiteSpace(SlotName.Text))throw new Exception("Real slot inspector is not connected");checks++;
    var selectedData=_selectedSlot!.Data;RenderInventory();if(!ReferenceEquals(selectedData,_selectedSlot?.Data))throw new Exception("Repainting loses slot selection");checks++;
    UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    var ug=FindVisual<System.Windows.Controls.Primitives.UniformGrid>(Slots)!;

    var slotButtons=ug.Children.Count;
    if(slotButtons==0 || Slots.ActualHeight<400)throw new Exception("Inventory visuals are not realized.");
    var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(this);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(output,dark?"wpf-dark.png":"wpf-light.png")))encoder.Save(file);
   }
   foreach(int page in new[]{0,1,2,0,1,2})
   {
    Navigate(new Button{Tag=page},new RoutedEventArgs());UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    if(_selectedPage!=page)throw new Exception("Page selection did not settle");
    if(page==0 && _playerFields.Count!=8)throw new Exception("Player page did not create its real fields");
    if(page==1 && (_exoTiles==null||_exoTiles.Children.Count<10))throw new Exception("Exosuit did not render its real inventory");checks++;
   }
   Navigate(new Button{Tag=0},new RoutedEventArgs());
   string previousUnits=_playerFields[3].Text;_playerFields[3].Text=previousUnits=="123456"?"123457":"123456";
   string requestedUnits=_playerFields[3].Text;
   Navigate(new Button{Tag=2},new RoutedEventArgs());
   if(_session.ReadPlayer().Units!=requestedUnits)throw new Exception("Player edits were lost when navigating away");
   Navigate(new Button{Tag=0},new RoutedEventArgs());if(_playerFields[3].Text!=requestedUnits)throw new Exception("Player draft did not survive navigation");checks+=2;
   Navigate(new Button{Tag=1},new RoutedEventArgs());
   var cargoButton=_exoTiles!.Children.OfType<Button>().First(b=>((SlotView)b.Tag).Data!=null);
   if(cargoButton.ContextMenu==null)throw new Exception("Cargo slot has no context menu");
   var cargo=(SlotView)cargoButton.Tag;
   SelectExosuitSlot(cargo);
   var picker=(StackPanel)((Border)_exoItemPicker!.Content).Child;
   var search=picker.Children.OfType<TextBox>().Single();
   search.Text="^OXYGEN";
   var choices=picker.Children.OfType<ComboBox>().Last();
   var oxygen=choices.Items.OfType<NMSE.Data.GameItem>().Single(item=>item.Id.TrimStart('^')=="OXYGEN");
   choices.SelectedItem=oxygen;
   picker.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(SaveSession.InventoryItemId(_exoSelected?.Data)!="^OXYGEN")throw new Exception("Item Picker did not replace the selected cargo item");
   var cargoMenu=new ContextMenu();
   PopulateInventoryMenu(cargoMenu,_exoSelected!,ExosuitInventoryKind,CommitExosuitPage,RenderExosuitInventory,()=>{});
   if(cargoMenu.Items.OfType<MenuItem>().Any(item=>item.Header.ToString()!.Contains("Supercharge")))throw new Exception("Cargo menu exposes supercharge");
   var fill=cargoMenu.Items.OfType<MenuItem>().Single(item=>(string)item.Header=="Fill Stack");
   fill.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   if(Int(_exoSelected!.Data,"Amount")!=Int(_exoSelected.Data,"MaxAmount"))throw new Exception("Fill Stack did not update the selected slot");
   checks+=4;
   var exoCell=_exoTiles!.Children.OfType<Button>().Select(b=>(SlotView)b.Tag).First(s=>s.Data!=null && Int(s.Data,"MaxAmount")>0 && Int(s.Data,"Amount")>=0);
   SelectExosuitSlot(exoCell);int amount=Int(exoCell.Data,"Amount");int max=Int(exoCell.Data,"MaxAmount");int desired=amount<max?amount+1:Math.Max(0,amount-1);_exoAmount!.Text=desired.ToString(System.Globalization.CultureInfo.InvariantCulture);
   Navigate(new Button{Tag=2},new RoutedEventArgs());if(Int(exoCell.Data,"Amount")!=desired)throw new Exception("Exosuit edit was lost across page switch");checks++;
   Navigate(new Button{Tag=1},new RoutedEventArgs());_exoInventoryKey="Inventory_TechOnly";_exoSelected=null;RenderExosuitInventory();if(_exoTiles.Children.Count<10)throw new Exception("Exosuit technology inventory missing");checks++;
   var techCell=_exoTiles.Children.OfType<Button>().Select(b=>(SlotView)b.Tag).First(s=>s.Activated);
   SelectExosuitSlot(techCell);
   var techMenu=new ContextMenu();
   PopulateInventoryMenu(techMenu,techCell,ExosuitInventoryKind,CommitExosuitPage,RenderExosuitInventory,()=>{});
   techMenu.Items.OfType<MenuItem>().Single(item=>(string)item.Header==(techCell.Supercharged?"Remove Supercharge":"Supercharge Slot")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
   if(_exoSelected?.Supercharged!=!techCell.Supercharged)throw new Exception("Supercharge did not update the selected technology slot");
   checks++;
   Navigate(new Button{Tag=5},new RoutedEventArgs());
   UpdateLayout();
   var records=_vehicleList??throw new Exception("Exocraft records missing.");
   if(records.Items[0] is not RecordEntry firstVehicle || firstVehicle.Title=="Entry 1")
    throw new Exception("First Exocraft is missing its vehicle name.");
   if(_exoTabButtons.Count!=2 || !Equals(_exoTabButtons["Inventory"].Content,"Cargo") ||
      !Equals(_exoTabButtons["Inventory_TechOnly"].Content,"Technology") ||
      !ReferenceEquals(CurrentExoInventory,((NMSE.Models.JsonObject)firstVehicle.Value!).GetObject("Inventory")))
    throw new Exception("Exocraft did not open directly to the Cargo and Technology workspace.");
   checks+=2;
   _exoTabButtons["Inventory_TechOnly"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   UpdateLayout();
   ContextMenu OpenVehicleSlotMenu(string slotName)
   {
    var tile=_exoTiles!.Children.OfType<Button>().First(button=>Equals(button.ToolTip,slotName));
    var menu=tile.ContextMenu!;
    menu.PlacementTarget=tile;
    menu.IsOpen=true;
    return menu;
   }
   void ChooseVehicleMenuItem(ContextMenu menu,string label)
   {
    var action=menu.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,label));
    if(!action.IsEnabled)throw new Exception(label+" is disabled.");
    menu.IsOpen=false;
    action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    UpdateLayout();
   }
   var vehicleTech=_session.Player!.GetArray("VehicleOwnership")!.GetObject(0)!.GetObject("Inventory_TechOnly")!;
   ChooseVehicleMenuItem(OpenVehicleSlotMenu("Locked"),"Enable All Slots");
   if(BuildSlotModels(vehicleTech).Items.Any(slot=>!slot.Activated))
    throw new Exception("Enable All Slots left an Exocraft technology slot locked.");
   ChooseVehicleMenuItem(OpenVehicleSlotMenu("Empty"),"Supercharge All Enabled Slots");
   if(BuildSlotModels(vehicleTech).Items.Any(slot=>!slot.Supercharged))
    throw new Exception("Exocraft technology supercharge did not apply.");
   checks+=2;

   var ownedShip = _session.Player!.GetArray("ShipOwnership")!.GetObject(0)!;
   if (GetShipInventoryEntries(true).Count == 0)
   {
    var archive = new NMSE.Models.JsonArray();
    var archiveRecord = new NMSE.Models.JsonObject();
    var archivedOwnership = ownedShip.DeepClone();
    var seed = new NMSE.Models.JsonArray();
    seed.Add(true);
    archivedOwnership.GetObject("Resource")!.Set("Seed", seed);
    archiveRecord.Set("Ownership", archivedOwnership);
    archiveRecord.Set("ArchivedName", "Archived ship verification");
    archive.Add(archiveRecord);
    _session.Player.Set("ArchivedShipOwnership", archive);
   }
   Navigate(new Button { Tag = 3 }, new RoutedEventArgs());
   UpdateLayout();
   if (!ReferenceEquals(CurrentExoInventory, ownedShip.GetObject("Inventory")) || _exoTiles!.Children.Count == 0)
    throw new Exception("Owned Starships did not open to Cargo.");
   _exoTabButtons["Inventory_TechOnly"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if (!ReferenceEquals(CurrentExoInventory, ownedShip.GetObject("Inventory_TechOnly")))
    throw new Exception("Owned Starships Technology opened the wrong inventory.");
   var archiveTab = FindArchiveTab(MigrationPage) ?? throw new Exception("Archive tab missing.");
   archiveTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   var archivedShip = _selectedShip?.Ownership;
   if (!_showArchive || archivedShip == null ||
       !ReferenceEquals(CurrentExoInventory, archivedShip.GetObject("Inventory_TechOnly") ?? archivedShip.GetObject("Inventory_Tech")))
    throw new Exception("Archive Technology did not open the archived ownership inventory.");
   _exoTabButtons["Inventory"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if (!ReferenceEquals(CurrentExoInventory, archivedShip.GetObject("Inventory_Cargo") ?? archivedShip.GetObject("Inventory")))
    throw new Exception("Archive Cargo opened the wrong inventory.");
   checks += 4;

   static Button? FindArchiveTab(DependencyObject parent)
   {
    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
    {
     var child = VisualTreeHelper.GetChild(parent, i);
     if (child is Button button && Equals(button.Content, "Archive")) return button;
     if (FindArchiveTab(child) is {} nested) return nested;
    }
    return null;
   }

   foreach(int page in new[]{0,1,2})
   {
    Navigate(new Button{Tag=page},new RoutedEventArgs());
    foreach(var size in new[]{new Size(800,500),new Size(1230,670)}){Width=size.Width;Height=size.Height;UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(_selectedPage!=page)throw new Exception("Resizing changed active page");checks++;}
    var capture=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);capture.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(capture));using(var file=File.Create(Path.Combine(output,"page-"+page+".png")))png.Save(file);
   }
   foreach(bool dark in new[]{false,true})
   {
    Themes.Apply(dark);
    foreach(int page in Enumerable.Range(3,MainPageCatalog.Names.Length-3))
    {
     Navigate(new Button{Tag=page},new RoutedEventArgs());UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
     if(_selectedPage!=page || MigrationPage.Child==_migrationPlaceholder)throw new Exception("Page remains a placeholder: "+page);
     if(page<=MainPageCatalog.AccountRewards && page is not (MainPageCatalog.Starships or MainPageCatalog.Exocraft or MainPageCatalog.Milestones or MainPageCatalog.Settlements or MainPageCatalog.AccountRewards) && _domainPages[page].SectionCount==0)throw new Exception("Page has no sections: "+page);
     foreach(var size in new[]{new Size(800,500),new Size(1230,670)}){Width=size.Width;Height=size.Height;UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(MigrationPage.ActualHeight<100)throw new Exception("Page layout collapsed: "+page);checks++;}
     var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(output,$"page-{page}-{(dark?"dark":"light")}.png")))enc.Save(f);
    }
   }
   Navigate(new Button{Tag=0},new RoutedEventArgs());
   foreach(bool dark in new[]{false,true})
   {
    Themes.Apply(dark);
    foreach(int tab in Enumerable.Range(0,5))
    {
     if(!CommitPlayerPage())throw new Exception("Player subtab commit failed.");
     string dataBefore=_session.Data.ToString(),accountBefore=_session.AccountData?.ToString()??"";
     SelectPlayerTab(tab);UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
     if(_playerTabHost?.Child==null)throw new Exception("Player subtab missing: "+tab);
     if(!CommitPlayerPage()||dataBefore!=_session.Data.ToString()||accountBefore!=(_session.AccountData?.ToString()??""))throw new Exception("Browsing Player subtab altered data: "+tab);
     foreach(var size in new[]{new Size(800,500),new Size(1230,670)})
     {
      Width=size.Width;Height=size.Height;UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
      if(_playerTabHost.ActualHeight<100)throw new Exception("Player subtab collapsed: "+tab);
      if(tab==0&&FindVisual<WrapPanel>(_playerTabHost) is {} cards&&cards.ActualWidth>MigrationPage.ActualWidth+1)throw new Exception("General cards do not reflow.");
      checks++;
     }
     if(tab==0&&_playerTabHost.Child is ScrollViewer playerScroll)
     {
      playerScroll.ScrollToBottom();UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
      if(playerScroll.VerticalOffset+playerScroll.ViewportHeight<playerScroll.ExtentHeight-1)throw new Exception("Player save utilities cannot be reached.");
      var bottom=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bottom.Render(this);var bottomPng=new PngBitmapEncoder();bottomPng.Frames.Add(BitmapFrame.Create(bottom));using(var f=File.Create(Path.Combine(output,$"player-general-bottom-{(dark?"dark":"light")}.png")))bottomPng.Save(f);
      playerScroll.ScrollToVerticalOffset(Math.Max(0,playerScroll.ScrollableHeight*0.35));UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
      var middle=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);middle.Render(this);var middlePng=new PngBitmapEncoder();middlePng.Frames.Add(BitmapFrame.Create(middle));using(var f=File.Create(Path.Combine(output,$"player-general-middle-{(dark?"dark":"light")}.png")))middlePng.Save(f);
      playerScroll.ScrollToTop();UpdateLayout();checks++;
     }
     var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(Path.Combine(output,$"player-{tab}-{(dark?"dark":"light")}.png")))encoder.Save(file);
    }
   }
   SelectPlayerTab(0);var coordinateField=_coordinateFields[1];string priorCoordinate=coordinateField.Text;coordinateField.Text=priorCoordinate=="1"?"2":"1";
   string unitsBefore=_playerFields[3].Text;_playerFields[3].Text="4294967296";
   string validationBefore=_session.Data.ToString();
   try{_session.ApplyPlayer(PlayerPageDraft(),true);throw new Exception("Invalid Player draft accepted.");}catch(ArgumentException){}
   if(validationBefore!=_session.Data.ToString())throw new Exception("Player draft validation changed data.");
   _playerFields[3].Text=unitsBefore;
   if(!CommitPlayerPage()||_session.ReadPlayerCoordinates()!.X!=int.Parse(coordinateField.Text))throw new Exception("Player coordinates did not commit.");
   SelectPlayerTab(4);SelectPlayerTab(0);if(_coordinateFields[1].Text!=coordinateField.Text)throw new Exception("Player subtab discarded coordinate edit.");checks+=3;
   if(!sourceHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(originalSource))))throw new Exception("UI checks modified the original save");checks++;
   _session.Load(originalSource);ClearPlayerPageState();ClearExosuitPageState();ClearDomainPageState();ClearUtilityPageState();
   File.WriteAllText(Path.Combine(output,"ui-verification.txt"),$"PASS: {checks} WPF layout, navigation, real inventory/inspector checks; 800/1050/1230 DIP widths; both themes.\nLoaded {_session.Tools.Count} tools.\n");
  }
  catch(Exception ex){File.WriteAllText(Path.Combine(output,"ui-verification.txt"),"FAIL: "+ex);Environment.ExitCode=1;}

  Closing -= OnClosing;
  Close();
 }
}

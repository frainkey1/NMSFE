using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NMSE.IO;
using NMSE.Core;
using NMSE.Data;
using System.Text.RegularExpressions;
using NMSE.Models;
using NMSE.Next.Engine;
namespace NMSE.Next.Desktop;

public sealed record SaveChoice(string Path,string Label){public override string ToString()=>Label;}
public sealed class SlotView
{
 public JsonObject? Data {get;init;}
 public JsonObject? Inventory {get;init;}
 public int X {get;init;}
 public int Y {get;init;}
 public string Name {get;init;}="Empty";
 public ImageSource? Icon {get;init;}
 public string Category {get;init;}="";
 public string Badge {get;init;}="";
 public string AmountLabel {get;init;}="";
 public bool Supercharged {get;init;}
 public bool Activated {get;init;}
 public Brush Background=>!Activated?new SolidColorBrush(Color.FromRgb(69,29,31)):Supercharged?new LinearGradientBrush(Color.FromRgb(255,225,130),Color.FromRgb(183,129,24),90):(Brush)Application.Current.FindResource("Surface");
 public Brush Border=>Activated&&Supercharged?Brushes.Goldenrod:(Brush)Application.Current.FindResource("Edge");
 public Brush LabelBackground=>Activated&&Supercharged?new SolidColorBrush(Color.FromRgb(112,76,14)):new SolidColorBrush(Themes.Dark?Color.FromRgb(28,46,47):Color.FromRgb(3,82,100));
 public Brush LabelForeground=>Themes.Dark?(Brush)Application.Current.FindResource("Text"):Brushes.White;
}
public partial class MainWindow:Window
{
 SaveSession _session;
 readonly Dictionary<string,ImageSource?> _icons=new(StringComparer.OrdinalIgnoreCase);
 int _activeTool=-1;
 SlotView? _selectedSlot;
 bool _binding,_busy;
 internal bool IsBusy=>_busy;
 readonly string[] _pages=MainPageCatalog.Names;
 string SettingsPath=>Path.Combine(AppContext.BaseDirectory,"theme.txt");
 public MainWindow(SaveSession session)
 {
  bool dark=File.Exists(SettingsPath)&&File.ReadAllText(SettingsPath)=="Dark";Themes.Apply(dark);InitializeComponent();
  _session=session;
  InitializeHeaderMenus();
  SlotName.Text=UiStrings.Get("inventory.select_slot");
  Width=Math.Min(1240,SystemParameters.WorkArea.Width-30);Height=Math.Min(800,SystemParameters.WorkArea.Height-35);
  Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;Top=SystemParameters.WorkArea.Top+(SystemParameters.WorkArea.Height-Height)/2;
  _migrationPlaceholder=MigrationPage.Child;
  ToolType.ItemsSource=MultitoolLogic.GetToolTypeItems();ToolType.DisplayMemberPath=nameof(MultitoolLogic.ToolTypeItem.DisplayName);ToolClass.ItemsSource=new[]{"C","B","A","S"};BuildNavigationV2();ApplyActionIcons();
 }
 async void OnLoaded(object sender,RoutedEventArgs e)
 {
  Activate();
  SaveChoice[] files=[];
  try{files=await Task.Run(()=>SaveFileManager.FindDefaultSaveDirectories().SelectMany(d=>SafeFiles(d)).Where(p=>!Path.GetFileName(p).StartsWith("savedata00",StringComparison.OrdinalIgnoreCase)).Select(p=>new SaveChoice(p,Label(p))).OrderBy(x=>x.Label).ToArray());}
  catch(Exception ex){Status.Text=string.Format(UiStrings.Get("ui.save.discovery_unavailable"),ex.Message);}
  FileSelector.ItemsSource=files;FileSelector.SelectedItem=files.FirstOrDefault(x=>Path.GetFileName(x.Path)=="save8.hg")??files.FirstOrDefault();
  var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--open");
  if(at>=0&&at+1<args.Length)await LoadPath(args[at+1]);
  int toolArg=Array.IndexOf(args,"--tool");if(toolArg>=0 && toolArg+1<args.Length && int.TryParse(args[toolArg+1],out int shown))ToolSelector.SelectedItem=_session.Tools.FirstOrDefault(t=>t.Index==shown)??ToolSelector.SelectedItem;
  int pageArg=Array.IndexOf(args,"--page");if(pageArg>=0&&pageArg+1<args.Length&&int.TryParse(args[pageArg+1],out int page)&&page>=0&&page<_pages.Length)SelectNavigation(page);
  RenderSelectedPage();
  if(args.Contains("--verify-accessories"))await VerifyAccessories();
  if(args.Contains("--verify-battle"))await VerifyBattlePreview();
  if(args.Contains("--verify-ui"))await VerifyPreview();
 }
 static string[] SafeFiles(string folder){try{return Directory.GetFiles(folder,"save*.hg");}catch(IOException){return [];}catch(UnauthorizedAccessException){return [];}}
 void SyncLoadedFileChoice()
 {
  if(_session.SourcePath==null)return;
  var choices=(FileSelector.ItemsSource as IEnumerable<SaveChoice>)?.ToList()??[];
  var current=choices.FirstOrDefault(c=>string.Equals(c.Path,_session.SourcePath,StringComparison.OrdinalIgnoreCase));
  if(_session.XboxContainerDirectory!=null){if(current!=null)choices.Remove(current);current=new SaveChoice(_session.SourcePath,"Xbox · "+_session.XboxSlotIdentifier+" · "+Path.GetFileName(_session.XboxContainerDirectory));choices.Add(current);FileSelector.ItemsSource=choices;}
  if(current==null){current=new SaveChoice(_session.SourcePath,Label(_session.SourcePath));choices.Add(current);FileSelector.ItemsSource=choices;}
  FileSelector.SelectedItem=current;SaveButton.ToolTip=string.Format(UiStrings.Get("ui.save.loaded_file"),_session.SourcePath);
 }
 static string Label(string path)
 {
  string name=Path.GetFileName(path);string digits=new(name.Where(char.IsDigit).ToArray());
  return int.TryParse(digits,out int n)?$"Slot {(n+1)/2} · {name} · {Directory.GetParent(path)!.Name}":name;
 }
 void FileChanged(object sender,SelectionChangedEventArgs e){}
 async void LoadFile(object sender,RoutedEventArgs e){if(FileSelector.SelectedItem is SaveChoice file){if(_consoleChoices.TryGetValue(file.Path,out var choice)&&choice.Directory!=null)await LoadXboxPath(choice.Directory,choice.Identifier!);else if(_consoleChoices.TryGetValue(file.Path,out choice))await LoadConsolePath(file.Path,choice.Platform);else await LoadPath(file.Path);}}
 bool ConfirmDiscard()=>!HasPendingChanges()||MessageBox.Show(this,UiStrings.LocalizeText("Discard the current unsaved changes?"),UiStrings.LocalizeText("Unsaved changes"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
 async Task LoadPath(string path,SaveFileManager.Platform? consolePlatform=null,string? xboxIdentifier=null,bool discardConfirmed=false)
 {
  if(_busy||(!discardConfirmed&&!ConfirmDiscard()))return;
  _busy=true;LoadButton.IsEnabled=false;SaveButton.IsEnabled=false;ToolPage.IsEnabled=false;MigrationPage.IsEnabled=false;Status.Text=UiStrings.Get("ui.dynamic.450e737f4206");
  try
  {
   var next=new SaveSession(_session.ResourceRoot);
   await Task.Run(()=>{if(consolePlatform==SaveFileManager.Platform.XboxGamePass)next.LoadXbox(path,xboxIdentifier!);else if(consolePlatform is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch)next.LoadConsoleFile(path,consolePlatform.Value);else next.Load(path);next.LoadDatabase();});
   if(next.ConsolePlatform is {} loadedPlatform&&next.SourcePath!=null)_consoleChoices[next.SourcePath]=(loadedPlatform,next.XboxContainerDirectory,next.XboxSlotIdentifier);
   _session=next;ApplyLanguagePreference(rebuildMenus:false);SyncLoadedFileChoice();ClearPlayerPageState();ClearExosuitPageState();ClearDomainPageState();ClearUtilityPageState();_viewRevision=_session.DocumentRevision;ClearEditor();
   _binding=true;_activeTool=-1;ToolSelector.ItemsSource=_session.Tools;
   ToolSelector.SelectedItem=_session.Tools.FirstOrDefault(x=>x.Index==_session.PrimaryIndex)??_session.Tools.FirstOrDefault();_binding=false;
   if(ToolSelector.SelectedItem is ToolEntry tool)ShowTool(tool.Index);
   SaveButton.IsEnabled=true;Status.Text=UiStrings.Format("ui.dynamic.4df9a7f35b33",Path.GetFileName(path),_session.Tools.Count);
  }
  catch(Exception ex){Error(ex);}finally{_binding=false;_busy=false;LoadButton.IsEnabled=true;ToolPage.IsEnabled=_activeTool>=0;MigrationPage.IsEnabled=true;SaveButton.IsEnabled=_session.SourcePath!=null;SyncLoadedFileChoice();UpdateHeaderMenuState();ConfigureExternalWatcher();RenderSelectedPage();}
 }
 void ClearEditor()
 {
  _activeTool=-1;_selectedSlot=null;Slots.ItemsSource=null;DetailIcon.Source=null;
  ToolName.Text=ToolSeed.Text=Damage.Text=Mining.Text=Scan.Text=SlotAmount.Text=SlotMaximum.Text=SlotDamage.Text="";
  ToolType.SelectedIndex=ToolClass.SelectedIndex=-1;SlotName.Text=UiStrings.Get("inventory.select_slot");SlotCategory.Text="—";
  PrimaryLabel.Text="No owned multi-tools";EmptyHint.Text="This save contains no owned multi-tools supported by this milestone.";EmptyHint.Visibility=Visibility.Visible;
 }
 ToolDraft Draft()=>new(ToolName.Text,ToolType.SelectedIndex,ToolClass.SelectedIndex,ToolSeed.Text,Damage.Text,Mining.Text,Scan.Text);
 bool HasPendingChanges()=>ShipDetailsPending||DomainPagesHavePendingChanges||_statsPage?.HasPending==true||UtilityPagesHavePendingChanges||PlayerPageHasPendingChanges||ExosuitPageHasPendingChanges||_session.Dirty||_activeTool>=0&&Draft()!=_session.ReadTool(_activeTool)||SlotPending();
 bool SlotPending()=>_selectedSlot?.Data is {} s&&(SlotAmount.Text!=Int(s,"Amount").ToString(CultureInfo.InvariantCulture)||SlotMaximum.Text!=Int(s,"MaxAmount").ToString(CultureInfo.InvariantCulture)||SlotDamage.Text!=Double(s,"DamageFactor").ToString("R",CultureInfo.InvariantCulture));
 bool CommitDraft()
 {
  if(_activeTool<0)return true;
  try{_session.ApplyTool(_activeTool,Draft());if(SlotPending())CommitSlot();return true;}catch(Exception ex){Error(ex);return false;}
 }
 void ToolChanged(object sender,SelectionChangedEventArgs e)
 {
  if(_binding)return;
  if(!CommitDraft()){_binding=true;ToolSelector.SelectedItem=_session.Tools.FirstOrDefault(x=>x.Index==_activeTool);_binding=false;return;}
  if(ToolSelector.SelectedItem is ToolEntry tool)ShowTool(tool.Index);
 }
 void ShowTool(int index)
 {
  _activeTool=index;var t=_session.ReadTool(index);ToolName.Text=t.Name;ToolType.SelectedIndex=t.TypeIndex;ToolClass.SelectedIndex=t.ClassIndex;ToolSeed.Text=t.Seed;Damage.Text=t.Damage;Mining.Text=t.Mining;Scan.Text=t.Scan;
  PrimaryLabel.Text=index==_session.PrimaryIndex?"Primary":"Secondary";
  RenderInventory();
 }
 static int Int(JsonObject? o,string key){try{return o?.GetInt(key)??0;}catch{return 0;}}
 static double Double(JsonObject? o,string key){try{return o?.GetDouble(key)??0;}catch{return 0;}}
 internal ImageSource? LoadIcon(string? filename)
 {
  if(string.IsNullOrWhiteSpace(filename))return null;
  if(_icons.TryGetValue(filename,out var cached))return cached;
  var path=Path.GetFullPath(Path.Combine(_session.ResourceRoot,"images",filename));var images=Path.GetFullPath(Path.Combine(_session.ResourceRoot,"images"))+Path.DirectorySeparatorChar;
  if(!path.StartsWith(images,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return _icons[filename]=null;
  var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=144;image.UriSource=new Uri(path);image.EndInit();image.Freeze();return _icons[filename]=image;
 }
 void RenderInventory()
 {
  var previousSelection=_selectedSlot;
  var inventory=_session.Inventory(_activeTool);
  ToolInventoryResize.Content=BuildInventoryResizeControls(inventory,10,6,CommitDraft,RenderInventory,compact:true);
  var (items,width,height)=BuildSlotModels(inventory);
  Slots.ItemsSource=items;Slots.Height=height*72;Slots.ApplyTemplate();
  if(FindVisual<System.Windows.Controls.Primitives.UniformGrid>(Slots) is {} grid)grid.Columns=width;
  EmptyHint.Visibility=items.Any(s=>s.Data!=null)?Visibility.Collapsed:Visibility.Visible;
  _selectedSlot=null;SlotName.Text=UiStrings.Get("inventory.select_slot");DetailIcon.Source=null;SlotCategory.Text="—";SlotAmount.Text=SlotMaximum.Text=SlotDamage.Text="";
  ToolItemPicker.Content=BuildItemPicker(null,ToolInventoryKind,CommitDraft,RenderInventory);
  if(previousSelection!=null && ReferenceEquals(previousSelection.Inventory,inventory) && items.FirstOrDefault(x=>x.X==previousSelection.X && x.Y==previousSelection.Y) is {} retained)SelectSlot(new Button{Tag=retained},new RoutedEventArgs());
 }
 (List<SlotView> Items,int Width,int Height) BuildSlotModels(JsonObject? store)
 {
  var slots=store?.GetArray("Slots");int width=Math.Clamp(Int(store,"Width"),1,20),height=Math.Clamp(Int(store,"Height"),1,20);
  var byPosition=new Dictionary<(int,int),JsonObject>();
  if(slots!=null)for(int i=0;i<slots.Length;i++)if(slots.GetObject(i) is {} s)byPosition[(Int(s.GetObject("Index"),"X"),Int(s.GetObject("Index"),"Y"))]=s;
  var valid=new HashSet<(int,int)>();var validIndices=store?.GetArray("ValidSlotIndices");if(validIndices!=null)for(int i=0;i<validIndices.Length;i++)if(validIndices.GetObject(i) is {} p)valid.Add((Int(p,"X"),Int(p,"Y")));
  var super=new HashSet<(int,int)>();var special=store?.GetArray("SpecialSlots");if(special!=null)for(int i=0;i<special.Length;i++)if(special.GetObject(i) is {} entry && entry.GetObject("Type")?.GetString("InventorySpecialSlotType")=="TechBonus" && entry.GetObject("Index") is {} p)super.Add((Int(p,"X"),Int(p,"Y")));
  var items=new List<SlotView>();
  for(int y=0;y<height;y++)for(int x=0;x<width;x++)
  {
   byPosition.TryGetValue((x,y),out var s);string id=SaveSession.InventoryItemId(s);string baseId=Regex.Replace(id,@"#\d+$","");var item=_session.Items.GetItem(baseId);string? displayName=item==null?null:_session.GameLocalisation.GetName(item);
   if(s?.Get("Id") is BinaryData)
   {
    if(TechPackLogic.TryResolveTechId(id,_session.Items,out var techId,out _) && _session.Items.GetItem(techId) is {} tech)
    {item=tech;displayName=_session.GameLocalisation.GetName(tech)+" "+UiStrings.LocalizeText("(Packaged)");}
    else displayName=UiStrings.LocalizeText("Packaged Technology");
   }
   var variant=Regex.Match(id,@"#(\d+)$");if(item?.IsProcedural==true && variant.Success)displayName=ProcTechLogic.GenerateTechName(item,variant.Groups[1].Value,_session.GameLocalisation)??displayName;
   if(!string.IsNullOrWhiteSpace(displayName))displayName=UiStrings.LocalizeText(displayName);
   bool active=valid.Contains((x,y))||s!=null;bool charged=super.Contains((x,y));
   items.Add(new(){Data=s,Inventory=store,X=x,Y=y,Name=displayName??(id.Length>0?id:active?UiStrings.Get("inventory.empty_slot"):UiStrings.Get("inventory.locked_slot")),Icon=LoadIcon(item?.Icon),Category=UiStrings.LocalizeText(item?.Category??""),Supercharged=charged,Activated=active,Badge=active&&charged?"✦":"",AmountLabel=s==null||Int(s,"Amount")<0?"":Int(s,"MaxAmount")>0?$"{Int(s,"Amount")} / {Int(s,"MaxAmount")}":""});
  }
  return (items,width,height);
 }
 static T? FindVisual<T>(DependencyObject d)where T:DependencyObject{if(d is T t)return t;for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++)if(FindVisual<T>(VisualTreeHelper.GetChild(d,i)) is {} found)return found;return null;}
 void SelectSlot(object sender,RoutedEventArgs e)
 {
  try{if(SlotPending())CommitSlot();}catch(Exception ex){Error(ex);return;}
  _selectedSlot=(SlotView)((Button)sender).Tag;
  ToolItemPicker.Content=BuildItemPicker(_selectedSlot,ToolInventoryKind,CommitDraft,RenderInventory);
  SlotName.Text=_selectedSlot.Name;DetailIcon.Source=_selectedSlot.Icon;SlotCategory.Text=_selectedSlot.Category;
  var s=_selectedSlot.Data;SlotAmount.Text=Int(s,"Amount").ToString(CultureInfo.InvariantCulture);SlotMaximum.Text=Int(s,"MaxAmount").ToString(CultureInfo.InvariantCulture);SlotDamage.Text=Double(s,"DamageFactor").ToString("R",CultureInfo.InvariantCulture);
 }
 void CommitSlot()
 {
  if(_selectedSlot?.Data is not {} s)throw new ArgumentException("Select an occupied inventory slot to edit.");
  if(!int.TryParse(SlotAmount.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int amount)||!int.TryParse(SlotMaximum.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int maximum)||!double.TryParse(SlotDamage.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double damage))throw new ArgumentException("Enter valid numeric slot values.");
  _session.ApplySlot(_activeTool,s,amount,maximum,damage);
 }
 void ApplySlot(object sender,RoutedEventArgs e){try{CommitSlot();RenderInventory();Status.Text=UiStrings.LocalizeText("Slot changes applied in memory.");}catch(Exception ex){Error(ex);}}
 void ApplyTool(object sender,RoutedEventArgs e){if(!CommitDraft())return;RefreshSelector();Status.Text=UiStrings.LocalizeText("Tool changes applied in memory.");}
 void RefreshSelector(){_binding=true;ToolSelector.ItemsSource=_session.Tools.ToArray();ToolSelector.SelectedItem=ToolSelector.Items.Cast<ToolEntry>().FirstOrDefault(x=>x.Index==_activeTool);_binding=false;PrimaryLabel.Text=_activeTool==_session.PrimaryIndex?"Primary":"Secondary";}
 void GenerateSeed(object sender,RoutedEventArgs e)=>ToolSeed.Text="0x"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
 void MakePrimary(object sender,RoutedEventArgs e){if(_activeTool<0||!CommitDraft())return;_session.MakePrimary(_activeTool);PrimaryLabel.Text="Primary";Status.Text=UiStrings.LocalizeText("Primary tool changed in memory.");}
 async void SaveCurrent(object sender,RoutedEventArgs e)
 {
  if(_busy||_session.SourcePath==null||!CommitAllEdits())return;
  _busy=true;ToolPage.IsEnabled=false;MigrationPage.IsEnabled=false;SaveButton.IsEnabled=false;LoadButton.IsEnabled=false;Status.Text=UiStrings.Get("ui.dynamic.94b5c009863d");
  try{var result=await Task.Run(()=>_session.Save());Status.Text=string.Format(UiStrings.Get("ui.backup.saved"),Path.GetFileName(result.Path),result.BackupDirectory);ConfigureExternalWatcher();}
  catch(Exception ex){Error(ex);}finally{_busy=false;ToolPage.IsEnabled=_activeTool>=0;MigrationPage.IsEnabled=true;SaveButton.IsEnabled=_session.SourcePath!=null;LoadButton.IsEnabled=true;UpdateHeaderMenuState();}
 }
 void ExportTool(object sender,RoutedEventArgs e)
 {
  if(_activeTool<0||!CommitDraft())return;var d=new SaveFileDialog{Filter=UiStrings.Get("multitool.tool_json_filter"),FileName="multitool.json"};if(d.ShowDialog(this)==true){try{File.WriteAllText(d.FileName,_session.Tool(_activeTool).ToFormattedString());Status.Text=UiStrings.LocalizeText("Exported tool JSON.");}catch(Exception ex){Error(ex);}}
 }
 void GoToMultitoolListJson(object sender,RoutedEventArgs e)=>NavigateToRawJsonPath("PlayerStateData","Multitools");
 void GoToMultitoolJson(object sender,RoutedEventArgs e){if(_activeTool>=0)NavigateToRawJsonPath("PlayerStateData","Multitools",$"[{_activeTool}]");}
 void GoToMultitoolStoreJson(object sender,RoutedEventArgs e){if(_activeTool>=0)NavigateToRawJsonPath("PlayerStateData","Multitools",$"[{_activeTool}]","Store");}
 void ToggleTheme(object sender,RoutedEventArgs e)
 {
  if(_busy)return;
  if(!CommitAllEdits())return;
  Themes.Apply(!Themes.Dark);ApplyHeaderMenuPalette();BuildNavigationV2();ApplyActionIcons();if(_activeTool>=0)RenderInventory();RenderSelectedPage();
  try{File.WriteAllText(SettingsPath,Themes.Dark?"Dark":"Light");}catch(IOException){}
 }
 UIElement? _migrationPlaceholder;
 bool CommitAllEdits()
 {
  if(!CommitUtilityPages())return false;
  if(_viewRevision!=_session.DocumentRevision)
  {
   ClearPlayerPageState();ClearExosuitPageState();ClearDomainPageState();
   int priorTool=_activeTool;_binding=true;ClearEditor();ToolSelector.ItemsSource=_session.Tools;ToolSelector.SelectedItem=_session.Tools.FirstOrDefault(t=>t.Index==priorTool)??_session.Tools.FirstOrDefault();_binding=false;
   if(ToolSelector.SelectedItem is ToolEntry tool)ShowTool(tool.Index);
   _viewRevision=_session.DocumentRevision;
  }
  return CommitPlayerPage() && CommitShipDetails() && CommitExosuitPage() && CommitDomainPages() && (_statsPage?.Commit()??true) && (_selectedPage!=MainPageCatalog.MultiTools||CommitDraft());
 }
 void Navigate(object sender,RoutedEventArgs e)
 {
  if(_busy||!CommitAllEdits())return;
  SelectNavigation((int)((Button)sender).Tag);RenderSelectedPage();
 }
 void RenderSelectedPage()
 {
  if(_selectedPage==MainPageCatalog.MultiTools){MigrationPage.Visibility=Visibility.Collapsed;Workspace.Visibility=Visibility.Visible;return;}
  MigrationPage.Padding=new Thickness(8);
  if(_selectedPage==MainPageCatalog.Player){ShowPlayerPage();return;}
  if(_selectedPage is MainPageCatalog.Exosuit or MainPageCatalog.Starships or MainPageCatalog.Exocraft or MainPageCatalog.Corvettes){ShowExosuitPage(_selectedPage);return;}
  if(_selectedPage<=MainPageCatalog.AccountRewards)ShowDomainPage(_selectedPage);else ShowUtilityPage(_selectedPage);
 }
 void ReturnToTools(object sender,RoutedEventArgs e){if(_busy||!CommitAllEdits())return;SelectNavigation(2);RenderSelectedPage();}
 void ApplyActionIcons()
 {
  SetAction(SaveButton,"Save","Save",true);SetAction(LoadButton,"Load","Load");SetAction(OpenButton,"Open","Open…");SetAction(GenerateButton,"Generate","Generate");
  ToolPageIcon.Content=ConceptIcons.Make("Multi-tools",24);
 }
 void SetAction(Button button,string icon,string label,bool primary=false)
 {
  var row=new StackPanel{Orientation=Orientation.Horizontal};var mark=ConceptIcons.Make(icon,17,(Brush)FindResource(primary?"OnAccent":"Text"));mark.Margin=new Thickness(0,0,6,0);row.Children.Add(mark);row.Children.Add(new TextBlock{Text=label});button.Content=row;
 }
 void Minimize(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 void Maximize(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
 void CloseWindow(object sender,RoutedEventArgs e)=>Close();
 void OnClosing(object? sender,CancelEventArgs e){if(_busy||!ConfirmDiscard())e.Cancel=true;else StopExternalWatcher();}
 void Error(Exception ex){string message=UiStrings.LocalizeText(ex.Message);Status.Text=message;MessageBox.Show(this,message,"NMSE.Next",MessageBoxButton.OK,MessageBoxImage.Warning);}
}

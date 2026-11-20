using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NMSE.Data;
using NMSE.IO;
namespace NMSE.Next.Desktop;
public partial class MainWindow
{
 readonly Dictionary<string,(SaveFileManager.Platform Platform,string? Directory,string? Identifier)> _consoleChoices=new(StringComparer.OrdinalIgnoreCase);
 Task LoadXboxPath(string directory,string identifier)=>LoadPath(directory,SaveFileManager.Platform.XboxGamePass,identifier);
 Task LoadConsolePath(string path,SaveFileManager.Platform platform)=>LoadPath(path,platform);
 void OpenFile(object sender,RoutedEventArgs e)=>ShowSaveOpenMenu();
 void ShowSaveOpenMenu()
 {
  if(_busy)return;
  var menu=new ContextMenu{PlacementTarget=OpenButton,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
  menu.SetResourceReference(Control.BackgroundProperty,"Panel");menu.SetResourceReference(Control.ForegroundProperty,"Text");
  void Item(string text,Action action){var item=new MenuItem{Header=text,Padding=new Thickness(12,8,12,8)};item.SetResourceReference(Control.BackgroundProperty,"Panel");item.SetResourceReference(Control.ForegroundProperty,"Text");item.Click+=(_,_)=>action();menu.Items.Add(item);}
  async void File(SaveFileManager.Platform? platform)
  {
   var dialog=new OpenFileDialog{Filter=UiStrings.LocalizeText(platform==null?"NMS save or JSON|*.hg;*.json|All files|*.*":"Extracted console saves|*.hg;*.dat|All files|*.*"),Title=UiStrings.LocalizeText(platform==null?"Open a save":"Open extracted "+platform+" game save")};
   if(dialog.ShowDialog(this)!=true)return;
   if(platform!=null)await LoadConsolePath(dialog.FileName,platform.Value);
   else
   {
    string name=Path.GetFileName(dialog.FileName);var dir=Path.GetDirectoryName(dialog.FileName)!;
    var detected=SaveFileManager.DetectPlatform(dir);
    if(name.StartsWith("savedata",StringComparison.OrdinalIgnoreCase)&&name.EndsWith(".hg",StringComparison.OrdinalIgnoreCase))await LoadConsolePath(dialog.FileName,detected==SaveFileManager.Platform.Switch?SaveFileManager.Platform.Switch:SaveFileManager.Platform.PS4);
    else if(SaveFileManager.IsNomanSkyFile(dialog.FileName))await LoadConsolePath(dialog.FileName,SaveFileManager.Platform.PS4);
    else await LoadPath(dialog.FileName);
   }
  }
  Item("PC save / JSON…",()=>File(null));Item("PlayStation — extracted save…",()=>File(SaveFileManager.Platform.PS4));Item("Nintendo Switch — extracted save…",()=>File(SaveFileManager.Platform.Switch));Item("Xbox / Game Pass — WGS or XGS container…",OpenXboxContainer);
  menu.IsOpen=true;
 }
 async void OpenXboxContainer()
 {
  if(_busy)return;
  var folder=new OpenFolderDialog{Title=UiStrings.LocalizeText("Choose an existing Xbox WGS or XGS save container"),Multiselect=false};if(folder.ShowDialog(this)!=true)return;
  try
  {
   var slots=SaveFileManager.EnumerateXboxSlots(folder.FolderName).Keys.Where(k=>k.StartsWith("Slot",StringComparison.OrdinalIgnoreCase)).OrderBy(k=>k,StringComparer.OrdinalIgnoreCase).ToArray();
   if(slots.Length==0)throw new InvalidDataException("No existing game slots were found in that container.");
   var body=new StackPanel{Margin=new Thickness(18)};body.Children.Add(new TextBlock{Text="Choose Xbox game save",FontSize=18,Margin=new Thickness(3,0,3,10)});var selected=new ComboBox{ItemsSource=slots,SelectedIndex=0,MinWidth=280};body.Children.Add(selected);
   var hint=new TextBlock{Text="Edits keep the container’s existing slot identity and include verified backups.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,10,3,10)};body.Children.Add(hint);var actions=new WrapPanel();body.Children.Add(actions);
   var dialog=new Window{Owner=this,Title="Xbox save slots",Content=body,SizeToContent=SizeToContent.WidthAndHeight,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};dialog.SetResourceReference(BackgroundProperty,"Panel");dialog.SetResourceReference(ForegroundProperty,"Text");
   var load=new Button{Content="Load selected slot",IsDefault=true};var cancel=new Button{Content="Cancel",IsCancel=true};load.Click+=(_,_)=>dialog.DialogResult=true;actions.Children.Add(load);actions.Children.Add(cancel);
   if(dialog.ShowDialog()==true&&selected.SelectedItem is string identifier)await LoadXboxPath(folder.FolderName,identifier);
  }catch(Exception ex){Error(ex);}
 }
}

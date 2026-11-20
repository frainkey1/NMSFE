using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NMSE.Data;
using NMSE.IO;
using NMSE.Next.Engine;

namespace NMSE.Next.Desktop;
public partial class MainWindow
{
 sealed record UtilityPlatform(SaveFileManager.Platform Value,string Label){public override string ToString()=>UiStrings.LocalizeText(Label);}
 FrameworkElement BuildPlayerSaveUtilities()
 {
  var body=new StackPanel();
  TextBlock Text(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,5,4,5)};
  body.Children.Add(new TextBlock{Text="Advanced save utilities",Style=(Style)FindResource("Heading")});
  body.Children.Add(Text("Close No Man’s Sky and pause cloud sync first. Copy, Move, Swap and Delete affect both auto and manual saves in the selected game slots. Exact backups are kept in NMSE.Next-backups before changes; Xbox operations back up the entire container, including its index and metadata."));
  body.Children.Add(Text("These actions use saved files on disk. Apply only changes memory; save or reload pending edits first. PS4 and Switch require extracted/decrypted savedata files. Xbox requires an accessible WGS/XGS container. Encrypted retail PS5 saves, account migration, and mobile-specific formats are not supported."));
  var choices=new[]{new UtilityPlatform(SaveFileManager.Platform.Steam,"Steam / PC-compatible"),new UtilityPlatform(SaveFileManager.Platform.GOG,"GOG"),new UtilityPlatform(SaveFileManager.Platform.XboxGamePass,"Xbox WGS / XGS"),new UtilityPlatform(SaveFileManager.Platform.PS4,"PS4 · extracted saves"),new UtilityPlatform(SaveFileManager.Platform.Switch,"Switch · extracted saves")};
  var directory=_session.SourcePath==null?null:(Directory.Exists(_session.SourcePath)?_session.SourcePath:Path.GetDirectoryName(_session.SourcePath));
  if(directory!=null&&PlayerSlotOperations.Detect(directory)==SaveFileManager.Platform.Unknown)
  {
   var parent=Path.GetDirectoryName(directory);if(parent!=null&&PlayerSlotOperations.Detect(parent)==SaveFileManager.Platform.XboxGamePass)directory=parent;
  }
  var pathLabel=Text(directory??"Choose a source save directory.");body.Children.Add(pathLabel);
  var folderRow=new WrapPanel();body.Children.Add(folderRow);
  var chooseSource=new Button{Content="Choose source directory…",Margin=new Thickness(4)};folderRow.Children.Add(chooseSource);
  var sourcePlatform=new ComboBox{Width=220,ItemsSource=choices,Margin=new Thickness(4)};folderRow.Children.Add(sourcePlatform);
  var detected=directory==null?SaveFileManager.Platform.Unknown:PlayerSlotOperations.Detect(directory);
  sourcePlatform.SelectedItem=choices.FirstOrDefault(c=>c.Value==detected);
  var row=new WrapPanel();body.Children.Add(row);
  ComboBox Slot(string title)
  {
   var panel=new StackPanel{Margin=new Thickness(4)};panel.Children.Add(Text(title));
   var box=new ComboBox{Width=165,ItemsSource=Enumerable.Range(1,15).ToArray(),SelectedIndex=0};
   panel.Children.Add(box);row.Children.Add(panel);return box;
  }
  var source=Slot("Source game slot");var destination=Slot("Destination game slot");destination.SelectedIndex=1;
  var operations=new WrapPanel{Margin=new Thickness(0,8,0,8)};body.Children.Add(operations);
  body.Children.Add(Text("Choose the matching format above. PS4/Switch extracted files share names, so confirm that choice. Steam / PC-compatible is only for files using the established save.hg + mf_save.hg layout; it does not imply a separately verified Epic or mobile format."));
  var transferText=Text("Transfer converts the selected source slot’s manual save (or auto if manual is absent) into the destination manual save. Xbox targets an existing slot, preferring manual and falling back to auto. Ownership identities are preserved; destination account ownership is not rewritten.");body.Children.Add(transferText);
  var transferRow=new WrapPanel();body.Children.Add(transferRow);
  var targetPlatform=new ComboBox{Width=220,ItemsSource=choices,SelectedIndex=0,Margin=new Thickness(4)};transferRow.Children.Add(targetPlatform);
  var transfer=new Button{Content="Transfer saved slot…",Margin=new Thickness(4)};transferRow.Children.Add(transfer);
  void Refresh(){operations.IsEnabled=directory!=null&&sourcePlatform.SelectedItem is UtilityPlatform;transfer.IsEnabled=operations.IsEnabled;pathLabel.Text=directory??"Choose a source save directory.";}
  sourcePlatform.SelectionChanged+=(_,_)=>Refresh();
  chooseSource.Click+=(_,_)=>{var dialog=new OpenFolderDialog{Title=UiStrings.LocalizeText("Choose save directory (Xbox: container root; PS4/Switch: extracted files)")};if(dialog.ShowDialog(this)!=true)return;directory=dialog.FolderName;var found=PlayerSlotOperations.Detect(directory);sourcePlatform.SelectedItem=choices.FirstOrDefault(c=>c.Value==found);Refresh();};
  bool Ready()
  {
   if(_busy||!CommitAllEdits())return false;
   if(_session.Dirty){MessageBox.Show(this,UiStrings.LocalizeText("Save your pending changes to the loaded file, or reload it to discard them, before running disk utilities."),UiStrings.LocalizeText("Unsaved changes"),MessageBoxButton.OK,MessageBoxImage.Warning);return false;}
   return true;
  }
  async System.Threading.Tasks.Task Finish(PlayerSlotResult result,string destinationDirectory,SaveFileManager.Platform destinationFormat)
  {
   var loaded=_session.SourcePath;
   if(_session.XboxContainerDirectory is {} container&&_session.XboxSlotIdentifier is {} identifier)
   {
    // Container writes can replace physical GUID blob paths while keeping slot identity.
    if(SaveFileManager.EnumerateXboxSlots(container).ContainsKey(identifier))
    {
     await LoadXboxPath(container,identifier);Status.Text=string.Format(UiStrings.LocalizeText("Disk operation complete. Exact backups: {0}"),result.BackupDirectory);return;
    }
   }
   else if(loaded!=null&&File.Exists(loaded))
   {
    if(_session.ConsolePlatform is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch)await LoadConsolePath(loaded,_session.ConsolePlatform.Value);
    else await LoadPath(loaded);
    Status.Text=string.Format(UiStrings.LocalizeText("Disk operation complete. Exact backups: {0}"),result.BackupDirectory);return;
   }
   if(result.SuggestedLoadPath is {} next&&File.Exists(next))
   {
    if(destinationFormat==SaveFileManager.Platform.XboxGamePass)
    {
     var slot=SaveFileManager.EnumerateXboxSlots(destinationDirectory).Values.FirstOrDefault(s=>string.Equals(s.DataFilePath,next,StringComparison.OrdinalIgnoreCase))
      ??throw new IOException("The destination Xbox slot could not be resolved after the disk operation.");
     await LoadXboxPath(destinationDirectory,slot.Identifier);
    }
    else if(destinationFormat is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch)await LoadConsolePath(next,destinationFormat);
    else await LoadPath(next);
   }
   else MessageBox.Show(this,string.Format(UiStrings.LocalizeText("Disk files changed. Open the desired save before editing again. Exact backups:\n{0}"),result.BackupDirectory),UiStrings.LocalizeText("Disk operation complete"),MessageBoxButton.OK,MessageBoxImage.Information);
   Status.Text=string.Format(UiStrings.LocalizeText("Disk operation complete. Exact backups: {0}"),result.BackupDirectory);
  }
  foreach(var action in Enum.GetValues<PlayerSlotAction>())
  {
   var button=new Button{Content=action.ToString()+" slot",Margin=new Thickness(4)};operations.Children.Add(button);
   button.Click+=async(_,_)=>{try
   {
    if(!Ready()||directory==null||sourcePlatform.SelectedItem is not UtilityPlatform selected)return;
    var platform=selected.Value;int src=(int)source.SelectedItem,dst=(int)destination.SelectedItem;
    if(action==PlayerSlotAction.Delete&&PlayerSlotOperations.ContainsLoadedFile(directory,src,platform,_session.SourcePath))
     throw new InvalidOperationException("Open a save from a different slot before deleting the currently loaded slot.");
    string actionText=UiStrings.LocalizeText(action.ToString());var detail=action==PlayerSlotAction.Delete?string.Format(UiStrings.LocalizeText("Delete source slot {0}, including both save halves?"),src):string.Format(UiStrings.LocalizeText("{0} source slot {1} to destination slot {2}? Destination files may be overwritten."),actionText,src,dst);
    if(MessageBox.Show(this,string.Format(UiStrings.LocalizeText("{0}\n{1}\n\nThis changes disk files immediately. Exact backups will be created first."),detail,directory),string.Format(UiStrings.LocalizeText("Confirm {0}"),actionText),MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
    await Finish(PlayerSlotOperations.Execute(directory,src,dst,platform,action),directory,platform);
   }catch(Exception ex){Error(ex);}};
  }
  transfer.Click+=async(_,_)=>{try
  {
   if(!Ready()||directory==null||sourcePlatform.SelectedItem is not UtilityPlatform selected||targetPlatform.SelectedItem is not UtilityPlatform target)return;
   var folder=new OpenFolderDialog{Title=UiStrings.LocalizeText("Choose destination save directory (Xbox requires an existing game slot)")};if(folder.ShowDialog(this)!=true)return;
   int src=(int)source.SelectedItem,dst=(int)destination.SelectedItem;
   if(MessageBox.Show(this,string.Format(UiStrings.LocalizeText("Transfer saved source slot {0} to {1} destination slot {2}?\n{3}\n\nThe destination manual save (Xbox: manual or auto) may be overwritten immediately. Source and destination files will be backed up. Ownership remains unchanged."),src,UiStrings.LocalizeText(target.Label),dst,folder.FolderName),UiStrings.LocalizeText("Confirm transfer"),MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
   await Finish(PlayerSlotOperations.Transfer(directory,src,folder.FolderName,dst,selected.Value,target.Value),folder.FolderName,target.Value);
  }catch(Exception ex){Error(ex);}};
  Refresh();
  return new Border{Style=(Style)FindResource("Card"),Child=body};
 }
}


using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NMSE.IO;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
 string? _consoleSource,_xboxContainer;
 public string? XboxContainerDirectory=>IsConsoleSession?_xboxContainer:null;
 public string? XboxSlotIdentifier {get;private set;}
 public SaveFileManager.Platform? ConsolePlatform {get;private set;}
 string[] _consoleRoots=[];
 Dictionary<string,string>? _consoleBaseline;
 internal Action<int>? ConsoleCommitProbe;
 internal Action<string>? ConsoleRestoreProbe;
 void ClearConsoleIdentity(){_consoleSource=null;_xboxContainer=null;XboxSlotIdentifier=null;ConsolePlatform=null;_consoleRoots=[];_consoleBaseline=null;ConsoleCommitProbe=null;ConsoleRestoreProbe=null;}
 void DisableConsoleAccount(){AccountData=null;AccountPath=null;AccountDirty=false;_accountHash=null;AccountLoadMessage="Account rewards are unavailable for console save transactions.";}
 bool IsConsoleSession=>ConsolePlatform!=null&&SourcePath==_consoleSource;
 /// <summary>Load only a decrypted/extracted PS4 streaming or Switch game-save file.</summary>
 public void LoadConsoleFile(string path,SaveFileManager.Platform platform)
 {
  if(platform is not (SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch))throw new ArgumentException("Choose PS4 or Switch extracted format.");
  path=Path.GetFullPath(path);
  if(Path.GetFileName(path).Equals("memory.dat",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("Monolithic PS4 memory.dat editing is not supported. Extract a slot first.");
  if(!Regex.IsMatch(Path.GetFileName(path),@"^savedata\d+\.hg$",RegexOptions.IgnoreCase)&&!SaveFileManager.IsNomanSkyFile(path))throw new NotSupportedException("Choose an extracted savedataNN.hg file or decrypted NOMANSKY save.");
  string[] roots=[Path.GetDirectoryName(path)!];var before=ConsoleSnapshot(roots);
  Load(path);
  ConsoleAssertSnapshot(roots,before);
  DisableConsoleAccount();
  _consoleSource=SourcePath;ConsolePlatform=platform;_xboxContainer=null;XboxSlotIdentifier=null;_consoleRoots=roots;_consoleBaseline=before;
 }
 /// <summary>Load an existing Xbox PC WGS/XGS slot by its stable identifier.</summary>
 public void LoadXbox(string containerDirectory,string slotIdentifier)
 {
  string root=Path.GetFullPath(containerDirectory);
  if(File.Exists(root)&&Path.GetFileName(root).Equals("containers.index",StringComparison.OrdinalIgnoreCase))root=Path.GetDirectoryName(root)!;
  if(!Regex.IsMatch(slotIdentifier,@"^Slot[1-9]\d*(Auto|Manual)$",RegexOptions.IgnoreCase))throw new ArgumentException("Choose an existing game slot such as Slot1Auto.");
  if(SaveFileManager.DetectXboxSaveFormat(root)==SaveFileManager.XboxSaveFormat.None)throw new InvalidDataException("This folder is not an Xbox WGS/XGS container.");
  var roots=new List<string>{root};
  if(SaveFileManager.DetectXboxSaveFormat(root)==SaveFileManager.XboxSaveFormat.Xgs&&SaveFileManager.FindSiblingWgsDirectory(root) is string mirror)roots.Add(mirror);
  var before=ConsoleSnapshot(roots.ToArray());
  var loaded=SaveFileManager.LoadXboxSave(root,slotIdentifier)??throw new InvalidDataException("The Xbox slot could not be read.");
  if(loaded.GetObject("PlayerStateData")==null)throw new InvalidDataException("The slot has no player state.");
  var files=SaveFileManager.GetXboxSlotFiles(root,slotIdentifier);
  if(files.DataFile==null)throw new InvalidDataException("The slot data file is unavailable.");
  ConsoleAssertSnapshot(roots.ToArray(),before);
  Data=loaded;SourcePath=Path.GetFullPath(files.DataFile);_loadedSourceHash=SHA256.HashData(File.ReadAllBytes(SourcePath));
  SaveFileManager.TryDetectActiveContext(loaded);Dirty=false;
  AccountData=null;AccountPath=null;AccountDirty=false;_accountHash=null;AccountLoadMessage="Xbox account rewards are not edited by this console save transaction.";
  _consoleSource=SourcePath;_xboxContainer=root;XboxSlotIdentifier=slotIdentifier;ConsolePlatform=SaveFileManager.Platform.XboxGamePass;_consoleRoots=roots.ToArray();_consoleBaseline=before;
  DocumentRevision++;RefreshTools();
 }
 static void EnsureConsolePathNoLinks(string path)
 {
  for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
   if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Console save and backup paths must not contain links or junctions: "+current);
 }
 static Dictionary<string,string> ConsoleSnapshot(string[] roots)
 {
  var snapshot=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var root in roots){
   EnsureConsolePathNoLinks(root);
   void Scan(string directory){
    if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Console save folders must not contain links or junctions.");
    foreach(var file in Directory.GetFiles(directory)){
     if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Console save files must not be links.");
     snapshot.Add(Path.GetFullPath(file),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
    }
    foreach(var child in Directory.GetDirectories(directory)){
     if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new IOException("Console save folders, including backup directories, must not contain links or junctions.");
     if(!Path.GetFileName(child).StartsWith(".nmse-next-",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(child).StartsWith("NMSE.Next-backups",StringComparison.OrdinalIgnoreCase))Scan(child);
    }
   }
   Scan(root);
  }
  return snapshot;
 }
 static void ConsoleAssertSnapshot(string[] roots,Dictionary<string,string> expected)
 {
  var current=ConsoleSnapshot(roots);
  if(current.Count!=expected.Count||expected.Any(p=>!current.TryGetValue(p.Key,out var hash)||hash!=p.Value))throw new IOException("Console save files or metadata changed on disk. Reload before saving.");
 }
 static void WriteConsoleNomanSky(string path,JsonObject data)
 {
  byte[] existing=File.ReadAllBytes(path);if(existing.Length<0x70)throw new InvalidDataException("The extracted NOMANSKY header is incomplete.");
  byte[] header=existing[..0x70];byte[] json=System.Text.Encoding.UTF8.GetBytes(data.ToString()+"\0");
  System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x5C,4),json.Length);
  using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);stream.Write(header);stream.Write(json);
 }
 SaveResult SaveConsole()
 {
  if(!IsConsoleSession||_consoleBaseline==null||SourcePath==null)throw new InvalidOperationException("Load this console save with its platform and slot identity before saving.");
  if(AccountDirty)throw new NotSupportedException("Console account reward writes are not supported in this transaction. Save the player data without account edits.");
  ConsoleAssertSnapshot(_consoleRoots,_consoleBaseline);
  string temporary=Path.Combine(Path.GetTempPath(),"nmse-next-console-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
  string backup=Path.Combine(_consoleRoots[0],".nmse-next-backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
  var committed=new List<(string Target,string? Backup)>();bool finished=false;Exception? failure=null;
  try{
   // Keep the original xgs/wgs relationship inside an isolated staging tree.
   string[] stages=_consoleRoots.Select((r,i)=>Path.Combine(temporary,ConsolePlatform==SaveFileManager.Platform.XboxGamePass?(i==0&&_consoleRoots.Length>1?"xgs":i==1?"wgs":"container"):"files",Path.GetFileName(r))).ToArray();
   for(int i=0;i<_consoleRoots.Length;i++){
    Directory.CreateDirectory(stages[i]);
    foreach(var path in _consoleBaseline.Keys.Where(p=>p.StartsWith(_consoleRoots[i]+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))){
     string to=Path.Combine(stages[i],Path.GetRelativePath(_consoleRoots[i],path));Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(path,to);
    }
   }
   string stagedSource=Path.Combine(stages[0],Path.GetRelativePath(_consoleRoots[0],SourcePath));
   if(ConsolePlatform==SaveFileManager.Platform.XboxGamePass){
    SaveFileManager.SaveXboxSave(stages[0],XboxSlotIdentifier!,Data);
    if(SaveFileManager.LoadXboxSave(stages[0],XboxSlotIdentifier!)?.ToString()!=Data.ToString())throw new InvalidDataException("Staged Xbox save did not verify.");
    if(stages.Length>1&&SaveFileManager.EnumerateXboxSlots(stages[1]).ContainsKey(XboxSlotIdentifier!)&&SaveFileManager.LoadXboxSave(stages[1],XboxSlotIdentifier!)?.ToString()!=Data.ToString())throw new InvalidDataException("Staged WGS mirror did not verify.");
   }else{
    if(SaveFileManager.IsNomanSkyFile(stagedSource))WriteConsoleNomanSky(stagedSource,Data);
    else {
     SaveFileManager.SaveToFile(stagedSource,Data,compress:true,writeMeta:true,platform:ConsolePlatform);
     var match=Regex.Match(Path.GetFileName(stagedSource),@"^savedata(\d+)\.hg$",RegexOptions.IgnoreCase);
     if(!match.Success)throw new InvalidDataException("The extracted slot filename is invalid.");
     string manifest=Path.Combine(Path.GetDirectoryName(stagedSource)!,"manifest"+int.Parse(match.Groups[1].Value).ToString("D2")+".hg");
     byte[] bytes=File.ReadAllBytes(manifest);uint size=(uint)(System.Text.Encoding.Latin1.GetByteCount(Data.ToString())+1);
     if(bytes.Length<40||BitConverter.ToUInt32(bytes,0)!=0xCA55E77E||BitConverter.ToUInt32(bytes,8)!=size||BitConverter.ToUInt32(bytes,36)!=size)throw new InvalidDataException("The staged console manifest did not verify.");
    }
    if(SaveFileManager.LoadSaveFile(stagedSource).ToString()!=Data.ToString())throw new InvalidDataException("Staged console save did not verify.");
   }
   var outputs=new List<(string Stage,string Target)>();
   for(int i=0;i<stages.Length;i++)foreach(var file in Directory.GetFiles(stages[i],"*",SearchOption.AllDirectories)){
    string target=Path.Combine(_consoleRoots[i],Path.GetRelativePath(stages[i],file));
    string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    if(!_consoleBaseline.TryGetValue(target,out var original)||hash!=original)outputs.Add((file,target));
   }
   // Existing Xbox writes must preserve all blob identities; unexpected removals are rejected.
   for(int i=0;i<stages.Length;i++)foreach(var path in _consoleBaseline.Keys.Where(p=>p.StartsWith(_consoleRoots[i]+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
    if(!File.Exists(Path.Combine(stages[i],Path.GetRelativePath(_consoleRoots[i],path))))throw new InvalidDataException("The console writer removed an existing container file. No source files were changed.");
   ConsoleAssertSnapshot(_consoleRoots,_consoleBaseline);
   EnsureConsolePathNoLinks(backup);Directory.CreateDirectory(backup);EnsureConsolePathNoLinks(backup);
   for(int n=0;n<outputs.Count;n++){
    var output=outputs[n];string saved=Path.Combine(backup,n.ToString("D4")+".original");
    EnsureConsolePathNoLinks(backup);EnsureConsolePathNoLinks(output.Target);
    if(File.Exists(output.Target))File.Copy(output.Target,saved);else saved="";
    // Keep an audit map without embedding save contents.
    File.AppendAllText(Path.Combine(backup,"files.txt"),n.ToString("D4")+" "+output.Target+Environment.NewLine);
   }
   ConsoleAssertSnapshot(_consoleRoots,_consoleBaseline);
   for(int n=0;n<outputs.Count;n++){
    ConsoleCommitProbe?.Invoke(n);
    var output=outputs[n];string saved=Path.Combine(backup,n.ToString("D4")+".original");
    EnsureConsolePathNoLinks(backup);EnsureConsolePathNoLinks(output.Target);
    Directory.CreateDirectory(Path.GetDirectoryName(output.Target)!);
    string pending=output.Target+".nmse-next-"+Guid.NewGuid().ToString("N");
    try{File.Copy(output.Stage,pending);File.Move(pending,output.Target,true);}
    catch(Exception writeError){try{if(File.Exists(pending))File.Delete(pending);}catch(Exception cleanupError){writeError.Data["PendingCleanupError"]=cleanupError.Message;}throw;}
    committed.Add((output.Target,File.Exists(saved)?saved:null));
   }
   _consoleBaseline=ConsoleSnapshot(_consoleRoots);_loadedSourceHash=SHA256.HashData(File.ReadAllBytes(SourcePath));Dirty=false;finished=true;
   return new(SourcePath,backup);
  }catch(Exception original){
   failure=original;var restoreErrors=new List<Exception>();
   foreach(var entry in committed.AsEnumerable().Reverse()){
    try{
     ConsoleRestoreProbe?.Invoke(entry.Target);EnsureConsolePathNoLinks(entry.Target);if(entry.Backup!=null)EnsureConsolePathNoLinks(entry.Backup);
     if(entry.Backup!=null)File.Copy(entry.Backup,entry.Target,true);else if(File.Exists(entry.Target))File.Delete(entry.Target);
    }catch(Exception restoreError){restoreErrors.Add(new IOException("Could not restore "+entry.Target+". Recovery backup: "+backup,restoreError));}
   }
   if(restoreErrors.Count>0){var aggregate=new AggregateException("Console save failed and some files could not be restored. Recovery backups: "+backup,new[]{original}.Concat(restoreErrors));aggregate.Data["BackupDirectory"]=backup;failure=aggregate;throw aggregate;}
   throw;
  }finally{
   // Cleanup is best effort and cannot replace a write or rollback exception.
   foreach(string directory in !finished&&committed.Count==0?new[]{temporary,backup}:new[]{temporary}){
    try{if(Directory.Exists(directory)){EnsureConsolePathNoLinks(directory);Directory.Delete(directory,true);}}
    catch(Exception cleanupError){if(failure!=null)failure.Data["ConsoleCleanup:"+directory]=cleanupError.Message;else System.Diagnostics.Trace.TraceWarning("Console staging cleanup failed: "+cleanupError.Message);}
   }
  }
 }
}


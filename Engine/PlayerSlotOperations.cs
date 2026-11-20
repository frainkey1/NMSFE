using NMSE.IO;

namespace NMSE.Next.Engine;

public enum PlayerSlotAction { Copy, Move, Swap, Delete }
public sealed record PlayerSlotResult(string BackupDirectory, string? SuggestedLoadPath);

/// <summary>Disk-only operations. Game and cloud synchronization must be stopped by the caller.</summary>
public static class PlayerSlotOperations
{
 public static bool Supported(SaveFileManager.Platform platform) =>
  platform is SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG or SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch or SaveFileManager.Platform.XboxGamePass;
 public static SaveFileManager.Platform Detect(string directory)=>SaveFileManager.DetectPlatform(directory);
 static void Validate(string directory,int slot,SaveFileManager.Platform platform)
 {
  if(slot<1||slot>15)throw new ArgumentOutOfRangeException(nameof(slot),"Choose a slot from 1 through 15.");
  if(!Directory.Exists(directory))throw new DirectoryNotFoundException(directory);
  if(!Supported(platform))throw new NotSupportedException("Choose Steam/GOG, extracted PS4/Switch saves, or a recognized Xbox WGS/XGS container.");
  if((platform is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch)&&File.Exists(Path.Combine(directory,"memory.dat")))throw new NotSupportedException("Slot operations require extracted PS4 savedata files; memory.dat and encrypted retail PlayStation containers are not supported.");
  if(platform==SaveFileManager.Platform.XboxGamePass&&SaveFileManager.DetectXboxSaveFormat(directory)==SaveFileManager.XboxSaveFormat.None)throw new InvalidDataException("Select the Xbox WGS directory containing containers.index, or the XGS directory containing SlotN folders.");
  var detected=Detect(directory);
  bool Compatible(SaveFileManager.Platform a,SaveFileManager.Platform b)=>
   a==b||((a is SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG)&&(b is SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG))||
   ((a is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch)&&(b is SaveFileManager.Platform.PS4 or SaveFileManager.Platform.Switch));
  if(detected!=SaveFileManager.Platform.Unknown&&!Compatible(detected,platform))throw new InvalidDataException("The selected format does not match this directory.");
  RejectLinks(directory);
 }
 static string[] Files(string directory,int slot,SaveFileManager.Platform platform)=>
  SaveSlotManager.GetAllSlotFiles(directory,slot-1,platform).SelectMany(p=>new[]{p.DataFile,p.MetaFile}).Where(p=>p!=null).Cast<string>().ToArray();
 public static string? SourceFile(string directory,int slot,SaveFileManager.Platform platform)
 {
  Validate(directory,slot,platform);
  if(platform==SaveFileManager.Platform.XboxGamePass)return XboxSource(directory,slot)?.DataFilePath;
  var pairs=SaveSlotManager.GetAllSlotFiles(directory,slot-1,platform);
  return pairs.Reverse().Select(p=>p.DataFile).FirstOrDefault(p=>p!=null&&File.Exists(p));
 }
 public static PlayerSlotResult Execute(string directory,int source,int destination,SaveFileManager.Platform platform,PlayerSlotAction action)
 {
  directory=Path.GetFullPath(directory);Validate(directory,source,platform);
  if(!Enum.IsDefined(action))throw new ArgumentOutOfRangeException(nameof(action));
  if(action!=PlayerSlotAction.Delete){Validate(directory,destination,platform);if(source==destination)throw new ArgumentException("Source and destination must differ.");}
  if(SourceFile(directory,source,platform)==null)throw new InvalidOperationException($"Source slot {source} is empty.");
  var paths=Files(directory,source,platform).Concat(action==PlayerSlotAction.Delete?[]:Files(directory,destination,platform)).ToArray();
  var touched=action==PlayerSlotAction.Delete?new[]{source}:new[]{source,destination};
  var roots=platform==SaveFileManager.Platform.XboxGamePass?XboxRoots(directory):[];
  var expected=platform==SaveFileManager.Platform.XboxGamePass?ExpectedXbox(directory,source,destination,action):null;
  if(roots.Length>1)VerifyXboxMirror(directory,roots[1],touched);
  string backup=Transact(directory,paths,()=>
  {
   switch(action)
   {
    case PlayerSlotAction.Copy:
    case PlayerSlotAction.Move:
     // A missing source half must remove the old destination half, including stale metadata.
     if(platform==SaveFileManager.Platform.XboxGamePass)
     {
      if(SaveSlotManager.GetExistingSlotIndices(directory,platform).Contains(destination-1))SaveSlotManager.DeleteSlot(directory,destination-1,platform);
     }
     else foreach(var path in Files(directory,destination,platform))if(File.Exists(path))File.Delete(path);
     if(action==PlayerSlotAction.Copy)SaveSlotManager.CopySlot(directory,source-1,destination-1,platform);
     else SaveSlotManager.MoveSlot(directory,source-1,destination-1,platform);
     break;
    case PlayerSlotAction.Swap:SaveSlotManager.SwapSlots(directory,source-1,destination-1,platform);break;
    case PlayerSlotAction.Delete:SaveSlotManager.DeleteSlot(directory,source-1,platform);break;
   }
   if(platform==SaveFileManager.Platform.PS4)
    foreach(int slot in touched)NormalizePlaystationIndexes(directory,slot);
   if(expected!=null)foreach(var root in roots)VerifyXbox(root,touched,expected);
  },roots);
  return new(backup,action==PlayerSlotAction.Delete?null:SourceFile(directory,destination,platform));
 }
 public static PlayerSlotResult Transfer(string sourceDirectory,int source,string destinationDirectory,int destination,SaveFileManager.Platform sourcePlatform,SaveFileManager.Platform destinationPlatform)
 {
  sourceDirectory=Path.GetFullPath(sourceDirectory);destinationDirectory=Path.GetFullPath(destinationDirectory);
  Validate(sourceDirectory,source,sourcePlatform);Validate(destinationDirectory,destination,destinationPlatform);
  if(sourceDirectory.StartsWith(destinationDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||destinationDirectory.StartsWith(sourceDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source and destination save directories must not contain each other.");
  if(string.Equals(sourceDirectory,destinationDirectory,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Transfer requires a different destination directory. Use Copy within this directory.");
  var input=SourceFile(sourceDirectory,source,sourcePlatform)??throw new InvalidOperationException("The selected source slot is empty.");
  if(destinationPlatform==SaveFileManager.Platform.XboxGamePass&&XboxSource(destinationDirectory,destination)==null)throw new InvalidOperationException("Xbox transfer requires an existing destination game slot. Create that slot in the game first.");
  var target=SaveSlotManager.GetSlotFiles(destinationDirectory,destination-1,destinationPlatform);
  var paths=Files(sourceDirectory,source,sourcePlatform).Concat(Files(destinationDirectory,destination,destinationPlatform)).ToArray();
  var trees=new List<string>();
  if(sourcePlatform==SaveFileManager.Platform.XboxGamePass)trees.AddRange(XboxRoots(sourceDirectory));
  var destinationRoots=destinationPlatform==SaveFileManager.Platform.XboxGamePass?XboxRoots(destinationDirectory):[];
  trees.AddRange(destinationRoots);
  if(destinationRoots.Length>1)VerifyXboxMirror(destinationDirectory,destinationRoots[1],[destination]);
  var backup=Transact(destinationDirectory,paths,()=>
  {
   string? temporary=null;
   try
   {
    if(sourcePlatform==SaveFileManager.Platform.XboxGamePass)
    {
     var identifier=XboxSource(sourceDirectory,source)!.Identifier;
     var data=SaveFileManager.LoadXboxSave(sourceDirectory,identifier)??throw new InvalidDataException("The selected Xbox slot could not be decoded.");
     temporary=Path.Combine(Path.GetTempPath(),"nmse-slot-transfer-"+Guid.NewGuid().ToString("N")+".hg");
     SaveFileManager.SaveToFile(temporary,data);input=temporary;
    }
    SaveSlotManager.TransferCrossPlatform(input,destinationDirectory,destination-1,destinationPlatform,
     new TransferOptions{TransferBases=false,TransferDiscoveries=false,TransferSettlements=false,TransferByteBeat=false});
    if(destinationRoots.Length>1)VerifyXboxMirror(destinationDirectory,destinationRoots[1],[destination]);
   }
   finally{if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);}
  },trees);
  return new(backup,SourceFile(destinationDirectory,destination,destinationPlatform));
 }
 static void NormalizePlaystationIndexes(string directory,int slot)
 {
  foreach(var pair in SaveSlotManager.GetAllSlotFiles(directory,slot-1,SaveFileManager.Platform.PS4))
  {
   if(pair.DataFile==null||pair.MetaFile==null||!File.Exists(pair.DataFile)||!File.Exists(pair.MetaFile))continue;
   var bytes=File.ReadAllBytes(pair.MetaFile);
   if(bytes.Length<16||BitConverter.ToUInt32(bytes,0)!=0xCA55E77E)throw new InvalidDataException("The PS4 slot manifest has an unsupported header.");
   int index=int.Parse(Path.GetFileNameWithoutExtension(pair.DataFile).AsSpan(8),System.Globalization.CultureInfo.InvariantCulture);
   BitConverter.GetBytes(index).CopyTo(bytes,12);File.WriteAllBytes(pair.MetaFile,bytes);
  }
 }
 static string[] XboxRoots(string directory)
 {
  var roots=new List<string>{directory};
  if(SaveFileManager.DetectXboxSaveFormat(directory)==SaveFileManager.XboxSaveFormat.Xgs&&SaveFileManager.FindSiblingWgsDirectory(directory) is {} mirror)
  {
   Validate(mirror,1,SaveFileManager.Platform.XboxGamePass);
   // Reject a malformed index before the primary container can be modified.
   _=SaveFileManager.EnumerateXboxSlots(mirror);
   roots.Add(mirror);
  }
  return roots.ToArray();
 }
 sealed record XboxPayload(byte[] Data,byte[] Meta);
 static Dictionary<string,XboxPayload> ReadXbox(string directory,int[] slots)
 {
  var result=new Dictionary<string,XboxPayload>(StringComparer.OrdinalIgnoreCase);
  foreach(var entry in SaveFileManager.EnumerateXboxSlots(directory).Values.Where(s=>ContainersIndexManager.IsSaveSlot(s.Identifier)&&slots.Contains(ContainersIndexManager.ExtractSlotNumber(s.Identifier))))
  {
   if(entry.DataFilePath==null||!File.Exists(entry.DataFilePath))throw new IOException("An Xbox slot entry has no readable data blob.");
   result.Add(entry.Identifier,new(File.ReadAllBytes(entry.DataFilePath),entry.MetaFilePath!=null&&File.Exists(entry.MetaFilePath)?File.ReadAllBytes(entry.MetaFilePath):new byte[24]));
  }
  return result;
 }
 static Dictionary<string,XboxPayload> ExpectedXbox(string directory,int source,int destination,PlayerSlotAction action)
 {
  var before=ReadXbox(directory,action==PlayerSlotAction.Delete?[source]:[source,destination]);
  var result=new Dictionary<string,XboxPayload>(StringComparer.OrdinalIgnoreCase);
  void Put(int from,int to)
  {
   foreach(var pair in before.Where(p=>ContainersIndexManager.ExtractSlotNumber(p.Key)==from))
    result.Add("Slot"+to+pair.Key.Substring(("Slot"+from).Length),pair.Value);
  }
  switch(action)
  {
   case PlayerSlotAction.Copy:Put(source,source);Put(source,destination);break;
   case PlayerSlotAction.Move:Put(source,destination);break;
   case PlayerSlotAction.Swap:Put(source,destination);Put(destination,source);break;
  }
  return result;
 }
 static void VerifyXbox(string directory,int[] slots,Dictionary<string,XboxPayload> expected)
 {
  var actual=ReadXbox(directory,slots);
  if(actual.Count!=expected.Count||expected.Any(p=>!actual.TryGetValue(p.Key,out var found)||!found.Data.SequenceEqual(p.Value.Data)||!found.Meta.SequenceEqual(p.Value.Meta)))
   throw new IOException("Xbox slot output or its WGS mirror is incomplete. The transaction will restore both containers.");
 }
 static void VerifyXboxMirror(string directory,string mirror,int[] slots)=>VerifyXbox(mirror,slots,ReadXbox(directory,slots));
 static XboxSlotInfo? XboxSource(string directory,int slot)=>SaveFileManager.EnumerateXboxSlots(directory).Values
  .Where(s=>ContainersIndexManager.IsSaveSlot(s.Identifier)&&ContainersIndexManager.ExtractSlotNumber(s.Identifier)==slot&&s.DataFilePath!=null&&File.Exists(s.DataFilePath))
  .OrderByDescending(s=>s.Identifier.Contains("Manual",StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
 public static bool ContainsLoadedFile(string directory,int slot,SaveFileManager.Platform platform,string? loadedPath)
 {
  if(loadedPath==null)return false;
  var files=platform==SaveFileManager.Platform.XboxGamePass
   ?SaveFileManager.EnumerateXboxSlots(directory).Values.Where(s=>ContainersIndexManager.IsSaveSlot(s.Identifier)&&ContainersIndexManager.ExtractSlotNumber(s.Identifier)==slot).Select(s=>s.DataFilePath)
   :SaveSlotManager.GetAllSlotFiles(directory,slot-1,platform).Select(s=>s.DataFile);
  return files.Any(p=>string.Equals(p,loadedPath,StringComparison.OrdinalIgnoreCase));
 }
 static void RejectLinks(string directory)
 {
  RejectPathLinks(directory);
  foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
  {
   if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)throw new IOException("Save utility paths cannot contain links or junctions.");
   if(Path.GetFileName(entry).Equals("NMSE.Next-backups",StringComparison.OrdinalIgnoreCase))continue;
   if(Directory.Exists(entry))RejectLinks(entry);
  }
 }
 static void RejectPathLinks(string path)
 {
  for(string? current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
   if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
    throw new IOException("Save utility and backup paths cannot contain links or junctions.");
 }
 static IEnumerable<string> TreeFiles(string directory)
 {
  foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
  {
   if(Path.GetFileName(entry).Equals("NMSE.Next-backups",StringComparison.OrdinalIgnoreCase))continue;
   if(Directory.Exists(entry)){foreach(var file in TreeFiles(entry))yield return file;}else yield return entry;
  }
 }
 static IEnumerable<string> TreeDirectories(string directory)
 {
  foreach(var child in Directory.EnumerateDirectories(directory))
  {
   if(Path.GetFileName(child).Equals("NMSE.Next-backups",StringComparison.OrdinalIgnoreCase))continue;
   yield return child;foreach(var nested in TreeDirectories(child))yield return nested;
  }
 }
 static string Transact(string directory,IEnumerable<string> affected,Action operation,IEnumerable<string>? fullTrees=null)
 {
  RejectLinks(directory);
  var trees=(fullTrees??[]).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  foreach(var root in trees)RejectLinks(root);
  var treeDirectories=trees.SelectMany(TreeDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);
  var paths=affected.Concat(trees.SelectMany(TreeFiles)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  // Snapshot every original byte before starting the first disk mutation.
  var original=paths.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null,StringComparer.OrdinalIgnoreCase);
  var backupBase=Path.Combine(directory,"NMSE.Next-backups");RejectPathLinks(backupBase);
  Directory.CreateDirectory(backupBase);RejectPathLinks(backupBase);
  var backup=Path.Combine(backupBase,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(backup);RejectPathLinks(backup);
  var manifest=new List<string>();
  for(int i=0;i<paths.Length;i++)
  {
   var path=paths[i];var bytes=original[path];var name=i.ToString("D2")+"-"+Path.GetFileName(path);
   if(bytes!=null){File.WriteAllBytes(Path.Combine(backup,name),bytes);if(!File.ReadAllBytes(Path.Combine(backup,name)).SequenceEqual(bytes))throw new IOException("Backup verification failed.");}
   manifest.Add(System.Text.Json.JsonSerializer.Serialize(new{Path=path,Backup=bytes==null?null:name,Existed=bytes!=null}));
  }
  File.WriteAllLines(Path.Combine(backup,"manifest.jsonl"),manifest);
  File.WriteAllLines(Path.Combine(backup,"directories.jsonl"),treeDirectories.Select(p=>System.Text.Json.JsonSerializer.Serialize(p)));
  // Detect external changes between snapshot and mutation.
  foreach(var path in paths)
  {
   var bytes=original[path];
   if(File.Exists(path)!=(bytes!=null)||(bytes!=null&&!File.ReadAllBytes(path).SequenceEqual(bytes)))
    throw new IOException("A save changed during backup. No operation was performed.");
  }
  foreach(var tree in trees)if(TreeFiles(tree).Any(p=>!original.ContainsKey(Path.GetFullPath(p))))throw new IOException("Container changed during backup. No operation was performed.");
  try{operation();}
  catch(Exception failure)
  {
   var errors=new List<Exception>{failure};
   foreach(var tree in trees)try
   {
    foreach(var generated in TreeFiles(tree).Where(p=>!original.ContainsKey(Path.GetFullPath(p))).ToArray())File.Delete(generated);
    foreach(var dir in treeDirectories.Where(d=>d.StartsWith(tree+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))Directory.CreateDirectory(dir);
   }catch(Exception restore){errors.Add(restore);}
   foreach(var path in paths)try{if(original[path] is {} bytes){Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(!File.Exists(path)||!File.ReadAllBytes(path).SequenceEqual(bytes))File.WriteAllBytes(path,bytes);}else if(File.Exists(path))File.Delete(path);}catch(Exception restore){errors.Add(restore);}
   foreach(var tree in trees)try
   {
    foreach(var generated in TreeDirectories(tree).Where(p=>!treeDirectories.Contains(p)).OrderByDescending(p=>p.Length).ToArray())Directory.Delete(generated,false);
   }catch(Exception restore){errors.Add(restore);}
   if(errors.Count>1)throw new AggregateException("Operation failed and rollback was incomplete. Exact backups: "+backup,errors);
   throw new IOException("Operation failed; affected files were restored. Exact backups: "+backup,failure);
  }
  return backup;
 }
}



using System.Security.Cryptography;
using NMSE.IO;
namespace NMSE.Next.Engine;
public sealed record SaveResult(string Path,string BackupDirectory);
public sealed partial class SaveSession
{
 byte[]? _loadedSourceHash;
 /// <summary>Stage and verify a Steam/GOG save pair, back up originals, then replace with rollback on failure.</summary>
 public SaveResult Save()
 {
  if(IsConsoleSession)return SaveConsole();
  if(SourcePath==null)throw new InvalidOperationException("Load a save before saving.");
  string source=SourcePath;string folder=Path.GetDirectoryName(source)!;
  if(_loadedSourceHash==null || !_loadedSourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))throw new IOException("The save changed on disk after loading. Reload it before saving to preserve the newer file.");
  ValidateAccountSource();
  var platform=SaveFileManager.DetectPlatform(folder);
  bool json=Path.GetExtension(source).Equals(".json",StringComparison.OrdinalIgnoreCase);
  if(!json && platform is not (SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG))throw new NotSupportedException("Direct saving for this platform has not been migrated yet. Steam/GOG saves are supported here.");
  string stage=Path.Combine(folder,".nmse-next-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
  string staged=Path.Combine(stage,Path.GetFileName(source));
  string backup=Path.Combine(folder,".nmse-next-backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
  var replaced=new List<(string Destination,string? Backup)>();
  try
  {
   if(!json)
   {
    string meta=MetaFileWriter.GetSteamMetaPath(source);
    if(File.Exists(meta))File.Copy(meta,MetaFileWriter.GetSteamMetaPath(staged));
    else
    {
     // Supply one valid sibling pair for the upstream new-slot metadata fallback.
     foreach(string sibling in Directory.GetFiles(folder,"save*.hg"))
     {
      if(string.Equals(sibling,source,StringComparison.OrdinalIgnoreCase))continue;
      var siblingMeta=MetaFileWriter.ReadSteamMeta(sibling,SaveSlotManager.StorageSlotFromFileName(sibling));
      if(siblingMeta is not {Length:>=18} || siblingMeta[0]!=MetaFileWriter.META_HEADER || siblingMeta[17]==0)continue;
      File.Copy(sibling,Path.Combine(stage,Path.GetFileName(sibling)));
      File.Copy(MetaFileWriter.GetSteamMetaPath(sibling),Path.Combine(stage,Path.GetFileName(MetaFileWriter.GetSteamMetaPath(sibling))));break;
     }
    }
   }
   if(json)File.WriteAllText(staged,Data.ToFormattedString());
   else SaveFileManager.SaveToFile(staged,Data,compress:true,writeMeta:true,platform:platform);
   var verify=SaveFileManager.LoadSaveFile(staged);
   if(verify.ToString()!=Data.ToString())throw new InvalidDataException("Staged save verification failed; original files were not changed.");
   var stagedFiles=json ? new[]{staged} : new[]{staged,MetaFileWriter.GetSteamMetaPath(staged)};
   if(AccountDirty && AccountData!=null && AccountPath!=null)
   {
    string accountStage=Path.Combine(stage,Path.GetFileName(AccountPath));SaveFileManager.SaveToFile(accountStage,AccountData);
    if(SaveFileManager.LoadSaveFile(accountStage).ToString()!=AccountData.ToString())throw new InvalidDataException("Account output verification failed.");
    stagedFiles=stagedFiles.Append(accountStage).ToArray();
   }
   // Only intended outputs are committed; staged fallback siblings are discarded.
   Directory.CreateDirectory(backup);
   foreach(string stagedFile in stagedFiles)
   {
    string target=Path.Combine(folder,Path.GetFileName(stagedFile));
    string? backed=File.Exists(target)?Path.Combine(backup,Path.GetFileName(target)):null;
    if(backed!=null)File.Copy(target,backed,false);
   }
   // Recheck after staging and before the first replacement; a game/cloud update must win.
   if(!_loadedSourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))throw new IOException("The save changed while preparing this write. Reload the save and try again.");
   ValidateAccountSource();
   foreach(string stagedFile in stagedFiles)
   {
    string target=Path.Combine(folder,Path.GetFileName(stagedFile));string saved=Path.Combine(backup,Path.GetFileName(target));
    string? backed=File.Exists(saved)?saved:null;
    File.Move(stagedFile,target,true);replaced.Add((target,backed));
   }
   _loadedSourceHash=SHA256.HashData(File.ReadAllBytes(source));if(AccountDirty && AccountPath!=null)_accountHash=SHA256.HashData(File.ReadAllBytes(AccountPath));AccountDirty=false;Dirty=false;
   return new(source,backup);
  }
  catch
  {
   foreach(var (target,saved) in replaced.AsEnumerable().Reverse())
   {
    if(saved!=null){string restore=target+".restore-"+Guid.NewGuid().ToString("N");File.Copy(saved,restore);File.Move(restore,target,true);}
    else if(File.Exists(target))File.Delete(target);
   }
   throw;
  }
  finally{if(Directory.Exists(stage) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(stage)),Path.GetFullPath(folder),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(stage).StartsWith(".nmse-next-stage-",StringComparison.Ordinal))Directory.Delete(stage,true);}
 }
}


using NMSE.Next.Engine;
using NMSE.IO;
using NMSE.Models;
internal static class SaveWriteChecks
{
 internal static void Run(string resources)
 {
  string folder=Path.Combine(Path.GetTempPath(),"nmse-next-save-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try
  {
   string path=Path.Combine(folder,"save8.hg");
   var fixture=JsonObject.Parse("{\"Version\":1,\"PlayerStateData\":{\"Multitools\":[],\"Units\":150,\"CustomPreserved\":\"untouched\"}}");
   SaveFileManager.SaveToFile(path,fixture);
   int storage=SaveSlotManager.StorageSlotFromFileName(path);
   MetaFileWriter.WriteSteamMeta(path,File.ReadAllBytes(path),100,new SaveMetaInfo{BaseVersion=5000,DifficultyPreset=6,DifficultyPresetTag="Abandoned Universe"},storage);
   var originalMeta=File.ReadAllBytes(MetaFileWriter.GetSteamMetaPath(path));
   var bytes=File.ReadAllBytes(path);var session=new SaveSession(resources);session.Load(path);session.Player!.Set("Units",200);
   var saved=session.Save();
   if(!bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(saved.BackupDirectory,"save8.hg"))))throw new Exception("Backup differs from original");
   if(SaveFileManager.LoadSaveFile(path).GetObject("PlayerStateData")!.GetInt("Units")!=200)throw new Exception("Direct save did not persist edit");
   if(!File.Exists(Path.Combine(folder,"mf_save8.hg")))throw new Exception("Companion metadata missing");
   var metadata=MetaFileWriter.ReadSteamMeta(path,storage)!;
   if(metadata[17]!=5000)throw new Exception("Existing metadata game-build version was replaced by JSON format version");
   var metadataBytes=MetaFileWriter.UIntsToBytes(metadata);
   if(System.Text.Encoding.ASCII.GetString(metadataBytes,364,64).TrimEnd('\0')!="Abandoned Universe")throw new Exception("Alternate difficulty metadata tag lost");
   if(!originalMeta.SequenceEqual(File.ReadAllBytes(Path.Combine(saved.BackupDirectory,"mf_save8.hg"))))throw new Exception("Companion metadata backup differs from original");
   string secondary=Path.Combine(folder,"save10.hg");SaveFileManager.SaveToFile(secondary,fixture);var siblingBytes=File.ReadAllBytes(path);
   var other=new SaveSession(resources);other.Load(secondary);other.Save();
   if(MetaFileWriter.ReadSteamMeta(secondary,SaveSlotManager.StorageSlotFromFileName(secondary))![17]!=5000)throw new Exception("Sibling metadata version fallback lost");
   if(!siblingBytes.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Staged sibling file was written back unexpectedly");
   if(session.Player.GetString("CustomPreserved")!="untouched")throw new Exception("Unrelated data lost");
   var beforeFailure=File.ReadAllBytes(path);File.Delete(Path.Combine(folder,"mf_save8.hg"));Directory.CreateDirectory(Path.Combine(folder,"mf_save8.hg"));session.Player.Set("Units",300);
   try{session.Save();throw new Exception("Blocked metadata destination accepted");}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
   if(!beforeFailure.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Failed pair save did not roll back the data file");
   Directory.Delete(Path.Combine(folder,"mf_save8.hg"));
   File.AppendAllText(path,"newer");
   try{session.Save();throw new Exception("Concurrent external change accepted");}catch(IOException){}
   Console.WriteLine("PASS: direct save staging/reload, exact backup, companion metadata, unrelated data, and external-change protection (temporary fixture only)");
  }
  finally{if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("nmse-next-save-",StringComparison.Ordinal))throw new Exception("Unexpected test cleanup path");Directory.Delete(folder,true);}
 }
}



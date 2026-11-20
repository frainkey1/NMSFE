using System.Reflection;
using System.Text;
using NMSE.IO;
using NMSE.Models;
using NMSE.Next.Engine;

internal static class ConsoleSlotChecks
{
 public static void Run(string resources)
 {
  _=new SaveSession(resources);
  string root=Path.Combine(Path.GetTempPath(),"nmse-console-slot-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
   string New(string name){var p=Path.Combine(root,name);Directory.CreateDirectory(p);return p;}
   foreach(var platform in new[]{SaveFileManager.Platform.PS4,SaveFileManager.Platform.Switch})
   {
    var dir=New(platform.ToString());
    File.WriteAllText(Path.Combine(dir,"savedata02.hg"),"auto");File.WriteAllText(Path.Combine(dir,"savedata03.hg"),"manual");
    foreach(int index in new[]{2,3}){var manifest=new byte[32];BitConverter.GetBytes(0xCA55E77Eu).CopyTo(manifest,0);BitConverter.GetBytes(index).CopyTo(manifest,12);File.WriteAllBytes(Path.Combine(dir,$"manifest{index:D2}.hg"),manifest);}
    PlayerSlotOperations.Execute(dir,1,2,platform,PlayerSlotAction.Copy);
    Assert(File.ReadAllText(Path.Combine(dir,"savedata04.hg"))=="auto"&&File.ReadAllText(Path.Combine(dir,"savedata05.hg"))=="manual","Extracted console copy failed.");
    Assert(BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(dir,"manifest04.hg")),12)==4&&BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(dir,"manifest05.hg")),12)==5,"Console copy manifest index was not normalized.");
    PlayerSlotOperations.Execute(dir,2,3,platform,PlayerSlotAction.Move);
    Assert(!File.Exists(Path.Combine(dir,"savedata04.hg"))&&File.ReadAllText(Path.Combine(dir,"savedata06.hg"))=="auto","Extracted console move failed.");
    Assert(BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(dir,"manifest06.hg")),12)==6,"Console move manifest index was not normalized.");
    File.WriteAllText(Path.Combine(dir,"savedata02.hg"),"changed");
    PlayerSlotOperations.Execute(dir,1,3,platform,PlayerSlotAction.Swap);
    Assert(File.ReadAllText(Path.Combine(dir,"savedata06.hg"))=="changed","Extracted console swap failed.");
    Assert(BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(dir,"manifest02.hg")),12)==2&&BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(dir,"manifest06.hg")),12)==6,"Console swap manifest indexes were not normalized.");
    PlayerSlotOperations.Execute(dir,3,1,platform,PlayerSlotAction.Delete);
    Assert(!File.Exists(Path.Combine(dir,"savedata06.hg"))&&!File.Exists(Path.Combine(dir,"manifest06.hg")),"Extracted console delete failed.");
   }
   foreach(bool xgs in new[]{false,true})
   {
    var dir=New(xgs?"xgs":"wgs");CreateXbox(dir,xgs,("Slot1Auto","auto"),("Slot1Manual","manual"),("AccountData","account"));
    var xbox=SaveFileManager.Platform.XboxGamePass;
    string Read(int slot)=>File.ReadAllText(PlayerSlotOperations.SourceFile(dir,slot,xbox)!);
    PlayerSlotOperations.Execute(dir,1,2,xbox,PlayerSlotAction.Copy);
    Assert(Read(2)=="manual","Xbox copy did not resolve selected manual slot.");
    PlayerSlotOperations.Execute(dir,2,3,xbox,PlayerSlotAction.Move);
    Assert(PlayerSlotOperations.SourceFile(dir,2,xbox)==null&&Read(3)=="manual","Xbox move failed.");
    File.WriteAllText(PlayerSlotOperations.SourceFile(dir,1,xbox)!,"changed");
    PlayerSlotOperations.Execute(dir,1,3,xbox,PlayerSlotAction.Swap);
    Assert(Read(3)=="changed"&&Read(1)=="manual","Xbox swap failed.");
    PlayerSlotOperations.Execute(dir,3,1,xbox,PlayerSlotAction.Delete);
    Assert(PlayerSlotOperations.SourceFile(dir,3,xbox)==null,"Xbox delete failed.");
    var account=SaveFileManager.EnumerateXboxSlots(dir)["AccountData"];Assert(File.ReadAllText(account.DataFilePath!)=="account","Xbox operations altered account data.");
    // Exercise the transaction boundary with a mid-write failure: index removal,
    // modified metadata, deleted source directory and newly generated container files.
    var before=Snapshot(dir);var originalDir=SaveFileManager.EnumerateXboxSlots(dir)["Slot1Auto"].BlobDirectoryPath;
    var transact=typeof(PlayerSlotOperations).GetMethod("Transact",BindingFlags.Static|BindingFlags.NonPublic)!;
    Action fail=()=>{
     Directory.Delete(originalDir,true);
     var generated=Path.Combine(dir,"generated-container");Directory.CreateDirectory(generated);File.WriteAllText(Path.Combine(generated,"data"),"generated");
     if(File.Exists(Path.Combine(dir,"containers.index")))File.Delete(Path.Combine(dir,"containers.index"));
     File.WriteAllText(account.MetaFilePath!,"changed-meta");
     throw new IOException("Synthetic interrupted container write");
    };
    try{transact.Invoke(null,new object?[]{dir,Array.Empty<string>(),fail,new[]{dir}});throw new Exception("Injected Xbox failure did not propagate.");}
    catch(TargetInvocationException e)when(e.InnerException is IOException){}
    var after=Snapshot(dir);
    Assert(before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out var bytes)&&bytes.SequenceEqual(p.Value)),"Xbox full-container rollback did not restore exact files.");
    Assert(!Directory.Exists(Path.Combine(dir,"generated-container")),"Xbox rollback left a generated container directory.");
   }
   {
    var xgs=New(Path.Combine("mirror","xgs","profile"));var wgs=New(Path.Combine("mirror","wgs","profile"));
    CreateXbox(xgs,true,("Slot1Auto","auto"),("Slot1Manual","manual"));CreateXbox(wgs,false,("Slot1Auto","auto"),("Slot1Manual","manual"));
    var xbox=SaveFileManager.Platform.XboxGamePass;
    PlayerSlotOperations.Execute(xgs,1,2,xbox,PlayerSlotAction.Copy);
    Assert(File.ReadAllText(PlayerSlotOperations.SourceFile(wgs,2,xbox)!)=="manual","XGS copy mirror missing.");
    PlayerSlotOperations.Execute(xgs,2,3,xbox,PlayerSlotAction.Move);
    Assert(PlayerSlotOperations.SourceFile(wgs,2,xbox)==null&&File.ReadAllText(PlayerSlotOperations.SourceFile(wgs,3,xbox)!)=="manual","XGS move mirror failed.");
    foreach(var dir in new[]{xgs,wgs})File.WriteAllText(PlayerSlotOperations.SourceFile(dir,1,xbox)!,"changed");
    PlayerSlotOperations.Execute(xgs,1,3,xbox,PlayerSlotAction.Swap);
    Assert(File.ReadAllText(PlayerSlotOperations.SourceFile(wgs,3,xbox)!)=="changed","XGS swap mirror failed.");
    PlayerSlotOperations.Execute(xgs,3,1,xbox,PlayerSlotAction.Delete);
    Assert(PlayerSlotOperations.SourceFile(wgs,3,xbox)==null,"XGS delete mirror failed.");
    var beforeXgs=Snapshot(xgs);var beforeWgs=Snapshot(wgs);
    using(var lockedIndex=new FileStream(Path.Combine(wgs,"containers.index"),FileMode.Open,FileAccess.Read,FileShare.Read))
    {
     try{PlayerSlotOperations.Execute(xgs,1,4,xbox,PlayerSlotAction.Copy);throw new Exception("Swallowed WGS mirror failure was accepted.");}
     catch(IOException){}
    }
    bool Same(Dictionary<string,byte[]> before,Dictionary<string,byte[]> after)=>before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out var bytes)&&bytes.SequenceEqual(p.Value));
    Assert(Same(beforeXgs,Snapshot(xgs))&&Same(beforeWgs,Snapshot(wgs)),"Partial mirror failure did not restore both containers exactly.");
    Assert(PlayerSlotOperations.SourceFile(xgs,4,xbox)==null&&PlayerSlotOperations.SourceFile(wgs,4,xbox)==null,"Partial mirror rollback left generated slot data.");
    File.WriteAllText(PlayerSlotOperations.SourceFile(wgs,1,xbox)!,"mismatched");
    var beforeMismatch=Snapshot(xgs);
    try{PlayerSlotOperations.Execute(xgs,1,2,xbox,PlayerSlotAction.Copy);throw new Exception("Mismatched WGS mirror was accepted.");}catch(IOException){}
    Assert(Same(beforeMismatch,Snapshot(xgs)),"Mirror preflight failure modified XGS files.");
   }
   foreach(var platform in new[]{SaveFileManager.Platform.PS4,SaveFileManager.Platform.Switch})
   foreach(var version in new[]{(Base:0,Format:2001,Length:100),(Base:4115,Format:2002,Length:356),(Base:4135,Format:2003,Length:372),(Base:4145,Format:2004,Length:380)})
   {
    // Separate directories avoid sibling version preservation masking each requested format.
    var dir=New("metadata-"+platform+"-"+version.Format);
    var path=Path.Combine(dir,"savedata03.hg");File.WriteAllText(path,"fixture");
    var info=new SaveMetaInfo{BaseVersion=version.Base,GameMode=1,Season=2,TotalPlayTime=123,SaveName="Named fixture",SaveSummary="Summary fixture",DifficultyPreset=4,DifficultyPresetTag="Custom"};
    if(platform==SaveFileManager.Platform.PS4)MetaFileWriter.WritePlaystationStreamingMeta(path,9876,info,99);
    else MetaFileWriter.WriteSwitchMeta(path,9876,info,99);
    var bytes=File.ReadAllBytes(Path.Combine(dir,"manifest03.hg"));
    Assert(bytes.Length==version.Length,"Console manifest length does not match its format.");
    Assert(BitConverter.ToUInt32(bytes,0)==0xCA55E77E&&BitConverter.ToUInt32(bytes,4)==version.Format,"Console manifest magic or format incorrect.");
    Assert(BitConverter.ToUInt32(bytes,8)==9876&&BitConverter.ToUInt32(bytes,12)==3&&BitConverter.ToUInt32(bytes,36)==9876,"Console manifest header offsets or filename-derived index incorrect.");
    Assert(BitConverter.ToUInt32(bytes,20)==version.Base&&BitConverter.ToUInt64(bytes,28)==123,"Console manifest version/playtime offsets incorrect.");
    if(version.Format==2001)Assert(bytes.Skip(40).All(b=>b==0),"Vanilla manifest unexpectedly contains newer extension fields.");
    else Assert(Encoding.UTF8.GetString(bytes,40,128).TrimEnd('\0')=="Named fixture"&&Encoding.UTF8.GetString(bytes,168,128).TrimEnd('\0')=="Summary fixture"&&BitConverter.ToUInt32(bytes,296)==4,"Console manifest name/summary/difficulty offsets incorrect.");
   }
   // Conversion uses valid synthetic compressed player saves, never actual user saves.
   var pc=New("transfer-pc");SaveFileManager.SaveToFile(Path.Combine(pc,"save2.hg"),JsonObject.Parse("{\"PlayerStateData\":{\"Health\":42},\"Platform\":\"PC\"}"));
   foreach(var target in new[]{SaveFileManager.Platform.PS4,SaveFileManager.Platform.Switch,SaveFileManager.Platform.XboxGamePass})
   {
    foreach(bool xgs in target==SaveFileManager.Platform.XboxGamePass?new[]{false,true}:new[]{false})
    {
     var dest=xgs?New(Path.Combine("transfer-mirror","xgs","profile")):New("transfer-"+target);
     string? mirror=xgs?New(Path.Combine("transfer-mirror","wgs","profile")):null;
     if(target==SaveFileManager.Platform.XboxGamePass)CreateXbox(dest,xgs,("Slot2Manual","old"));
     if(mirror!=null)CreateXbox(mirror,false,("Slot2Manual","old"));
     var result=PlayerSlotOperations.Transfer(pc,1,dest,2,SaveFileManager.Platform.Steam,target);
     JsonObject converted=target==SaveFileManager.Platform.XboxGamePass?SaveFileManager.LoadXboxSave(dest,"Slot2Manual")!:SaveFileManager.LoadSaveFile(result.SuggestedLoadPath!);
     Assert(converted.GetObject("PlayerStateData")!.GetInt("Health")==42,"Converted player data changed.");
     if(mirror!=null)
     {
      Assert(SaveFileManager.LoadXboxSave(mirror,"Slot2Manual")!.GetObject("PlayerStateData")!.GetInt("Health")==42,"Transferred XGS data was not mirrored to WGS.");
      var failedSource=New("failed-transfer-source");SaveFileManager.SaveToFile(Path.Combine(failedSource,"save2.hg"),JsonObject.Parse("{\"PlayerStateData\":{\"Health\":99},\"Platform\":\"PC\"}"));
      var beforeX=Snapshot(dest);var beforeW=Snapshot(mirror);
      using(var locked=new FileStream(Path.Combine(mirror,"containers.index"),FileMode.Open,FileAccess.Read,FileShare.Read))
      {
       try{PlayerSlotOperations.Transfer(failedSource,1,dest,2,SaveFileManager.Platform.Steam,target);throw new Exception("Locked transfer mirror was accepted.");}catch(IOException){}
      }
      var afterX=Snapshot(dest);var afterW=Snapshot(mirror);
      Assert(beforeX.Count==afterX.Count&&beforeX.All(p=>afterX.TryGetValue(p.Key,out var bytes)&&bytes.SequenceEqual(p.Value))&&beforeW.Count==afterW.Count&&beforeW.All(p=>afterW.TryGetValue(p.Key,out var bytes)&&bytes.SequenceEqual(p.Value)),"Transfer rollback did not restore both XGS/WGS containers.");
     }
     var back=New("back-"+target+(xgs?"-xgs":""));PlayerSlotOperations.Transfer(dest,2,back,1,target,SaveFileManager.Platform.Steam);
     Assert(SaveFileManager.LoadSaveFile(Path.Combine(back,"save2.hg")).GetObject("PlayerStateData")!.GetInt("Health")==42,"Console-to-PC transfer changed player data.");
    }
   }
   {
    var directory=New("backup-link-source");var outside=New("backup-link-target");
    File.WriteAllText(Path.Combine(directory,"save.hg"),"fixture");
    var link=Path.Combine(directory,"NMSE.Next-backups");bool linked=false;
    try{Directory.CreateSymbolicLink(link,outside);linked=true;}
    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException){Console.WriteLine("SKIP: backup-link fixture unavailable on this machine.");}
    if(linked)try
    {
     try{PlayerSlotOperations.Execute(directory,1,2,SaveFileManager.Platform.Steam,PlayerSlotAction.Copy);throw new Exception("Linked backup directory was accepted.");}
     catch(IOException){}
     Assert(Directory.GetFileSystemEntries(outside).Length==0&&!File.Exists(Path.Combine(directory,"save3.hg")),"Backup link rejection wrote outside the save directory.");
    }
    finally{Directory.Delete(link);}
   }
   Console.WriteLine("PASS: extracted PS4/Switch and Xbox WGS/XGS temporary slot fixtures, full-container rollback and bidirectional platform transfers.");
  }
  finally{Directory.Delete(root,true);}
 }
 static Dictionary<string,byte[]> Snapshot(string dir)=>Directory.GetFiles(dir,"*",SearchOption.AllDirectories)
  .Where(p=>!Path.GetRelativePath(dir,p).StartsWith("NMSE.Next-backups"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
  .ToDictionary(p=>Path.GetRelativePath(dir,p),File.ReadAllBytes,StringComparer.OrdinalIgnoreCase);
 static void CreateXbox(string dir,bool xgs,params (string Identifier,string Data)[] entries)
 {
  if(xgs)
  {
   foreach(var entry in entries){var folder=Path.Combine(dir,entry.Identifier);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"data"),entry.Data);File.WriteAllBytes(Path.Combine(folder,"meta"),new byte[24]);}
   return;
  }
  // Same synthetic WGS index/container structure as upstream PlatformIOTests.
  using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
  writer.Write(14);writer.Write((long)entries.Length);writer.Write(0);writer.Write(0L);writer.Write(0);writer.Write(0);writer.Write(268435456L);
  foreach(var entry in entries)
  {
   var directoryGuid=Guid.NewGuid();var folder=Path.Combine(dir,directoryGuid.ToString("N").ToUpperInvariant());Directory.CreateDirectory(folder);
   var dataGuid=Guid.NewGuid();var metaGuid=Guid.NewGuid();File.WriteAllText(Path.Combine(folder,dataGuid.ToString("N").ToUpperInvariant()),entry.Data);File.WriteAllBytes(Path.Combine(folder,metaGuid.ToString("N").ToUpperInvariant()),new byte[24]);
   byte[] container=new byte[328];using(var buffer=new MemoryStream(container))using(var cw=new BinaryWriter(buffer))
   {
    cw.Write(4);cw.Write(2);cw.Write(Encoding.Unicode.GetBytes("data"));buffer.Position=136;cw.Write(new byte[16]);cw.Write(dataGuid.ToByteArray());
    cw.Write(Encoding.Unicode.GetBytes("meta"));buffer.Position=296;cw.Write(new byte[16]);cw.Write(metaGuid.ToByteArray());
   }
   File.WriteAllBytes(Path.Combine(folder,"container.1"),container);
   writer.Write(entry.Identifier.Length);writer.Write(Encoding.Unicode.GetBytes(entry.Identifier));writer.Write(0);writer.Write(0);writer.Write((byte)1);writer.Write(0);writer.Write(directoryGuid.ToByteArray());writer.Write(0L);writer.Write(0L);writer.Write(0L);
  }
  while(stream.Position<200)writer.Write((byte)0);
  File.WriteAllBytes(Path.Combine(dir,"containers.index"),stream.ToArray());
 }
}

using System.Security.Cryptography;
using System.Text;
using NMSE.IO;
using NMSE.Models;
using NMSE.Next.Engine;

internal static class ConsoleSaveChecks
{
 static JsonObject Fixture()=>JsonObject.Parse("{\"Version\":4720,\"PlayerStateData\":{\"Health\":10,\"SaveSummary\":\"synthetic\"},\"CommonStateData\":{\"SaveName\":\"console fixture\"}}");
 static string Capture(string root)=>string.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>!p.Contains(".nmse-next-",StringComparison.OrdinalIgnoreCase)&&!p.Contains("NMSE.Next-backups",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p).Select(p=>Path.GetRelativePath(root,p)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
 static void Fail(Action action){try{action();throw new Exception("Expected console write rejection.");}catch(IOException){}}
 public static void Run(string resources)
 {
  string temp=Path.Combine(Path.GetTempPath(),"nmse-next-console-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
  try{
   foreach(var platform in new[]{SaveFileManager.Platform.Switch,SaveFileManager.Platform.PS4}){
    string root=Path.Combine(temp,platform.ToString());Directory.CreateDirectory(root);string file=Path.Combine(root,"savedata02.hg");
    SaveFileManager.SaveToFile(file,Fixture(),writeMeta:true,platform:platform);
    SaveFileManager.SaveToFile(Path.Combine(root,"accountdata.hg"),new JsonObject());
    var session=new SaveSession(resources);session.LoadConsoleFile(file,platform);string initial=Capture(root);
    if(session.AccountData!=null||session.AccountPath!=null||string.IsNullOrEmpty(session.AccountLoadMessage))throw new Exception("Console account editing was not disabled.");
    Fail(()=>session.Load(file+".missing"));if(session.ConsolePlatform!=platform)throw new Exception("Failed ordinary load cleared existing console identity.");
    session.Load(file);if(session.ConsolePlatform!=null||session.XboxSlotIdentifier!=null||session.XboxContainerDirectory!=null)throw new Exception("Ordinary load retained console identity.");
    session.LoadConsoleFile(file,platform);
    Directory.CreateDirectory(Path.Combine(root,"NMSE.Next-backups","utility-check"));File.WriteAllText(Path.Combine(root,"NMSE.Next-backups","utility-check","old-save"),"ignored backup");
    Directory.CreateDirectory(Path.Combine(root,".nmse-next-backups","old-check"));File.WriteAllText(Path.Combine(root,".nmse-next-backups","old-check","old-save"),"ignored backup");
    if(session.Dirty||session.Player!.GetInt("Health")!=10||Capture(root)!=initial)throw new Exception("Console load mutated files.");
    session.Player!.Set("Health",20);var result=session.Save();
    if(SaveFileManager.LoadSaveFile(file).GetObject("PlayerStateData")!.GetInt("Health")!=20||!Directory.Exists(result.BackupDirectory)||!File.Exists(Path.Combine(root,"manifest02.hg")))throw new Exception("Console data/manifest save failed.");
    session.LoadConsoleFile(file,platform);string rollback=Capture(root);session.Player!.Set("Health",3000);
    session.ConsoleCommitProbe=n=>{if(n==1)throw new IOException("Injected commit failure.");};
    Fail(()=>session.Save());session.ConsoleCommitProbe=null;
    if(Capture(root)!=rollback)throw new Exception("Console rollback did not restore metadata and data.");
    session.LoadConsoleFile(file,platform);File.AppendAllText(Path.Combine(root,"manifest02.hg"),"external");string external=Capture(root);Fail(()=>session.Save());
    if(Capture(root)!=external)throw new Exception("Console conflict rejection changed source.");
   }
   string ps=Path.Combine(temp,"noman");Directory.CreateDirectory(ps);string psFile=Path.Combine(ps,"savedata02.hg");
   byte[] header=new byte[0x70];Encoding.ASCII.GetBytes("NOMANSKY").CopyTo(header,0);header[40]=197;
   File.WriteAllBytes(psFile,header.Concat(Encoding.UTF8.GetBytes(Fixture().ToString()+"\0")).ToArray());
   var psSession=new SaveSession(resources);psSession.LoadConsoleFile(psFile,SaveFileManager.Platform.PS4);psSession.Player!.Set("SaveSummary","Unicode 星");psSession.Save();
   if(File.ReadAllBytes(psFile)[40]!=197||SaveFileManager.LoadSaveFile(psFile).GetObject("PlayerStateData")!.GetString("SaveSummary")!="Unicode 星")throw new Exception("NOMANSKY header/UTF8 preservation failed.");
   // Existing XGS and legacy WGS fixtures, including the paired XGS→WGS mirror.
   string xgs=Path.Combine(temp,"SystemAppData","xgs","fixture");string wgs=Path.Combine(temp,"SystemAppData","wgs","fixture");
   Directory.CreateDirectory(Path.Combine(xgs,"Slot1Auto"));Directory.CreateDirectory(wgs);
   string xdata=Path.Combine(xgs,"Slot1Auto","data");SaveFileManager.SaveToFile(xdata,Fixture());File.WriteAllBytes(Path.Combine(xgs,"Slot1Auto","meta"),new byte[24]);
   var guid=Guid.NewGuid();var slot=new XboxSlotInfo{Identifier="Slot1Auto",DirectoryGuid=guid,BlobDirectoryPath=Path.Combine(wgs,guid.ToString("N").ToUpperInvariant()),BlobContainerExtension=1,LastModified=DateTimeOffset.UtcNow};
   ContainersIndexManager.WriteXboxSave(slot,File.ReadAllBytes(xdata),new byte[24]);
   ContainersIndexManager.WriteContainersIndex(Path.Combine(wgs,"containers.index"),new[]{slot},"synthetic","synthetic",DateTimeOffset.UtcNow);
   foreach(string container in new[]{wgs,xgs}){
    var xbox=new SaveSession(resources);xbox.LoadXbox(container,"Slot1Auto");var paths=SaveFileManager.GetXboxSlotFiles(container,"Slot1Auto");xbox.Player!.Set("Health",container==wgs?40:50);xbox.Save();
    var after=SaveFileManager.GetXboxSlotFiles(container,"Slot1Auto");
    if(paths.DataFile!=after.DataFile||paths.MetaFile!=after.MetaFile||SaveFileManager.LoadXboxSave(container,"Slot1Auto")!.GetObject("PlayerStateData")!.GetInt("Health")!=(container==wgs?40:50))throw new Exception("Xbox slot identity/roundtrip failed.");
   }
   if(SaveFileManager.LoadXboxSave(wgs,"Slot1Auto")!.GetObject("PlayerStateData")!.GetInt("Health")!=50)throw new Exception("XGS mirror was not committed.");
   var mirrored=new SaveSession(resources);mirrored.LoadXbox(xgs,"Slot1Auto");string before=Capture(Path.Combine(temp,"SystemAppData"));mirrored.Player!.Set("Health",60);mirrored.ConsoleCommitProbe=n=>{if(n==1)throw new IOException("Injected Xbox failure.");};
   Fail(()=>mirrored.Save());if(Capture(Path.Combine(temp,"SystemAppData"))!=before)throw new Exception("Xbox multi-file rollback failed.");
   // A restore failure must not stop restoration of the remaining committed files.
   mirrored.LoadXbox(xgs,"Slot1Auto");
   var originals=Directory.GetFiles(Path.Combine(temp,"SystemAppData"),"*",SearchOption.AllDirectories).Where(p=>!p.Contains(".nmse-next-",StringComparison.OrdinalIgnoreCase)&&!p.Contains("NMSE.Next-backups",StringComparison.OrdinalIgnoreCase)).ToDictionary(p=>p,File.ReadAllBytes);
   var attempted=new List<string>();string? successfullyRestored=null;
   mirrored.Player!.Set("Health",70);
   mirrored.ConsoleCommitProbe=n=>{if(n==2)throw new IOException("Original commit failure.");};
   mirrored.ConsoleRestoreProbe=path=>{attempted.Add(path);if(attempted.Count==1)throw new IOException("Injected restore failure.");successfullyRestored=path;};
   try{mirrored.Save();throw new Exception("Expected aggregate restore failure.");}
   catch(AggregateException aggregate){
    if(aggregate.InnerExceptions.Count!=2||aggregate.InnerExceptions[0].Message!="Original commit failure."||aggregate.Data["BackupDirectory"] is not string recovery||!Directory.Exists(recovery))throw new Exception("Rollback aggregate lost original failure or backup path.");
   }
   if(attempted.Count!=2||successfullyRestored==null||!File.ReadAllBytes(successfullyRestored).SequenceEqual(originals[successfullyRestored]))throw new Exception("Restore failure prevented another committed file from restoring.");
   foreach(var original in originals)File.WriteAllBytes(original.Key,original.Value);
   mirrored.ConsoleCommitProbe=null;mirrored.ConsoleRestoreProbe=null;
   mirrored.ConsoleCommitProbe=null;mirrored.LoadXbox(xgs,"Slot1Auto");File.AppendAllText(Path.Combine(wgs,"containers.index"),"external");string conflict=Capture(Path.Combine(temp,"SystemAppData"));Fail(()=>mirrored.Save());if(Capture(Path.Combine(temp,"SystemAppData"))!=conflict)throw new Exception("Xbox mirror conflict changed source.");
   string linkedRoot=Path.Combine(temp,"linked-backup");Directory.CreateDirectory(linkedRoot);string linkedFile=Path.Combine(linkedRoot,"savedata02.hg");
   SaveFileManager.SaveToFile(linkedFile,Fixture(),writeMeta:true,platform:SaveFileManager.Platform.Switch);
   var linkedSession=new SaveSession(resources);linkedSession.LoadConsoleFile(linkedFile,SaveFileManager.Platform.Switch);
   string link=Path.Combine(linkedRoot,".nmse-next-backups");string linkTarget=Path.Combine(temp,"link-target");Directory.CreateDirectory(linkTarget);
   bool madeLink=false;
   try{Directory.CreateSymbolicLink(link,linkTarget);madeLink=true;}catch(Exception unavailable)when(unavailable is UnauthorizedAccessException or IOException or PlatformNotSupportedException){Console.WriteLine("SKIP: directory-link creation unavailable on this test host.");}
   if(madeLink){try{string dataBefore=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(linkedFile)));Fail(()=>linkedSession.Save());if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(linkedFile)))!=dataBefore||Directory.GetFiles(linkTarget).Length!=0)throw new Exception("Backup link rejection wrote outside the source.");}finally{Directory.Delete(link);}}
   Console.WriteLine("PASS: synthetic extracted PS4/Switch and existing Xbox WGS/XGS writes, UTF8 headers, metadata backups, mirror identity, conflicts and rollback.");
  }finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
 }
}


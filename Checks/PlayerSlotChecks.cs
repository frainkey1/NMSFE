using NMSE.IO;
using NMSE.Next.Engine;

internal static class PlayerSlotChecks
{
 public static void Run(string resources)
 {
  var root=Path.Combine(Path.GetTempPath(),"nmse-slot-fixtures-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try
  {
   void Write(string name,string value)=>File.WriteAllText(Path.Combine(root,name),value);
   string Read(string name)=>File.ReadAllText(Path.Combine(root,name));
   void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
   var steam=SaveFileManager.Platform.Steam;
   Write("save.hg","source-auto");Write("save3.hg","old-auto");Write("save4.hg","stale-manual");
   var copied=PlayerSlotOperations.Execute(root,1,2,steam,PlayerSlotAction.Copy);
   Assert(Read("save.hg")=="source-auto"&&Read("save3.hg")=="source-auto"&&!File.Exists(Path.Combine(root,"save4.hg")),"Copy did not replace the complete destination slot.");
   Assert(Directory.Exists(copied.BackupDirectory)&&File.ReadAllText(Path.Combine(copied.BackupDirectory,"manifest.jsonl")).Contains("save4.hg"),"Exact backup manifest missing.");
   Assert(Directory.GetFiles(copied.BackupDirectory).Any(p=>File.ReadAllText(p)=="stale-manual"),"Overwritten destination was not backed up exactly.");
   PlayerSlotOperations.Execute(root,1,3,steam,PlayerSlotAction.Move);
   Assert(!File.Exists(Path.Combine(root,"save.hg"))&&Read("save5.hg")=="source-auto","Move did not remove source.");
   Write("save3.hg","second");
   PlayerSlotOperations.Execute(root,2,3,steam,PlayerSlotAction.Swap);
   Assert(Read("save3.hg")=="source-auto"&&Read("save5.hg")=="second","Swap failed.");
   PlayerSlotOperations.Execute(root,3,1,steam,PlayerSlotAction.Delete);
   Assert(!File.Exists(Path.Combine(root,"save5.hg")),"Delete failed.");
   foreach(int invalid in new[]{0,16})
   {
    try{PlayerSlotOperations.Execute(root,2,invalid,steam,PlayerSlotAction.Copy);throw new Exception("Invalid slot accepted.");}
    catch(ArgumentOutOfRangeException){}
   }
   Write("save.hg","original-auto");Write("save2.hg","original-manual");Write("save3.hg","destination-before");
   Directory.CreateDirectory(Path.Combine(root,"save4.hg"));
   try{PlayerSlotOperations.Execute(root,1,2,steam,PlayerSlotAction.Copy);throw new Exception("Injected destination failure did not fail.");}
   catch(IOException){}
   Assert(Read("save.hg")=="original-auto"&&Read("save2.hg")=="original-manual"&&Read("save3.hg")=="destination-before","Rollback lost original bytes.");
   Assert(Directory.Exists(Path.Combine(root,"save4.hg")),"Rollback modified unrelated directory.");
   Assert(PlayerSlotOperations.Supported(SaveFileManager.Platform.XboxGamePass),"Xbox container support missing.");
   Console.WriteLine("PASS: temporary slot fixtures: copy full replacement, exact backups, move, swap, delete, bounds, and rollback after partial mutation.");
  }
  finally{Directory.Delete(root,true);}
 }
}


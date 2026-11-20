using NMSE.Next.Engine;
using NMSE.Models;
using NMSE.IO;
internal static class FieldChecks
{
 internal static void Run(string resources)
 {
  string folder=Path.Combine(Path.GetTempPath(),"nmse-next-fields-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try
  {
   string path=Path.Combine(folder,"save8.hg"),account=Path.Combine(folder,"accountdata.hg");
   SaveFileManager.SaveToFile(path,JsonObject.Parse("{\"Version\":1,\"PlayerStateData\":{\"Multitools\":[],\"Units\":150,\"Name\":\"Original\"}}"));
   SaveFileManager.SaveToFile(account,JsonObject.Parse("{\"UserSettingsData\":{\"UnlockedSeasonRewards\":[\"FIRST\"]}}"));
   var originalAccount=File.ReadAllBytes(account);var session=new SaveSession(resources);session.Load(path);var player=session.Player!;string before=session.Data.ToString();
   try{session.ApplyFields(new[]{new FieldEdit(player,"Name","Changed","Original"),new FieldEdit(player,"Units","bad","150")});throw new Exception("Invalid number accepted");}catch(System.Text.Json.JsonException){}
   if(before!=session.Data.ToString())throw new Exception("Invalid batch partially applied");
   var detached=new JsonObject();detached.Set("Name","Original");try{session.ApplyFields(new[]{new FieldEdit(detached,"Name","Changed","Original")});throw new Exception("Detached field accepted");}catch(InvalidOperationException){}
   var rewards=session.AccountData!.GetObject("UserSettingsData")!.GetArray("UnlockedSeasonRewards")!;session.ApplyFields(new[]{new FieldEdit(rewards,0,"SECOND","FIRST")},true);var saved=session.Save();
   if(SaveFileManager.LoadSaveFile(account).GetObject("UserSettingsData")!.GetArray("UnlockedSeasonRewards")!.GetString(0)!="SECOND")throw new Exception("Account edit did not persist");
   if(!originalAccount.SequenceEqual(File.ReadAllBytes(Path.Combine(saved.BackupDirectory,"accountdata.hg"))))throw new Exception("Account backup differs");
   session.ApplyFields(new[]{new FieldEdit(rewards,0,"THIRD","SECOND")},true);var originalSave=File.ReadAllBytes(path);File.AppendAllText(account,"changed");try{session.Save();throw new Exception("Account conflict accepted");}catch(IOException){}
   if(!originalSave.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Account conflict changed save");
   Console.WriteLine("PASS: field batch atomicity, detached-record protection, account save/backup and external-change protection (temporary fixtures).");
  }
  finally{if(Path.GetFileName(folder).StartsWith("nmse-next-fields-"))Directory.Delete(folder,true);}
 }
}

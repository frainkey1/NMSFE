using System.Reflection;
using System.Text;
using NMSE.Models;
using NMSE.Next.Engine;

internal static class PlayerKnowledgeChecks
{
 public static void Run(string resources)
 {
  SaveSession Session(JsonObject account)
  {
   var s=new SaveSession(resources);
   typeof(SaveSession).GetProperty(nameof(SaveSession.AccountData))!.SetValue(s,account);
   return s;
  }
  var account=JsonObject.Parse("{\"UserSettingsData\":{\"SeenWikiTopics\":[\"UNKNOWN_TOPIC\"],\"UnlockedWikiTopics\":[],\"UnlockedTitles\":[\"^UNKNOWN_TITLE\"]},\"Untouched\":1.23456789}");
  var s=Session(account);
  var guide=s.ReadPlayerGuides().First(r=>r.Id!="UNKNOWN_TOPIC");
  var title=s.ReadPlayerTitles().First();
  var titles=account.GetObject("UserSettingsData")!.GetArray("UnlockedTitles")!;
  var binary=new BinaryData(Encoding.Latin1.GetBytes("^"+title.Id.ToLowerInvariant()));
  titles.Add(binary);
  var before=account.ToString();
  s.ApplyPlayerGuide(guide.Id,false,false);
  s.ApplyPlayerTitle(title.Id,true);
  if(account.ToString()!=before||s.Dirty||s.AccountDirty||!ReferenceEquals(binary,titles.Get(1)))throw new Exception("Knowledge no-op changed binary representation or dirty state.");
  s.ApplyPlayerGuide(guide.Id,true,false);
  var changed=s.ReadPlayerGuides().Single(r=>r.Id==guide.Id);
  if(!changed.Seen||changed.Unlocked||!s.Dirty||!s.AccountDirty)throw new Exception("Guide flags are not independent or account changes were not tracked.");
  s.ApplyPlayerTitle(title.Id,false);
  s.ApplyPlayerTitle(title.Id,true);
  if(titles.GetString(titles.Length-1)!="^"+title.Id)throw new Exception("New title ID is missing its caret.");
  s.ApplyAllPlayerTitles(false);
  if(titles.Length!=1||titles.GetString(0)!="^UNKNOWN_TITLE")throw new Exception("Bulk title changes lost unknown IDs.");
  var snapshot=account.ToString();
  try{s.ApplyPlayerTitle("NOT_A_DATABASE_TITLE",true);throw new Exception("Invalid title accepted.");}catch(ArgumentException){}
  if(account.ToString()!=snapshot)throw new Exception("Rejected title changed account data.");
  var root=JsonObject.Parse("{\"UnlockedTitles\":[]}");
  var fallback=Session(root);fallback.ApplyPlayerTitle(title.Id,true);
  if(!fallback.ReadPlayerTitles().Single(r=>r.Id==title.Id).Unlocked)throw new Exception("Root account fallback failed.");
  var absent=new SaveSession(resources);
  try{absent.ApplyPlayerGuide(guide.Id,true,true);throw new Exception("Missing account data accepted.");}catch(InvalidOperationException){}
  if(absent.Dirty||absent.AccountDirty)throw new Exception("Missing account data set dirty flags.");
  var roundtrip=Session(JsonObject.Parse(account.ToString()));
  if(roundtrip.ReadPlayerGuides().Single(r=>r.Id==guide.Id)!=changed)throw new Exception("Guide state failed JSON roundtrip.");
  var bulk=Session(JsonObject.Parse("{\"UserSettingsData\":{\"SeenWikiTopics\":[\"UNKNOWN_TOPIC\"],\"UnlockedWikiTopics\":[],\"UnlockedTitles\":[]}}"));
  bulk.ApplyAllPlayerGuides(true);
  if(bulk.ReadPlayerGuides().Where(r=>r.Id!="UNKNOWN_TOPIC").Any(r=>!r.Seen||!r.Unlocked))throw new Exception("Bulk guide unlock missed known topics.");
  bulk.ApplyAllPlayerGuides(false);
  var unknown=bulk.ReadPlayerGuides().Single(r=>r.Id=="UNKNOWN_TOPIC");
  if(!unknown.Seen||unknown.Unlocked||bulk.ReadPlayerGuides().Where(r=>r.Id!="UNKNOWN_TOPIC").Any(r=>r.Seen||r.Unlocked))throw new Exception("Bulk guide lock modified unknown topics or missed known topics.");
  Console.WriteLine("PASS: account guide independent flags, binary/caret title fidelity, unknown-ID preservation, bulk title lock, root fallback and validation.");
 }
}


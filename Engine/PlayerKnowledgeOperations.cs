using System.Text;
using NMSE.Data;
using NMSE.Models;
namespace NMSE.Next.Engine;

public sealed record GuideKnowledge(string Id,string Name,string Category,string IconKey,bool Seen,bool Unlocked);
public sealed record TitleKnowledge(string Id,string Name,string Description,string UnlockedDescription,bool Unlocked);
public sealed partial class SaveSession
{
 bool _playerKnowledgeDatabaseLoaded;
 public void LoadPlayerKnowledgeDatabase()
 {
  if(_playerKnowledgeDatabaseLoaded)return;
  _playerKnowledgeDatabaseLoaded=true;
  WikiGuideDatabase.LoadFromFile(Path.Combine(ResourceRoot,"json","Wiki Guide.json"));
  TitleDatabase.LoadFromFile(Path.Combine(ResourceRoot,"json","Titles.json"));
 }
 JsonObject? KnowledgeSettings => AccountData?.GetObject("UserSettingsData") ?? AccountData;
 public bool GuideKnowledgeAvailable => KnowledgeSettings?.GetArray("SeenWikiTopics") != null && KnowledgeSettings?.GetArray("UnlockedWikiTopics") != null;
 public bool TitleKnowledgeAvailable => KnowledgeSettings?.GetArray("UnlockedTitles") != null;
 static string? KnowledgeString(object? value) => value switch { string s=>s, BinaryData b=>Encoding.Latin1.GetString(b.ToByteArray()), _=>null };
 static string TitleKey(string id)=>id.StartsWith('^')?id[1..]:id;
 static HashSet<string> KnowledgeIds(JsonArray? array,bool titles=false)
 {
  var result=new HashSet<string>(titles?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
  if(array!=null)for(int i=0;i<array.Length;i++)if(KnowledgeString(array.Get(i)) is {} id)result.Add(titles?TitleKey(id):id);
  return result;
 }
 public IReadOnlyList<GuideKnowledge> ReadPlayerGuides()
 {
  LoadPlayerKnowledgeDatabase();
  // Guide names and categories use game-language strings; topic IDs remain unchanged.
  WikiGuideDatabase.ApplyLocalisation(GameLocalisation);
  var seen=KnowledgeIds(KnowledgeSettings?.GetArray("SeenWikiTopics"));
  var unlocked=KnowledgeIds(KnowledgeSettings?.GetArray("UnlockedWikiTopics"));
  var rows=WikiGuideDatabase.Topics.Select(t=>new GuideKnowledge(t.Id,t.Name,t.Category,t.IconKey,seen.Contains(t.Id),unlocked.Contains(t.Id))).ToList();
  var known=rows.Select(t=>t.Id).ToHashSet(StringComparer.Ordinal);
  foreach(var id in seen.Union(unlocked))if(!known.Contains(id))rows.Add(new(id,id,"Other topics","",seen.Contains(id),unlocked.Contains(id)));
  return rows;
 }
 public string PlayerAccountDisplayName
 {
  get
  {
   var owners=Data.GetObject("CommonStateData")?.GetArray("UsedDiscoveryOwnersV2");
   if(owners==null||owners.Length==0)return "Traveller";
   string platform=Data.GetString("Platform")??"";
   string ptk=platform.StartsWith("Win",StringComparison.OrdinalIgnoreCase)?"ST":platform.StartsWith("PS",StringComparison.OrdinalIgnoreCase)?"PS":platform.StartsWith("XB",StringComparison.OrdinalIgnoreCase)?"XB":"";
   var owner=owners.GetObject(0);
   for(int i=0;i<owners.Length;i++)if(ptk!=""&&owners.GetObject(i)?.GetString("PTK")==ptk){owner=owners.GetObject(i);break;}
   var name=owner?.GetString("USN");if(!string.IsNullOrEmpty(name))return name;
   var uid=owner?.GetString("UID");var bases=Player?.GetArray("PersistentPlayerBases");
   if(!string.IsNullOrEmpty(uid)&&bases!=null)for(int i=0;i<bases.Length;i++){var b=bases.GetObject(i)?.GetObject("Owner");if(b!=null&&b.GetString("UID")==uid&&b.GetString("USN") is {Length:>0} baseName)return baseName;}
   return "Traveller";
  }
 }
 public IReadOnlyList<TitleKnowledge> ReadPlayerTitles()
 {
  LoadPlayerKnowledgeDatabase();
  // Title names and descriptions come from the game's language database. Keep
  // localization display-only and reapply it here so language changes are
  // reflected without touching the save's unlocked-title IDs.
  TitleDatabase.ApplyLocalisation(GameLocalisation);
  var unlocked=KnowledgeIds(KnowledgeSettings?.GetArray("UnlockedTitles"),true);
  string Format(string name)=>name.Replace("{0}",PlayerAccountDisplayName).Replace("%NAME%",PlayerAccountDisplayName);
  return TitleDatabase.Titles.Select(t=>new TitleKnowledge(t.Id,Format(t.Name),t.UnlockDescription,t.AlreadyUnlockedDescription,unlocked.Contains(t.Id))).ToArray();
 }
 static bool ChangeKnowledgeId(JsonArray array,string id,bool enabled,bool title)
 {
  bool Match(object? value){var s=KnowledgeString(value);return s!=null&&string.Equals(title?TitleKey(s):s,id,title?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);}
  bool found=false;for(int i=0;i<array.Length;i++)if(Match(array.Get(i))){found=true;break;}
  if(found==enabled)return false;
  if(enabled)array.Add(title?"^"+id:id);
  else for(int i=array.Length-1;i>=0;i--)if(Match(array.Get(i)))array.RemoveAt(i);
  return true;
 }
 public void ApplyPlayerGuide(string id,bool seen,bool unlocked)
 {
  if(string.IsNullOrWhiteSpace(id)||!ReadPlayerGuides().Any(t=>t.Id==id))throw new ArgumentException("Select a guide topic.");
  if(!GuideKnowledgeAvailable)throw new InvalidOperationException("The account guide arrays are unavailable.");
  bool changed=ChangeKnowledgeId(KnowledgeSettings!.GetArray("SeenWikiTopics")!,id,seen,false);
  changed=ChangeKnowledgeId(KnowledgeSettings.GetArray("UnlockedWikiTopics")!,id,unlocked,false)||changed;
  if(changed){AccountDirty=true;Dirty=true;}
 }
 public void ApplyAllPlayerGuides(bool unlocked)
 {
  if(!GuideKnowledgeAvailable)throw new InvalidOperationException("The account guide arrays are unavailable.");
  LoadPlayerKnowledgeDatabase();
  foreach(var row in WikiGuideDatabase.Topics)ApplyPlayerGuide(row.Id,unlocked,unlocked);
 }
 public void ApplyPlayerTitle(string id,bool unlocked)
 {
  id=TitleKey(id);
  LoadPlayerKnowledgeDatabase();
  if(TitleDatabase.GetTitle(id)==null)throw new ArgumentException("Select a known title.");
  if(!TitleKnowledgeAvailable)throw new InvalidOperationException("The account title array is unavailable.");
  if(ChangeKnowledgeId(KnowledgeSettings!.GetArray("UnlockedTitles")!,id,unlocked,true)){AccountDirty=true;Dirty=true;}
 }
 public void ApplyAllPlayerTitles(bool unlocked)
 {
  if(!TitleKnowledgeAvailable)throw new InvalidOperationException("The account title array is unavailable.");
  foreach(var row in ReadPlayerTitles())ApplyPlayerTitle(row.Id,unlocked);
 }
}

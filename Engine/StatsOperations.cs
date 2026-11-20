using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record StatsSection(string Key,string Name,string? Icon);
public sealed record StatsValue(string StatId,string Name,string? SubcategoryKey,string? SubcategoryName,int Value,bool Exists,bool ShowGuildRank,int GuildRank,int MaxGuildRank,int NextRankIn);

/// <summary>Reads and writes translated milestone and global-stat values from an existing save.</summary>
public static class StatsOperations
{
 static MilestoneTab ParseTab(string name)=>name switch
 {
  "Milestones"=>MilestoneTab.Milestones,
  "Lifeforms"=>MilestoneTab.Lifeforms,
  "Factions"=>MilestoneTab.Factions,
  "Other Stats" or "OtherStats"=>MilestoneTab.OtherStats,
  _=>throw new ArgumentOutOfRangeException(nameof(name),name,"Unknown stats group.")
 };

 public static IReadOnlyList<StatsSection> GetSections(string tab)=>MilestoneCatalog.GetSections(ParseTab(tab))
  .Select(key=>new StatsSection(key,UiStrings.Get(key),MilestoneCatalog.IconForSection(key))).ToArray();

 public static IReadOnlyList<StatsValue> GetValues(JsonObject save,string tab,string sectionKey)
 {
  var which=ParseTab(tab);
  var placements=MilestoneCatalog.GetSectionPlacements(which,sectionKey).ToList();
  if(which==MilestoneTab.OtherStats)
  {
   var ordered=new List<MilestonePlacement>();
   foreach(string subcategory in MilestoneCatalog.GetSubcategories(which,sectionKey))
    ordered.AddRange(placements.Where(p=>p.SubcategoryKey==subcategory)
     .OrderBy(p=>UiStrings.Get(p.GameNameKey),StringComparer.CurrentCultureIgnoreCase).ThenBy(p=>p.Order));
   if(ordered.Count>0)placements=ordered;
  }
  var values=new Dictionary<string,(int Value,bool Exists)>(StringComparer.Ordinal);
  var stats=MilestoneLogic.FindGlobalStats(save);
  if(stats!=null)
  {
   for(int i=0;i<stats.Length;i++)
   {
    if(stats.GetObject(i) is not {} entry || entry.GetString("Id") is not {} id || values.ContainsKey(id))continue;
    values[id]=(MilestoneLogic.ReadStatEntryValue(entry),entry.GetObject("Value")!=null);
   }
  }
  return placements.Select(p=>
  {
   bool exists=values.TryGetValue(p.StatId,out var current)&&current.Exists;
   int value=values.TryGetValue(p.StatId,out current)?current.Value:0;
   int rank=p.ShowGuildRank?MilestoneLogic.GetGuildRank(p.StatId,value):0;
   int max=p.ShowGuildRank?MilestoneLogic.GetGuildMaxRank(p.StatId):0;
   int next=p.ShowGuildRank?MilestoneLogic.GetGuildNextRankIn(p.StatId,value):0;
   return new StatsValue(p.StatId,UiStrings.Get(p.GameNameKey),p.SubcategoryKey,p.SubcategoryKey==null?null:UiStrings.Get(p.SubcategoryKey),value,exists,p.ShowGuildRank,rank,max,next);
  }).ToArray();
 }

 public static bool Apply(JsonObject save,IReadOnlyDictionary<string,int> updates)
 {
  if(updates.Count==0)return false;
  var stats=MilestoneLogic.FindGlobalStats(save);
  if(stats==null)return false;
  bool changed=false;
  for(int i=0;i<stats.Length;i++)
  {
   if(stats.GetObject(i) is not {} entry || entry.GetString("Id") is not {} id || !updates.TryGetValue(id,out int value))continue;
   if(entry.GetObject("Value") is not {} valueObject)continue;
   int current=MilestoneLogic.ReadStatEntryValue(entry);
   if(current==value)continue;
   MilestoneLogic.WriteStatEntryValue(entry,value);changed=true;
  }
  return changed;
 }
}

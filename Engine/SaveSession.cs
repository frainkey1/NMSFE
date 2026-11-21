using System.Globalization;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.IO;
using NMSE.Models;
namespace NMSE.Next.Engine;

public sealed record ToolEntry(int Index,string Name){public override string ToString()=>Name;}
public sealed record ToolDraft(string Name,int TypeIndex,int ClassIndex,string Seed,string Damage,string Mining,string Scan);
public sealed partial class SaveSession
{
 public JsonObject Data {get;private set;}=new();
 public string? SourcePath {get;private set;}
 public bool Dirty {get;private set;}
 public void MarkDirty()=>Dirty=true;
 public GameItemDatabase Items {get;}=new();
 public LocalisationService GameLocalisation {get;}=new();
 public string ResourceRoot {get;}
 public JsonObject? Player => Data.GetObject("PlayerStateData");
 public List<ToolEntry> Tools {get;private set;}=[];
 public SaveSession(string resources)
 {
  ResourceRoot=resources;
  var mapper=new JsonNameMapper();mapper.Load(Path.Combine(resources,"map","mapping.json"));JsonParser.SetDefaultMapper(mapper);
  UiStrings.SetDirectory(Path.Combine(resources,"ui","lang"));UiStrings.Load("en");
 }
 public void LoadDatabase(){Items.LoadItemsFromJsonDirectory(Path.Combine(ResourceRoot,"json"));SettlementDatabase.LoadFromFile(Path.Combine(ResourceRoot,"json","Settlement Perks.json"));RewardDatabase.LoadFromJsonDirectory(Path.Combine(ResourceRoot,"json"));FrigateTraitDatabase.LoadFromFile(Path.Combine(ResourceRoot,"json","Frigate Traits.json"));SpacePoiTableDatabase.LoadFromFile(Path.Combine(ResourceRoot,"json","Space POI.json"));}
 public void SetGameLanguage(string? languageTag)
 {
  GameLocalisation.SetLangDirectory(Path.Combine(ResourceRoot,"json","lang"));
  if(!GameLocalisation.LoadLanguage(languageTag))GameLocalisation.LoadLanguage(null);
  Items.ApplyLocalisation(GameLocalisation);
  SettlementDatabase.ApplyLocalisation(GameLocalisation);
  RewardDatabase.ApplyLocalisation(GameLocalisation);
  FrigateTraitDatabase.ApplyLocalisation(GameLocalisation);
  SpacePoiTableDatabase.ApplyLocalisation(GameLocalisation);
 }
 public void Load(string path)
 {
  path=Path.GetFullPath(path);
  var incomingHash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
  var loaded=SaveFileManager.LoadSaveFile(path);
  if(!incomingHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))throw new IOException("The save changed while loading. Please load it again.");
  if(loaded.GetObject("PlayerStateData")==null)throw new InvalidDataException("This file does not contain player save data.");
  ClearConsoleIdentity();Data=loaded;SourcePath=Path.GetFullPath(path);_loadedSourceHash=incomingHash;SaveFileManager.TryDetectActiveContext(loaded);Dirty=false;LoadAccountCompanion(path);DocumentRevision++;RefreshTools();
 }
 public void RefreshTools()
 {
  Tools=[];var tools=Player?.GetArray("Multitools");if(tools==null)return;
  foreach(var item in MultitoolLogic.BuildToolList(tools))Tools.Add(new(item.DataIndex,item.DisplayName));
 }
 internal JsonObject Tool(int index)=>Player?.GetArray("Multitools")?.GetObject(index)??throw new ArgumentOutOfRangeException(nameof(index));
 public JsonObject? Inventory(int index)=>Tool(index).GetObject("Store");
 public ToolDraft ReadTool(int index)
 {
  var d=MultitoolLogic.LoadToolData(Tool(index));
  return new(d.Name,d.TypeIndex,d.ClassIndex,d.Seed,d.DamageText,d.MiningText,d.ScanText);
 }
 public void ApplyTool(int index,ToolDraft draft)
 {
  var old=ReadTool(index);if(draft==old)return;
  var seed=SeedHelper.NormalizeSeed(draft.Seed)??throw new ArgumentException("Enter a valid hexadecimal seed, up to 16 digits.");
  bool typeChanged=draft.TypeIndex!=old.TypeIndex, classChanged=draft.ClassIndex!=old.ClassIndex;
  if(typeChanged && (draft.TypeIndex<0 || draft.TypeIndex>=MultitoolLogic.ToolTypes.Length) || classChanged && (draft.ClassIndex<0 || draft.ClassIndex>=4))throw new ArgumentException("Choose a valid type and class.");
  double Parse(string text){if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<0)throw new ArgumentException("Base stats must be finite, non-negative numbers.");return n;}
  var values=new MultitoolLogic.ToolSaveValues{Name=draft.Name,TypeIndex=draft.TypeIndex==old.TypeIndex?-1:draft.TypeIndex,ClassIndex=draft.ClassIndex,OriginalClassIndex=old.ClassIndex,Seed=seed,Damage=Parse(draft.Damage),Mining=Parse(draft.Mining),Scan=Parse(draft.Scan),DamageText=draft.Damage,MiningText=draft.Mining,ScanText=draft.Scan,RawStatValues=new(){["^WEAPON_DAMAGE"]=Parse(old.Damage),["^WEAPON_MINING"]=Parse(old.Mining),["^WEAPON_SCAN"]=Parse(old.Scan)}};
  if(draft.TypeIndex!=old.TypeIndex)values.IsLargeIndex=MultitoolLogic.GetCanonicalIsLargeIndex(MultitoolLogic.ToolTypes[draft.TypeIndex].Name);
  MultitoolLogic.SaveToolData(Tool(index),Player,values,PrimaryIndex==index);Dirty=true;RefreshTools();
 }
 public int PrimaryIndex {get {try{return Player?.GetInt("ActiveMultioolIndex")??0;}catch{return 0;}}}
 public void MakePrimary(int index)
 {
  var tool=Tool(index);RawNumberGuard.SetInt(Player,"ActiveMultioolIndex",index);
  if(tool.GetObject("Store") is {} store)Player!.Set("WeaponInventory",store.DeepClone());
  var weapon=Player?.GetObject("CurrentWeapon");
  if(weapon!=null){if(tool.GetObject("Resource")?.GetString("Filename") is {} file)weapon.Set("Filename",file);if(tool.GetArray("Seed") is {} seed)weapon.Set("GenerationSeed",seed.DeepClone());}
  Dirty=true;
 }
 public void ApplySlot(int toolIndex,JsonObject slot,int amount,int maximum,double damage)
 {
  if(amount< -1||maximum<0||amount>maximum||!double.IsFinite(damage)||damage<0||damage>1)throw new ArgumentException("Amount must be between zero and maximum (or -1 for installed tech); damage must be between zero and one.");
  RawNumberGuard.SetInt(slot,"Amount",amount);RawNumberGuard.SetInt(slot,"MaxAmount",maximum);
  if(slot.GetDouble("DamageFactor")!=damage)slot.Set("DamageFactor",damage);
  if(PrimaryIndex==toolIndex && Inventory(toolIndex) is {} store)Player!.Set("WeaponInventory",store.DeepClone());
  Dirty=true;
 }
 public void SaveCopy(string path)
 {
  path=Path.GetFullPath(path);
  if(string.Equals(path,SourcePath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Use a different filename for this preview's save copy.");
  if(File.Exists(path))throw new IOException("Choose a new output filename; an existing file will not be overwritten.");
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {SaveFileManager.SaveToFile(temporary,Data);var check=SaveFileManager.LoadSaveFile(temporary);if(check.ToString()!=Data.ToString())throw new InvalidDataException("Output verification failed.");File.Move(temporary,path);Dirty=false;}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public IReadOnlyList<StatsSection> GetStatsSections(string tab)=>StatsOperations.GetSections(tab);
 public IReadOnlyList<StatsValue> GetStats(string tab,string sectionKey)=>StatsOperations.GetValues(Data,tab,sectionKey);
 public bool ApplyStats(IReadOnlyDictionary<string,int> updates)
 {
  bool changed=StatsOperations.Apply(Data,updates);if(changed)Dirty=true;return changed;
 }
 public static string[] TypeNames=>MultitoolLogic.ToolTypes.Select(t=>t.Name).ToArray();
}


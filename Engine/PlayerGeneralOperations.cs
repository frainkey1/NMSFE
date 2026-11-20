using System.Globalization;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
namespace NMSE.Next.Engine;
public sealed record PlayerCoordinates(int Galaxy,int X,int Y,int Z,int System,int Planet);
public sealed partial class SaveSession
{
 public static readonly string[] PlayerPresets=["Invalid","Custom","Normal","Creative","Relaxed","Survival","Permadeath"];
 public bool PlayerExpedition=>SaveContext.IsExpeditionSave;
 public PlayerCoordinates? ReadPlayerCoordinates()
 {
  var a=Player?.GetObject("UniverseAddress");var g=a?.GetObject("GalacticAddress");return g==null?null:new(a!.GetInt("RealityIndex")+1,g.GetInt("VoxelX"),g.GetInt("VoxelY"),g.GetInt("VoxelZ"),g.GetInt("SolarSystemIndex"),g.GetInt("PlanetIndex"));
 }
 public static PlayerCoordinates ConvertPlayerPortal(string text,int galaxy)
 {
  if(!CoordinateHelper.PortalCodeToVoxel(text.Trim(),out int x,out int y,out int z,out int system,out int planet))throw new ArgumentException("Enter exactly 12 hexadecimal portal digits.");
  return new(galaxy,x,y,z,system,planet);
 }
 public void ApplyPlayerCoordinates(PlayerCoordinates c,bool validateOnly=false)
 {
  if(c.Galaxy<1||c.Galaxy>256||c.X< -2048||c.X>2047||c.Y< -128||c.Y>127||c.Z< -2048||c.Z>2047||c.System<0||c.System>4095||c.Planet<0||c.Planet>15)throw new ArgumentException("Coordinates are outside their supported ranges.");
  var a=Player?.GetObject("UniverseAddress")??throw new InvalidOperationException("No player address in this save.");var g=a.GetObject("GalacticAddress")??throw new InvalidOperationException("No galactic address.");
  var fields=new List<FieldEdit>();void Add(JsonObject target,string key,int n){if(!target.Contains(key))throw new InvalidOperationException("Address field unavailable: "+key);if(target.GetInt(key)==n)return;fields.Add(new(target,key,n.ToString(CultureInfo.InvariantCulture),FieldText(target.Get(key))));}
  Add(a,"RealityIndex",c.Galaxy-1);Add(g,"VoxelX",c.X);Add(g,"VoxelY",c.Y);Add(g,"VoxelZ",c.Z);Add(g,"SolarSystemIndex",c.System);Add(g,"PlanetIndex",c.Planet);ApplyFields(fields,validateOnly:validateOnly);
 }
 public string PlayerPortalCode=>ReadPlayerCoordinates() is {} c?CoordinateHelper.VoxelToPortalCode(c.X,c.Y,c.Z,c.System,c.Planet):"";
 public IReadOnlyDictionary<string,string> ReadPlayerLocationInfo()
 {
  var rows=new Dictionary<string,string>();var c=ReadPlayerCoordinates();if(c==null)return rows;
  rows["Galaxy"]=GalaxyDatabase.GetGalaxyDisplayName(c.Galaxy-1)+" · "+GalaxyDatabase.GetGalaxyType(c.Galaxy-1);
  rows["Portal code (hex)"]=PlayerPortalCode;rows["Portal code (decimal)"]=CoordinateHelper.PortalHexToDec(PlayerPortalCode);
  rows["Signal booster"]=CoordinateHelper.VoxelToSignalBooster(c.X,c.Y,c.Z,c.System);
  double distance=CoordinateHelper.GetDistanceToCenter(c.X,c.Y,c.Z);rows["Distance to center"]=distance.ToString("F0",CultureInfo.InvariantCulture)+" ly";rows["Jumps to center"]=CoordinateHelper.GetJumpsToCenter(distance,CoordinateHelper.DefaultHyperdriveRange).ToString();
  bool Here(string key){var a=Player!.GetObject(key);var g=a?.GetObject("GalacticAddress");return a!=null&&g!=null&&a.GetInt("RealityIndex")==c.Galaxy-1&&g.GetInt("VoxelX")==c.X&&g.GetInt("VoxelY")==c.Y&&g.GetInt("VoxelZ")==c.Z&&g.GetInt("SolarSystemIndex")==c.System;}
  rows["Freighter in system"]=Here("FreighterUniverseAddress")?"Yes":"No";rows["Nexus in system"]=Here("NexusUniverseAddress")?"Yes":"No";
  var planetSeeds=Player?.GetArray("PlanetSeeds");int count=0;if(planetSeeds!=null)for(int i=0;i<planetSeeds.Length;i++)if(planetSeeds.GetArray(i) is {} seed&&seed.Length>=2&&seed.Get(1)?.ToString()!="0x0")count++;rows["Planets in system"]=planetSeeds==null?"Unavailable":count.ToString(CultureInfo.InvariantCulture);
  return rows;
 }
 public int PlayerTotalWarps()
 {
  var groups=Player?.GetArray("Stats");if(groups!=null)for(int i=0;i<groups.Length;i++){var group=groups.GetObject(i);if(group?.GetString("GroupId")!="^GLOBAL_STATS")continue;var stats=group.GetArray("Stats");if(stats!=null)for(int j=0;j<stats.Length;j++)if(stats.GetObject(j) is {} stat&&stat.GetString("Id")=="^DIST_WARP")return stat.GetObject("Value")?.GetInt("IntValue")??0;}return 0;
 }
 public int PlayerWarpsUntilBattle=>Math.Max(0,5-(PlayerTotalWarps()-(Player?.GetInt("WarpsLastSpaceBattle")??0)));
 public int PlayerSecondsUntilBattle=>Math.Clamp(10800-((Data.GetObject("CommonStateData")?.GetInt("TotalPlayTime")??0)-(Player?.GetInt("TimeLastSpaceBattle")??0)),0,10800);
 public void SetPlayerBattleWarps(int remaining,bool validateOnly=false)
 {
  if(remaining<0||remaining>999)throw new ArgumentException("Warps must be between 0 and 999.");var p=Player??throw new InvalidOperationException();if(!p.Contains("WarpsLastSpaceBattle"))throw new InvalidOperationException("Space battle state is unavailable.");
  int value=Math.Max(0,PlayerTotalWarps()-5+remaining);ApplyFields([new(p,"WarpsLastSpaceBattle",value.ToString(CultureInfo.InvariantCulture),FieldText(p.Get("WarpsLastSpaceBattle")))],validateOnly:validateOnly);
 }
 public void TriggerPlayerBattle()
 {
  var p=Player??throw new InvalidOperationException();if(!p.Contains("TimeLastSpaceBattle")||!p.Contains("WarpsLastSpaceBattle"))throw new InvalidOperationException("Space battle state is unavailable.");
  ApplyFields([new(p,"TimeLastSpaceBattle","0",FieldText(p.Get("TimeLastSpaceBattle"))),new(p,"WarpsLastSpaceBattle","0",FieldText(p.Get("WarpsLastSpaceBattle")))]);
 }
 public void ApplyPlayerGeneralFields(IEnumerable<FieldEdit> fields,bool validateOnly=false)
 {
  var edits=fields.ToArray();var p=Player??throw new InvalidOperationException();var preset=p.GetObject("DifficultyState")?.GetObject("Preset");
  foreach(var f in edits)
  {
   if(f.Text==f.Baseline)continue;
   if(f.Key is string key && key=="DifficultyPresetType")
   {
    if(PlayerExpedition)throw new InvalidOperationException("Expedition difficulty is controlled by the game.");
    if(!PlayerPresets.Contains(f.Text))throw new ArgumentException("Choose a supported difficulty preset.");
   }
   if(ReferenceEquals(f.Parent,Data.GetObject("SpawnStateData"))&&f.Key is string k&&k=="LastKnownPlayerState"&&!CoordinateHelper.PlayerStates.Contains(f.Text))throw new ArgumentException("Choose a supported player state.");
  }
  ApplyFields(edits,validateOnly:validateOnly);if(validateOnly)return;
  if(preset!=null&&edits.Any(f=>ReferenceEquals(f.Parent,preset)&&f.Text!=f.Baseline)){MainStatsLogic.ApplyGameModeForPreset(Data,preset.GetString("DifficultyPresetType"));Dirty=true;}
  if(edits.Any(f=>ReferenceEquals(f.Parent,p)&&Equals(f.Key,"HasDiscoveredPurpleSystems")&&f.Text!=f.Baseline))
  {
   var known=p.GetArray("KnownTech");if(known!=null){int at=known.IndexOf("^HDRIVEBOOST4");if(p.GetBool("HasDiscoveredPurpleSystems")&&at<0)known.Add("^HDRIVEBOOST4");else if(!p.GetBool("HasDiscoveredPurpleSystems")&&at>=0)known.RemoveAt(at);}
  }
 }
 public void ImportPlayerOutfit(int index,string path)
 {
  var outfits=Player?.GetArray("Outfits")??throw new InvalidOperationException("No outfits in this save.");if(index<0||index>=outfits.Length)throw new ArgumentOutOfRangeException(nameof(index));
  var imported=JsonObject.ImportFromFile(path);if(!new[]{"DescriptorGroups","Colours","PaletteID"}.Any(imported.Contains))throw new ArgumentException("The file does not contain outfit customization.");
  var clone=outfits.DeepClone();OutfitLogic.ImportOutfit(clone,index,path);if(outfits.GetObject(index)?.ToString()==clone.GetObject(index)?.ToString())return;outfits.Set(index,clone.GetObject(index)!.DeepClone());Dirty=true;
 }
 public void ApplyPlayerOutfit(int index)
 {
  var p=Player??throw new InvalidOperationException();var outfits=p.GetArray("Outfits")??throw new InvalidOperationException("No outfits.");if(index<0||index>=outfits.Length)throw new ArgumentOutOfRangeException(nameof(index));var outfit=outfits.GetObject(index)??throw new ArgumentException("Empty outfit.");
  var copy=p.DeepClone();OutfitLogic.CopyToCustomData(outfit.DeepClone(),copy);var next=copy.GetArray("CharacterCustomisationData");if(next==null)throw new InvalidOperationException("No character customization.");if(p.GetArray("CharacterCustomisationData")?.ToString()==next.ToString())return;p.Set("CharacterCustomisationData",next.DeepClone());Dirty=true;
 }
}

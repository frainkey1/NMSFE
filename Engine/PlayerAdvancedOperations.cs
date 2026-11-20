using System.Globalization;
using System.Security.Cryptography;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Models;

namespace NMSE.Next.Engine;

/// <summary>A detached, lossless editing buffer. Merely reading it never changes the save.</summary>
public sealed class PlayerAdvancedDraft
{
 public JsonObject Player {get;}
 public JsonObject? Season {get;}
 internal string OriginalPlayer {get;}
 internal string? OriginalSeason {get;}
 internal PlayerAdvancedDraft(JsonObject player,JsonObject? season){Player=player.DeepClone();Season=season?.DeepClone();OriginalPlayer=Player.ToString();OriginalSeason=Season?.ToString();}
 public bool Changed=>Player.ToString()!=OriginalPlayer || Season?.ToString()!=OriginalSeason;
 public static string GenerateSeed(){var bytes=RandomNumberGenerator.GetBytes(8);return SeedHelper.FormatSeed(BitConverter.ToUInt64(bytes));}
 public static readonly string[] TeamValues=["Red","Green","Blue"];
 public static readonly string[] RaceValues=["None","Traders","Warriors","Explorers","Robots","Atlas","Diplomats","Exotics","Builders"];
 public static readonly string[] BooleanFields=["UseTeamShipSeeds","UseTeamShipPalettes","UseCommunityTeamPalettes","NeverAllowShipPurchases","AllowOnlyCorvetteShipPurchases","NeverAllowCorvettePurchases","AllowSaveContextCorvetteTransfer","AllowSaveContextShipTransfer","AllowSaveContextMultitoolTransfer","OnlyCorvettesSpawnWhenPlayerTeleports","OnlyCorvetteLauncherCanBeRepaired"];
 public static readonly string[] SeedFields=["BlockShipPurchasesUntilMilestoneWithSeedComplete","BlockShipRepairUntilMilestoneWithSeedComplete","WeaponSeed","ShipSeed"];
 public static void SetSeed(JsonArray pair,bool active,string text)
 {
  if(pair.Length<2)throw new ArgumentException("The existing seed pair is incomplete.");
  var normalized=SeedHelper.NormalizeSeed(text);
  string hex=(normalized??"").Replace("0x","",StringComparison.Ordinal);
  if(normalized==null || hex.Length>16)throw new ArgumentException("A seed must contain 1–16 hexadecimal digits, optionally beginning with 0x.");
  pair.Set(0,active);pair.Set(1,normalized);
 }
 internal void ValidateChanges()
 {
  var original=JsonObject.Parse(OriginalPlayer);
  var hot=Player.GetArray("HotActions");var oldHot=original.GetArray("HotActions");
  if(hot!=null)for(int c=0;c<Math.Min(3,hot.Length);c++){
   var slots=hot.GetObject(c)?.GetArray("KeyActions");var oldSlots=oldHot!=null&&c<oldHot.Length?oldHot.GetObject(c)?.GetArray("KeyActions"):null;
   if(slots==null)continue;
   for(int s=0;s<Math.Min(10,slots.Length);s++){
    if(slots.Get(s) is not JsonObject slot)continue;
    var old=oldSlots!=null&&s<oldSlots.Length?oldSlots.Get(s):null;
    if(slot.ToString()==old?.ToString())continue;
    var name=HotActionsLogic.GetActionName(slot);var info=HotActionsLogic.FindAction(name);
    if(info==null)continue; // Keep future game enum values losslessly.
    int Integer(object? raw,int fallback){if(raw==null)return fallback;if(!int.TryParse(raw.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int result))throw new ArgumentException("Hotkey parameters must be whole numbers.");return result;}
    var index=slot.GetObject("InventoryIndex");
    if(info.ParameterKind!=HotActionParameterKind.None&&info.ParameterKind!=HotActionParameterKind.EmoteId)
     ValidateHotSlot(name,Integer(slot.Get("Number"),0),info.ParameterKind==HotActionParameterKind.InventoryItem?Integer(index?.Get("X"),-1):-1,info.ParameterKind==HotActionParameterKind.InventoryItem?Integer(index?.Get("Y"),-1):-1);
   }
  }
  if(Season==null)return;var oldSeason=OriginalSeason==null?null:JsonObject.Parse(OriginalSeason);
  void Pair(JsonArray? value,JsonArray? old){if(value==null||value.ToString()==old?.ToString())return;if(value.Length<2||value.Get(0) is not bool)throw new ArgumentException("A seed requires an active flag and a hexadecimal value.");var normalized=SeedHelper.NormalizeSeed(value.Get(1)?.ToString());if(normalized==null||normalized.Replace("0x","",StringComparison.Ordinal).Length>16)throw new ArgumentException("A seed must contain 1–16 hexadecimal digits.");}
  foreach(var key in SeedFields)Pair(Season.GetArray(key),oldSeason?.GetArray(key));
  var teams=Season.GetArray("TeamShipSeeds");var oldTeams=oldSeason?.GetArray("TeamShipSeeds");
  if(teams!=null)for(int i=0;i<Math.Min(3,teams.Length);i++)Pair(teams.GetArray(i),oldTeams!=null&&i<oldTeams.Length?oldTeams.GetArray(i):null);
 }
 internal static void ValidateHotSlot(string action,int number,int x,int y)
 {
  var info=HotActionsLogic.FindAction(action)??throw new ArgumentException("Unknown hotkey action.");
  int max=info.ParameterKind is HotActionParameterKind.Index or HotActionParameterKind.CreatureFood?9999:999;
  if(info.ParameterKind is HotActionParameterKind.Index or HotActionParameterKind.InventoryItem or HotActionParameterKind.WeaponMode or HotActionParameterKind.CreatureFood)
   if(number<0||number>max)throw new ArgumentException($"The action number must be between 0 and {max}.");
  if(info.ParameterKind==HotActionParameterKind.InventoryItem && (x< -1||x>999||y< -1||y>999))throw new ArgumentException("Inventory coordinates must be between -1 and 999.");
 }
}
public sealed partial class SaveSession
{
 public PlayerAdvancedDraft ReadPlayerAdvanced()=>new(Player??new JsonObject(),Data.GetObject("CommonStateData")?.GetObject("SeasonData"));
 public void ApplyPlayerAdvanced(PlayerAdvancedDraft draft)
 {
  if(!draft.Changed)return;
  draft.ValidateChanges();
  if(draft.Player.ToString()!=draft.OriginalPlayer && (Player==null||draft.Player.GetArray("HotActions")==null))throw new InvalidOperationException("Player hotkeys are unavailable.");
  if(draft.Season?.ToString()!=draft.OriginalSeason && Data.GetObject("CommonStateData")?.GetObject("SeasonData")==null)throw new InvalidOperationException("Season data is unavailable.");
  // Only these two subtrees are replaced; unrelated pending editor changes stay intact.
  if(draft.Player.ToString()!=draft.OriginalPlayer) {
   if(Player==null)throw new InvalidOperationException("Player state is unavailable.");
   Player.Set("HotActions",draft.Player.GetArray("HotActions")!.DeepClone());
  }
  if(draft.Season?.ToString()!=draft.OriginalSeason){
   var common=Data.GetObject("CommonStateData")??throw new InvalidOperationException("Common state is unavailable.");
   if(common.GetObject("SeasonData")==null)throw new InvalidOperationException("Season data is unavailable.");
   common.Set("SeasonData",draft.Season!.DeepClone());
  }
  Dirty=true;
 }
}

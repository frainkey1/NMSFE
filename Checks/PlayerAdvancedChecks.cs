using NMSE.Core;
using NMSE.Models;
using NMSE.Next.Engine;

internal static class PlayerAdvancedChecks
{
 public static void Run(string resources)
 {
  var session=new SaveSession(resources);
  session.Data.Set("PlayerStateData",JsonObject.Parse("{\"Unrelated\":1.23456789}"));
  session.Data.Set("CommonStateData",JsonObject.Parse("{\"SeasonData\":{\"CachedPlayerCommunityTeam\":{\"CommunityTeam\":\"FutureTeam\",\"Hidden\":17},\"WeaponSeed\":[false,\"0xABC\",\"hidden\"],\"Hidden\":99}}"));
  string initial=session.Data.ToString();
  var draft=session.ReadPlayerAdvanced();
  if(draft.Changed||session.Dirty||initial!=session.Data.ToString())throw new Exception("Advanced read mutated data.");
  session.ApplyPlayerAdvanced(draft);
  if(session.Dirty||initial!=session.Data.ToString())throw new Exception("Advanced no-op changed data.");
  var pair=draft.Season!.GetArray("WeaponSeed")!;
  string before=draft.Season.ToString();
  try{PlayerAdvancedDraft.SetSeed(pair,true,"0xINVALID");throw new Exception("Invalid seed accepted.");}catch(ArgumentException){}
  if(draft.Season.ToString()!=before||session.Data.ToString()!=initial)throw new Exception("Seed validation was not atomic.");
  foreach(var seed in Enumerable.Range(0,32).Select(_=>PlayerAdvancedDraft.GenerateSeed()))if(seed.Length!=18||!seed.StartsWith("0x")||!ulong.TryParse(seed[2..],System.Globalization.NumberStyles.HexNumber,null,out _))throw new Exception("Generated seed is invalid.");
  PlayerAdvancedDraft.SetSeed(pair,true,"0xabcd");
  if(pair.GetString(1)!="0xABCD"||pair.GetString(2)!="hidden")throw new Exception("Seed normalization destroyed hidden state.");
  HotActionsLogic.SetSlot(HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(draft.Player,2)).GetObject(9),"Emote","^EMOTE_WAVE");
  session.ApplyPlayerAdvanced(draft);
  if(!session.Dirty||session.Player!.GetArray("HotActions")!.Length!=3||session.Data.GetObject("CommonStateData")!.GetObject("SeasonData")!.GetObject("CachedPlayerCommunityTeam")!.GetString("CommunityTeam")!="FutureTeam")throw new Exception("Advanced apply lost unknown data.");
  var export=HotActionsLogic.BuildExport(session.Player!);var imported=new JsonObject();
  if(HotActionsLogic.ApplyImport(imported,export)!=30||HotActionsLogic.GetActionName(imported.GetArray("HotActions")!.GetObject(2).GetArray("KeyActions")!.GetObject(9))!="Emote")throw new Exception("Hotkey export/import roundtrip failed.");
  foreach(var invalid in new[]{("CallShip",10000,0,0),("Charge",-1,0,0),("Repair",0,-2,0),("ChangeSecondaryWeapon",1000,0,0)}){
   try{PlayerAdvancedDraft.ValidateHotSlot(invalid.Item1,invalid.Item2,invalid.Item3,invalid.Item4);throw new Exception("Hotkey bounds accepted.");}catch(ArgumentException){}
  }
  // Exercise every canonical action through the real atomic apply boundary.
  foreach(var action in HotActionsLogic.Actions){
   var isolated=new SaveSession(resources);isolated.Data.Set("PlayerStateData",new JsonObject());isolated.Data.Set("CommonStateData",JsonObject.Parse("{\"SeasonData\":{\"WeaponSeed\":[false,\"0x1\"]}}"));
   string untouched=isolated.Data.ToString();var noop=isolated.ReadPlayerAdvanced();isolated.ApplyPlayerAdvanced(noop);
   if(isolated.Dirty||isolated.Data.ToString()!=untouched)throw new Exception("Missing-hotkeys no-op mutated save.");
   var valid=isolated.ReadPlayerAdvanced();HotActionsLogic.SetSlot(HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(valid.Player,0)).GetObject(0),action.Name);
   isolated.ApplyPlayerAdvanced(valid);
   if(HotActionsLogic.GetActionName(isolated.Player!.GetArray("HotActions")!.GetObject(0).GetArray("KeyActions")!.GetObject(0))!=action.Name)throw new Exception("Canonical action did not apply: "+action.Name);
   if(action.ParameterKind is HotActionParameterKind.None or HotActionParameterKind.EmoteId)continue;
   var invalidDraft=isolated.ReadPlayerAdvanced();var invalidSlot=HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(invalidDraft.Player,0)).GetObject(0);
   invalidSlot.Set("Number",action.ParameterKind is HotActionParameterKind.Index or HotActionParameterKind.CreatureFood?10000:1000);
   invalidDraft.Season!.Set("PersistentPOI","Must not commit");
   string snapshot=isolated.Data.ToString();
   try{isolated.ApplyPlayerAdvanced(invalidDraft);throw new Exception("Invalid imported parameters accepted: "+action.Name);}catch(ArgumentException){}
   if(isolated.Data.ToString()!=snapshot)throw new Exception("Invalid action partially committed: "+action.Name);
  }
  var atomic=new SaveSession(resources);atomic.Data.Set("PlayerStateData",new JsonObject());atomic.Data.Set("CommonStateData",JsonObject.Parse("{\"SeasonData\":{\"WeaponSeed\":[false,\"0x1\"]}}"));
  var badSeed=atomic.ReadPlayerAdvanced();HotActionsLogic.SetSlot(HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(badSeed.Player,0)).GetObject(0),"PhotoMode");badSeed.Season!.GetArray("WeaponSeed")!.Set(1,"not hex");
  string atomicBefore=atomic.Data.ToString();try{atomic.ApplyPlayerAdvanced(badSeed);throw new Exception("Invalid direct seed accepted.");}catch(ArgumentException){}
  if(atomic.Dirty||atomic.Data.ToString()!=atomicBefore)throw new Exception("Invalid seed partially committed hotkeys.");
  Console.WriteLine("PASS: advanced Player read/no-op fidelity, seed generation and atomic validation, hidden fields and hotkey roundtrip.");
 }
}


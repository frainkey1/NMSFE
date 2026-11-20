using NMSE.Models;
using NMSE.Next.Engine;
internal static class PlayerGeneralChecks
{
 internal static void Run(string resources)
 {
  var s=new SaveSession(resources);s.Data.Set("PlayerStateData",JsonObject.Parse("{\"UniverseAddress\":{\"RealityIndex\":0,\"GalacticAddress\":{\"VoxelX\":1136,\"VoxelY\":29,\"VoxelZ\":-1270,\"SolarSystemIndex\":17,\"PlanetIndex\":0}},\"PlanetSeeds\":[[true,\"0xABC\"],[true,\"0x0\"]],\"WarpsLastSpaceBattle\":2,\"TimeLastSpaceBattle\":30,\"OnOtherSideOfPortal\":false,\"HasDiscoveredPurpleSystems\":false,\"KnownTech\":[\"^UNKNOWN\"],\"DifficultyState\":{\"Preset\":{\"DifficultyPresetType\":\"Normal\"}},\"Outfits\":[{\"DescriptorGroups\":[\"^TEST\"],\"Scale\":1.2}]}"));s.Data.Set("CommonStateData",JsonObject.Parse("{\"TotalPlayTime\":300}"));
  var rawAddress=s.Player!.GetObject("UniverseAddress")!.GetObject("GalacticAddress")!;rawAddress.Set("VoxelX",JsonParser.ParseValue("1.136e3"));
  string originalToken=SaveSession.FieldText(rawAddress.Get("VoxelX"));var same=s.ReadPlayerCoordinates()!;s.ApplyPlayerCoordinates(same);if(SaveSession.FieldText(rawAddress.Get("VoxelX"))!=originalToken)throw new Exception("No-op rewrote raw coordinate token.");
  s.ApplyPlayerCoordinates(same with{Planet=1});if(SaveSession.FieldText(rawAddress.Get("VoxelX"))!=originalToken)throw new Exception("Sibling edit rewrote raw coordinate token.");s.ApplyPlayerCoordinates(same);
  var before=s.Data.ToString();var c=s.ReadPlayerCoordinates()!;if(s.PlayerPortalCode!="00111D B0A470".Replace(" ",""))throw new Exception("Portal mapping differs from original");
  s.ApplyPlayerCoordinates(c);if(before!=s.Data.ToString())throw new Exception("No-op coordinates mutated save.");
  if(s.ReadPlayerLocationInfo()["Planets in system"]!="1")throw new Exception("Planet count uses wrong source.");
  foreach(var v in new[]{new PlayerCoordinates(0,0,0,0,0,0),new(1,2048,0,0,0,0),new(1,0,128,0,0,0),new(1,0,0,0,4096,0),new(1,0,0,0,0,16)}){try{s.ApplyPlayerCoordinates(v);throw new Exception("Invalid coordinates accepted");}catch(ArgumentException){}}
  if(before!=s.Data.ToString())throw new Exception("Invalid coordinates partially applied.");
  var converted=SaveSession.ConvertPlayerPortal(s.PlayerPortalCode,1);if(c!=converted)throw new Exception("Portal conversion failed.");
  s.ApplyPlayerCoordinates(new(257,-2048,-128,2047,4095,15));if(s.ReadPlayerCoordinates()!=new PlayerCoordinates(257,-2048,-128,2047,4095,15))throw new Exception("Coordinate boundaries failed.");
  var p=s.Player!;s.ApplyPlayerGeneralFields([new(p,"HasDiscoveredPurpleSystems","true","false")]);if(p.GetArray("KnownTech")!.IndexOf("^HDRIVEBOOST4")<0||p.GetArray("KnownTech")!.IndexOf("^UNKNOWN")<0)throw new Exception("Purple warp lost knowledge.");
  s.ApplyPlayerGeneralFields([new(p,"HasDiscoveredPurpleSystems","false","true")]);if(p.GetArray("KnownTech")!.IndexOf("^HDRIVEBOOST4")>=0)throw new Exception("Purple warp disable mismatch.");
  s.TriggerPlayerBattle();if(p.GetInt("TimeLastSpaceBattle")!=0||p.GetInt("WarpsLastSpaceBattle")!=0)throw new Exception("Battle trigger mismatch.");
  s.ApplyPlayerOutfit(0);if(p.GetArray("CharacterCustomisationData")?.GetObject(0)?.GetObject("CustomData")?.GetArray("DescriptorGroups")?.GetString(0)!="^TEST")throw new Exception("Outfit apply failed.");
  Console.WriteLine("PASS: Player coordinates/portal roundtrip, ranges/no-op fidelity, original planet count, purple-drive synchronization, battle and outfit application.");
 }
}

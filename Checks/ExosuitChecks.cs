using NMSE.Next.Engine;
using NMSE.Models;
internal static class ExosuitChecks
{
 public static void Run(string resources)
 {
  var session=new SaveSession(resources);var slot=JsonObject.Parse("{\"Id\":\"^FUEL1\",\"Amount\":5,\"MaxAmount\":100,\"DamageFactor\":0,\"Untouched\":1.23456789}");
  var list=new JsonArray();list.Add(slot);var inv=new JsonObject();inv.Set("Slots",list);var player=new JsonObject();player.Set("Inventory",inv);session.Data.Set("PlayerStateData",player);
  string original=session.Data.ToString();session.ApplyExosuitSlot("Inventory",slot,5,100,0);
  if(session.Data.ToString()!=original||session.Dirty)throw new Exception("No-op exosuit apply changed data");
  try{session.ApplyExosuitSlot("Inventory",slot,101,100,0);throw new Exception("Invalid exosuit amount accepted");}catch(ArgumentException){}
  if(session.Data.ToString()!=original)throw new Exception("Invalid exosuit edit changed data");
  try{session.ApplyExosuitSlot("Inventory_TechOnly",slot,5,100,0);throw new Exception("Cross-inventory slot accepted");}catch(ArgumentException){}
  var untouched=slot.Get("Untouched");session.ApplyExosuitSlot("Inventory",slot,8,100,.5);
  if(!session.Dirty||slot.GetInt("Amount")!=8||slot.GetDouble("DamageFactor")!=.5||!ReferenceEquals(untouched,slot.Get("Untouched")))throw new Exception("Exosuit edit or raw-value preservation failed");
  Console.WriteLine("PASS: synthetic exosuit slot edits, membership validation, invalid-edit atomicity and no-op raw fidelity");
 }
}

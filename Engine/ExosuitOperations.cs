using NMSE.Models;
using NMSE.Core.Utilities;
namespace NMSE.Next.Engine;
public sealed partial class SaveSession
{
 public void ApplyExosuitSlot(string inventoryKey,JsonObject slot,int amount,int maximum,double damage)
 {
  if(inventoryKey is not ("Inventory" or "Inventory_TechOnly"))throw new ArgumentException("Unknown exosuit inventory.");
  ApplyInventorySlot(Player?.GetObject(inventoryKey),slot,amount,maximum,damage);
 }
 public void ApplyInventorySlot(JsonObject? inventory,JsonObject slot,int amount,int maximum,double damage)
 {
  var slots=inventory != null && IsAttached(inventory) ? inventory.GetArray("Slots") : null;
  if(slots==null || !Enumerable.Range(0,slots.Length).Any(i=>ReferenceEquals(slots.GetObject(i),slot)))throw new ArgumentException("This slot does not belong to the selected inventory.");
  if(amount< -1||maximum<0||amount>maximum||!double.IsFinite(damage)||damage<0||damage>1)throw new ArgumentException("Amount must be between zero and maximum (or -1 for installed tech); damage must be between zero and one.");
  if(slot.GetInt("Amount")==amount && slot.GetInt("MaxAmount")==maximum && slot.GetDouble("DamageFactor")==damage)return;
  RawNumberGuard.SetInt(slot,"Amount",amount);RawNumberGuard.SetInt(slot,"MaxAmount",maximum);
  if(slot.GetDouble("DamageFactor")!=damage)slot.Set("DamageFactor",damage);Dirty=true;
 }
}

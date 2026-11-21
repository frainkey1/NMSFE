using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record InventoryKind(string Owner, string Group, bool Technology, bool Supercharge, bool CanDisable = true)
{
 public bool Accepts(GameItem item) =>
  !GameItemDatabase.IsPickerExcluded(item.Id)
  && InventoryStackDatabase.CanAddItemToInventory(item, Technology, !Technology)
  && (!Technology || item.IsValidForOwner(Owner));
}

public sealed partial class SaveSession
{
 public void EditInventory(JsonObject inventory, Action<JsonObject> edit)
 {
  if (!IsAttached(inventory)) throw new InvalidOperationException("This inventory is no longer open.");
  var draft = inventory.DeepClone();
  edit(draft);
  foreach (string key in new[] { "Slots", "ValidSlotIndices", "SpecialSlots" })
  {
   if (draft.GetArray(key) is {} next && next.ToString() != inventory.GetArray(key)?.ToString())
   {
    inventory.Set(key, next);
    Dirty = true;
   }
  }
  foreach (string key in new[] { "Width", "Height" })
  {
   int next=0,current=0;try{next=draft.GetInt(key);}catch{}try{current=inventory.GetInt(key);}catch{}
   if(next!=current){inventory.Set(key,next);Dirty=true;}
  }
  SyncPrimaryInventory(inventory);
 }

 public void ResizeInventory(JsonObject inventory,int width,int height)
 {
  if(width<1||width>20||height<1||height>20)throw new ArgumentOutOfRangeException(nameof(width),"Inventory dimensions must be from 1 to 20.");
  EditInventory(inventory,draft=>{draft.Set("Width",width);draft.Set("Height",height);});
 }

 void SyncPrimaryInventory(JsonObject inventory)
 {
  if (Player != null && Tools.Any(tool => tool.Index == PrimaryIndex && ReferenceEquals(Inventory(tool.Index), inventory)))
   Player.Set("WeaponInventory", inventory.DeepClone());
 }

 public static string InventoryItemId(JsonObject? item) => item?.Get("Id") switch
 {
  string id => id,
  BinaryData id => id.ToString(),
  _ => ""
 };

 public object? BestInventoryRoll(JsonObject slot)
 {
  string id = InventoryItemId(slot);
  int separator = id.IndexOf('#');
  if (separator < 1) return null;
  bool binary = slot.Get("Id") is BinaryData;
  string techId = id[..separator];
  if (binary && !TechPackLogic.TryResolveTechId(id, Items, out techId, out _)) return null;
  var item = Items.GetItem(techId);
  if (item?.IsProcedural != true || !ProcTechData.TryGetBestSeed(item.Id, out uint seed)) return null;
  string updated = id[..(separator + 1)] + seed.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
  return binary ? new BinaryData(System.Text.Encoding.Latin1.GetBytes(updated)) : updated;
 }

 public IReadOnlyList<(string Name, Action Move)> TechnologyTransfers(JsonObject source, int x, int y, bool packing)
 {
  if (Player is not {} player || source.GetArray("Slots") is not {} slots) return [];
  int index = -1;
  for (int i = 0; i < slots.Length; i++)
   if (AtPosition(slots.GetObject(i)?.GetObject("Index"), x, y)) { index = i; break; }
  if (index < 0 || slots.GetObject(index) is not {} slot) return [];
  string id = InventoryItemId(slot);
  string[] parts = id.Split('#', 2);
  string techId = parts[0];
  string? seed = parts.Length == 2 ? parts[1] : null;
  GameItem? tech = Items.GetItem(techId);
  if (packing)
  {
   if (!TechPackLogic.CanPackageTech(tech, id, slot.GetDouble("DamageFactor"), slots)) return [];
  }
  else
  {
   if (slot.GetInt("Amount") != 1) return [];
   if (!TechPackLogic.TryResolveTechId(id, Items, out techId, out seed) &&
       !TechPackLogic.TryResolveModuleTech(tech, out techId)) return [];
   tech = Items.GetItem(techId);
   if (tech == null) return [];
  }

  var transfers = new List<(string, Action)>();
  foreach (var destination in TechPackLogic.EnumerateDestinations(player, !packing))
  {
   if (ReferenceEquals(destination.Inventory, source)) continue;
   if (!packing)
   {
    string owner = destination.NameKey switch
    {
     "techpack.dest_tech_exosuit" => "Suit",
     "techpack.dest_tech_multitool" => "Weapon",
     "techpack.dest_tech_ship" => ShipInventoryOwner(destination.Inventory.Parent as JsonObject),
     "techpack.dest_tech_freighter" => "Freighter",
     _ => VehicleInventoryOwner(destination.Inventory)
    };
    if (!tech!.IsValidForOwner(owner)) continue;
   }
   transfers.Add((UiStrings.Format(destination.NameKey, destination.NameArgs), () =>
   {
    if (!IsAttached(source) || !IsAttached(destination.Inventory) ||
        !ReferenceEquals(source.GetArray("Slots"), slots) ||
        !ReferenceEquals(destination.Inventory.GetArray("Slots"), destination.Slots) ||
        !ReferenceEquals(slots.GetObject(index), slot))
     throw new InvalidOperationException("The inventory changed. Reopen the menu and try again.");
    var free = TechPackLogic.GetFreePositions(destination.Inventory, destination.Slots);
    if (free.Count == 0) throw new InvalidOperationException("The destination has no free slots.");
    var (targetX, targetY) = free[0];
    bool moved = packing
     ? TechPackLogic.TryPack(slots, index, techId, seed, destination, targetX, targetY, Items)
     : TechPackLogic.TryUnpack(slots, index, destination, tech!, techId, seed, targetX, targetY);
    if (!moved) throw new InvalidOperationException("The technology could not be moved.");
    SyncPrimaryInventory(source);
    SyncPrimaryInventory(destination.Inventory);
    Dirty = true;
   }));
  }
  return transfers;
 }

 public static string ShipInventoryOwner(JsonObject? ship)
 {
  string resource = ship?.GetObject("Resource")?.GetString("Filename") ?? "";
  return StarshipLogic.GetOwnerTypeForShip(StarshipLogic.LookupShipTypeName(resource));
 }

 public static string VehicleInventoryOwner(JsonObject inventory)
 {
  if (inventory.Parent is not JsonObject vehicle || vehicle.Parent is not JsonArray vehicles) return "Vehicle";
  foreach (var (index, name) in ExocraftLogic.VehicleTypes)
   if (index < vehicles.Length && ReferenceEquals(vehicles.GetObject(index), vehicle))
    return ExocraftLogic.GetOwnerTypeForVehicle(name);
  return "Vehicle";
 }

 public static JsonObject SlotPosition(int x, int y)
 {
  var index = new JsonObject();
  index.Set("X", x);
  index.Set("Y", y);
  return index;
 }

 public static bool AtPosition(JsonObject? index, int x, int y) =>
  index != null && index.GetInt("X") == x && index.GetInt("Y") == y;

 public static void RemoveEntries(JsonArray array, Func<JsonObject, bool> match)
 {
  for (int i = array.Length - 1; i >= 0; i--)
   if (array.GetObject(i) is {} item && match(item)) array.RemoveAt(i);
 }

 public static void PutInventoryItem(JsonObject draft, int x, int y, JsonObject item)
 {
  var slots = draft.GetArray("Slots") ?? throw new InvalidOperationException("This inventory has no slot data.");
  item.Set("Index", SlotPosition(x, y));
  for (int i = 0; i < slots.Length; i++)
  {
   if (!AtPosition(slots.GetObject(i)?.GetObject("Index"), x, y)) continue;
   slots.Set(i, item);
   return;
  }
  slots.Add(item);
 }

 public static JsonObject NewInventoryItem(InventoryKind kind, GameItem item)
 {
  if (!kind.Accepts(item)) throw new ArgumentException("This item cannot go in this inventory.");
  var slot = new JsonObject();
  string id = "^" + item.Id.TrimStart('^');
  if (item.IsProcedural) id += "#" + TechPackLogic.GenerateProceduralSeed();
  string inventoryType = InventoryStackDatabase.ResolveInventoryTypeForItem(item, kind.Technology);
  int maximum = InventoryStackDatabase.GetMaxAmount(item, inventoryType, kind.Group);
  var type = new JsonObject();
  type.Set("InventoryType", inventoryType);
  slot.Set("Type", type);
  slot.Set("Id", id);
  slot.Set("Amount", kind.Technology ? item.BuildFullyCharged ? maximum : 0 : Math.Min(1, maximum));
  slot.Set("MaxAmount", maximum);
  slot.Set("DamageFactor", 0.0);
  slot.Set("FullyInstalled", true);
  slot.Set("AddedAutomatically", false);
  return slot;
 }

 public static void SetSlotEnabled(JsonObject draft, int x, int y, bool enabled)
 {
  var valid = draft.GetArray("ValidSlotIndices");
  if (valid == null) { valid = new JsonArray(); draft.Set("ValidSlotIndices", valid); }
  RemoveEntries(valid, index => AtPosition(index, x, y));
  if (enabled) valid.Add(SlotPosition(x, y));
 }

 public static void SetSupercharge(JsonObject draft, int x, int y, bool enabled)
 {
  var special = draft.GetArray("SpecialSlots");
  if (special == null) { special = new JsonArray(); draft.Set("SpecialSlots", special); }
  RemoveEntries(special, slot => IsSupercharge(slot) && AtPosition(slot.GetObject("Index"), x, y));
  if (!enabled) return;
  var entry = new JsonObject();
  var type = new JsonObject();
  type.Set("InventorySpecialSlotType", "TechBonus");
  entry.Set("Type", type);
  entry.Set("Index", SlotPosition(x, y));
  special.Add(entry);
 }

 public static bool IsSupercharge(JsonObject slot) =>
  slot.GetObject("Type")?.GetString("InventorySpecialSlotType") == "TechBonus";

 public void RepairInventory(JsonObject draft, InventoryKind kind, Func<JsonObject, bool> match)
 {
  var slots = draft.GetArray("Slots")!;
  var repaired = new HashSet<(int,int)>();
  for (int i = slots.Length - 1; i >= 0; i--)
  {
   if (slots.GetObject(i) is not {} slot || !match(slot)) continue;
   var index = slot.GetObject("Index");
   if (index != null) repaired.Add((index.GetInt("X"), index.GetInt("Y")));
   string id = InventoryItemId(slot);
   if (InventorySlotHelper.IsDamageSlotItem(id)) { slots.RemoveAt(i); continue; }
   bool damaged = slot.GetDouble("DamageFactor") > 0;
   if (damaged && kind.Technology && slot.GetInt("Amount") < 0 &&
       Items.GetItem(id.Split('#')[0]) is {} tech)
   {
    int maximum = InventoryStackDatabase.GetMaxAmount(tech, "Technology", kind.Group);
    slot.Set("Amount", tech.BuildFullyCharged ? maximum : 0);
   }
   if (damaged) slot.Set("DamageFactor", 0.0);
   if (damaged || slot.Get("FullyInstalled") is false) slot.Set("FullyInstalled", true);
  }
  if (draft.GetArray("SpecialSlots") is {} special)
   RemoveEntries(special, entry => entry.GetObject("Type")?.GetString("InventorySpecialSlotType") == "BlockedByBrokenTech"
    && entry.GetObject("Index") is {} index && repaired.Contains((index.GetInt("X"), index.GetInt("Y"))));
 }

 public (bool Success, int StacksPlaced, int SlotsAvailable, int SlotsFreed) SortAllStorage(bool byType)
 {
  var player = Player ?? throw new InvalidOperationException("Open a player save first.");
  var result = InventoryBulkActions.MergeAndSortChestsIndividually(player, Items,
   byType ? ChestSortMode.Type : ChestSortMode.Name, 0);
  if (result.Success) Dirty = true;
  return (result.Success, result.StacksPlaced, result.SlotsAvailable, result.SlotsFreed);
 }

 public void StackInventoryItem(JsonObject source, string destination, int x, int y, ISet<(int,int)> pinned)
 {
  var player = Player ?? throw new InvalidOperationException("Open a player save first.");
  if (!IsAttached(source)) throw new InvalidOperationException("This inventory is no longer open.");
  bool changed = destination switch
  {
   "Chests" => InventoryBulkActions.AutoStackCargoToChests(source, player, out _, out _, pinned, (x,y)),
   "Starship" => InventoryBulkActions.AutoStackCargoToStarship(source, player, out _, out _, pinned, (x,y)),
   "Freighter" => InventoryBulkActions.AutoStackCargoToFreighter(source, player, out _, out _, pinned, (x,y)),
   _ => throw new ArgumentException("Unknown destination.")
  };
  if (!changed) throw new InvalidOperationException("No matching stack has room at the destination.");
  Dirty = true;
 }
}

using NMSE.Core;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    public int RechargeAllTechnology()
    {
        int count = Player is { } player ? InventoryBulkActions.RechargeAllTechnology(player, Items) : 0;
        if (count > 0) Dirty = true;
        return count;
    }

    public int RefillAllStacks()
    {
        int count = Player is { } player ? InventoryBulkActions.RefillAllStacks(player, Items) : 0;
        if (count > 0) Dirty = true;
        return count;
    }

    public int RepairAllSlots()
    {
        int count = Player is { } player ? InventoryBulkActions.RepairAllSlots(player, Items) : 0;
        if (count > 0) Dirty = true;
        return count;
    }

    public int RepairAllTechnology()
    {
        int count = Player is { } player ? InventoryBulkActions.RepairAllTechnology(player, Items) : 0;
        if (count > 0) Dirty = true;
        return count;
    }
}

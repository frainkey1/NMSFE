using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    private bool? _accessoryCatalogLoaded;

    internal bool LoadAccessoryCatalog() => _accessoryCatalogLoaded ??=
        CompanionDatabase.LoadFromFile(Path.Combine(ResourceRoot, "json", "Creature Species.json")) &&
        CompanionAccessoryDatabase.LoadFromFile(Path.Combine(ResourceRoot, "json", "Companion Accessories.json"));

    internal void EquipCompanionAccessory(JsonArray slots, int index, CompanionAccessoryEntry? accessory)
    {
        var slot = slots.GetObject(index) ?? throw new InvalidOperationException("This accessory slot is missing from the save.");
        var updated = slot.DeepClone();
        var data = updated.GetObject("CustomData") ?? throw new InvalidOperationException("This slot has no customization data.");
        var descriptors = data.GetArray("DescriptorGroups") ?? throw new InvalidOperationException("This slot has no descriptor list.");
        if (accessory == null)
        {
            updated.Set("SelectedPreset", "^DEFAULT_PET");
            descriptors.Clear();
            data.GetArray("Colours")?.Clear();
            data.Set("Scale", 1.0);
        }
        else
        {
            updated.Set("SelectedPreset", "^");
            if (descriptors.Length == 0)
                descriptors.Add("^" + accessory.Id);
            else
                descriptors.Set(0, "^" + accessory.Id);
        }

        if (updated.ToString() == slot.ToString()) return;
        slots.Set(index, updated);
        Dirty = true;
    }
}

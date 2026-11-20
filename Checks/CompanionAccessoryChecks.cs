using NMSE.Data;
using NMSE.Models;
using NMSE.Next.Engine;

internal static class CompanionAccessoryChecks
{
    internal static void Run(string resources)
    {
        var session = new SaveSession(resources);
        if (!session.LoadAccessoryCatalog()) throw new Exception("Accessory catalog did not load.");
        var slots = JsonArray.Parse("""[{"SelectedPreset":"^DEFAULT_PET","CustomData":{"DescriptorGroups":[],"Colours":[],"Scale":1.0,"TextureOptions":[]}}]""");
        var accessory = CompanionAccessoryDatabase.GetEntriesForSlot(AccessorySlot.Right).First(a => a.Id != "PET_ACC_NULL");
        session.EquipCompanionAccessory(slots, 0, accessory);
        if (slots.GetObject(0)!.GetObject("CustomData")!.GetArray("DescriptorGroups")!.GetString(0) != "^" + accessory.Id)
            throw new Exception("Cannot equip an accessory in an empty descriptor list.");
        var groups = slots.GetObject(0)!.GetObject("CustomData")!.GetArray("DescriptorGroups")!;
        groups.Add("^EXISTING_DECAL");
        session.EquipCompanionAccessory(slots, 0, accessory);
        if (slots.GetObject(0)!.GetObject("CustomData")!.GetArray("DescriptorGroups")!.GetString(1) != "^EXISTING_DECAL")
            throw new Exception("Accessory replacement removed the decal.");
        session.EquipCompanionAccessory(slots, 0, null);
        if (slots.GetObject(0)!.GetString("SelectedPreset") != "^DEFAULT_PET" ||
            slots.GetObject(0)!.GetObject("CustomData")!.GetArray("DescriptorGroups")!.Length != 0)
            throw new Exception("Accessory reset failed.");
        Console.WriteLine("Companion accessory equip, decal preservation and reset passed.");
    }
}

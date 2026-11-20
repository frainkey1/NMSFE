using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record SettlementEntry(int Index, string Name, string Seed)
{
    public override string ToString() => $"[{Index}] {Name}";
}
public sealed record SettlementPerkChoice(string Id, string Name, string Effect, bool Procedural);
public sealed record SettlementBuildingMilestone(int Value, string Label);

public static class SettlementOperations
{
    public static IReadOnlyList<SettlementBuildingMilestone> BuildingMilestones =>
        SettlementDatabase.KnownMilestones
            .Select(x => new SettlementBuildingMilestone(x.Value, UiStrings.Get(x.LocKey)))
            .ToArray();

    public static IReadOnlyList<SettlementPerkChoice> PerkChoices =>
        SettlementDatabase.Perks
            .Select(p => new SettlementPerkChoice(p.Id, p.Name, p.StatEffectSummary, p.Procedural))
            .ToArray();

    public static string DescribeBuildingState(int value) =>
        SettlementLogic.SettlementBuildingState.GetBuildingSlotDescription(value);

    public static (string Class, string State) DecodeBuildingState(int value)
    {
        if (SettlementLogic.SettlementBuildingState.IsEmpty(value))
            return ("-", UiStrings.Get("settlement.bs_empty"));
        var keys = SettlementLogic.SettlementBuildingState.DetermineClassAndState(value);
        return (UiStrings.Get(keys.ClassLocKey), UiStrings.Get(keys.StateLocKey));
    }

    public static string FormatPerk(string id, string seed)
    {
        if (string.IsNullOrWhiteSpace(id)) return "^";
        var perk = SettlementDatabase.ById.GetValueOrDefault(id);
        return perk?.Procedural == true && !string.IsNullOrWhiteSpace(seed)
            ? id + "#" + seed.Trim()
            : id;
    }
}

public sealed partial class SaveSession
{
    public IReadOnlyList<SettlementEntry> GetSettlements()
    {
        var player = Player;
        var settlements = player?.GetArray("SettlementStatesV2");
        if (player == null || settlements == null) return [];
        var owned = SettlementLogic.FilterSettlements(Data, player, settlements);
        if (owned.Count == 0 && settlements.Length > 0) owned = Enumerable.Range(0, settlements.Length).ToList();
        return owned.Select(i =>
        {
            var item = settlements.GetObject(i);
            return new SettlementEntry(i, item?.GetString("Name") ?? $"Settlement {i + 1}",
                item?.GetString("SeedValue") ?? "");
        }).ToArray();
    }

    public int SettlementCount => Player?.GetArray("SettlementStatesV2")?.Length ?? 0;

    public JsonObject ReadSettlement(int index)
    {
        var settlements = Player?.GetArray("SettlementStatesV2")
            ?? throw new InvalidOperationException("This save has no settlements.");
        return settlements.GetObject(index)?.DeepClone()
            ?? throw new ArgumentOutOfRangeException(nameof(index));
    }

    public void ApplySettlement(int index, JsonObject edited)
    {
        var settlements = Player?.GetArray("SettlementStatesV2")
            ?? throw new InvalidOperationException("This save has no settlements.");
        if (index < 0 || index >= settlements.Length) throw new ArgumentOutOfRangeException(nameof(index));
        settlements.Set(index, edited.DeepClone());
        Dirty = true;
    }

    public void DeleteSettlement(int index)
    {
        var settlements = Player?.GetArray("SettlementStatesV2")
            ?? throw new InvalidOperationException("This save has no settlements.");
        if (index < 0 || index >= settlements.Length) throw new ArgumentOutOfRangeException(nameof(index));
        SettlementLogic.RemoveSettlement(settlements, index);
        Dirty = true;
    }

    public int ImportSettlement(JsonObject imported, int? selectedIndex)
    {
        var player = Player ?? throw new InvalidOperationException("Load a save before importing a settlement.");
        var settlements = player.GetArray("SettlementStatesV2");
        if (settlements == null)
        {
            settlements = new JsonArray();
            player.Set("SettlementStatesV2", settlements);
        }

        if (selectedIndex is int index)
        {
            if (index < 0 || index >= settlements.Length) throw new ArgumentOutOfRangeException(nameof(selectedIndex));
            settlements.Set(index, imported.DeepClone());
            Dirty = true;
            return index;
        }

        if (settlements.Length >= 100)
            throw new InvalidOperationException("All 100 settlement slots are occupied. Select a settlement to replace.");
        settlements.Add(imported.DeepClone());
        Dirty = true;
        return settlements.Length - 1;
    }
}

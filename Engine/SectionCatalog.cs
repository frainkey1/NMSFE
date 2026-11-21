using NMSE.Models;
using NMSE.Data;

namespace NMSE.Next.Engine;

public sealed record SectionSpec(string Name, string Root, string Path, string[] PreferredFields);

/// <summary>Existing save containers only; resolving a section never creates missing data.</summary>
public static class SectionCatalog
{
    private static SectionSpec P(string name, string path, params string[] fields) => new(name, "Player", path, fields);
    private static SectionSpec S(string name, string path, params string[] fields) => new(name, "Save", path, fields);
    private static SectionSpec A(string name, string key) => new(name, "Account", $"UserSettingsData.{key}|{key}", []);

    public static SectionSpec[] Get(int page) => page switch
    {
        3 => [P("Owned starships", "ShipOwnership", "Name", "Resource", "Inventory", "Inventory_TechOnly", "Inventory_Cargo"),
              P("Archived starships", "ArchivedShipOwnership", "Ownership", "ArchivedClass", "ArchivedInventoryClass", "Customisation")],
        4 => [P("Freighter", "CurrentFreighter", "Filename", "Seed"),
              P("Frigates", "FleetFrigates", "CustomName", "FrigateClass", "Race", "ResourceSeed", "Stats", "TraitIDs"),
              P("Squadron pilots", "SquadronPilots", "NPCResource", "ShipResource", "PilotRank")],
        5 => [P("Vehicles", "VehicleOwnership", "Name", "Resource", "Inventory", "Inventory_TechOnly", "Location", "Position")],
        6 => [P("Companions", "Pets", "CustomName", "CreatureID", "CreatureSeed", "CreatureType", "Traits", "Moods"),
              P("Descriptors", "Pets", "CreatureID", "Descriptors"),
              P("Eggs", "Eggs", "CustomName", "CreatureID", "CreatureSeed", "Traits"),
              P("Battle team", "PetBattleTeam", "TeamMembers"), P("Unlocked companion slots", "UnlockedPetSlots"),
              P("Accessories", "PetAccessoryCustomisation")],
        7 => StorageSections(),
        8 => [P("Owned Stations", "PersistentPlayerBases", "Name", "BaseType", "GalacticAddress", "Position", "Objects"),
              P("Visited Stations", "Stats", "Address", "Stats"),
              P("Construction", "KnownProducts")],
        9 => [P("Known technology", "KnownTech"), P("Known products", "KnownProducts"), P("Known specials", "KnownSpecials"),
              P("Known Words", "KnownWordGroups"), P("Known Glyphs", "KnownPortalRunes"),
              P("Teleport destinations", "TeleportEndpoints", "Name", "UniverseAddress"),
              P("Fishing", "FishingRecord"), P("Recipes", "KnownRefinerRecipes"),
              P("Wonders", "Wonders"), P("Collected Knowledge", "CollectedKnowledge"), P("Fossils", "Fossils"),
              P("Raw Materials", "RawMaterials"), P("Discovery Stats", "DiscoveryStats")],
        10 => [P("Statistics groups", "Stats", "GroupId", "Stats")],
        11 => [P("Settlements", "SettlementStatesV2", "Name", "UniverseAddress", "Population", "Stats", "Owner", "SeedValue", "Perks", "ProductionState")],
        12 => [S("My songs", "CommonStateData.ByteBeatLibrary.MySongs|PlayerStateData.ByteBeatLibrary.MySongs", "Name", "AuthorUsername", "AuthorOnlineID", "AuthorPlatform", "Data"),
               S("ByteBeat library", "CommonStateData.ByteBeatLibrary|PlayerStateData.ByteBeatLibrary", "MySongs")],
        13 => [A("Expedition rewards", "UnlockedSeasonRewards"), A("Twitch rewards", "UnlockedTwitchRewards"),
               A("Platform rewards", "UnlockedPlatformRewards"), A("Special unlocks", "UnlockedSpecials"),
               P("Redeemed expedition rewards", "RedeemedSeasonRewards"), P("Redeemed Twitch rewards", "RedeemedTwitchRewards"),
               P("Redeemed platform rewards", "RedeemedPlatformRewards")],
        _ => []
    };

    public static string DisplayName(SectionSpec spec)
    {
        if (spec.Name.StartsWith("Storage container ", StringComparison.Ordinal)
            && int.TryParse(spec.Name.AsSpan("Storage container ".Length), out int container))
            return UiStrings.Format("domain.section_storage_container", container);

        string? key = spec.Name switch
        {
            "Owned starships" => "domain.section_owned_starships",
            "Archived starships" => "domain.section_archived_starships",
            "Freighter" => "freighter.tab_freighter",
            "Frigates" => "fleet.tab_frigates",
            "Squadron pilots" => "domain.section_squadron_pilots",
            "Vehicles" => "domain.section_vehicles",
            "Companions" => "tab.companions",
            "Descriptors" => "domain.section_descriptors",
            "Eggs" => "milestone.subcat_eggs",
            "Battle team" => "domain.section_battle_team",
            "Unlocked companion slots" => "domain.section_unlocked_companion_slots",
            "Accessories" => "domain.section_accessories",
            "Owned Stations" => "domain.owned_stations",
            "Visited Stations" => "ui.visited.title",
            "Construction" => "milestone.section_construction",
            "Known technology" => "domain.section_known_technology",
            "Known products" => "domain.section_known_products",
            "Known specials" => "domain.section_known_specials",
            "Known Words" => "discovery.tab_words",
            "Known Glyphs" => "discovery.tab_glyphs",
            "Teleport destinations" => "domain.section_teleport_destinations",
            "Fishing" => "discovery.tab_fish",
            "Recipes" => "discovery.tab_recipes",
            "Wonders" => "discovery.tab_wonders",
            "Collected Knowledge" => "discovery.tab_knowledge",
            "Fossils" => "discovery.group_fossils",
            "Raw Materials" => "discovery.group_raw_materials",
            "Discovery Stats" => "discovery.tab_stats",
            "Statistics groups" => "domain.section_statistics_groups",
            "Settlements" => "tab.settlements",
            "My songs" => "domain.my_songs",
            "ByteBeat library" => "domain.bytebeat_library",
            "Redeemed expedition rewards" => "ui.account.redeemed_expedition",
            "Redeemed Twitch rewards" => "ui.account.twitch",
            "Redeemed platform rewards" => "ui.account.redeemed_platform",
            "Bases" => "base.tab_bases",
            "Base workers" => "domain.section_base_workers",
            "Ingredients" => "base.storage_ingredient",
            "Base salvage" => "base.storage_salvage_capsule",
            "Rocket locker" => "base.storage_rocket",
            "Fishing platform" => "base.storage_fishing_platform",
            "Fishing bait" => "base.storage_fish_bait",
            "Food unit" => "base.storage_food_unit",
            "Freighter refund" => "base.storage_freighter_refund",
            _ => null
        };
        return key is null ? UiStrings.LocalizeText(spec.Name) : UiStrings.Get(key);
    }

    private static SectionSpec[] StorageSections()
    {
        var sections = new List<SectionSpec> { P("Bases", "PersistentPlayerBases", "Name", "BaseType", "GalacticAddress", "Position", "Objects"), P("Base workers", "NPCWorkers", "ResourceElement") };
        for (int i = 1; i <= 10; i++) sections.Add(P($"Storage container {i - 1}", $"Chest{i}Inventory", "Name", "Slots"));
        foreach (var entry in new[] { ("Ingredients", "CookingIngredientsInventory"), ("Base salvage", "ChestMagicInventory"), ("Rocket locker", "RocketLockerInventory"), ("Fishing platform", "FishPlatformInventory"), ("Fishing bait", "FishBaitBoxInventory"), ("Food unit", "FoodUnitInventory"), ("Freighter refund", "ChestMagic2Inventory") })
            sections.Add(P(entry.Item1, entry.Item2, "Name", "Slots"));
        return sections.ToArray();
    }

    public static object? Resolve(JsonObject save, JsonObject? account, SectionSpec spec)
    {
        JsonObject? root = spec.Root switch { "Player" => save.GetObject("PlayerStateData"), "Save" => save, "Account" => account, _ => null };
        if (root == null) return null;
        foreach (var path in spec.Path.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var value = root.GetValue(path);
            if (value is JsonObject or JsonArray) return value;
        }
        return null;
    }

    public static string VehicleCaption(object? value, int index)
    {
        if (value is JsonObject vehicle)
        {
            foreach (string key in new[] { "CustomName", "Name" })
                if (vehicle.GetValue(key) is string name && !string.IsNullOrWhiteSpace(name))
                    return UiStrings.LocalizeText(name);
            foreach (var (vehicleIndex, name) in NMSE.Core.ExocraftLogic.VehicleTypes)
                if (vehicleIndex == index)
                    return NMSE.Core.ExocraftLogic.GetLocalisedVehicleTypeName(name);
        }
        var caption = Caption(value, index);
        return caption == $"Entry {index + 1}" ? UiStrings.Format("companion.empty_slot_number", index + 1) : caption;
    }

    public static string CompanionCaption(object? value, int index)
    {
        if (value is JsonObject companion)
        {
            foreach (string key in new[] { "CustomName", "Name" })
                if (companion.GetValue(key) is string name && !string.IsNullOrWhiteSpace(name))
                    return name;

            if (companion.GetString("CreatureID") is { } creatureId &&
                !string.IsNullOrWhiteSpace(creatureId.TrimStart('^')))
                return creatureId;
        }

        return UiStrings.Format("companion.empty_slot_number", index + 1);
    }

    public static bool IsSpaceStationRecord(JsonObject baseData) =>
        NMSE.Core.SpaceStationLogic.GetBaseKind(baseData) == NMSE.Core.SpaceStationLogic.BaseKind.SpaceStation;

    public static int[] BaseRecordIndices(JsonArray bases, bool stationsOnly)
    {
        var indices = new List<int>();
        for (int i = 0; i < bases.Length; i++)
        {
            var baseData = bases.GetObject(i);
            bool station = baseData is not null && IsSpaceStationRecord(baseData);
            if (station == stationsOnly) indices.Add(i);
        }
        return indices.ToArray();
    }

    public static string FrigateCaption(JsonObject frigate, int index)
    {
        string name = UiStrings.LocalizeText(NMSE.Core.FrigateLogic.GetFrigateName(frigate, index));
        string type = NMSE.Core.FrigateLogic.GetLocalisedFrigateTypeName(NMSE.Core.FrigateLogic.GetFrigateType(frigate));
        string grade = NMSE.Core.FrigateLogic.ComputeClassFromTraits(frigate);
        return $"{(string.IsNullOrWhiteSpace(name) ? "" : name + " ")}[{type}] ({grade})";
    }

    public static string BaseCaption(JsonObject? playerState, JsonObject baseData, int index)
    {
        string? name = baseData.GetString("Name");
        var kind = NMSE.Core.SpaceStationLogic.GetBaseKind(baseData);
        if (kind == NMSE.Core.SpaceStationLogic.BaseKind.Freighter)
            return FreighterBaseCaption(playerState);
        if (kind == NMSE.Core.SpaceStationLogic.BaseKind.Corvette && playerState is not null)
            name = NMSE.Core.SpaceStationLogic.ResolveCorvetteBaseName(playerState, baseData) ?? name;
        return string.IsNullOrWhiteSpace(name)
            ? UiStrings.Format("base.fallback_base_name", index + 1)
            : UiStrings.LocalizeText(name);
    }

    private static string? GetFreighterTeleportName(JsonObject? playerState)
    {
        var endpoints = playerState?.GetArray("TeleportEndpoints");
        if (endpoints is null) return null;

        for (int i = 0; i < endpoints.Length; i++)
        {
            var endpoint = endpoints.GetObject(i);
            if (endpoint is null) continue;

            string? type = endpoint.GetString("TeleporterType");
            if (type != "9" && !string.Equals(type, "FreighterTeleport", StringComparison.OrdinalIgnoreCase))
                continue;

            string? name = endpoint.GetString("Name")?.Trim();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        return null;
    }

    private static string? GetFreighterName(JsonObject? playerState, JsonObject? freighter)
    {
        string? name = playerState?.GetString("PlayerFreighterName")?.Trim();
        if (!string.IsNullOrWhiteSpace(name)) return name;

        name = freighter?.GetString("Name")?.Trim();
        if (!string.IsNullOrWhiteSpace(name)) return name;

        name = GetFreighterTeleportName(playerState)?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static string FreighterCaption(JsonObject? playerState, JsonObject? freighter = null) =>
        UiStrings.LocalizeText(GetFreighterName(playerState, freighter ?? playerState?.GetObject("CurrentFreighter")) ?? UiStrings.Get("freighter.title"));

    public static string FreighterBaseCaption(JsonObject? playerState) =>
        UiStrings.LocalizeText(GetFreighterName(playerState, playerState?.GetObject("CurrentFreighter")) ?? UiStrings.Get("freighter.title"));

    public static string FreighterInventoryCaption(JsonObject? playerState) =>
        FreighterCaption(playerState, playerState?.GetObject("CurrentFreighter"));

    public static string BaseWorkerLocationCaption(JsonObject? playerState, JsonObject worker)
    {
        bool freighterBase = false;
        try { freighterBase = worker.GetBool("FreighterBase"); }
        catch { }

        if (freighterBase)
            return FreighterBaseCaption(playerState);

        object? address = worker.Get("BaseUA");
        if (address is null || string.IsNullOrWhiteSpace(SaveSession.FieldText(address)))
            return UiStrings.Get("base.worker_location_not_assigned");

        var bases = playerState?.GetArray("PersistentPlayerBases");
        if (bases is null)
            return UiStrings.Get("base.worker_location_unresolved");

        string addressText = SaveSession.FieldText(address);
        var candidates = Enumerable.Range(0, bases.Length)
            .Select(index => (Index: index, Data: bases.GetObject(index)))
            .Where(item => item.Data is not null
                && NMSE.Core.SpaceStationLogic.GetBaseKind(item.Data) is not (
                    NMSE.Core.SpaceStationLogic.BaseKind.Freighter
                    or NMSE.Core.SpaceStationLogic.BaseKind.SpaceStation)
                && string.Equals(SaveSession.FieldText(item.Data.Get("GalacticAddress")), addressText, StringComparison.OrdinalIgnoreCase))
            .Select(item => (item.Index, Data: item.Data!))
            .ToList();

        if (candidates.Count == 0)
            return UiStrings.Get("base.worker_location_unresolved");

        if (candidates.Count > 1 && worker.GetArray("BaseOffset") is { Length: >= 3 } offset)
        {
            var positionMatches = candidates.Where(candidate =>
            {
                var position = candidate.Data.GetArray("Position");
                if (position is null || position.Length < 3) return false;
                for (int i = 0; i < 3; i++)
                    if (!string.Equals(SaveSession.FieldText(position.Get(i)), SaveSession.FieldText(offset.Get(i)), StringComparison.Ordinal))
                        return false;
                return true;
            }).ToList();
            if (positionMatches.Count > 0)
                candidates = positionMatches;
        }

        if (candidates.Count != 1)
            return UiStrings.Format("base.worker_location_ambiguous", candidates.Count);

        return BaseCaption(playerState, candidates[0].Data, candidates[0].Index);
    }

    public static string Caption(object? value, int index, string? fallback = null)
    {
        if (value is JsonObject obj)
        {
            foreach (var key in new[] { "CustomName", "Name", "Ownership.Name", "GroupId", "Id", "CreatureID", "Word" })
                if (obj.GetValue(key) is string text && !string.IsNullOrWhiteSpace(text)) return text;
            foreach (var key in new[] { "Resource.Filename", "Ownership.Resource.Filename", "ShipResource.Filename", "NPCResource.Filename", "ResourceElement.Filename", "Filename" })
                if (obj.GetValue(key) is string file && !string.IsNullOrWhiteSpace(file))
                    return Path.GetFileNameWithoutExtension(file.Replace('\\', '/'));
        }
        else if (value is bool boolean) return boolean ? UiStrings.Get("common.yes") : UiStrings.Get("common.no");
        else if (value != null && value is not JsonArray) return value.ToString() ?? $"Entry {index + 1}";
        return fallback ?? UiStrings.Format("base.entry_n", index + 1);
    }
}



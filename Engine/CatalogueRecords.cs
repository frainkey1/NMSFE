using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    private RecipeDatabase? _recipes;
    internal IReadOnlyList<Recipe> Recipes
    {
        get
        {
            if (_recipes == null)
            {
                _recipes = new RecipeDatabase();
                _recipes.LoadFromFile(Path.Combine(ResourceRoot, "json", "Recipes.json"));
            }
            return _recipes.Recipes;
        }
    }

    internal IReadOnlyList<string> RecipeCompletionIds => Catalogue.KnownRefinerRecipes;
    internal IReadOnlyList<CatalogueDatabase.FishEntry> FishChoices => Catalogue.Fishing;
    internal (int Have, int Total) FishingCompletion => CatalogueCompletionLogic.GetFishingCompletion(Player!, FishChoices);
    internal int KnownGlyphBits => CatalogueLogic.LoadGlyphBitfield(Player!) & 0xffff;

    internal void SetGlyphBits(int bits)
    {
        if (bits == KnownGlyphBits) return;
        CatalogueLogic.SaveGlyphBitfield(Player!, (CatalogueLogic.LoadGlyphBitfield(Player!) & ~0xffff) | (bits & 0xffff));
        Dirty = true;
    }

    internal int AddMissingFishing()
    {
        var (have, total) = FishingCompletion;
        if (have == total) return 0;
        int added = CatalogueCompletionLogic.AddMissingFish(Player!, FishChoices);
        Dirty = true;
        return added;
    }

    internal JsonObject? FishingRecord => Player?.GetObject("FishingRecord");
    internal JsonArray? TeleportEndpoints => Player?.GetArray("TeleportEndpoints");

    internal void ImportCatalogueRecords(string key, object records)
    {
        if (key == "FishingRecord" && records is JsonObject fish)
        {
            var ids = fish.GetArray("ProductList");
            var counts = fish.GetArray("ProductCountList");
            var sizes = fish.GetArray("LargestCatchList");
            if (ids == null || counts?.Length != ids.Length || sizes?.Length != ids.Length)
                throw new InvalidDataException("Fishing record lists must have matching lengths.");

            for (int i = 0; i < ids.Length; i++)
            {
                _ = ids.GetString(i);
                _ = counts.GetInt(i);
                _ = sizes.GetDouble(i);
            }
            Player!.Set(key, fish);
        }
        else if (key == "TeleportEndpoints" && records is JsonArray endpoints)
        {
            var imported = Enumerable.Range(0, endpoints.Length).Select(endpoints.GetObject).ToArray();
            if (imported.Any(item => item?.GetObject("UniverseAddress")?.GetObject("GalacticAddress") == null))
                throw new InvalidDataException("Each destination must have a galactic address.");

            var destination = TeleportEndpoints ?? new JsonArray();
            foreach (var endpoint in imported) destination.Add(endpoint);
            Player!.Set(key, destination);
        }
        else throw new InvalidDataException($"Choose a discovery file containing {key}.");
        Dirty = true;
    }

    internal void TravelToCatalogueSystem(PlayerCoordinates coordinates)
    {
        ApplyPlayerCoordinates(coordinates with { Planet = 0 });
        if (Data?.GetObject("SpawnStateData") is { } spawn && spawn.GetString("LastKnownPlayerState") != "InShip")
        {
            spawn.Set("LastKnownPlayerState", "InShip");
            Dirty = true;
        }
    }

    internal void RemoveTeleportEndpoints(IEnumerable<int> indexes)
    {
        if (TeleportEndpoints is not { } endpoints) return;
        foreach (int index in indexes.Distinct().OrderDescending())
        {
            endpoints.RemoveAt(index);
            Dirty = true;
        }
    }

    internal void RemoveFishingEntries(IEnumerable<int> indexes)
    {
        if (FishingRecord?.GetArray("ProductList") is not { } ids) return;
        var counts = FishingRecord.GetArray("ProductCountList");
        var sizes = FishingRecord.GetArray("LargestCatchList");
        foreach (int index in indexes.Distinct())
        {
            ids.Set(index, "^");
            if (counts != null && index < counts.Length) counts.Set(index, 0);
            if (sizes != null && index < sizes.Length) sizes.Set(index, 0.0);
            Dirty = true;
        }
    }

    internal void AddFishingEntry(string id)
    {
        if (CatalogueCompletionLogic.GetCurrentFishIdSet(Player!).Contains(id)) return;
        var record = FishingRecord ?? new JsonObject();
        var ids = record.GetArray("ProductList") ?? new JsonArray();
        var counts = record.GetArray("ProductCountList") ?? new JsonArray();
        var sizes = record.GetArray("LargestCatchList") ?? new JsonArray();
        int index = Enumerable.Range(0, ids.Length)
            .FirstOrDefault(i => CatalogueCompletionLogic.NormalizeId(ids.Get(i) as string).Length == 0, ids.Length);
        if (index >= CatalogueCompletionLogic.FishRecordCapacity)
            throw new InvalidOperationException("There are no empty fishing record slots.");
        int length = CatalogueCompletionLogic.FishRecordCapacity;
        while (ids.Length < length) ids.Add("^");
        while (counts.Length < ids.Length) counts.Add(0);
        while (sizes.Length < ids.Length) sizes.Add(0.0);
        ids.Set(index, "^" + id);
        counts.Set(index, 0);
        sizes.Set(index, 0.0);
        record.Set("ProductList", ids);
        record.Set("ProductCountList", counts);
        record.Set("LargestCatchList", sizes);
        Player!.Set("FishingRecord", record);
        Dirty = true;
    }

    internal void EditFishingEntry(int index, int? count, double? size)
    {
        if (count is < 0 || size is double value && (!double.IsFinite(value) || value < 0))
            throw new ArgumentException("Catch values must be nonnegative.");
        if (count.HasValue && FishingRecord?.GetArray("ProductCountList") is { } counts && index < counts.Length)
        {
            counts.Set(index, count.Value);
            Dirty = true;
        }
        if (size.HasValue && FishingRecord?.GetArray("LargestCatchList") is { } sizes && index < sizes.Length)
        {
            sizes.Set(index, size.Value);
            Dirty = true;
        }
    }
}

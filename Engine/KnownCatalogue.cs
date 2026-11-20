using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    private CatalogueDatabase? _catalogue;
    private CatalogueDatabase Catalogue => _catalogue ??= new(Path.Combine(ResourceRoot, "json"));
    private JsonObject? CatalogueAccount => AccountData?.GetObject("UserSettingsData") ?? AccountData;

    internal static bool IsKnownCatalogue(string path) => path is "KnownTech" or "KnownProducts" or "KnownSpecials";
    internal static string KnownCatalogueExportKey(string path) =>
        path == "KnownTech" ? "KnownTechnologies" : path;

    private IReadOnlyList<string> CompletionIds(string path) => path switch
    {
        "KnownTech" => Catalogue.KnownTechForCompletion(Items),
        "KnownProducts" => Catalogue.KnownProductsForCompletion(Items),
        _ => Catalogue.KnownSpecialsForCompletion(Items)
    };

    private static string SeenKey(string path) => path == "KnownTech" ? "SeenTechnologies" : "SeenProducts";

    internal (int Have, int Total) KnownCatalogueCompletion(string path) =>
        CatalogueCompletionLogic.GetCombinedCompletion(Player!, path, CatalogueAccount, SeenKey(path), CompletionIds(path));

    internal string[] KnownCatalogueIds(string path) =>
        CatalogueCompletionLogic.GetCurrentIdSet(Player!, path).ToArray();

    internal IEnumerable<GameItem> CatalogueChoices(string path)
    {
        var known = CatalogueCompletionLogic.GetCurrentIdSet(Player!, path);
        return Items.Items.Values.Where(item =>
            !known.Contains(CatalogueCompletionLogic.NormalizeId(item.Id))
            && !GameItemDatabase.IsPickerExcluded(item.Id)
            && (path switch
            {
                "KnownTech" => CatalogueLogic.TechItemTypes.Contains(item.ItemType)
                    && CatalogueLogic.IsLearnableTechnology(item),
                "KnownProducts" => CatalogueLogic.ProductItemTypes.Contains(item.ItemType)
                    && CatalogueLogic.IsLearnableProduct(item),
                _ => string.Equals(item.TradeCategory, "SpecialShop", StringComparison.OrdinalIgnoreCase)
            }));
    }

    internal int AddKnownCatalogueIds(string path, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return 0;
        int added = CatalogueCompletionLogic.AddMissingIds(Player!, path, ids);
        Dirty |= added > 0;
        return added;
    }

    internal int AddMissingCatalogue(string path)
    {
        var ids = CompletionIds(path);
        var specials = path == "KnownProducts" ? Catalogue.KnownSpecialsForCompletion(Items) : [];
        int added = AddKnownCatalogueIds(path, ids);
        added += AddKnownCatalogueIds("KnownSpecials", specials);
        if (CatalogueAccount is { } account)
        {
            int seen = CatalogueCompletionLogic.AddMissingSeen(account, SeenKey(path),
                path == "KnownTech" ? Catalogue.KnownTech : ids);
            if (path == "KnownProducts")
                seen += CatalogueCompletionLogic.AddMissingSeen(account, "SeenSubstances", Catalogue.SeenSubstances);
            Dirty |= seen > 0;
            AccountDirty |= seen > 0;
            added += seen;
        }
        return added;
    }

    internal int RemoveKnownCatalogueIds(string path, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = Player?.GetArray(path);
        if (entries == null) return 0;

        int removed = 0;
        for (int index = entries.Length - 1; index >= 0; index--)
        {
            var value = entries.Get(index);
            var id = CatalogueCompletionLogic.NormalizeId(value is JsonObject item ? item.GetString("Id") : value as string);
            if (!selected.Contains(id)) continue;
            entries.RemoveAt(index);
            removed++;
        }

        Dirty |= removed > 0;
        return removed;
    }
}
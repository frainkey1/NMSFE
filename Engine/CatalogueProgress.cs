using System.Globalization;
using NMSE.Core;
using NMSE.Models;

namespace NMSE.Next.Engine;

internal sealed record CatalogueProgressRow(string Category, string Name, string Id, string Progress,
    string Target, string Status, string Detail = "", string? Icon = null, int Index = -1,
    Func<bool, bool>? Edit = null, bool HasPartialProgress = false)
{
    public bool Complete => Status == "Complete";
    public bool CanChange(bool complete) => Edit != null && (complete ? !Complete : Complete || HasPartialProgress || Status == "Under");
}

public sealed partial class SaveSession
{
    internal static bool IsCatalogueProgress(string path) =>
        path is "Wonders" or "CollectedKnowledge" or "Fossils" or "RawMaterials" or "DiscoveryStats";

    internal bool CanEditCatalogueProgress(string path) =>
        Player != null && (path is not ("Fossils" or "RawMaterials") || CatalogueAccount != null);

    internal IEnumerable<CatalogueProgressRow> CatalogueProgress(string path)
    {
        if (Player is not { } player) yield break;
        if (path is "Fossils" or "RawMaterials")
        {
            bool fossils = path == "Fossils";
            var key = fossils ? "SeenProducts" : "SeenSubstances";
            var ids = fossils ? Catalogue.Fossils : Catalogue.SeenSubstances;
            var account = CatalogueAccount;
            var seen = account == null ? null : CatalogueCompletionLogic.GetSeenIdSet(account, key);
            foreach (string id in ids)
            {
                var item = Items.GetItem(id);
                yield return new("", item?.Name ?? id, id, "", "",
                    seen == null ? "Unavailable" : seen.Contains(id) ? "Complete" : "Missing", Icon: item?.Icon,
                    Edit: account == null ? null : complete => (complete
                        ? CatalogueCompletionLogic.AddMissingSeen(account, key, [id])
                        : CatalogueCompletionLogic.RemoveSeen(account, key, [id])) > 0);
            }
            yield break;
        }

        if (path == "DiscoveryStats")
        {
            var stats = CatalogueCompletionLogic.GetGlobalStatsMap(player);
            foreach (var (id, target) in Catalogue.DiscoveryStats)
            {
                int? value = stats.TryGetValue(id, out var stat) ? CatalogueCompletionLogic.GetGlobalInt(stat) : null;
                yield return new("", "", id, Format(value ?? 0), Format(target),
                    CatalogueCompletionLogic.IsGlobalStatComplete(value, target) ? "Complete" : "Missing",
                    Edit: complete => complete ? CatalogueCompletionLogic.SetGlobalStatAtLeast(player, id, target)
                        : CatalogueCompletionLogic.ClearGlobalStat(player, id), HasPartialProgress: value is not null and not 0);
            }
            yield break;
        }

        if (path == "CollectedKnowledge")
        {
            foreach (var status in CatalogueCompletionLogic.GetKnowledgeStatuses(player, Catalogue))
            {
                var page = status.Page;
                int? progress = status.LastSeen;
                int target = CatalogueCompletionLogic.GetPageTarget(page, Catalogue);
                bool words = page.Recipe == KnowledgeRecipe.Words;
                string state = status.Status;
                if (words)
                {
                    var (have, total) = CatalogueCompletionLogic.GetWordPageCompletion(player, Catalogue, page);
                    (progress, target) = (have, total);
                    state = total > 0 && have >= total ? "OK" : "UNDER";
                }
                yield return new(page.Category, page.Name, page.Id, Format(progress), Format(target > 0 ? target : null),
                    StatusText(state), words ? "" : status.Detail,
                    Edit: words ? null : complete => complete
                        ? CatalogueCompletionLogic.ApplyKnowledgePage(player, page, Catalogue.StoryCompleters, Catalogue, includeCompleters: false)
                        : CatalogueCompletionLogic.ClearKnowledgePage(player, page));
            }
            foreach (var status in CatalogueCompletionLogic.GetKnowledgeCompleterStatuses(player, Catalogue.StoryCompleters, Catalogue))
            {
                if (status.Kind == KnowledgeCompleterKind.DevNotes) continue;
                yield return new("Story completers", status.Name, "", Format(status.Current), Format(status.Target),
                    status.Complete ? "Complete" : status.Current is null or 0 ? "Missing" : "Under", status.Detail,
                    Edit: complete => complete
                        ? CatalogueCompletionLogic.ApplyKnowledgeCompleter(player, status, Catalogue.StoryCompleters, Catalogue)
                        : CatalogueCompletionLogic.ClearKnowledgeCompleter(player, status, Catalogue));
            }
            yield break;
        }

        foreach (string key in CatalogueCompletionLogic.WonderKeys)
        {
            var pack = key switch
            {
                "WonderTreasureRecords" => Catalogue.WonderTreasureRecords,
                "WonderWeirdBasePartRecords" => Catalogue.WonderWeirdBasePartRecords,
                _ => null
            };
            var fallback = Catalogue.WonderFallbackSlots;
            var (_, total) = CatalogueCompletionLogic.GetWonderCompletion(player, key, pack, fallback);
            var slots = player.GetArray(key);
            string category = key == "WonderWeirdBasePartRecords" ? "Weird Base Part" : key[6..^7];
            for (int i = 0; i < total; i++)
            {
                int index = i;
                var record = slots != null && index < slots.Length ? slots.GetObject(index) : null;
                bool filled = CatalogueCompletionLogic.IsWonderSlotFilled(record);
                var seeds = record?.GetArray("GenerationID");
                string seed = filled && seeds is { Length: >= 2 } ? $"{Format(seeds.Get(0))} / {Format(seeds.Get(1))}" : "—";
                yield return new(category, $"Slot {index + 1}", key, Format(filled ? record?.Get("WonderStatValue") : null),
                    "", filled ? "Complete" : "Missing", seed, Index: index,
                    Edit: complete => complete
                        ? CatalogueCompletionLogic.FillWonderSlot(Data, player, key, index, pack, fallback)
                        : CatalogueCompletionLogic.ClearWonderSlot(player, key, index));
            }
        }
    }

    internal int EditCatalogueProgress(string path, IEnumerable<CatalogueProgressRow> rows, bool complete)
    {
        if (!CanEditCatalogueProgress(path)) return 0;
        int changed = 0;
        foreach (var row in rows)
        {
            if (!row.CanChange(complete) || !row.Edit!(complete)) continue;
            changed++;
            Dirty = true;
            if (path is "Fossils" or "RawMaterials") AccountDirty = true;
        }
        return changed;
    }

    private static string Format(object? value) => value switch
    {
        RawDouble number => number.Value.ToString("0.###", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        null => "—",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—"
    };

    private static string StatusText(string state) => state switch
    {
        "OK" => "Complete",
        "MISSING" => "Missing",
        "UNDER" => "Under",
        _ => state
    };
}


using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    private WordDatabase? _wordDatabase;
    internal IReadOnlyList<WordEntry> CatalogueWords
    {
        get
        {
            if (_wordDatabase == null)
            {
                var database = new WordDatabase();
                if (!database.LoadFromFile(Path.Combine(ResourceRoot, "json", "Words.json")))
                    throw new InvalidOperationException("The bundled word database could not be loaded.");
                _wordDatabase = database;
            }

            return _wordDatabase.Words;
        }
    }

    internal static IReadOnlyList<(string Name, int Index)> WordRaces => CatalogueLogic.RaceColumns;
    private JsonArray? KnownWordGroups => Player?.GetArray("KnownWordGroups");

    internal HashSet<(string Group, int Race)> ReadKnownWordFlags()
    {
        var flags = new HashSet<(string, int)>();
        if (KnownWordGroups is not { } groups) return flags;

        for (int index = 0; index < groups.Length; index++)
        {
            var entry = groups.GetObject(index);
            if (entry?.GetString("Group") is not { } group || entry.GetArray("Races") is not { } races) continue;
            foreach (var (_, race) in WordRaces)
                if (race < races.Length && races.GetBool(race)) flags.Add((group, race));
        }

        return flags;
    }

    internal (int Have, int Total) WordCompletion() =>
        CatalogueCompletionLogic.GetWordGroupCompletion(Player!, Catalogue.KnownWordGroups);

    private JsonArray EnsureKnownWordGroups()
    {
        if (KnownWordGroups is { } existing) return existing;
        var groups = new JsonArray();
        Player!.Set("KnownWordGroups", groups);
        return groups;
    }

    internal void SetCatalogueWord(string group, int race, bool known)
    {
        var groups = known ? EnsureKnownWordGroups() : KnownWordGroups;
        if (groups == null) return;
        CatalogueLogic.SetWordKnown(groups, group, race, known);
        FinishWordChanges(groups);
    }

    internal void SetCatalogueWords(IReadOnlyList<WordEntry> words, bool known, int? race = null)
    {
        if (words.Count == 0) return;
        var groups = known ? EnsureKnownWordGroups() : KnownWordGroups;
        if (groups == null) return;
        if (race is int index)
            CatalogueLogic.SetWordFlagsForRace(groups, words, index, known);
        else
            CatalogueLogic.SetWordFlagsForEntries(groups, words, CatalogueLogic.RaceColumns, known);
        FinishWordChanges(groups);
    }

    internal void AddMissingCatalogueWords()
    {
        var (added, upgraded) = CatalogueCompletionLogic.AddMissingWordGroups(Player!, Catalogue.KnownWordGroups);
        if (added + upgraded > 0 && KnownWordGroups is { } groups) FinishWordChanges(groups);
    }

    internal IReadOnlyList<string> ExportCatalogueWords() =>
        KnownWordGroups is { } groups ? CatalogueLogic.CollectKnownWordIds(groups, CatalogueWords) : [];

    internal (int Applied, int Skipped) ImportCatalogueWords(IReadOnlyList<string> entries)
    {
        if (entries.Count == 0) return (0, 0);
        var groups = KnownWordGroups ?? new JsonArray();
        var result = CatalogueLogic.ApplyKnownWords(groups, CatalogueWords, entries);
        if (result.Applied > 0)
        {
            if (KnownWordGroups == null) Player!.Set("KnownWordGroups", groups);
            FinishWordChanges(groups);
        }

        return result;
    }

    private void FinishWordChanges(JsonArray groups)
    {
        CatalogueLogic.SyncWordStats(Data, groups);
        Dirty = true;
    }
}
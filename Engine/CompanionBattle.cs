using NMSE.Core;
using NMSE.Data;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    private bool? _battleCatalogLoaded;

    internal bool LoadBattleCatalog()
    {
        if (_battleCatalogLoaded is { } loaded) return loaded;
        string directory = Path.Combine(ResourceRoot, "json");
        CompanionDatabase.LoadFromFile(Path.Combine(directory, "Creature Species.json"));
        PetBiomeAffinityMap.LoadFromFile(Path.Combine(directory, "Game Table Globals.json"));
        loaded = PetBattleMoveDatabase.LoadFromFile(Path.Combine(directory, "Pet Battle Moves.json")) &&
                 PetBattleMovesetDatabase.LoadFromFile(Path.Combine(directory, "Pet Battle Movesets.json"));
        _battleCatalogLoaded = loaded;
        return loaded;
    }

    internal void ResetCompanionBattleRecord(JsonObject pet)
    {
        if (!IsAttached(pet)) throw new InvalidOperationException("Reload the companion before editing.");
        PetBattleLogic.ResetBattleRecord(pet);
        Dirty = true;
    }
}

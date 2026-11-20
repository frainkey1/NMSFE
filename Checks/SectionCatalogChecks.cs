using System.Security.Cryptography;
using NMSE.Models;
using NMSE.Next.Engine;

internal static class SectionCatalogChecks
{
    public static void Run(string resources)
    {
        var save = JsonObject.Parse("{\"PlayerStateData\":{\"ShipOwnership\":[{\"Name\":\"Test ship\"}],\"ByteBeatLibrary\":{\"MySongs\":[]}},\"CommonStateData\":{}}");
        var account = JsonObject.Parse("{\"UserSettingsData\":{\"UnlockedSeasonRewards\":[\"^TEST\"]}}");
        var before = save.ToString();
        var fleetTabs = SectionCatalog.Get(4).Select(section => section.Name).ToArray();
        if (!fleetTabs.SequenceEqual(["Freighter cargo", "Freighter technology", "Frigates", "Squadron pilots", "Freighter", "Freighter crew"]))
            throw new Exception("Fleet tabs are not in the requested order.");
        if (SectionCatalog.Get(11).FirstOrDefault()?.Name != "Settlements" ||
            SectionCatalog.Get(13).FirstOrDefault()?.Name != "My songs" ||
            SectionCatalog.Get(14).FirstOrDefault()?.Name != "Expedition rewards")
            throw new Exception("Page section indices do not match the new Construction and Alliance navigation order.");
        if (SectionCatalog.Get(8).FirstOrDefault()?.Path != "PersistentPlayerBases")
            throw new Exception("Construction is not backed by persistent player station records.");
        var stationRecord = JsonObject.Parse("{\"Name\":\"Di-yuh-rhea Station\",\"BaseType\":{\"PersistentBaseTypes\":\"PlayerSpaceStationBase\"}}");
        var planetaryRecord = JsonObject.Parse("{\"Name\":\"Pilsdem Colony\",\"BaseType\":{\"PersistentBaseTypes\":\"HomePlanetBase\"}}");
        var spaceBaseRecord = JsonObject.Parse("{\"Name\":\"Other\",\"BaseType\":{\"PersistentBaseTypes\":\"PlayerSpaceBase\"}}");
        if (!SectionCatalog.IsSpaceStationRecord(stationRecord) ||
            SectionCatalog.IsSpaceStationRecord(planetaryRecord) ||
            SectionCatalog.IsSpaceStationRecord(spaceBaseRecord) ||
            SectionCatalog.Caption(stationRecord, 0) != "Di-yuh-rhea Station")
            throw new Exception("Construction does not distinguish owned stations or preserve their saved name.");
        var mixedBases = JsonObject.Parse("{\"items\":[{\"Name\":\"Planet\",\"BaseType\":{\"PersistentBaseTypes\":\"HomePlanetBase\"}},{\"Name\":\"Station 1\",\"BaseType\":{\"PersistentBaseTypes\":\"PlayerSpaceStationBase\"}},{\"Name\":\"Space Base\",\"BaseType\":{\"PersistentBaseTypes\":\"PlayerSpaceBase\"}},{\"Name\":\"Station 2\",\"BaseType\":{\"PersistentBaseTypes\":\"PlayerSpaceStationBase\"}}]}").GetArray("items")!;
        if (!SectionCatalog.BaseRecordIndices(mixedBases, false).SequenceEqual([0, 2]) ||
            !SectionCatalog.BaseRecordIndices(mixedBases, true).SequenceEqual([1, 3]))
            throw new Exception("Construction filtering did not preserve original base-array indices.");
        if (SectionCatalog.Resolve(save, null, SectionCatalog.Get(3)[0]) is not JsonArray { Length: 1 }) throw new Exception("Owned ship resolution failed.");
        if (SectionCatalog.Resolve(save, null, SectionCatalog.Get(13)[0]) is not JsonArray { Length: 0 }) throw new Exception("Legacy ByteBeat path fallback failed.");
        if (SectionCatalog.Resolve(save, account, SectionCatalog.Get(14)[0]) is not JsonArray { Length: 1 }) throw new Exception("Account resolution failed.");
        var bareAccount = account.GetObject("UserSettingsData")!;
        if (SectionCatalog.Resolve(save, bareAccount, SectionCatalog.Get(14)[0]) is not JsonArray) throw new Exception("Bare account fallback failed.");
        if (SectionCatalog.Resolve(save, null, SectionCatalog.Get(14)[0]) != null) throw new Exception("Absent account was fabricated.");
        if (SectionCatalog.Resolve(save, null, SectionCatalog.Get(4)[0]) != null || before != save.ToString()) throw new Exception("Resolving absent data changed save.");
        if (SectionCatalog.Caption(JsonObject.Parse("{\"CustomName\":\"\",\"Resource\":{\"Filename\":\"MODELS/SHIP.SCENE.MBIN\"}}"), 0) != "SHIP.SCENE") throw new Exception("Resource caption failed.");
        if (SectionCatalog.Caption(JsonObject.Parse("{\"Name\":\"Named ship\"}"), 0) != "Named ship") throw new Exception("Name caption failed.");
        var session = new SaveSession(resources);
        var unnamedFrigate = JsonObject.Parse("{\"CustomName\":\"\",\"FrigateClass\":{\"FrigateClass\":\"Normandy\"}}");
        if (SectionCatalog.FrigateCaption(unnamedFrigate, 0) != "[Normandy] (C)") throw new Exception("Frigate type/class fallback caption failed.");
        var namedFrigate = JsonObject.Parse("{\"CustomName\":\"The Leviathan\",\"FrigateClass\":{\"FrigateClass\":\"DeepSpace\"}}");
        if (SectionCatalog.FrigateCaption(namedFrigate, 1) != "The Leviathan [DeepSpace] (C)") throw new Exception("Named frigate caption failed.");
        var unnamedBase = JsonObject.Parse("{\"Name\":\"\",\"BaseType\":{\"PersistentBaseTypes\":\"HomePlanetBase\"}}");
        if (SectionCatalog.BaseCaption(null, unnamedBase, 1) != "Base 2") throw new Exception("Default base caption failed.");
        var freighterState = JsonObject.Parse("{\"CurrentFreighter\":{\"Filename\":\"MODELS/COMMON/SPACESHIP/INDUSTRIAL/PIRATEFREIGHTER.SCENE.MBIN\"}}");
        if (SectionCatalog.FreighterInventoryCaption(freighterState) != "PIRATEFREIGHTER.SCENE") throw new Exception("Freighter inventory fallback caption failed.");
        var saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelloGames", "NMS");
        var source = Directory.GetFiles(saveDirectory, "save8.hg", SearchOption.AllDirectories).First();
        var hash = SHA256.HashData(File.ReadAllBytes(source));
        session.Load(source);
        before = session.Data.ToString();
        for (int page = 3; page <= MainPageCatalog.AccountRewards; page++)
        {
            int found = SectionCatalog.Get(page).Count(spec => SectionCatalog.Resolve(session.Data, null, spec) != null);
            Console.WriteLine($"SECTION {page}: {found} existing save containers");
        }
        if (SectionCatalog.Resolve(session.Data, null, SectionCatalog.Get(3)[0]) is not JsonArray { Length: > 0 }) throw new Exception("Real save ships missing.");
        if (SectionCatalog.Resolve(session.Data, null, SectionCatalog.Get(5)[0]) is not JsonArray) throw new Exception("Real save vehicles missing.");
        if (SectionCatalog.Resolve(session.Data, null, SectionCatalog.Get(10)[0]) is not JsonArray) throw new Exception("Real save statistics missing.");
        if (before != session.Data.ToString() || session.Dirty || !hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new Exception("Read-only catalogue checks changed save.");
        Console.WriteLine("PASS: section catalog resolves real save containers, legacy song/account fallbacks, captions, absent-data handling and read-only fidelity.");
    }
}

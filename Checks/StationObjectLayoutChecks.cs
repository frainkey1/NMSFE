using NMSE.Next.Engine;
using NMSE.Models;

internal static class StationObjectLayoutChecks
{
    internal static void Run()
    {
        var save = JsonObject.Parse("{\"Objects\":[{\"ObjectID\":\"^STATION_SOFA\",\"Position\":[-10,0,-10]},{\"ObjectID\":\"^STA_HOLOMED\",\"Position\":[10,0,10]},{\"ObjectID\":\"^STATION_CORE\"}]}");
        var objects = save.GetArray("Objects")!;
        var original = save.ToString();
        var areas = StationObjectLayout.BuildAreas(objects);

        if (areas.Single(area => area.Name == "Aft · Port").Objects.Single().Index != 0)
            throw new Exception("Station object coordinates did not map to the aft port area.");
        if (areas.Single(area => area.Name == "Forward · Starboard").Objects.Single().Index != 1)
            throw new Exception("Station object coordinates did not map to the forward starboard area.");
        if (areas.Single(area => area.Name == "Unassigned").Objects.Single().Index != 2)
            throw new Exception("Objects without positions were not preserved in Unassigned.");
        if (save.ToString() != original)
            throw new Exception("Building station area groups mutated the saved objects.");
    }
}
SectionCatalogChecks.Run(resourceRoot);
StationObjectLayoutChecks.Run();
AllianceChecks.Run();

using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record AllianceLeaderboardRow
{
    public int Rank { get; }
    public string AllianceName { get; }
    public long Score { get; }

    public AllianceLeaderboardRow(int rank, string allianceName, long score)
    {
        Rank = rank;
        AllianceName = allianceName;
        Score = score;
    }
}

public sealed record AllianceStationDestination(string Name, string Type, string AllianceIdData);

public sealed record ReportedAlliance(string Name, string Tag, string Id);

public sealed record AlliancePageData(
    string Status,
    IReadOnlyList<AllianceLeaderboardRow> Rankings,
    IReadOnlyList<AllianceStationDestination> Stations,
    IReadOnlyList<ReportedAlliance> ReportedAlliances)
{
    public static AlliancePageData FromSave(JsonObject? player, JsonObject? account)
    {
        var stations = new List<AllianceStationDestination>();
        var seenStations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var endpoints = player?.GetArray("TeleportEndpoints");
        if (endpoints != null)
        {
            for (int index = 0; index < endpoints.Length; index++)
            {
                var endpoint = endpoints.GetObject(index);
                if (endpoint == null) continue;
                string allianceIdData = ReadAllianceId(endpoint.GetObject("AllianceId"));
                if (allianceIdData.Length == 0) continue;
                string name = endpoint.GetString("Name")?.Trim() ?? "";
                if (name.Length == 0) name = $"Alliance destination {index + 1}";
                string type = endpoint.GetString("TeleporterType")?.Trim() ?? "Unknown";
                if (seenStations.Add($"{type}\0{name}"))
                    stations.Add(new(name, type, allianceIdData));
            }
        }

        var reports = new List<ReportedAlliance>();
        var reported = account?.GetObject("UserSettingsData")?.GetObject("BlockList")?.GetArray("ReportedAllianceArray");
        if (reported != null)
        {
            var seenReports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < reported.Length; index++)
            {
                var entry = reported.GetObject(index);
                if (entry == null) continue;
                string id = entry.GetString("AllianceId")?.Trim() ?? "";
                string name = entry.GetString("OriginalName")?.Trim() ?? "";
                string tag = entry.GetString("OriginalTag")?.Trim() ?? "";
                if (id.Length == 0 && name.Length == 0 && tag.Length == 0) continue;
                if (seenReports.Add($"{id}\0{name}\0{tag}"))
                    reports.Add(new(name, tag, id));
            }
        }

        return new(
            "Live Alliance profile and leaderboard values are unavailable. Saved destinations and reported-alliance records below are a local snapshot and may be stale.",
            Array.Empty<AllianceLeaderboardRow>(),
            stations,
            reports);
    }

    static string ReadAllianceId(JsonObject? allianceId)
    {
        var data = allianceId?.GetArray("Data");
        if (data == null) return "";
        var values = new List<string>(data.Length);
        bool nonzero = false;
        for (int i = 0; i < data.Length; i++)
        {
            var value = data.Get(i)?.ToString();
            value ??= "";
            values.Add(value);
            if (!string.IsNullOrWhiteSpace(value) && value != "0") nonzero = true;
        }
        return nonzero ? string.Join(", ", values) : "";
    }
}

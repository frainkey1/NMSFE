using NMSE.Next.Engine;

internal static class AllianceChecks
{
    public static void Run()
    {
        var unavailable = AlliancePageData.Unavailable();
        if (string.IsNullOrWhiteSpace(unavailable.Status) || unavailable.Rankings.Count != 0)
            throw new Exception("Unavailable Alliance data must explain the source state and contain no ranking rows.");

        foreach (var property in typeof(AllianceLeaderboardRow).GetProperties())
            if (property.SetMethod is { IsPublic: true })
                throw new Exception($"Alliance leaderboard field {property.Name} must not have a public setter.");

        Console.WriteLine("PASS: Alliance source-unavailable state contains no fabricated rankings and leaderboard rows are immutable.");
    }
}

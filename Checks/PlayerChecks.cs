using NMSE.Next.Engine;
using NMSE.Models;

internal static class PlayerChecks
{
    public static void Run(string resources)
    {
        var session = new SaveSession(resources);
        session.Data.Set("PlayerStateData", JsonObject.Parse("{\"Health\":1234567.0,\"Shield\":20,\"Energy\":30,\"Units\":-1,\"Nanites\":-2147483648,\"Specials\":40,\"SaveSummary\":\"Original\",\"Untouched\":1.23456789}"));
        session.Data.Set("CommonStateData", JsonObject.Parse("{\"SaveName\":\"Synthetic check\"}"));
        var before = session.Data.ToString();
        var originalHealth = session.Player!.Get("Health");
        var draft = session.ReadPlayer();
        if (draft.Health != "999999" || draft.Units != "4294967295" || draft.Nanites != "2147483648") throw new Exception("Player stat display coercion failed.");
        session.ApplyPlayer(draft);
        if (session.Data.ToString() != before || session.Dirty) throw new Exception("No-op player apply altered raw data or dirty state.");
        foreach (var invalid in new[] { draft with { Units = "4294967296", SaveName = "Changed" }, draft with { Health = "1.5", Shield = "50" }, draft with { Energy = "-1" } })
        {
            try { session.ApplyPlayer(invalid); throw new Exception("Invalid player value accepted."); }
            catch (ArgumentException) { }
            if (session.Data.ToString() != before || session.Dirty) throw new Exception("Invalid player apply was not atomic.");
        }
        session.ApplyPlayer(draft with { Quicksilver = "4294967295", SaveName = "Updated" });
        if (!session.Dirty || session.ReadPlayer().Quicksilver != "4294967295" || session.Player!.GetInt("Specials") != -1 || session.ReadPlayer().SaveName != "Updated") throw new Exception("Player edit unsigned roundtrip failed.");
        if (!ReferenceEquals(originalHealth, session.Player!.Get("Health")))
            throw new Exception("Editing currency changed the untouched out-of-range health value.");
        var roundtrip = JsonObject.Parse(session.Data.ToString());
        var reopened = new SaveSession(resources);
        reopened.Data.Set("PlayerStateData", roundtrip.GetObject("PlayerStateData")!);
        reopened.Data.Set("CommonStateData", roundtrip.GetObject("CommonStateData")!);
        if (reopened.ReadPlayer() != session.ReadPlayer()) throw new Exception("Player values changed after JSON serialization.");
        session.Player.Set("Health",JsonObject.Parse("{\"v\":1.5}").Get("v"));
        var fractionalHealth=session.Player.Get("Health");var withFraction=session.ReadPlayer();session.ApplyPlayer(withFraction with{Units="12"});
        if(!ReferenceEquals(fractionalHealth,session.Player.Get("Health")))throw new Exception("Editing currency changed an untouched fractional vital");
        Console.WriteLine("PASS: synthetic player no-op fidelity, raw stat preservation, unsigned currency roundtrip and validation atomicity.");
    }
}

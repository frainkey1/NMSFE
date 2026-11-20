using System.Globalization;
using NMSE.Core;

namespace NMSE.Next.Engine;

public sealed record PlayerDraft(string Health, string Shield, string Energy, string Units,
    string Nanites, string Quicksilver, string? SaveName, string? SaveSummary);

public sealed partial class SaveSession
{
    public PlayerDraft ReadPlayer()
    {
        var player = Player ?? throw new InvalidOperationException("Open a player save first.");
        string Stat(string key, decimal maximum) => MainStatsLogic.ReadStatValue(player, key, 0, maximum).ToString(CultureInfo.InvariantCulture);
        return new(Stat("Health", 999999), Stat("Shield", 999999), Stat("Energy", 999999),
            Stat("Units", uint.MaxValue), Stat("Nanites", uint.MaxValue), Stat("Specials", uint.MaxValue),
            Data.GetObject("CommonStateData")?.Get("SaveName") is string name ? name : null,
            player.Get("SaveSummary") is string summary ? summary : null);
    }

    public void ApplyPlayer(PlayerDraft draft,bool validateOnly=false)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var player = Player ?? throw new InvalidOperationException("Open a player save first.");
        var old = ReadPlayer();
        string[] texts = [draft.Health, draft.Shield, draft.Energy, draft.Units, draft.Nanites, draft.Quicksilver];
        var fields = MainStatsLogic.StatFields;
        var values = new decimal[fields.Length];
        var raw = new Dictionary<string, decimal>();
        string[] oldTexts=[old.Health,old.Shield,old.Energy,old.Units,old.Nanites,old.Quicksilver];
        bool changed = false;
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            raw[field.Key] = MainStatsLogic.ReadRawStatValue(player, field.Key);
            if(texts[i]==oldTexts[i]){values[i]=Math.Clamp(raw[field.Key],0,field.Maximum);continue;}
            if (!decimal.TryParse(texts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]) || values[i] < 0 || values[i] > field.Maximum)
                throw new ArgumentException($"{field.Label} must be a whole number between 0 and {field.Maximum.ToString(CultureInfo.InvariantCulture)}.");
            raw[field.Key] = MainStatsLogic.ReadRawStatValue(player, field.Key);
            changed |= values[i] != Math.Clamp(raw[field.Key], 0, field.Maximum);
        }
        // Unsupported or missing metadata fields are never invented.
        if (old.SaveName == null && draft.SaveName != null || old.SaveSummary == null && draft.SaveSummary != null)
            throw new ArgumentException("This save does not contain the requested name or summary field.");
        if (old.SaveName != null && draft.SaveName == null || old.SaveSummary != null && draft.SaveSummary == null)
            throw new ArgumentException("Save name and summary cannot be null.");
        changed |= draft.SaveName != old.SaveName || draft.SaveSummary != old.SaveSummary;
        if (!changed || validateOnly) return;
        MainStatsLogic.WriteStatValues(player, values[0], values[1], values[2], values[3], values[4], values[5], raw);
        if (old.SaveName != draft.SaveName) Data.GetObject("CommonStateData")!.Set("SaveName", draft.SaveName);
        if (old.SaveSummary != draft.SaveSummary) player.Set("SaveSummary", draft.SaveSummary);
        Dirty = true;
    }
}

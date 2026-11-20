using System.Text.Json;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record BaseNpcWorker(int Index, string Name, JsonObject Data)
{
    public override string ToString() => Name;
}

public sealed record BaseNpcRace(string Name, string Filename)
{
    public override string ToString() => Name;
}

/// <summary>Save-field helpers for the five persistent base specialists.</summary>
public static class BaseNpcOperations
{
    private static readonly string[] WorkerNames =
        ["Armorer", "Farmer", "Overseer", "Technician", "Scientist"];

    public static readonly BaseNpcRace[] Races =
    [
        new("Vy'keen", "MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCVYKEEN.SCENE.MBIN"),
        new("Korvax", "MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCKORVAX.SCENE.MBIN"),
        new("Gek", "MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCGEK.SCENE.MBIN"),
        new("Traveller", "MODELS/COMMON/PLAYER/PLAYERCHARACTER/NPCFOURTH.SCENE.MBIN"),
        new("Vy'keen (Old)", "MODELS/PLANETS/NPCS/WARRIOR/WARRIOR.SCENE.MBIN"),
        new("Korvax (Old)", "MODELS/PLANETS/NPCS/EXPLORER/EXPLORERIPAD.SCENE.MBIN"),
        new("Gek (Old)", "MODELS/PLANETS/NPCS/LOWERORDER/LOWERORDER.SCENE.MBIN"),
        new("Traveller (Old)", "MODELS/PLANETS/NPCS/FOURTHRACE/FOURTHRACE.SCENE.MBIN")
    ];

    public static IReadOnlyList<BaseNpcWorker> GetHiredWorkers(JsonObject? playerState)
    {
        var workers = playerState?.GetArray("NPCWorkers");
        if (workers is null) return [];

        var result = new List<BaseNpcWorker>();
        for (int i = 0; i < workers.Length && i < WorkerNames.Length; i++)
        {
            var worker = workers.GetObject(i);
            if (worker is null) continue;

            bool hired;
            try { hired = worker.GetBool("HiredWorker"); }
            catch { continue; }

            if (hired) result.Add(new BaseNpcWorker(i, WorkerNames[i], worker));
        }
        return result;
    }

    public static string? GetRaceName(string? filename) =>
        Races.FirstOrDefault(race => string.Equals(race.Filename, filename, StringComparison.OrdinalIgnoreCase))?.Name;
}

public sealed partial class SaveSession
{
    public void SummonBaseNpc(JsonObject worker, JsonObject baseRecord)
    {
        if (!IsAttached(worker) || !IsAttached(baseRecord))
            throw new InvalidOperationException("The worker or base is no longer part of the loaded save.");

        var address = baseRecord.Get("GalacticAddress")
            ?? throw new ArgumentException("The selected base has no galactic address.");
        var position = baseRecord.GetArray("Position");
        if (position is null || position.Length < 3)
            throw new ArgumentException("The selected base does not have a complete position.");

        var baseOffset = new JsonArray();
        for (int i = 0; i < 3; i++)
        {
            object? coordinate = position.Get(i);
            if (!IsFiniteNumber(coordinate))
                throw new ArgumentException("The selected base position contains an invalid coordinate.");
            baseOffset.Add(CloneJsonValue(coordinate));
        }
        baseOffset.Add(1.0);

        string? baseType = baseRecord.GetString("BaseType.PersistentBaseTypes")
            ?? baseRecord.GetString("BaseType");
        bool isFreighterBase = string.Equals(baseType, "FreighterBase", StringComparison.OrdinalIgnoreCase);

        worker.Set("BaseUA", CloneJsonValue(address));
        worker.Set("BaseOffset", baseOffset);
        worker.Set("FreighterBase", isFreighterBase);
        Dirty = true;
    }

    private static bool IsFiniteNumber(object? value)
    {
        if (value is null) return false;
        try
        {
            using var document = JsonDocument.Parse(FieldText(value));
            return document.RootElement.ValueKind == JsonValueKind.Number
                && document.RootElement.TryGetDouble(out double number)
                && double.IsFinite(number);
        }
        catch
        {
            return false;
        }
    }

    private static object? CloneJsonValue(object? value) => value switch
    {
        JsonObject obj => obj.DeepClone(),
        JsonArray array => array.DeepClone(),
        _ => value
    };
}

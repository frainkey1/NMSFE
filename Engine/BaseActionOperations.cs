using NMSE.Core;
using NMSE.Models;

namespace NMSE.Next.Engine;

public sealed record BaseImportInspection(
    string Name,
    bool IsSupported,
    bool InvalidTerrainSidecar,
    bool TypeMismatch,
    bool MetadataMismatch,
    bool TerrainConflict);

public sealed record BaseActionImportResult(int BaseIndex, int TerrainBuffers);

/// <summary>Save-editing actions for player base records and their terrain data.</summary>
public sealed partial class SaveSession
{
    public void ExportBase(JsonObject baseRecord, string path)
    {
        if (!IsAttached(baseRecord))
            throw new InvalidOperationException("The selected base is no longer part of the loaded save.");
        BaseExportLogic.WriteExport(Player, baseRecord, path);
    }

    public BaseImportInspection InspectBaseImport(string path, int targetIndex = -1)
    {
        var player = Player ?? throw new InvalidOperationException("Open a player save first.");
        path = BaseExportLogic.ResolveBaseFilePath(path);
        var file = JsonObject.ImportFromFile(path);
        var read = BaseExportLogic.ReadExport(file);
        if (read.Status != BaseExportLogic.ReadStatus.Ok || read.Payload is null)
            return new("Base", false, false, false, false, false);

        var baseRecord = read.Payload.Base;
        string name = BaseExportLogic.GetDisplayName(baseRecord);
        bool supported = BaseExportLogic.IsSupportedKind(baseRecord);
        var sidecar = BaseExportLogic.ReadTerrainSidecar(path, baseRecord);
        bool invalidSidecar = sidecar.Status == BaseExportLogic.TerrainSidecarStatus.Invalid;
        var payload = new BaseExportLogic.ExportPayload(
            baseRecord, sidecar.Status == BaseExportLogic.TerrainSidecarStatus.Ok ? sidecar.TerrainEditData : null);
        var bases = player.GetArray("PersistentPlayerBases");
        var target = targetIndex >= 0 ? bases?.GetObject(targetIndex) : null;
        bool typeMismatch = target is not null && !BaseExportLogic.IsSameType(target, baseRecord);
        bool metadataMismatch = BaseExportLogic.HasMetadataMismatch(player, baseRecord, targetIndex);
        bool terrainConflict = BaseExportLogic.HasTerrainConflict(player, payload, targetIndex);
        return new(name, supported, invalidSidecar, typeMismatch, metadataMismatch, terrainConflict);
    }

    public BaseActionImportResult ImportBase(string path, int targetIndex = -1)
    {
        var player = Player ?? throw new InvalidOperationException("Open a player save first.");
        path = BaseExportLogic.ResolveBaseFilePath(path);
        var file = JsonObject.ImportFromFile(path);
        var read = BaseExportLogic.ReadExport(file);
        if (read.Status != BaseExportLogic.ReadStatus.Ok || read.Payload is null)
            throw new InvalidDataException("The selected file does not contain a player base.");

        var baseRecord = read.Payload.Base;
        if (!BaseExportLogic.IsSupportedKind(baseRecord))
            throw new InvalidDataException("This base type cannot be imported.");

        var sidecar = BaseExportLogic.ReadTerrainSidecar(path, baseRecord);
        if (sidecar.Status == BaseExportLogic.TerrainSidecarStatus.Invalid)
            throw new InvalidDataException("The terrain sidecar is invalid or does not match this base.");

        var payload = new BaseExportLogic.ExportPayload(
            baseRecord, sidecar.Status == BaseExportLogic.TerrainSidecarStatus.Ok ? sidecar.TerrainEditData : null);
        BaseExportLogic.ImportResult result;
        if (targetIndex < 0)
            result = BaseExportLogic.ImportAsNew(player, payload);
        else
        {
            var target = player.GetArray("PersistentPlayerBases")?.GetObject(targetIndex)
                ?? throw new InvalidOperationException("The selected base is no longer available.");
            if (!BaseExportLogic.IsSameType(target, baseRecord))
                throw new InvalidOperationException("A base can only be replaced by an export of the same type.");
            result = BaseExportLogic.ReplaceBase(player, targetIndex, payload);
        }
        Dirty = true;
        return new(result.BaseIndex, result.TerrainBuffers);
    }

    public void DeleteBase(int index)
    {
        var bases = Player?.GetArray("PersistentPlayerBases")
            ?? throw new InvalidOperationException("This save has no player bases.");
        if (index < 0 || index >= bases.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        bases.RemoveAt(index);
        Dirty = true;
    }

    public bool CanMoveBaseComputerOrClearTerrain(JsonObject baseRecord)
    {
        if (!IsAttached(baseRecord))
            return false;
        return BaseListLogic.Classify(baseRecord) is not (
            BaseListLogic.BaseListKind.Freighter
            or BaseListLogic.BaseListKind.SpaceStation
            or BaseListLogic.BaseListKind.Corvette
            or BaseListLogic.BaseListKind.SpaceBase
            or BaseListLogic.BaseListKind.TemporaryShip
            or BaseListLogic.BaseListKind.Legacy);
    }

    public void MoveBaseComputer(JsonObject baseRecord, JsonObject target)
    {
        if (!CanMoveBaseComputerOrClearTerrain(baseRecord) || !IsAttached(target))
            throw new InvalidOperationException("The selected base or object is no longer available for this action.");
        var objects = baseRecord.GetArray("Objects")
            ?? throw new InvalidOperationException("The selected base has no objects.");
        var flag = Enumerable.Range(0, objects.Length)
            .Select(objects.GetObject)
            .FirstOrDefault(item => item is not null
                && string.Equals(item.GetString("ObjectID"), "^BASE_FLAG", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The base computer object (^BASE_FLAG) was not found.");
        BaseLogic.MoveBaseComputer(baseRecord, flag, target);
        Dirty = true;
    }

    public int ClearBaseTerrainEdits(JsonObject baseRecord)
    {
        if (!CanMoveBaseComputerOrClearTerrain(baseRecord))
            throw new InvalidOperationException("Terrain cleanup is unavailable for this base type.");
        int removed = Player is { } player ? BaseLogic.ClearTerrainEdits(player, baseRecord) : 0;
        if (removed > 0) Dirty = true;
        return removed;
    }

    public int ClearAllTerrainEdits()
    {
        int removed = Player is { } player ? BaseLogic.ClearAllTerrainEdits(player) : 0;
        if (removed > 0) Dirty = true;
        return removed;
    }

    public int ClearAllTerrainEditsExceptBases()
    {
        int removed = Player is { } player ? BaseLogic.ClearAllTerrainEditsExceptBases(player) : 0;
        if (removed > 0) Dirty = true;
        return removed;
    }
}
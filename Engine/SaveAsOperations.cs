using System.Security.Cryptography;
using NMSE.IO;

namespace NMSE.Next.Engine;

public sealed partial class SaveSession
{
    /// <summary>Writes a verified new save beside the current save and makes it active without overwriting.</summary>
    public string SaveAs(string path)
    {
        if (SourcePath == null) throw new InvalidOperationException("Load a save before using Save As.");
        if (IsConsoleSession) throw new NotSupportedException("Save As is not available for console saves.");
        path = Path.GetFullPath(path);
        string sourceDirectory = Path.GetDirectoryName(SourcePath)!;
        if (!string.Equals(Path.GetDirectoryName(path), sourceDirectory, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Save As must stay in the current save directory to preserve platform and account metadata.");
        if (string.Equals(path, SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different filename for Save As.");
        bool json = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
        if (!json && !Path.GetExtension(path).Equals(".hg", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Save As supports .hg game saves and .json saves.");
        if (File.Exists(path)) throw new IOException("Choose a new filename; existing files are not overwritten.");

        var platform = SaveFileManager.DetectPlatform(sourceDirectory);
        if (!json && platform is not (SaveFileManager.Platform.Steam or SaveFileManager.Platform.GOG))
            throw new NotSupportedException("Save As for compressed .hg files requires a supported Steam or GOG save directory.");
        string staging = Path.Combine(sourceDirectory, ".nmse-next-saveas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string staged = Path.Combine(staging, Path.GetFileName(path));
        string? stagedMeta = null;
        var committed = new List<string>();
        try
        {
            if (json)
            {
                File.WriteAllText(staged, Data.ToFormattedString());
            }
            else
            {
                SaveFileManager.SaveToFile(staged, Data, compress: true, writeMeta: true, platform: platform);
                stagedMeta = MetaFileWriter.GetSteamMetaPath(staged);
            }
            if (SaveFileManager.LoadSaveFile(staged).ToString() != Data.ToString())
                throw new InvalidDataException("Save As output verification failed.");
            if (File.Exists(path)) throw new IOException("Choose a new filename; existing files are not overwritten.");
            string? targetMeta = stagedMeta == null ? null : MetaFileWriter.GetSteamMetaPath(path);
            if (targetMeta != null && File.Exists(targetMeta))
                throw new IOException("The destination save metadata file already exists; choose another filename.");

            File.Move(staged, path);
            committed.Add(path);
            if (stagedMeta != null && targetMeta != null)
            {
                File.Move(stagedMeta, targetMeta);
                committed.Add(targetMeta);
            }
            SourcePath = path;
            _loadedSourceHash = SHA256.HashData(File.ReadAllBytes(path));
            Dirty = false;
            return path;
        }
        catch
        {
            foreach (string file in committed.AsEnumerable().Reverse())
                if (File.Exists(file)) File.Delete(file);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging) &&
                string.Equals(Path.GetDirectoryName(Path.GetFullPath(staging)), Path.GetFullPath(sourceDirectory), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(staging).StartsWith(".nmse-next-saveas-", StringComparison.Ordinal))
                Directory.Delete(staging, true);
        }
    }
}

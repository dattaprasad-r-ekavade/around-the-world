using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Scene;

namespace Ember.World;

/// <summary>File operations used by an editor to create and rename manifest-backed cells.</summary>
public static class WorldCellWorkspace
{
    public static WorldManifest CreateWorld(string manifestPath, float exteriorCellWidth = 32f)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("A world manifest path is required.", nameof(manifestPath));
        var fullPath = Path.GetFullPath(manifestPath);
        if (File.Exists(fullPath))
            throw new IOException($"A world manifest already exists at '{fullPath}'.");

        WorldManifest.SaveAtomic(fullPath, exteriorCellWidth, Array.Empty<WorldCellDefinition>());
        return WorldManifest.Load(fullPath);
    }

    public static WorldCellDefinition CreateCell(string manifestPath, WorldCellKind kind, string name,
        ExteriorCellCoordinate? exteriorCoordinate = null)
    {
        var manifest = WorldManifest.Load(manifestPath);
        ValidateCellDefinition(kind, name, exteriorCoordinate);
        var scenePath = CreateUniqueScenePath(manifest, kind, name);
        var cell = new WorldCellDefinition
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ExteriorCoordinate = exteriorCoordinate,
            ScenePath = scenePath
        };
        var fullScenePath = ResolveScenePath(manifest, scenePath);

        SceneFile.SaveAtomic(new SceneGraph(), fullScenePath);
        try
        {
            WorldManifest.SaveAtomic(manifest.FilePath, manifest.ExteriorCellWidth,
                manifest.Cells.Append(cell));
        }
        catch
        {
            if (File.Exists(fullScenePath)) File.Delete(fullScenePath);
            throw;
        }

        return WorldManifest.Load(manifest.FilePath).FindCell(cell.Id)!;
    }

    /// <summary>Renames a cell's scene file while keeping its stable ID and exterior coordinate.</summary>
    public static WorldCellDefinition RenameCell(string manifestPath, Guid cellId, string newName)
    {
        var manifest = WorldManifest.Load(manifestPath);
        var cell = manifest.FindCell(cellId)
            ?? throw new KeyNotFoundException($"World manifest '{manifest.FilePath}' has no cell with ID {cellId}.");
        ValidateName(newName);

        var newScenePath = CreateUniqueScenePath(manifest, cell.Kind, newName, cellId);
        if (string.Equals(cell.ScenePath, newScenePath, StringComparison.OrdinalIgnoreCase))
            return cell;

        var oldFullPath = manifest.ResolveScenePath(cellId);
        var newFullPath = ResolveScenePath(manifest, newScenePath);
        Directory.CreateDirectory(Path.GetDirectoryName(newFullPath)!);
        File.Copy(oldFullPath, newFullPath, overwrite: false);
        var renamed = new WorldCellDefinition
        {
            Id = cell.Id,
            Kind = cell.Kind,
            ExteriorCoordinate = cell.ExteriorCoordinate,
            ScenePath = newScenePath
        };

        try
        {
            WorldManifest.SaveAtomic(manifest.FilePath, manifest.ExteriorCellWidth,
                manifest.Cells.Select(existing => existing.Id == cellId ? renamed : existing));
        }
        catch
        {
            if (File.Exists(newFullPath)) File.Delete(newFullPath);
            throw;
        }

        // The manifest already points at the new file. A cleanup failure only leaves an orphan;
        // it must not make a committed rename appear to have failed.
        try { File.Delete(oldFullPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return WorldManifest.Load(manifest.FilePath).FindCell(cellId)!;
    }

    public static string GetCellName(WorldCellDefinition cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return Path.GetFileNameWithoutExtension(cell.ScenePath);
    }

    private static void ValidateCellDefinition(WorldCellKind kind, string name,
        ExteriorCellCoordinate? exteriorCoordinate)
    {
        ValidateName(name);
        switch (kind)
        {
            case WorldCellKind.Exterior when exteriorCoordinate is null:
                throw new ArgumentException("Exterior cells require X and Z coordinates.", nameof(exteriorCoordinate));
            case WorldCellKind.Interior when exteriorCoordinate is not null:
                throw new ArgumentException("Interior cells cannot have exterior coordinates.", nameof(exteriorCoordinate));
            case WorldCellKind.Exterior:
            case WorldCellKind.Interior:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown world cell kind.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A cell name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(ToFileName(name)))
            throw new ArgumentException("A cell name must contain at least one letter or number.", nameof(name));
    }

    private static string CreateUniqueScenePath(WorldManifest manifest, WorldCellKind kind,
        string name, Guid replacingCellId = default)
    {
        var folder = kind == WorldCellKind.Exterior ? "Cells/Exterior" : "Cells/Interiors";
        var baseName = ToFileName(name);
        var replacingCell = replacingCellId == Guid.Empty ? null : manifest.FindCell(replacingCellId);
        var referencedPaths = new HashSet<string>(manifest.Cells
            .Where(cell => cell.Id != replacingCellId)
            .Select(cell => cell.ScenePath), StringComparer.OrdinalIgnoreCase);

        for (var suffix = 1; ; suffix++)
        {
            var fileName = suffix == 1 ? $"{baseName}.json" : $"{baseName}-{suffix}.json";
            var relativePath = $"{folder}/{fileName}";
            var fullPath = ResolveScenePath(manifest, relativePath);
            var isCurrentPath = replacingCell is not null
                && string.Equals(replacingCell.ScenePath, relativePath, StringComparison.OrdinalIgnoreCase);
            if (!referencedPaths.Contains(relativePath) && (isCurrentPath || !File.Exists(fullPath)))
                return relativePath;
        }
    }

    private static string ResolveScenePath(WorldManifest manifest, string relativePath) =>
        Path.GetFullPath(relativePath, manifest.RootDirectory);

    private static string ToFileName(string name)
    {
        var builder = new System.Text.StringBuilder();
        char? pendingSeparator = null;
        foreach (var character in name.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                if (pendingSeparator is { } separator && builder.Length > 0
                    && builder[^1] != ' ' && builder[^1] != '-')
                    builder.Append(separator);
                builder.Append(character);
                pendingSeparator = null;
            }
            else
            {
                pendingSeparator = char.IsWhiteSpace(character) ? ' ' : '-';
            }
        }

        var safeName = builder.ToString().Trim(' ', '-', '_');
        return IsReservedWindowsName(safeName) ? $"cell-{safeName}" : safeName;
    }

    private static bool IsReservedWindowsName(string name)
    {
        var baseName = name.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        return baseName.Length == 4
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && baseName[3] is >= '1' and <= '9';
    }
}

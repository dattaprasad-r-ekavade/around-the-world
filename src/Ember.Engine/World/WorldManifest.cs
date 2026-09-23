using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.World;

public enum WorldCellKind
{
    Exterior,
    Interior
}

/// <summary>A stable reference to one authored exterior or interior scene.</summary>
public sealed class WorldCellDefinition
{
    public Guid Id { get; init; }
    public WorldCellKind Kind { get; init; }
    public int? ExteriorX { get; init; }
    public int? ExteriorZ { get; init; }
    public string ScenePath { get; init; } = string.Empty;
}

/// <summary>Versioned world index whose scene paths are relative to the manifest file.</summary>
public sealed class WorldManifest
{
    public const int CurrentVersion = 1;
    public const string DefaultFileName = "world.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<WorldCellDefinition> _cells;

    private WorldManifest(string filePath, IEnumerable<WorldCellDefinition> cells)
    {
        FilePath = Path.GetFullPath(filePath);
        RootDirectory = Path.GetDirectoryName(FilePath)!;
        _cells = cells.ToList();
        Validate(_cells, RootDirectory);
    }

    public string FilePath { get; }
    public string RootDirectory { get; }
    public IReadOnlyList<WorldCellDefinition> Cells => _cells.AsReadOnly();

    public WorldCellDefinition? FindCell(Guid id) => _cells.FirstOrDefault(cell => cell.Id == id);

    public string ResolveScenePath(Guid cellId)
    {
        var cell = FindCell(cellId)
            ?? throw new KeyNotFoundException($"World manifest '{FilePath}' has no cell with ID {cellId}.");
        return ResolveScenePath(RootDirectory, cell.ScenePath);
    }

    public static WorldManifest Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A world manifest path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        WorldManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize<WorldManifestDocument>(File.ReadAllText(fullPath), JsonOptions)
                ?? throw new InvalidDataException("World manifest JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"World manifest JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported world manifest version {document.Version}; expected {CurrentVersion}.");
        if (document.Cells is null)
            throw new InvalidDataException("World manifest cell list is missing.");

        try
        {
            return new WorldManifest(fullPath, document.Cells);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"World manifest contains an invalid scene path: {exception.Message}", exception);
        }
    }

    public static void SaveAtomic(string path, IEnumerable<WorldCellDefinition> cells)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A world manifest path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(cells);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("World manifest path has no parent directory.");
        var snapshot = cells.ToList();
        _ = new WorldManifest(fullPath, snapshot);
        Directory.CreateDirectory(directory);

        var document = new WorldManifestDocument { Version = CurrentVersion, Cells = snapshot };
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, document, JsonOptions);
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void Validate(IReadOnlyList<WorldCellDefinition> cells, string rootDirectory)
    {
        var ids = new HashSet<Guid>();
        var exteriorCoordinates = new HashSet<(int X, int Z)>();
        foreach (var cell in cells)
        {
            if (cell is null)
                throw new InvalidDataException("World manifest contains a null cell entry.");
            if (cell.Id == Guid.Empty)
                throw new InvalidDataException("World cell ID cannot be empty.");
            if (!ids.Add(cell.Id))
                throw new InvalidDataException($"Duplicate world cell ID: {cell.Id}.");
            if (string.IsNullOrWhiteSpace(cell.ScenePath))
                throw new InvalidDataException($"World cell {cell.Id} has no scene path.");

            switch (cell.Kind)
            {
                case WorldCellKind.Exterior:
                    if (cell.ExteriorX is null || cell.ExteriorZ is null)
                        throw new InvalidDataException($"Exterior cell {cell.Id} must define ExteriorX and ExteriorZ.");
                    if (!exteriorCoordinates.Add((cell.ExteriorX.Value, cell.ExteriorZ.Value)))
                        throw new InvalidDataException(
                            $"Duplicate exterior cell coordinates ({cell.ExteriorX.Value}, {cell.ExteriorZ.Value}).");
                    break;
                case WorldCellKind.Interior:
                    if (cell.ExteriorX is not null || cell.ExteriorZ is not null)
                        throw new InvalidDataException($"Interior cell {cell.Id} cannot define exterior coordinates.");
                    break;
                default:
                    throw new InvalidDataException($"World cell {cell.Id} has unknown kind '{cell.Kind}'.");
            }

            var scenePath = ResolveScenePath(rootDirectory, cell.ScenePath);
            if (!File.Exists(scenePath))
                throw new InvalidDataException(
                    $"World cell {cell.Id} refers to missing scene '{cell.ScenePath}' at '{scenePath}'.");
        }
    }

    private static string ResolveScenePath(string rootDirectory, string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            throw new ArgumentException("Scene path is required.", nameof(scenePath));
        if (!scenePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scene path must name a .json scene file.", nameof(scenePath));

        var normalized = scenePath.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            || Path.IsPathRooted(normalized))
            throw new ArgumentException("Scene path must be relative to the world manifest.", nameof(scenePath));
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
            throw new ArgumentException("Scene path cannot contain '..' segments.", nameof(scenePath));

        var fullPath = Path.GetFullPath(normalized, rootDirectory);
        var relativePath = Path.GetRelativePath(rootDirectory, fullPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Scene path escapes the world manifest directory.", nameof(scenePath));
        return fullPath;
    }

    private sealed class WorldManifestDocument
    {
        public int Version { get; set; }
        public List<WorldCellDefinition>? Cells { get; set; }
    }
}

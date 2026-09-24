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
    public ExteriorCellCoordinate? ExteriorCoordinate { get; init; }
    public string ScenePath { get; init; } = string.Empty;
}

/// <summary>Versioned world index whose scene paths are relative to the manifest file.</summary>
public sealed class WorldManifest
{
    public const int CurrentVersion = 2;
    public const float Version1DefaultExteriorCellWidth = 32f;
    public const string DefaultFileName = "world.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<WorldCellDefinition> _cells;
    private readonly Dictionary<Guid, WorldCellDefinition> _cellsById;
    private readonly Dictionary<ExteriorCellCoordinate, WorldCellDefinition> _exteriorCells;

    private WorldManifest(string filePath, float exteriorCellWidth,
        IEnumerable<WorldCellDefinition> cells, bool requireSceneFiles)
    {
        FilePath = Path.GetFullPath(filePath);
        RootDirectory = Path.GetDirectoryName(FilePath)!;
        ExteriorCellWidth = ValidateCellWidth(exteriorCellWidth);
        _cells = cells.ToList();
        Validate(_cells, RootDirectory, requireSceneFiles);
        _cellsById = _cells.ToDictionary(cell => cell.Id);
        _exteriorCells = _cells
            .Where(cell => cell.Kind == WorldCellKind.Exterior)
            .ToDictionary(cell => cell.ExteriorCoordinate!.Value);
    }

    public string FilePath { get; }
    public string RootDirectory { get; }
    public float ExteriorCellWidth { get; }
    public IReadOnlyList<WorldCellDefinition> Cells => _cells.AsReadOnly();

    public WorldCellDefinition? FindCell(Guid id) => _cellsById.GetValueOrDefault(id);

    /// <summary>Returns false for coordinates outside the authored world.</summary>
    public bool TryGetExterior(ExteriorCellCoordinate coordinate, out WorldCellDefinition? cell) =>
        _exteriorCells.TryGetValue(coordinate, out cell);

    public ExteriorCellCoordinate GetExteriorCoordinate(Microsoft.Xna.Framework.Vector3 worldPosition) =>
        ExteriorCellGrid.FromWorldPosition(worldPosition, ExteriorCellWidth);

    public ExteriorCellLoadingRing CreateLoadingRing(int radiusInCells, int? retentionRadiusInCells = null) =>
        new(radiusInCells, ExteriorCellWidth, retentionRadiusInCells);

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

        if (document.Cells is null)
            throw new InvalidDataException("World manifest cell list is missing.");

        float cellWidth;
        List<WorldCellDefinition> cells;
        switch (document.Version)
        {
            case 1:
                if (document.ExteriorCellWidth is not null)
                    throw new InvalidDataException("Version 1 world manifests cannot define exteriorCellWidth.");
                cellWidth = Version1DefaultExteriorCellWidth;
                cells = ConvertVersion1Cells(document.Cells);
                break;
            case CurrentVersion:
                if (document.ExteriorCellWidth is null)
                    throw new InvalidDataException("World manifest exteriorCellWidth is missing.");
                cellWidth = document.ExteriorCellWidth.Value;
                cells = ConvertVersion2Cells(document.Cells);
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported world manifest version {document.Version}; expected 1 or {CurrentVersion}.");
        }

        try
        {
            return new WorldManifest(fullPath, cellWidth, cells, requireSceneFiles: true);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"World manifest contains an invalid scene path: {exception.Message}", exception);
        }
    }

    public static void SaveAtomic(string path, float exteriorCellWidth, IEnumerable<WorldCellDefinition> cells)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A world manifest path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(cells);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("World manifest path has no parent directory.");
        var snapshot = cells.ToList();
        var validated = new WorldManifest(fullPath, exteriorCellWidth, snapshot, requireSceneFiles: false);
        Directory.CreateDirectory(directory);

        var document = new WorldManifestDocument
        {
            Version = CurrentVersion,
            ExteriorCellWidth = validated.ExteriorCellWidth,
            Cells = snapshot.Select(ToDocumentCell).ToList()
        };
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

    private static List<WorldCellDefinition> ConvertVersion1Cells(IEnumerable<WorldCellDocument> cells) =>
        cells.Select(cell =>
        {
            if (cell.ExteriorCoordinate is not null)
                throw new InvalidDataException("Version 1 world manifests must use exteriorX and exteriorZ.");
            return new WorldCellDefinition
            {
                Id = cell.Id,
                Kind = cell.Kind,
                ExteriorCoordinate = cell.ExteriorX is null && cell.ExteriorZ is null
                    ? null
                    : cell.ExteriorX is not null && cell.ExteriorZ is not null
                        ? new ExteriorCellCoordinate(cell.ExteriorX.Value, cell.ExteriorZ.Value)
                        : throw new InvalidDataException($"Exterior cell {cell.Id} must define both exteriorX and exteriorZ."),
                ScenePath = cell.ScenePath
            };
        }).ToList();

    private static List<WorldCellDefinition> ConvertVersion2Cells(IEnumerable<WorldCellDocument> cells) =>
        cells.Select(cell =>
        {
            if (cell.ExteriorX is not null || cell.ExteriorZ is not null)
                throw new InvalidDataException("Version 2 world manifests must use exteriorCoordinate.");
            return new WorldCellDefinition
            {
                Id = cell.Id,
                Kind = cell.Kind,
                ExteriorCoordinate = cell.ExteriorCoordinate,
                ScenePath = cell.ScenePath
            };
        }).ToList();

    private static WorldCellDocument ToDocumentCell(WorldCellDefinition cell) => new()
    {
        Id = cell.Id,
        Kind = cell.Kind,
        ExteriorCoordinate = cell.ExteriorCoordinate,
        ScenePath = cell.ScenePath
    };

    private static float ValidateCellWidth(float cellWidth)
    {
        if (!float.IsFinite(cellWidth) || cellWidth <= 0f)
            throw new InvalidDataException("World manifest exteriorCellWidth must be finite and positive.");
        return cellWidth;
    }

    private static void Validate(IReadOnlyList<WorldCellDefinition> cells, string rootDirectory, bool requireSceneFiles)
    {
        var ids = new HashSet<Guid>();
        var exteriorCoordinates = new HashSet<ExteriorCellCoordinate>();
        var scenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    if (cell.ExteriorCoordinate is null)
                        throw new InvalidDataException($"Exterior cell {cell.Id} must define ExteriorCoordinate.");
                    if (!exteriorCoordinates.Add(cell.ExteriorCoordinate.Value))
                        throw new InvalidDataException(
                            $"Duplicate exterior cell coordinates ({cell.ExteriorCoordinate.Value.X}, {cell.ExteriorCoordinate.Value.Z}).");
                    break;
                case WorldCellKind.Interior:
                    if (cell.ExteriorCoordinate is not null)
                        throw new InvalidDataException($"Interior cell {cell.Id} cannot define exterior coordinates.");
                    break;
                default:
                    throw new InvalidDataException($"World cell {cell.Id} has unknown kind '{cell.Kind}'.");
            }

            var scenePath = ResolveScenePath(rootDirectory, cell.ScenePath);
            if (!scenePaths.Add(scenePath))
                throw new InvalidDataException($"Multiple world cells refer to the same scene path '{cell.ScenePath}'.");
            if (requireSceneFiles && !File.Exists(scenePath))
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
        public float? ExteriorCellWidth { get; set; }
        public List<WorldCellDocument>? Cells { get; set; }
    }

    private sealed class WorldCellDocument
    {
        public Guid Id { get; set; }
        public WorldCellKind Kind { get; set; }
        public int? ExteriorX { get; set; }
        public int? ExteriorZ { get; set; }
        public ExteriorCellCoordinate? ExteriorCoordinate { get; set; }
        public string ScenePath { get; set; } = string.Empty;
    }
}

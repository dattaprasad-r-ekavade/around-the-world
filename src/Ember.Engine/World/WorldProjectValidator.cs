using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Ember.Scene;

namespace Ember.World;

public sealed record WorldProjectDiagnostic(string SourceFile, string SourceRecord, string Message)
{
    public override string ToString() => $"{SourceFile} [{SourceRecord}]: {Message}";
}

/// <summary>The loaded world records and diagnostics gathered without stopping at the first bad file.</summary>
public sealed class WorldProjectValidationResult
{
    internal WorldProjectValidationResult(WorldManifest? manifest,
        IDictionary<Guid, SceneGraph> scenes, IDictionary<Guid, string> sceneFiles,
        IDictionary<Guid, CellPathGraph> pathGraphs, IReadOnlyList<WorldProjectDiagnostic> diagnostics)
    {
        Manifest = manifest;
        Scenes = new ReadOnlyDictionary<Guid, SceneGraph>(new Dictionary<Guid, SceneGraph>(scenes));
        SceneFiles = new ReadOnlyDictionary<Guid, string>(new Dictionary<Guid, string>(sceneFiles));
        PathGraphs = new ReadOnlyDictionary<Guid, CellPathGraph>(new Dictionary<Guid, CellPathGraph>(pathGraphs));
        Diagnostics = diagnostics;
    }

    public WorldManifest? Manifest { get; }
    public IReadOnlyDictionary<Guid, SceneGraph> Scenes { get; }
    public IReadOnlyDictionary<Guid, string> SceneFiles { get; }
    public IReadOnlyDictionary<Guid, CellPathGraph> PathGraphs { get; }
    public IReadOnlyList<WorldProjectDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>Loads and checks each authored world file while retaining errors from other files.</summary>
public static class WorldProjectValidator
{
    public static WorldProjectValidationResult Validate(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var diagnostics = new List<WorldProjectDiagnostic>();
        WorldManifest world;
        try
        {
            world = WorldManifest.LoadForValidation(fullManifestPath);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new WorldProjectDiagnostic(fullManifestPath, "world manifest", exception.Message));
            return BuildResult(null, new Dictionary<Guid, SceneGraph>(), new Dictionary<Guid, string>(),
                new Dictionary<Guid, CellPathGraph>(), diagnostics);
        }

        var scenes = new Dictionary<Guid, SceneGraph>();
        var sceneFiles = new Dictionary<Guid, string>();
        foreach (var cell in world.Cells.OrderBy(cell => cell.Id))
        {
            var scenePath = world.ResolveScenePath(cell.Id);
            sceneFiles.Add(cell.Id, scenePath);
            try
            {
                scenes.Add(cell.Id, SceneFile.Load(scenePath));
            }
            catch (Exception exception)
            {
                diagnostics.Add(new WorldProjectDiagnostic(scenePath, $"cell '{cell.Id}' scene", exception.Message));
            }
        }

        ValidateSpawnsAndDoors(world, scenes, sceneFiles, diagnostics);
        var pathGraphs = LoadPathGraphs(world, diagnostics);
        ValidateWorldPathNetwork(world.RootDirectory, pathGraphs.Values, diagnostics);
        return BuildResult(world, scenes, sceneFiles, pathGraphs, diagnostics);
    }

    private static void ValidateSpawnsAndDoors(WorldManifest world,
        IReadOnlyDictionary<Guid, SceneGraph> scenes, IReadOnlyDictionary<Guid, string> sceneFiles,
        List<WorldProjectDiagnostic> diagnostics)
    {
        foreach (var (cellId, scene) in scenes.OrderBy(pair => pair.Key))
        {
            var seenSpawns = new HashSet<Guid>();
            foreach (var sceneObject in scene.Objects.OrderBy(item => item.Id))
            {
                if (sceneObject.SpawnPoint is not { } spawn || seenSpawns.Add(spawn.Id)) continue;
                diagnostics.Add(new WorldProjectDiagnostic(sceneFiles[cellId],
                    $"cell '{cellId}' spawn '{spawn.Id}' on object '{sceneObject.Name}' ({sceneObject.Id})",
                    "spawn ID is duplicated within this cell"));
            }
        }

        foreach (var (cellId, scene) in scenes.OrderBy(pair => pair.Key))
        foreach (var sceneObject in scene.Objects.OrderBy(item => item.Id))
        {
            if (sceneObject.Door is not { } door) continue;
            try
            {
                _ = WorldTravelValidator.ResolveDestination(world, scenes, door);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new WorldProjectDiagnostic(sceneFiles[cellId],
                    $"cell '{cellId}' door '{sceneObject.Name}' ({sceneObject.Id})", exception.Message));
            }
        }
    }

    private static Dictionary<Guid, CellPathGraph> LoadPathGraphs(WorldManifest world,
        List<WorldProjectDiagnostic> diagnostics)
    {
        var result = new Dictionary<Guid, CellPathGraph>();
        var pathDirectory = Path.Combine(world.RootDirectory, "Paths");
        if (!Directory.Exists(pathDirectory)) return result;

        foreach (var path in Directory.EnumerateFiles(pathDirectory, "*.paths.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            CellPathGraph graph;
            try
            {
                graph = CellPathGraphFile.Load(path);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new WorldProjectDiagnostic(path,
                    IdentifyPathRecord(exception.Message, "cell path graph"), exception.Message));
                continue;
            }

            var cell = world.FindCell(graph.CellId);
            if (cell is null)
            {
                diagnostics.Add(new WorldProjectDiagnostic(path, $"path graph cell '{graph.CellId}'",
                    "cell ID is not present in the world manifest"));
                continue;
            }
            if (graph.Kind is { } kind && kind != cell.Kind)
            {
                diagnostics.Add(new WorldProjectDiagnostic(path, $"path graph cell '{graph.CellId}'",
                    $"cell kind '{kind}' does not match manifest kind '{cell.Kind}'"));
                continue;
            }
            if (!result.TryAdd(graph.CellId, graph))
                diagnostics.Add(new WorldProjectDiagnostic(path, $"path graph cell '{graph.CellId}'",
                    "more than one path graph is authored for this cell"));
        }

        return result;
    }

    private static void ValidateWorldPathNetwork(string worldRoot,
        IEnumerable<CellPathGraph> pathGraphs, List<WorldProjectDiagnostic> diagnostics)
    {
        var path = Path.Combine(worldRoot, "Paths", "world-paths.json");
        if (!File.Exists(path)) return;
        try
        {
            _ = WorldPathNetworkFile.Load(path, pathGraphs.ToArray());
        }
        catch (Exception exception)
        {
            diagnostics.Add(new WorldProjectDiagnostic(path,
                IdentifyPathRecord(exception.Message, "world path connections"), exception.Message));
        }
    }

    private static string IdentifyPathRecord(string message, string fallback)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6 && parts[0] == "Navigation" && parts[1] == "edge" && parts[3] == "->")
            return $"edge {parts[2]} -> {parts[4]}";
        if (parts.Length >= 5 && parts[0] == "World" && parts[1] == "path" && parts[2] == "connection")
            return $"connection {parts[3]}";
        if (parts.Length >= 6 && parts[0] == "World" && parts[1] == "path" && parts[2] == "network"
            && parts[3] == "repeats" && parts[4] == "connection")
            return $"connection {parts[5].TrimEnd('.') }";
        return fallback;
    }

    private static WorldProjectValidationResult BuildResult(WorldManifest? manifest,
        IDictionary<Guid, SceneGraph> scenes, IDictionary<Guid, string> sceneFiles,
        IDictionary<Guid, CellPathGraph> pathGraphs, IEnumerable<WorldProjectDiagnostic> diagnostics) =>
        new(manifest, scenes, sceneFiles, pathGraphs, diagnostics
            .OrderBy(item => item.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceRecord, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToArray());
}

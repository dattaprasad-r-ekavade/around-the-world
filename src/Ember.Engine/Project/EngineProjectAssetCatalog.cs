using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.Scene;

namespace Ember.Project;

/// <summary>Stable project-level identities for GLB files available in the asset browser.</summary>
public static class EngineProjectAssetCatalog
{
    private const int CurrentVersion = 1;
    private const string CatalogRelativePath = "Assets/.ember-assets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Lists project GLBs and persists IDs for previously uncatalogued files. Existing scene
    /// references seed the catalog so older projects keep their authored IDs when an instance is
    /// later removed from a scene.
    /// </summary>
    public static IReadOnlyList<GltfAssetReference> ListAssets(EngineProjectFile project,
        IEnumerable<GltfAssetReference>? sceneReferences = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var assetsDirectory = project.ResolveContentPath("Assets");
        if (!Directory.Exists(assetsDirectory)) return Array.Empty<GltfAssetReference>();

        var records = LoadRecords(project);
        var recordsByPath = records.ToDictionary(value => value.SourcePath, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        if (sceneReferences is not null)
        {
            foreach (var reference in sceneReferences)
            {
                ArgumentNullException.ThrowIfNull(reference);
                if (!IsAssetPath(reference.SourcePath)) continue;
                var fullPath = project.ResolveContentPath(reference.SourcePath);
                if (!File.Exists(fullPath)) continue;

                if (recordsByPath.TryGetValue(reference.SourcePath, out var existing))
                {
                    if (existing.AssetId != reference.AssetId)
                    {
                        records.Remove(existing);
                        recordsByPath.Remove(existing.SourcePath);
                        EnsureIdAvailable(records, reference.AssetId, reference.SourcePath);
                        var replacement = new AssetRecord(reference.AssetId, reference.SourcePath);
                        records.Add(replacement);
                        recordsByPath.Add(replacement.SourcePath, replacement);
                        changed = true;
                    }
                    continue;
                }

                EnsureIdAvailable(records, reference.AssetId, reference.SourcePath);
                var record = new AssetRecord(reference.AssetId, reference.SourcePath);
                records.Add(record);
                recordsByPath.Add(record.SourcePath, record);
                changed = true;
            }
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var fullPath in Directory.EnumerateFiles(assetsDirectory, "*.glb", options))
        {
            var relativePath = Path.GetRelativePath(project.RootDirectory, fullPath).Replace('\\', '/');
            if (IsImportStagingPath(relativePath) || recordsByPath.ContainsKey(relativePath)) continue;

            var record = new AssetRecord(Guid.NewGuid(), relativePath);
            records.Add(record);
            recordsByPath.Add(record.SourcePath, record);
            changed = true;
        }

        if (changed) SaveRecords(project, records);

        return records
            .Where(record => File.Exists(project.ResolveContentPath(record.SourcePath)))
            .OrderBy(record => record.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(record => new GltfAssetReference(record.AssetId, record.SourcePath))
            .ToArray();
    }

    /// <summary>Registers a validated imported asset without changing its assigned identity.</summary>
    public static void Register(EngineProjectFile project, GltfAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(reference);
        if (!IsAssetPath(reference.SourcePath))
            throw new InvalidDataException($"Project asset path '{reference.SourcePath}' must be inside Assets.");

        var fullPath = project.ResolveContentPath(reference.SourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Project asset '{reference.SourcePath}' was not found.", fullPath);

        var records = LoadRecords(project);
        var existingPath = records.FirstOrDefault(value =>
            string.Equals(value.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (existingPath is not null)
        {
            if (existingPath.AssetId == reference.AssetId) return;
            throw new InvalidDataException(
                $"Project asset path '{reference.SourcePath}' is already registered as {existingPath.AssetId}.");
        }

        EnsureIdAvailable(records, reference.AssetId, reference.SourcePath);
        records.Add(new AssetRecord(reference.AssetId, reference.SourcePath));
        SaveRecords(project, records);
    }

    /// <summary>Removes an uncommitted preview entry while preserving all other project assets.</summary>
    public static void Unregister(EngineProjectFile project, GltfAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(reference);
        var records = LoadRecords(project);
        var existing = records.FirstOrDefault(value =>
            string.Equals(value.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null || existing.AssetId != reference.AssetId) return;
        records.Remove(existing);
        SaveRecords(project, records);
    }

    private static List<AssetRecord> LoadRecords(EngineProjectFile project)
    {
        var catalogPath = project.ResolveContentPath(CatalogRelativePath);
        if (!File.Exists(catalogPath)) return new List<AssetRecord>();

        CatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(catalogPath), JsonOptions)
                ?? throw new InvalidDataException("Project asset catalog is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Project asset catalog is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported project asset catalog version {document.Version}; expected {CurrentVersion}.");
        if (document.Assets is null)
            throw new InvalidDataException("Project asset catalog is missing its asset list.");

        var records = new List<AssetRecord>(document.Assets.Count);
        foreach (var entry in document.Assets)
        {
            if (entry.AssetId == Guid.Empty || !IsAssetPath(entry.SourcePath))
                throw new InvalidDataException("Project asset catalog contains an invalid ID or path.");

            GltfAssetReference reference;
            try { reference = new GltfAssetReference(entry.AssetId, entry.SourcePath); }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Project asset catalog path is invalid: {exception.Message}", exception);
            }

            if (records.Any(value => value.AssetId == reference.AssetId))
                throw new InvalidDataException($"Project asset catalog repeats ID {reference.AssetId}.");
            if (records.Any(value => string.Equals(value.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Project asset catalog repeats path '{reference.SourcePath}'.");
            records.Add(new AssetRecord(reference.AssetId, reference.SourcePath));
        }

        return records;
    }

    private static void SaveRecords(EngineProjectFile project, IReadOnlyCollection<AssetRecord> records)
    {
        var document = new CatalogDocument
        {
            Version = CurrentVersion,
            Assets = records.OrderBy(value => value.SourcePath, StringComparer.OrdinalIgnoreCase).ToList()
        };
        AtomicFile.Write(project.ResolveContentPath(CatalogRelativePath),
            stream => JsonSerializer.Serialize(stream, document, JsonOptions));
    }

    private static void EnsureIdAvailable(IEnumerable<AssetRecord> records, Guid assetId, string sourcePath)
    {
        var conflict = records.FirstOrDefault(value => value.AssetId == assetId
            && !string.Equals(value.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (conflict is not null)
            throw new InvalidDataException(
                $"Project asset ID {assetId} is already registered for '{conflict.SourcePath}'.");
    }

    private static bool IsAssetPath(string? path) => !string.IsNullOrWhiteSpace(path)
        && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

    private static bool IsImportStagingPath(string path) => path.Split('/')
        .Any(segment => segment.Contains(".importing-", StringComparison.OrdinalIgnoreCase)
            || segment.Contains(".creating-", StringComparison.OrdinalIgnoreCase));

    private sealed class CatalogDocument
    {
        public int Version { get; set; }
        public List<AssetRecord>? Assets { get; set; }
    }

    private sealed record AssetRecord(Guid AssetId, string SourcePath);
}

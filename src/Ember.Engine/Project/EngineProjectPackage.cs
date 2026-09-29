using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ember.Scene;
using Ember.World;

namespace Ember.Project;

/// <summary>Creates a relocatable project folder containing its scenes, world manifests, extra content, and referenced GLBs.</summary>
public static class EngineProjectPackage
{
    public static EngineProjectPackageResult Create(string projectFilePath, string destinationDirectory)
    {
        var project = EngineProjectFile.Load(projectFilePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("A package destination directory is required.", nameof(destinationDirectory));

        var destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Package destination already exists: '{destination}'.");

        var contents = CollectPackageContent(project);
        var parentDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Package destination has no parent directory.");
        Directory.CreateDirectory(parentDirectory);

        var stagingDirectory = destination + $".staging-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var stagedProjectPath = Path.Combine(stagingDirectory, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(stagedProjectPath, project.StartupScenePath,
                project.WorldManifestPath, project.ExtraContentPaths);
            foreach (var file in contents.Files)
                CopyRelativeFile(file.FullPath, stagingDirectory, file.ProjectRelativePath);

            Directory.Move(stagingDirectory, destination);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }

        return new EngineProjectPackageResult(destination, project.StartupScenePath,
            contents.GlbAssetCount, project.WorldManifestPath, contents.Files.Count);
    }

    private static PackageContents CollectPackageContent(EngineProjectFile project)
    {
        var assetsById = new Dictionary<Guid, PackageAsset>();
        var filesByPath = new Dictionary<string, PackageFile>(StringComparer.OrdinalIgnoreCase);

        // 1. Startup scene if present
        if (project.StartupScenePath is not null)
        {
            var startupScenePath = project.ResolveStartupScenePath();
            var scene = SceneFile.Load(startupScenePath);
            AddPackageFile(filesByPath, project.StartupScenePath, startupScenePath);
            CollectSceneGlbAssets(project, scene, assetsById);
        }

        // 2. World manifest if present
        if (project.WorldManifestPath is not null)
        {
            var manifestPath = project.ResolveWorldManifestPath()!;
            var validation = WorldProjectValidator.Validate(manifestPath);
            if (!validation.IsValid)
                throw new InvalidDataException(
                    $"Cannot package project because world validation failed:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, validation.Diagnostics));

            var manifest = validation.Manifest
                ?? throw new InvalidDataException($"World validation did not load manifest '{manifestPath}'.");
            AddPackageFile(filesByPath, project.WorldManifestPath, manifestPath);

            foreach (var cell in manifest.Cells)
            {
                var cellSceneFullPath = manifest.ResolveScenePath(cell.Id);
                var cellSceneProjectRel = Path.GetRelativePath(project.RootDirectory, cellSceneFullPath).Replace('\\', '/');
                AddPackageFile(filesByPath, cellSceneProjectRel, cellSceneFullPath);

                var cellScene = SceneFile.Load(cellSceneFullPath);
                CollectSceneGlbAssets(project, cellScene, assetsById);
            }

            foreach (var pathFile in EnumerateWorldPathFiles(manifest.RootDirectory))
            {
                var rel = Path.GetRelativePath(project.RootDirectory, pathFile).Replace('\\', '/');
                AddPackageFile(filesByPath, rel, pathFile);
            }
        }

        // 3. Extra content if present
        foreach (var extra in project.ExtraContentPaths)
        {
            var fullPath = project.ResolveContentPath(extra);
            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(project.RootDirectory, file).Replace('\\', '/');
                    AddPackageFile(filesByPath, rel, file);
                }
            }
            else if (File.Exists(fullPath))
            {
                AddPackageFile(filesByPath, extra, fullPath);
            }
            else
            {
                throw new FileNotFoundException($"Extra content '{extra}' was not found at '{fullPath}'.", fullPath);
            }
        }

        // 4. For each collected GLB asset, inspect external URIs (buffers/images)
        foreach (var asset in assetsById.Values)
        {
            AddPackageFile(filesByPath, asset.Reference.SourcePath, asset.FullPath);
            foreach (var uri in ReadExternalUris(asset.FullPath, asset.Reference, asset.ObjectId))
            {
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var dependencyPath = ResolveExternalDependencyPath(project, asset, uri);
                AddPackageFile(filesByPath, dependencyPath.ProjectRelativePath, dependencyPath.FullPath);
            }
        }

        // 5. Optional notices
        const string noticesRelativePath = "ThirdPartyNotices.txt";
        var noticesPath = project.ResolveContentPath(noticesRelativePath);
        if (File.Exists(noticesPath))
            AddPackageFile(filesByPath, noticesRelativePath, noticesPath);

        var files = filesByPath.Values
            .OrderBy(file => file.ProjectRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new PackageContents(files, assetsById.Count);
    }

    private static void CollectSceneGlbAssets(
        EngineProjectFile project,
        SceneGraph scene,
        IDictionary<Guid, PackageAsset> assetsById)
    {
        foreach (var item in scene.Objects)
        foreach (var reference in EnumerateAssetReferences(item))
        {
            string fullPath;
            try
            {
                fullPath = project.ResolveContentPath(reference.SourcePath);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Scene object {item.Id} references invalid GLB asset {reference.AssetId} at project-relative path '{reference.SourcePath}': {exception.Message}",
                    exception);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Scene object {item.Id} references invalid GLB asset {reference.AssetId} at project-relative path '{reference.SourcePath}': {exception.Message}",
                    exception);
            }

            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    $"Scene object {item.Id} references missing GLB asset {reference.AssetId} at project-relative path '{reference.SourcePath}' resolved to '{fullPath}'.",
                    fullPath);

            if (assetsById.TryGetValue(reference.AssetId, out var existing)
                && !string.Equals(existing.Reference.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Scene object {item.Id} refers to GLB asset {reference.AssetId} at '{reference.SourcePath}', which conflicts with '{existing.Reference.SourcePath}'.");

            assetsById.TryAdd(reference.AssetId, new PackageAsset(reference, fullPath, item.Id));
        }
    }

    private static IEnumerable<GltfAssetReference> EnumerateAssetReferences(SceneObject item)
    {
        if (item.GltfAsset is { } asset) yield return asset;
        if (item.StaticMeshLod is { } lod)
        {
            yield return lod.NearAsset;
            yield return lod.FarAsset;
        }
    }

    internal static IReadOnlyList<string> ReadExternalUris(string assetPath, GltfAssetReference asset, Guid objectId)
    {
        try
        {
            using var stream = File.OpenRead(assetPath);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 12 || reader.ReadUInt32() != 0x46546C67 || reader.ReadUInt32() != 2)
                throw new InvalidDataException("GLB header is invalid or does not use version 2.");
            var declaredLength = reader.ReadUInt32();
            if (declaredLength != stream.Length)
                throw new InvalidDataException($"GLB header length {declaredLength} does not match file length {stream.Length}.");

            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 8)
                    throw new InvalidDataException("GLB contains a truncated chunk header.");
                var chunkLength = reader.ReadUInt32();
                var chunkType = reader.ReadUInt32();
                if (chunkLength > stream.Length - stream.Position)
                    throw new InvalidDataException("GLB contains a truncated chunk.");
                if (chunkType != 0x4E4F534A)
                {
                    stream.Position += chunkLength;
                    continue;
                }
                if (chunkLength > int.MaxValue)
                    throw new InvalidDataException("GLB JSON chunk is too large to inspect.");
                var jsonBytes = reader.ReadBytes((int)chunkLength);
                if (jsonBytes.Length != chunkLength)
                    throw new InvalidDataException("GLB JSON chunk is truncated.");

                using var document = JsonDocument.Parse(jsonBytes);
                var uris = new List<string>();
                ReadUris(document.RootElement, "buffers", uris);
                ReadUris(document.RootElement, "images", uris);
                return uris;
            }

            throw new InvalidDataException("GLB JSON chunk is missing.");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Scene object {objectId} references GLB asset {asset.AssetId} at '{asset.SourcePath}', but its dependencies could not be read: {exception.Message}",
                exception);
        }

        static void ReadUris(JsonElement root, string collectionName, ICollection<string> values)
        {
            if (!root.TryGetProperty(collectionName, out var collection) || collection.ValueKind != JsonValueKind.Array)
                return;
            foreach (var item in collection.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("uri", out var uriValue)
                    && uriValue.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(uriValue.GetString()))
                    values.Add(uriValue.GetString()!);
            }
        }
    }

    private static (string ProjectRelativePath, string FullPath) ResolveExternalDependencyPath(
        EngineProjectFile project,
        PackageAsset asset,
        string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out _)
            || uri.Contains("?", StringComparison.Ordinal)
            || uri.Contains("#", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Scene object {asset.ObjectId} references GLB asset {asset.Reference.AssetId} with unsupported external dependency URI '{uri}'.");

        string fullPath;
        try
        {
            var decodedPath = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(decodedPath))
                throw new InvalidDataException("External dependency path must be relative to its GLB.");
            fullPath = Path.GetFullPath(decodedPath, Path.GetDirectoryName(asset.FullPath)!);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"Scene object {asset.ObjectId} references GLB asset {asset.Reference.AssetId} with invalid external dependency URI '{uri}'.",
                exception);
        }

        var projectRelativePath = Path.GetRelativePath(project.RootDirectory, fullPath);
        if (Path.IsPathRooted(projectRelativePath)
            || projectRelativePath == ".."
            || projectRelativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || projectRelativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Scene object {asset.ObjectId} references GLB asset {asset.Reference.AssetId} with dependency URI '{uri}' outside project root '{project.RootDirectory}'.");

        projectRelativePath = projectRelativePath.Replace(Path.DirectorySeparatorChar, '/');
        fullPath = project.ResolveContentPath(projectRelativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Scene object {asset.ObjectId} references GLB asset {asset.Reference.AssetId} with missing dependency URI '{uri}' at project-relative path '{projectRelativePath}'.",
                fullPath);
        return (projectRelativePath, fullPath);
    }

    private static void AddPackageFile(IDictionary<string, PackageFile> files, string projectRelativePath, string fullPath)
    {
        var normalizedPath = projectRelativePath.Replace('\\', '/');
        if (files.TryGetValue(normalizedPath, out var existing))
        {
            if (!string.Equals(existing.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Different source files resolve to the same package content path '{normalizedPath}'.");
            return;
        }
        files.Add(normalizedPath, new PackageFile(normalizedPath, fullPath));
    }

    private static IEnumerable<string> EnumerateWorldPathFiles(string worldRoot)
    {
        foreach (var directoryName in new[] { "Paths", "Navigation" })
        {
            var directory = Path.Combine(worldRoot, directoryName);
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.paths.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                yield return path;

            var worldNetworkPath = Path.Combine(directory, "world-paths.json");
            if (File.Exists(worldNetworkPath)) yield return worldNetworkPath;
        }
    }

    private static void CopyRelativeFile(string sourcePath, string packageRoot, string projectRelativePath)
    {
        var destinationPath = Path.GetFullPath(projectRelativePath, packageRoot);
        var relativePath = Path.GetRelativePath(packageRoot, destinationPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Package content path '{projectRelativePath}' escapes package root.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath);
    }

    private sealed record PackageAsset(GltfAssetReference Reference, string FullPath, Guid ObjectId);
    private sealed record PackageFile(string ProjectRelativePath, string FullPath);
    private sealed record PackageContents(IReadOnlyList<PackageFile> Files, int GlbAssetCount);
}

public sealed record EngineProjectPackageResult(
    string DirectoryPath,
    string? StartupScenePath,
    int GlbAssetCount,
    string? WorldManifestPath = null,
    int PackagedFileCount = 0);

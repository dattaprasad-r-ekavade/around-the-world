using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ember.Assets;
using Ember.Sequence;
using Ember.Scene;
using Ember.World;
using SharpGLTF.Schema2;

namespace Ember.Project;

/// <summary>Creates a relocatable project folder containing its scenes, world manifests, extra content, and referenced GLBs.</summary>
public static class EngineProjectPackage
{
    /// <summary>Checks package dependencies without creating or modifying files.</summary>
    public static EngineProjectPackageValidationResult Validate(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        try
        {
            var project = EngineProjectFile.Load(projectFilePath);
            var contents = CollectPackageContent(project);
            if (contents.Diagnostics.Count > 0)
                return new EngineProjectPackageValidationResult(
                    null, null, null, null, null,
                    contents.Diagnostics.Select(diagnostic => diagnostic.Message).ToArray());
            return new EngineProjectPackageValidationResult(
                contents.SceneCount, contents.GlbAssetCount, contents.AudioAssetCount,
                contents.SequenceCount, contents.Files.Count, Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException
                                           or InvalidDataException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or InvalidOperationException
                                           or NotSupportedException)
        {
            return new EngineProjectPackageValidationResult(
                null, null, null, null, null, [exception.Message]);
        }
    }

    public static EngineProjectPackageResult Create(string projectFilePath, string destinationDirectory)
    {
        var project = EngineProjectFile.Load(projectFilePath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("A package destination directory is required.", nameof(destinationDirectory));

        var destination = Path.GetFullPath(destinationDirectory);
        if (IsWithinOrSame(project.RootDirectory, destination))
            throw new ArgumentException(
                $"Package destination must be outside the project directory '{project.RootDirectory}'.",
                nameof(destinationDirectory));
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Package destination already exists: '{destination}'.");

        var contents = CollectPackageContent(project);
        if (contents.Diagnostics.Count == 1)
            throw contents.Diagnostics[0].Exception;
        if (contents.Diagnostics.Count > 1)
            throw new InvalidDataException(
                $"Cannot package project because {contents.Diagnostics.Count} dependency errors were found:" +
                Environment.NewLine + string.Join(Environment.NewLine,
                    contents.Diagnostics.Select(diagnostic => $"- {diagnostic.Message}")));

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
            contents.GlbAssetCount, project.WorldManifestPath, contents.Files.Count,
            contents.SceneCount, contents.AudioAssetCount, contents.SequenceCount);
    }

    private static PackageContents CollectPackageContent(EngineProjectFile project)
    {
        var assetsById = new Dictionary<Guid, PackageAsset>();
        var audioPathsById = new Dictionary<Guid, string>();
        var filesByPath = new Dictionary<string, PackageFile>(StringComparer.OrdinalIgnoreCase);
        var scenesByPath = new Dictionary<string, SceneGraph>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<PackageDiagnostic>();

        // 1. Startup scene if present
        if (project.StartupScenePath is not null)
        {
            try
            {
                var startupScenePath = project.ResolveStartupScenePath();
                var scene = SceneFile.Load(startupScenePath);
                TryAddPackageFile(filesByPath, project.StartupScenePath, startupScenePath, diagnostics);
                scenesByPath[project.StartupScenePath] = scene;
                CollectSceneGlbAssets(project, scene, assetsById, diagnostics);
                CollectSceneAudioAssets(project, scene, filesByPath, audioPathsById, diagnostics);
            }
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                AddDiagnostic(diagnostics, exception);
            }
        }

        // 2. World manifest if present
        if (project.WorldManifestPath is not null)
        {
            try
            {
                var manifestPath = project.ResolveWorldManifestPath();
                var validation = WorldProjectValidator.Validate(manifestPath);
                foreach (var diagnostic in validation.Diagnostics)
                    AddDiagnostic(diagnostics, new InvalidDataException(diagnostic.ToString()));

                var manifest = validation.Manifest;
                if (manifest is null && validation.Diagnostics.Count == 0)
                    diagnostics.Add(new PackageDiagnostic(
                        $"World validation did not load manifest '{manifestPath}'.",
                        new InvalidDataException($"World validation did not load manifest '{manifestPath}'.")));
                else if (manifest is not null)
                {
                    TryAddPackageFile(filesByPath, project.WorldManifestPath, manifestPath, diagnostics);
                    foreach (var cell in manifest.Cells.OrderBy(value => value.Id))
                    {
                        if (!validation.Scenes.TryGetValue(cell.Id, out var cellScene)) continue;
                        try
                        {
                            var cellSceneFullPath = validation.SceneFiles[cell.Id];
                            var cellSceneProjectRel = Path.GetRelativePath(project.RootDirectory, cellSceneFullPath)
                                .Replace('\\', '/');
                            TryAddPackageFile(filesByPath, cellSceneProjectRel, cellSceneFullPath, diagnostics);
                            scenesByPath.TryAdd(cellSceneProjectRel, cellScene);
                            CollectSceneGlbAssets(project, cellScene, assetsById, diagnostics);
                            CollectSceneAudioAssets(project, cellScene, filesByPath, audioPathsById, diagnostics);
                        }
                        catch (Exception exception) when (IsPackageContentFailure(exception))
                        {
                            AddDiagnostic(diagnostics, exception);
                        }
                    }

                    try
                    {
                        foreach (var pathFile in EnumerateWorldPathFiles(manifest.RootDirectory))
                        {
                            var rel = Path.GetRelativePath(project.RootDirectory, pathFile).Replace('\\', '/');
                            TryAddPackageFile(filesByPath, rel, pathFile, diagnostics);
                        }
                    }
                    catch (Exception exception) when (IsPackageContentFailure(exception))
                    {
                        AddDiagnostic(diagnostics, exception);
                    }
                }
            }
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                AddDiagnostic(diagnostics, exception);
            }
        }

        // 3. Extra content if present
        foreach (var extra in project.ExtraContentPaths)
        {
            try
            {
                var fullPath = project.ResolveContentPath(extra);
                if (Directory.Exists(fullPath))
                {
                    foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(project.RootDirectory, file).Replace('\\', '/');
                        TryAddPackageFile(filesByPath, rel, file, diagnostics);
                    }
                }
                else if (File.Exists(fullPath))
                    TryAddPackageFile(filesByPath, extra, fullPath, diagnostics);
                else
                    AddDiagnostic(diagnostics, new FileNotFoundException(
                        $"Extra content '{extra}' was not found at '{fullPath}'.", fullPath));
            }
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                AddDiagnostic(diagnostics, exception);
            }
        }

        // 3. Audio imported under the project's standard audio folder is bundled automatically.
        try
        {
            foreach (var audioFile in EnumerateProjectAudioFiles(project))
            {
                var relativePath = Path.GetRelativePath(project.RootDirectory, audioFile).Replace('\\', '/');
                TryAddPackageFile(filesByPath, relativePath, audioFile, diagnostics);
            }
        }
        catch (Exception exception) when (IsPackageContentFailure(exception))
        {
            AddDiagnostic(diagnostics, exception);
        }

        // 4. Validate every registered sequence against the scenes and character assets being packaged.
        ValidateSequenceFiles(filesByPath.Values, scenesByPath, assetsById, diagnostics);

        // 5. For each collected GLB asset, inspect external URIs (buffers/images)
        foreach (var asset in assetsById.Values)
        {
            try
            {
                TryAddPackageFile(filesByPath, asset.Reference.SourcePath, asset.FullPath, diagnostics);
                foreach (var uri in ReadExternalUris(asset.FullPath, asset.Reference, asset.ObjectId))
                {
                    if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var dependencyPath = ResolveExternalDependencyPath(project, asset, uri);
                        TryAddPackageFile(filesByPath, dependencyPath.ProjectRelativePath,
                            dependencyPath.FullPath, diagnostics);
                    }
                    catch (Exception exception) when (IsPackageContentFailure(exception))
                    {
                        AddDiagnostic(diagnostics, exception);
                    }
                }
            }
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                AddDiagnostic(diagnostics, exception);
            }
        }

        // 6. Optional notices
        try
        {
            const string noticesRelativePath = "ThirdPartyNotices.txt";
            var noticesPath = project.ResolveContentPath(noticesRelativePath);
            if (File.Exists(noticesPath))
                TryAddPackageFile(filesByPath, noticesRelativePath, noticesPath, diagnostics);
        }
        catch (Exception exception) when (IsPackageContentFailure(exception))
        {
            AddDiagnostic(diagnostics, exception);
        }

        var files = filesByPath.Values
            .OrderBy(file => file.ProjectRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sequenceCount = files.Count(file =>
            file.ProjectRelativePath.EndsWith(".sequence.json", StringComparison.OrdinalIgnoreCase));
        return new PackageContents(files, scenesByPath.Count, assetsById.Count,
            audioPathsById.Count, sequenceCount, diagnostics
                .OrderBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ToArray());
    }

    private static IEnumerable<string> EnumerateProjectAudioFiles(EngineProjectFile project)
    {
        var audioDirectory = project.ResolveContentPath("Assets/Audio");
        if (!Directory.Exists(audioDirectory)) yield break;
        if ((File.GetAttributes(audioDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Project audio folder 'Assets/Audio' cannot be a symbolic link or junction.");

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(audioDirectory, "*", options))
            yield return file;
    }

    private static void CollectSceneAudioAssets(
        EngineProjectFile project,
        SceneGraph scene,
        IDictionary<string, PackageFile> filesByPath,
        IDictionary<Guid, string> pathsByAssetId,
        List<PackageDiagnostic> diagnostics)
    {
        foreach (var reference in scene.AudioAssets)
        {
            try
            {
                if (pathsByAssetId.TryGetValue(reference.AssetId, out var existingPath)
                    && !string.Equals(existingPath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                    AddDiagnostic(diagnostics, new InvalidDataException(
                        $"Audio asset ID {reference.AssetId} refers to both '{existingPath}' and '{reference.SourcePath}'."));
                pathsByAssetId.TryAdd(reference.AssetId, reference.SourcePath);

                string fullPath;
                try
                {
                    fullPath = project.ResolveContentPath(reference.SourcePath);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
                {
                    AddDiagnostic(diagnostics, new InvalidDataException(
                        $"Scene audio asset {reference.AssetId} has invalid project-relative path '{reference.SourcePath}': {exception.Message}",
                        exception));
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    AddDiagnostic(diagnostics, new FileNotFoundException(
                        $"Scene audio asset {reference.AssetId} is missing at project-relative path '{reference.SourcePath}'.",
                        fullPath));
                    continue;
                }
                if (HasReparsePointInPath(project.RootDirectory, fullPath))
                {
                    AddDiagnostic(diagnostics, new InvalidDataException(
                        $"Scene audio asset {reference.AssetId} at '{reference.SourcePath}' cannot be a symbolic link or junction."));
                    continue;
                }

                TryAddPackageFile(filesByPath, reference.SourcePath, fullPath, diagnostics);
            }
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                AddDiagnostic(diagnostics, new InvalidDataException(
                    $"Scene audio asset {reference.AssetId} at '{reference.SourcePath}' could not be checked: {exception.Message}",
                    exception));
            }
        }
    }

    private static bool HasReparsePointInPath(string projectRoot, string fullPath)
    {
        var currentPath = projectRoot;
        var relativePath = Path.GetRelativePath(projectRoot, fullPath);
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!File.Exists(currentPath) && !Directory.Exists(currentPath)) return false;
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static void ValidateSequenceFiles(
        IEnumerable<PackageFile> files,
        IReadOnlyDictionary<string, SceneGraph> scenesByPath,
        IReadOnlyDictionary<Guid, PackageAsset> assetsById,
        List<PackageDiagnostic> diagnostics)
    {
        var scenes = scenesByPath.ToArray();
        var clipCatalogByAssetId = new Dictionary<Guid, IReadOnlyList<GltfAnimationClipData>>();
        foreach (var file in files.Where(file =>
                     file.ProjectRelativePath.EndsWith(".sequence.json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                ValidateSequenceFile(file, scenes, assetsById, clipCatalogByAssetId);
            }
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                var message = exception.Message.Contains(file.ProjectRelativePath, StringComparison.OrdinalIgnoreCase)
                    ? exception.Message
                    : $"Cannot package sequence '{file.ProjectRelativePath}': {exception.Message}";
                AddDiagnostic(diagnostics, new InvalidDataException(message, exception));
            }
        }
    }

    private static void ValidateSequenceFile(
        PackageFile file,
        IReadOnlyList<KeyValuePair<string, SceneGraph>> scenes,
        IReadOnlyDictionary<Guid, PackageAsset> assetsById,
        IDictionary<Guid, IReadOnlyList<GltfAnimationClipData>> clipCatalogByAssetId)
    {
        SequenceFile.FileDependencies dependencies;
        try
        {
            dependencies = SequenceFile.ReadDependencies(file.FullPath);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"Cannot package sequence '{file.ProjectRelativePath}': {exception.Message}", exception);
        }

        var candidates = scenes.Where(scene =>
        {
            var hasTracks = dependencies.CharacterTracks.All(track =>
                scene.Value.Find(track.TargetObjectId)?.GltfAsset?.AssetId == track.AssetId);
            var hasTrigger = dependencies.TriggerObjectId is not { } triggerId
                || scene.Value.Find(triggerId) is not null;
            return hasTracks && hasTrigger;
        }).ToArray();

        if (dependencies.CharacterTracks.Count == 0 && dependencies.TriggerObjectId is null)
            candidates = scenes.Take(1).ToArray();

        if (candidates.Length == 0)
        {
            var missingTrack = dependencies.CharacterTracks.FirstOrDefault(track =>
                !scenes.Any(scene => scene.Value.Find(track.TargetObjectId) is not null));
            if (missingTrack is not null)
                throw new InvalidDataException(
                    $"Sequence '{file.ProjectRelativePath}' track {missingTrack.TrackId} refers to scene object {missingTrack.TargetObjectId}, which is not in any packaged scene.");

            var mismatchedTrack = dependencies.CharacterTracks.FirstOrDefault(track =>
                !scenes.Any(scene => scene.Value.Find(track.TargetObjectId)?.GltfAsset?.AssetId == track.AssetId));
            if (mismatchedTrack is not null)
                throw new InvalidDataException(
                    $"Sequence '{file.ProjectRelativePath}' track {mismatchedTrack.TrackId} expects asset {mismatchedTrack.AssetId} on scene object {mismatchedTrack.TargetObjectId}, but no packaged scene has that assignment.");

            if (dependencies.TriggerObjectId is { } missingTrigger
                && !scenes.Any(scene => scene.Value.Find(missingTrigger) is not null))
                throw new InvalidDataException(
                    $"Sequence '{file.ProjectRelativePath}' cutscene trigger {missingTrigger} is not in any packaged scene.");

            throw new InvalidDataException(
                $"Sequence '{file.ProjectRelativePath}' does not resolve all of its scene references in one packaged scene.");
        }

        if (candidates.Length > 1)
            throw new InvalidDataException(
                $"Sequence '{file.ProjectRelativePath}' is ambiguous because its scene object IDs match more than one packaged scene.");

        var clipsByAssetId = new Dictionary<Guid, IReadOnlyList<GltfAnimationClipData>>();
        foreach (var track in dependencies.CharacterTracks)
        {
            if (!assetsById.TryGetValue(track.AssetId, out var asset))
                throw new InvalidDataException(
                    $"Sequence '{file.ProjectRelativePath}' track {track.TrackId} refers to asset {track.AssetId}, which is not included by any packaged scene.");
            if (!clipCatalogByAssetId.TryGetValue(track.AssetId, out var clips))
            {
                try
                {
                    clips = GltfSkinnedCharacterData.Import(ModelRoot.Load(asset.FullPath)).Animations;
                    clipCatalogByAssetId.Add(track.AssetId, clips);
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        $"Sequence '{file.ProjectRelativePath}' track {track.TrackId} cannot load character asset '{asset.Reference.SourcePath}': {exception.Message}",
                        exception);
                }
            }
            clipsByAssetId[track.AssetId] = clips;
        }

        try
        {
            _ = SequenceFile.Load(file.FullPath, candidates[0].Value, clipsByAssetId);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"Cannot package sequence '{file.ProjectRelativePath}': {exception.Message}", exception);
        }
    }

    private static void CollectSceneGlbAssets(
        EngineProjectFile project,
        SceneGraph scene,
        IDictionary<Guid, PackageAsset> assetsById,
        List<PackageDiagnostic> diagnostics)
    {
        foreach (var item in scene.Objects)
        foreach (var reference in EnumerateAssetReferences(item))
        {
            try
            {
                var fullPath = project.ResolveContentPath(reference.SourcePath);

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
            catch (Exception exception) when (IsPackageContentFailure(exception))
            {
                AddDiagnostic(diagnostics, exception);
            }
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

    private static void TryAddPackageFile(IDictionary<string, PackageFile> files, string projectRelativePath,
        string fullPath, List<PackageDiagnostic> diagnostics)
    {
        try
        {
            AddPackageFile(files, projectRelativePath, fullPath);
        }
        catch (Exception exception) when (IsPackageContentFailure(exception))
        {
            AddDiagnostic(diagnostics, exception);
        }
    }

    private static void AddDiagnostic(List<PackageDiagnostic> diagnostics, Exception exception)
    {
        if (diagnostics.Any(diagnostic =>
                string.Equals(diagnostic.Message, exception.Message, StringComparison.Ordinal)))
            return;
        diagnostics.Add(new PackageDiagnostic(exception.Message, exception));
    }

    private static bool IsPackageContentFailure(Exception exception) =>
        exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or NotSupportedException or UriFormatException;

    private static bool IsWithinOrSame(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
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
    private sealed record PackageContents(IReadOnlyList<PackageFile> Files, int SceneCount,
        int GlbAssetCount, int AudioAssetCount, int SequenceCount,
        IReadOnlyList<PackageDiagnostic> Diagnostics);
    private sealed record PackageDiagnostic(string Message, Exception Exception);
}

public sealed record EngineProjectPackageResult(
    string DirectoryPath,
    string? StartupScenePath,
    int GlbAssetCount,
    string? WorldManifestPath = null,
    int PackagedFileCount = 0,
    int SceneCount = 0,
    int AudioAssetCount = 0,
    int SequenceCount = 0);

/// <summary>Read-only package preflight details for an editor or command-line publish flow.</summary>
public sealed class EngineProjectPackageValidationResult
{
    internal EngineProjectPackageValidationResult(int? sceneCount, int? glbAssetCount,
        int? audioAssetCount, int? sequenceCount, int? packagedFileCount,
        IReadOnlyList<string> diagnostics)
    {
        SceneCount = sceneCount;
        GlbAssetCount = glbAssetCount;
        AudioAssetCount = audioAssetCount;
        SequenceCount = sequenceCount;
        PackagedFileCount = packagedFileCount;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public int? SceneCount { get; }
    public int? GlbAssetCount { get; }
    /// <summary>Unique stable audio asset references across the packaged scenes.</summary>
    public int? AudioAssetCount { get; }
    public int? SequenceCount { get; }
    public int? PackagedFileCount { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>True when content dependency preflight found no blockers; destination validity is separate.</summary>
    public bool IsValid => Diagnostics.Count == 0;
}

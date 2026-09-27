using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ember.Scene;

namespace Ember.Project;

/// <summary>Creates starter projects and keeps the editor's ordered recent-project list.</summary>
public static class EngineProjectWorkspace
{
    private static readonly JsonSerializerOptions RecentProjectOptions = new() { WriteIndented = true };

    public static EngineProjectFile CreateEmpty(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
            throw new ArgumentException("A new project directory is required.", nameof(projectDirectory));

        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Project destination already exists: '{destination}'.");

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Project destination has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.creating-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "Assets"));
            var scenePath = Path.Combine(staging, "Scenes", "Main.json");
            SceneFile.SaveAtomic(new SceneGraph(), scenePath);
            EngineProjectFile.SaveAtomic(Path.Combine(staging, EngineProjectFile.DefaultFileName),
                "Scenes/Main.json");
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        return EngineProjectFile.Load(Path.Combine(destination, EngineProjectFile.DefaultFileName));
    }

    public static EngineProjectAssetImport ImportGlb(EngineProjectFile project, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A GLB source path is required.", nameof(sourcePath));
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath)) throw new FileNotFoundException("GLB source was not found.", fullSourcePath);
        if (!fullSourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .glb files can be imported.");

        var assetsDirectory = project.ResolveContentPath("Assets");
        Directory.CreateDirectory(assetsDirectory);
        var baseName = Path.GetFileNameWithoutExtension(fullSourcePath);
        var folderName = baseName;
        for (var suffix = 2; Directory.Exists(Path.Combine(assetsDirectory, folderName))
             || File.Exists(Path.Combine(assetsDirectory, folderName)); suffix++)
            folderName = $"{baseName}_{suffix}";

        var destinationDirectory = Path.Combine(assetsDirectory, folderName);
        var stagingDirectory = Path.Combine(assetsDirectory, $".{folderName}.importing-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var assetId = Guid.NewGuid();
            var importReference = new GltfAssetReference(assetId, "Assets/import.glb");
            var externalUris = EngineProjectPackage.ReadExternalUris(fullSourcePath, importReference, Guid.Empty);
            var sourceDirectory = Path.GetDirectoryName(fullSourcePath)!;
            var dependencies = new List<string>();
            foreach (var uri in externalUris)
            {
                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (uri.Contains('?') || uri.Contains('#')
                    || Uri.TryCreate(uri, UriKind.Absolute, out _)
                    || Path.IsPathRooted(uri)
                    || uri.StartsWith("\\\\", StringComparison.Ordinal))
                    throw new InvalidDataException($"GLB dependency URI '{uri}' must be a local relative path.");

                var decoded = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
                var dependencyPath = Path.GetFullPath(decoded, sourceDirectory);
                if (!File.Exists(dependencyPath))
                    throw new FileNotFoundException($"GLB dependency '{uri}' was not found beside '{fullSourcePath}'.", dependencyPath);
                if (!dependencies.Contains(dependencyPath, StringComparer.OrdinalIgnoreCase))
                    dependencies.Add(dependencyPath);
            }

            var commonRoot = FindCommonDirectory(sourceDirectory, dependencies);
            var files = dependencies.Append(fullSourcePath).Distinct(StringComparer.OrdinalIgnoreCase);
            var relativeAssetPath = string.Empty;
            foreach (var sourceFile in files)
            {
                var relative = Path.GetRelativePath(commonRoot, sourceFile);
                if (Path.IsPathRooted(relative) || relative == ".."
                    || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"GLB dependency '{sourceFile}' cannot be represented inside the project.");

                var stagedFile = Path.GetFullPath(relative, stagingDirectory);
                var stagedRelative = Path.GetRelativePath(stagingDirectory, stagedFile);
                if (Path.IsPathRooted(stagedRelative) || stagedRelative == ".."
                    || stagedRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || stagedRelative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException("GLB import dependency escapes its staging directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(stagedFile)!);
                File.Copy(sourceFile, stagedFile);
                if (string.Equals(sourceFile, fullSourcePath, StringComparison.OrdinalIgnoreCase))
                    relativeAssetPath = Path.Combine("Assets", folderName, relative).Replace('\\', '/');
            }

            Directory.Move(stagingDirectory, destinationDirectory);
            var reference = new GltfAssetReference(assetId, relativeAssetPath);
            return new EngineProjectAssetImport(reference, destinationDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static IReadOnlyList<string> LoadRecent(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            throw new ArgumentException("Recent-project store path is required.", nameof(storePath));
        if (!File.Exists(storePath)) return Array.Empty<string>();

        try
        {
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(storePath), RecentProjectOptions)
                ?? throw new InvalidDataException("Recent-project list is empty.");
            return paths.Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Recent-project list is invalid: {exception.Message}", exception);
        }
    }

    public static void RecordRecent(string storePath, string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            throw new ArgumentException("Recent-project store path is required.", nameof(storePath));
        if (string.IsNullOrWhiteSpace(projectFilePath))
            throw new ArgumentException("Project file path is required.", nameof(projectFilePath));

        var projectPath = Path.GetFullPath(projectFilePath);
        var paths = LoadRecent(storePath)
            .Where(path => !string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(projectPath)
            .Take(12)
            .ToArray();
        var fullStorePath = Path.GetFullPath(storePath);
        var directory = Path.GetDirectoryName(fullStorePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        Ember.IO.AtomicFile.Write(fullStorePath,
            stream => JsonSerializer.Serialize(stream, paths, RecentProjectOptions));
    }

    private static string FindCommonDirectory(string sourceDirectory, IReadOnlyList<string> dependencies)
    {
        var current = new DirectoryInfo(sourceDirectory);
        while (dependencies.Any(path => !IsWithinOrSame(current.FullName, path)))
            current = current.Parent
                ?? throw new InvalidDataException("GLB dependency is on a different volume from its source file.");
        return current.FullName;
    }

    private static bool IsWithinOrSame(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}

/// <summary>Imported project files are removed unless the caller validates and commits the asset.</summary>
public sealed class EngineProjectAssetImport : IDisposable
{
    private readonly string _assetDirectory;
    private bool _committed;

    internal EngineProjectAssetImport(GltfAssetReference reference, string assetDirectory)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _assetDirectory = assetDirectory ?? throw new ArgumentNullException(nameof(assetDirectory));
    }

    public GltfAssetReference Reference { get; }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        if (_committed) return;
        if (Directory.Exists(_assetDirectory)) Directory.Delete(_assetDirectory, recursive: true);
    }
}

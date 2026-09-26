using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;

namespace Ember.Authoring;

public sealed record AuthoredProjectRecoveryStaging(
    string ProjectRoot,
    string StagingRoot,
    string WorldManifestPath,
    string RpgContentPath,
    IReadOnlyList<string> RestoredFiles,
    Guid SnapshotId);

/// <summary>Captures authored project files and restores a complete snapshot into an isolated staging directory.</summary>
public static class AuthoredProjectRecoveryService
{
    public static string GetProjectRoot(string worldManifestPath, string rpgContentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpgContentPath);
        return FindCommonRoot(Path.GetDirectoryName(Path.GetFullPath(worldManifestPath))!,
            Path.GetDirectoryName(Path.GetFullPath(rpgContentPath))!);
    }

    public static string GetDefaultRecoveryDirectory(string worldManifestPath, string rpgContentPath) =>
        AuthoredContentRecoveryStore.GetDefaultStorageDirectory(GetProjectRoot(worldManifestPath, rpgContentPath));

    public static AuthoredRecoverySnapshot Capture(string worldManifestPath, string rpgContentPath,
        string recoveryDirectory, DateTimeOffset? capturedUtc = null,
        string? currentScenePath = null, byte[]? currentSceneJson = null, byte[]? currentRpgContentJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpgContentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);

        var fullManifestPath = Path.GetFullPath(worldManifestPath);
        var fullContentPath = Path.GetFullPath(rpgContentPath);
        var projectRoot = GetProjectRoot(fullManifestPath, fullContentPath);
        var world = WorldManifest.LoadForValidation(fullManifestPath);
        if ((currentScenePath is null) != (currentSceneJson is null))
            throw new ArgumentException("Current scene path and JSON must be provided together.");
        var fullCurrentScenePath = currentScenePath is null ? null : Path.GetFullPath(currentScenePath);
        var scenePaths = world.Cells.Select(cell => Path.GetFullPath(world.ResolveScenePath(cell.Id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (fullCurrentScenePath is not null && !scenePaths.Contains(fullCurrentScenePath))
            throw new InvalidDataException("The current scene is not part of the authored world manifest.");
        var files = new Dictionary<string, AuthoredRecoveryFile>(StringComparer.OrdinalIgnoreCase);

        AddFile(fullManifestPath);
        foreach (var cell in world.Cells.OrderBy(cell => cell.Id))
        {
            var scenePath = world.ResolveScenePath(cell.Id);
            AddFile(scenePath, string.Equals(Path.GetFullPath(scenePath), fullCurrentScenePath,
                    StringComparison.OrdinalIgnoreCase) ? currentSceneJson : null);
        }
        AddFile(fullContentPath, currentRpgContentJson);

        var pathDirectory = Path.Combine(world.RootDirectory, "Paths");
        if (Directory.Exists(pathDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(pathDirectory, "*.paths.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                AddFile(path);

            var worldPathsPath = Path.Combine(pathDirectory, "world-paths.json");
            if (File.Exists(worldPathsPath)) AddFile(worldPathsPath);
        }

        return AuthoredContentRecoveryStore.SaveLatest(projectRoot, recoveryDirectory, files.Values, capturedUtc);

        void AddFile(string path, byte[]? inMemoryContent = null)
        {
            var fullPath = Path.GetFullPath(path);
            if (inMemoryContent is null && !File.Exists(fullPath))
                throw new FileNotFoundException($"Authored project snapshot is incomplete; file '{fullPath}' is missing.", fullPath);
            var relativePath = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
            var content = inMemoryContent is null ? File.ReadAllBytes(fullPath) : (byte[])inMemoryContent.Clone();
            if (!files.TryAdd(relativePath, new AuthoredRecoveryFile(relativePath, content)))
                throw new InvalidDataException($"Authored project snapshot repeats file '{relativePath}'.");
        }
    }

    public static AuthoredProjectRecoveryStaging RestoreLatestToStaging(string worldManifestPath,
        string rpgContentPath, string recoveryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rpgContentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);

        var fullManifestPath = Path.GetFullPath(worldManifestPath);
        var fullContentPath = Path.GetFullPath(rpgContentPath);
        var projectRoot = GetProjectRoot(fullManifestPath, fullContentPath);
        var snapshot = AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory);
        var manifestRelativePath = RelativePath(projectRoot, fullManifestPath);
        var contentRelativePath = RelativePath(projectRoot, fullContentPath);
        var availablePaths = new HashSet<string>(snapshot.Files.Select(file => file.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        if (!availablePaths.Contains(manifestRelativePath) || !availablePaths.Contains(contentRelativePath))
            throw new InvalidDataException("Authored recovery snapshot is missing the world manifest or RPG content file.");

        var stagingParent = Path.Combine(Path.GetFullPath(recoveryDirectory), "staging");
        Directory.CreateDirectory(stagingParent);
        var stagingRoot = Path.Combine(stagingParent,
            $"{snapshot.SnapshotId:N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        var restoredPaths = new List<string>(snapshot.Files.Count);
        try
        {
            foreach (var file in snapshot.Files)
            {
                var destination = Path.GetFullPath(Path.Combine(stagingRoot,
                    file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                EnsureWithin(stagingRoot, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(file.Content);
                stream.Flush(flushToDisk: true);
                restoredPaths.Add(destination);
            }
        }
        catch
        {
            Directory.Delete(stagingRoot, recursive: true);
            throw;
        }

        return new AuthoredProjectRecoveryStaging(projectRoot, stagingRoot,
            Path.Combine(stagingRoot, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(stagingRoot, contentRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            restoredPaths.AsReadOnly(), snapshot.SnapshotId);
    }

    /// <summary>Validates and atomically applies a reviewed snapshot's authored files, rolling back on write failure.</summary>
    public static void ApplyValidatedStaging(AuthoredProjectRecoveryStaging staging)
    {
        ArgumentNullException.ThrowIfNull(staging);
        var projectRoot = Path.GetFullPath(staging.ProjectRoot);
        var stagingRoot = Path.GetFullPath(staging.StagingRoot);
        EnsureWithin(stagingRoot, Path.GetFullPath(staging.WorldManifestPath));
        EnsureWithin(stagingRoot, Path.GetFullPath(staging.RpgContentPath));
        if (IsWithinOrSame(projectRoot, stagingRoot))
            throw new InvalidDataException("Recovery staging must remain outside the project root.");

        var validation = AuthoredProjectValidator.Validate(staging.WorldManifestPath, staging.RpgContentPath);
        if (validation.Diagnostics.Count > 0)
            throw new InvalidDataException("Recovery cannot be applied until project validation passes: "
                + string.Join(" ", validation.Diagnostics));

        var world = WorldManifest.LoadForValidation(staging.WorldManifestPath);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RelativeToRoot(stagingRoot, staging.WorldManifestPath),
            RelativeToRoot(stagingRoot, staging.RpgContentPath)
        };
        foreach (var cell in world.Cells)
            expected.Add(RelativeToRoot(stagingRoot, world.ResolveScenePath(cell.Id)));

        var pathDirectory = Path.Combine(world.RootDirectory, "Paths");
        if (Directory.Exists(pathDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(pathDirectory, "*.paths.json", SearchOption.TopDirectoryOnly))
                expected.Add(RelativeToRoot(stagingRoot, path));
            var worldPathsPath = Path.Combine(pathDirectory, "world-paths.json");
            if (File.Exists(worldPathsPath)) expected.Add(RelativeToRoot(stagingRoot, worldPathsPath));
        }

        var stagedFiles = staging.RestoredFiles.Select(Path.GetFullPath).ToArray();
        if (stagedFiles.Length == 0 || stagedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != stagedFiles.Length)
            throw new InvalidDataException("Recovery staging has an empty or duplicate file list.");

        var plan = new List<ApplyFile>(stagedFiles.Length);
        foreach (var stagedPath in stagedFiles)
        {
            EnsureWithin(stagingRoot, stagedPath);
            if (!File.Exists(stagedPath))
                throw new FileNotFoundException("A staged recovery file is missing.", stagedPath);
            var relative = RelativeToRoot(stagingRoot, stagedPath);
            if (!expected.Contains(relative))
                throw new InvalidDataException($"Recovery staging contains a file outside the authored project allowlist: '{relative}'.");
            var destination = Path.GetFullPath(Path.Combine(projectRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            EnsureWithin(projectRoot, destination);
            plan.Add(new ApplyFile(destination, File.ReadAllBytes(stagedPath),
                File.Exists(destination) ? File.ReadAllBytes(destination) : null));
        }

        foreach (var required in expected)
            if (!stagedFiles.Any(path => string.Equals(RelativeToRoot(stagingRoot, path), required,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Recovery staging is missing authored file '{required}'.");

        var prepared = new List<PreparedApplyFile>(plan.Count);
        try
        {
            foreach (var file in plan)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
                var temporary = file.Destination + $".{Guid.NewGuid():N}.recovery.tmp";
                WriteDurably(temporary, file.Content);
                prepared.Add(new PreparedApplyFile(file, temporary));
            }

            var applied = new List<ApplyFile>();
            try
            {
                foreach (var file in prepared)
                {
                    if (File.Exists(file.Source.Destination)) File.Replace(file.TemporaryPath, file.Source.Destination, null);
                    else File.Move(file.TemporaryPath, file.Source.Destination);
                    applied.Add(file.Source);
                }
            }
            catch (Exception applyException)
            {
                var rollbackErrors = new List<Exception>();
                foreach (var file in applied.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (file.PreviousContent is null) File.Delete(file.Destination);
                        else
                        {
                            var rollbackPath = file.Destination + $".{Guid.NewGuid():N}.rollback.tmp";
                            WriteDurably(rollbackPath, file.PreviousContent);
                            if (File.Exists(file.Destination)) File.Replace(rollbackPath, file.Destination, null);
                            else File.Move(rollbackPath, file.Destination);
                        }
                    }
                    catch (Exception rollbackException) { rollbackErrors.Add(rollbackException); }
                }
                if (rollbackErrors.Count > 0)
                    throw new AggregateException("Recovery apply failed and one or more source files could not be rolled back.",
                        [applyException, .. rollbackErrors]);
                throw new IOException("Recovery apply failed; source files were rolled back.", applyException);
            }
        }
        finally
        {
            foreach (var file in prepared)
                if (File.Exists(file.TemporaryPath)) File.Delete(file.TemporaryPath);
        }
    }

    private static string FindCommonRoot(string firstDirectory, string secondDirectory)
    {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstDirectory));
        var other = Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondDirectory));
        while (!IsWithinOrSame(candidate, other))
        {
            var parent = Directory.GetParent(candidate)?.FullName;
            if (parent is null || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("World manifest and RPG content must be inside one project root.");
            candidate = Path.TrimEndingDirectorySeparator(parent);
        }
        return candidate;
    }

    private static string RelativePath(string projectRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException("Authored file path is outside the project root.");
        return relative;
    }

    private static string RelativeToRoot(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(fullPath)).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException("Authored file path is outside the project root.");
        return relative;
    }

    private static bool IsWithinOrSame(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void EnsureWithin(string root, string path)
    {
        if (!IsWithinOrSame(root, path) || string.Equals(Path.GetFullPath(root), Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Authored recovery staging path escapes its staging directory.");
    }

    private static void WriteDurably(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private sealed record ApplyFile(string Destination, byte[] Content, byte[]? PreviousContent);
    private sealed record PreparedApplyFile(ApplyFile Source, string TemporaryPath);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.Project;

namespace Ember.Authoring;

public sealed record MissionProjectCheckpoint(Guid CheckpointId, string MissionId,
    string CheckpointPath, DateTimeOffset CapturedUtc, int FileCount, long TotalBytes);

public sealed record MissionProjectRewindResult(string ProjectFilePath, Guid CheckpointId,
    string? RetainedBackupPath);

/// <summary>Captures and restores a mission's authored project tree while preserving learner progress.</summary>
public static class MissionProjectCheckpointStore
{
    public const int CurrentVersion = 1;
    private const string ManifestEntryName = ".ember-mission-checkpoint.json";
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const int MaximumFiles = 100_000;

    private static readonly HashSet<string> PreservedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".vscode", "bin", "obj"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string GetDefaultCheckpointPath(EngineProjectFile project, string missionId)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateMissionId(missionId);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("The local application-data directory is unavailable.");

        var projectRoot = NormalizeRoot(project.RootDirectory);
        var projectKey = CreateProjectKey(projectRoot);
        return Path.Combine(localApplicationData, "Ember", "MissionCheckpoints", projectKey,
            missionId + ".checkpoint.zip");
    }

    /// <summary>Writes an immutable checkpoint archive, replacing an older checkpoint only on success.</summary>
    public static MissionProjectCheckpoint Capture(EngineProjectFile project, string missionId,
        string checkpointPath, DateTimeOffset? capturedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateMissionId(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);

        var currentProject = EngineProjectFile.Load(project.FilePath);
        var projectRoot = NormalizeRoot(currentProject.RootDirectory);
        var fullCheckpointPath = Path.GetFullPath(checkpointPath);
        EnsureOutsideProject(projectRoot, fullCheckpointPath);
        if (Directory.Exists(fullCheckpointPath))
            throw new IOException($"Mission checkpoint path is a directory: '{fullCheckpointPath}'.");

        var projectFiles = EnumerateAuthoredFiles(projectRoot);
        if (projectFiles.Count > MaximumFiles)
            throw new InvalidDataException($"Mission checkpoint exceeds the supported limit of {MaximumFiles} files.");
        var projectRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(projectRoot, currentProject.FilePath), "project file");
        if (!projectFiles.Any(file => string.Equals(file.RelativePath, projectRelativePath,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Mission checkpoint did not include the project file.");

        var checkpointId = Guid.NewGuid();
        var timestamp = (capturedUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (timestamp == default)
            throw new ArgumentOutOfRangeException(nameof(capturedUtc), "Mission checkpoint time cannot be empty.");
        var projectKey = CreateProjectKey(projectRoot);
        var manifestFiles = new List<CheckpointFileDocument>(projectFiles.Count);
        AtomicFile.Write(fullCheckpointPath, output =>
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var projectFile in projectFiles)
            {
                EnsureNotReparsePoint(projectFile.FullPath, "project file");
                var entry = archive.CreateEntry(projectFile.RelativePath, CompressionLevel.Fastest);
                using var source = new FileStream(projectFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var destination = entry.Open();
                var (length, checksum) = CopyAndHash(source, destination);
                manifestFiles.Add(new CheckpointFileDocument
                {
                    RelativePath = projectFile.RelativePath,
                    Length = length,
                    Sha256 = checksum
                });
            }

            var document = new CheckpointDocument
            {
                Version = CurrentVersion,
                CheckpointId = checkpointId,
                MissionId = missionId,
                ProjectKey = projectKey,
                ProjectFileRelativePath = projectRelativePath,
                CapturedUtc = timestamp,
                Files = manifestFiles
            };
            using var manifest = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal).Open();
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (manifestBytes.Length > MaximumManifestBytes)
                throw new InvalidDataException("Mission checkpoint manifest exceeds the supported size limit.");
            manifest.Write(manifestBytes, 0, manifestBytes.Length);
        });

        var totalBytes = manifestFiles.Aggregate(0L, (total, file) => checked(total + file.Length));
        return new MissionProjectCheckpoint(checkpointId, missionId, fullCheckpointPath,
            timestamp, manifestFiles.Count, totalBytes);
    }

    /// <summary>
    /// Restores the checkpoint through a sibling staging directory. Project progress and current
    /// version-control/editor metadata are carried forward; a failed restore leaves the live tree in place.
    /// </summary>
    public static MissionProjectRewindResult Restore(EngineProjectFile project, string missionId,
        string checkpointPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateMissionId(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointPath);

        var projectFilePath = Path.GetFullPath(project.FilePath);
        var projectRoot = NormalizeRoot(project.RootDirectory);
        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Mission project directory was not found: '{projectRoot}'.");
        var fullCheckpointPath = Path.GetFullPath(checkpointPath);
        EnsureOutsideProject(projectRoot, fullCheckpointPath);
        if (!File.Exists(fullCheckpointPath))
            throw new FileNotFoundException("Mission checkpoint was not found.", fullCheckpointPath);

        var parent = Path.GetDirectoryName(projectRoot)
            ?? throw new InvalidDataException("Mission project directory has no parent directory.");
        var rootName = Path.GetFileName(projectRoot);
        if (string.IsNullOrWhiteSpace(rootName))
            throw new InvalidDataException("Mission rewind cannot replace a volume root.");
        var token = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(parent, $".{rootName}.mission-rewind-{token}");
        var backupRoot = Path.Combine(parent, $".{rootName}.mission-backup-{token}");

        try
        {
            using var checkpoint = OpenValidatedCheckpoint(fullCheckpointPath, projectRoot, missionId);
            Directory.CreateDirectory(stagingRoot);
            ExtractCheckpoint(checkpoint, stagingRoot);
            PreserveLearningProgress(projectRoot, stagingRoot);
            PreserveRootDirectories(projectRoot, stagingRoot);

            var relativeProjectFile = NormalizeRelativePath(
                Path.GetRelativePath(projectRoot, projectFilePath), "project file");
            if (!string.Equals(relativeProjectFile, checkpoint.Document.ProjectFileRelativePath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Mission checkpoint uses a different project file path.");
            var stagedProjectFile = ResolveWithin(stagingRoot, relativeProjectFile);
            _ = EngineProjectFile.Load(stagedProjectFile);

            var originalMoved = false;
            try
            {
                Directory.Move(projectRoot, backupRoot);
                originalMoved = true;
                Directory.Move(stagingRoot, projectRoot);
            }
            catch (Exception applyError)
            {
                if (originalMoved && !Directory.Exists(projectRoot) && Directory.Exists(backupRoot))
                {
                    try
                    {
                        Directory.Move(backupRoot, projectRoot);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new AggregateException(
                            $"Mission rewind failed; the original project is preserved at '{backupRoot}'.",
                            applyError, rollbackError);
                    }
                }
                throw;
            }

            string? retainedBackupPath = null;
            try
            {
                Directory.Delete(backupRoot, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                retainedBackupPath = backupRoot;
            }

            return new MissionProjectRewindResult(projectFilePath,
                checkpoint.Document.CheckpointId, retainedBackupPath);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static List<ProjectFileSource> EnumerateAuthoredFiles(string projectRoot)
    {
        var files = new List<ProjectFileSource>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(projectRoot);
        while (pendingDirectories.TryPop(out var directory))
        {
            EnsureNotReparsePoint(directory, "project directory");
            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                if (string.Equals(directory, projectRoot, StringComparison.OrdinalIgnoreCase)
                    && PreservedRootDirectories.Contains(Path.GetFileName(childDirectory)))
                {
                    EnsureNotReparsePoint(childDirectory, "preserved project metadata directory");
                    continue;
                }
                EnsureNotReparsePoint(childDirectory, "project directory");
                pendingDirectories.Push(childDirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(directory, projectRoot, StringComparison.OrdinalIgnoreCase)
                    && PreservedRootDirectories.Contains(Path.GetFileName(file)))
                {
                    EnsureNotReparsePoint(file, "preserved project metadata file");
                    continue;
                }
                EnsureNotReparsePoint(file, "project file");
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(projectRoot, file), "project file");
                if (string.Equals(relativePath, ProjectLearningProgressStore.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(relativePath, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Project file '{relativePath}' uses a reserved mission checkpoint name.");
                files.Add(new ProjectFileSource(relativePath, file));
            }
        }

        return files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExtractCheckpoint(ValidatedCheckpoint checkpoint, string stagingRoot)
    {
        foreach (var file in checkpoint.Document.Files!)
        {
            var entry = checkpoint.Entries[file.RelativePath];
            var destinationPath = ResolveWithin(stagingRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var source = entry.Open();
            using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.WriteThrough);
            var (length, checksum) = CopyAndHash(source, destination);
            destination.Flush(flushToDisk: true);
            if (length != file.Length || !string.Equals(checksum, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Mission checkpoint content failed validation for '{file.RelativePath}'.");
        }
    }

    private static ValidatedCheckpoint OpenValidatedCheckpoint(string checkpointPath, string projectRoot,
        string missionId)
    {
        var archive = new ZipArchive(new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            ZipArchiveMode.Read, leaveOpen: false);
        try
        {
            var manifestEntries = archive.Entries.Where(entry =>
                string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal)).ToArray();
            if (manifestEntries.Length != 1 || manifestEntries[0].Length > MaximumManifestBytes)
                throw new InvalidDataException("Mission checkpoint manifest is missing, duplicated or too large.");

            CheckpointDocument document;
            try
            {
                using var stream = manifestEntries[0].Open();
                document = JsonSerializer.Deserialize<CheckpointDocument>(stream, JsonOptions)
                    ?? throw new InvalidDataException("Mission checkpoint manifest is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Mission checkpoint manifest is invalid: {exception.Message}", exception);
            }

            ValidateCheckpointDocument(document, projectRoot, missionId);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (string.Equals(entry.FullName, ManifestEntryName, StringComparison.Ordinal)) continue;
                var relativePath = NormalizeRelativePath(entry.FullName, "checkpoint entry");
                if (IsPreservedPath(relativePath))
                    throw new InvalidDataException($"Mission checkpoint contains preserved project data '{relativePath}'.");
                if (!entries.TryAdd(relativePath, entry))
                    throw new InvalidDataException($"Mission checkpoint repeats file '{relativePath}'.");
            }

            ValidateFilePathSet(document.Files!);
            if (entries.Count != document.Files!.Count)
                throw new InvalidDataException("Mission checkpoint file list does not match its archive entries.");
            foreach (var file in document.Files)
            {
                if (!entries.TryGetValue(file.RelativePath, out var entry) || entry.Length != file.Length)
                    throw new InvalidDataException($"Mission checkpoint is missing or has an invalid entry for '{file.RelativePath}'.");
            }

            return new ValidatedCheckpoint(archive, document, entries);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    private static void ValidateCheckpointDocument(CheckpointDocument document, string projectRoot,
        string missionId)
    {
        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported mission checkpoint version {document.Version}; expected {CurrentVersion}.");
        if (document.CheckpointId == Guid.Empty || document.CapturedUtc == default)
            throw new InvalidDataException("Mission checkpoint identity or capture time is invalid.");
        if (!string.Equals(document.MissionId, missionId, StringComparison.Ordinal))
            throw new InvalidDataException($"Mission checkpoint belongs to '{document.MissionId}', not '{missionId}'.");
        if (!string.Equals(document.ProjectKey, CreateProjectKey(projectRoot), StringComparison.Ordinal))
            throw new InvalidDataException("Mission checkpoint belongs to a different project directory.");
        if (document.Files is null || document.Files.Count == 0 || document.Files.Count > MaximumFiles)
            throw new InvalidDataException("Mission checkpoint file list is empty or exceeds the supported limit.");

        foreach (var file in document.Files)
        {
            if (file is null || file.Length < 0)
                throw new InvalidDataException("Mission checkpoint contains an invalid file entry.");
            file.RelativePath = NormalizeRelativePath(file.RelativePath, "checkpoint file");
            if (IsPreservedPath(file.RelativePath))
                throw new InvalidDataException($"Mission checkpoint contains preserved project data '{file.RelativePath}'.");
            if (string.Equals(file.RelativePath, ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.RelativePath, ProjectLearningProgressStore.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Mission checkpoint contains reserved project data '{file.RelativePath}'.");
            try
            {
                if (Convert.FromHexString(file.Sha256 ?? string.Empty).Length != 32)
                    throw new FormatException();
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Mission checkpoint checksum is invalid for '{file.RelativePath}'.", exception);
            }
        }

        document.ProjectFileRelativePath = NormalizeRelativePath(document.ProjectFileRelativePath, "project file");
        if (!document.Files.Any(file => string.Equals(file.RelativePath,
                document.ProjectFileRelativePath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Mission checkpoint does not contain its project file.");
    }

    private static void PreserveLearningProgress(string projectRoot, string stagingRoot)
    {
        var source = Path.Combine(projectRoot,
            ProjectLearningProgressStore.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var progressDirectory = Path.GetDirectoryName(source)!;
        if (Directory.Exists(progressDirectory))
            EnsureNotReparsePoint(progressDirectory, "learning progress directory");
        if (Directory.Exists(source))
            throw new InvalidDataException("Project learning progress path is a directory and cannot be preserved.");
        if (!File.Exists(source)) return;
        EnsureNotReparsePoint(source, "learning progress file");
        var destination = ResolveWithin(stagingRoot, ProjectLearningProgressStore.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static void PreserveRootDirectories(string projectRoot, string stagingRoot)
    {
        foreach (var name in PreservedRootDirectories)
        {
            var source = Path.Combine(projectRoot, name);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, Path.Combine(stagingRoot, name));
            }
            else if (File.Exists(source))
            {
                EnsureNotReparsePoint(source, "preserved project metadata file");
                File.Copy(source, Path.Combine(stagingRoot, name), overwrite: false);
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        EnsureNotReparsePoint(sourceDirectory, "preserved project metadata directory");
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            EnsureNotReparsePoint(file, "preserved project metadata file");
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
    }

    private static bool IsPreservedPath(string relativePath)
    {
        var firstSeparator = relativePath.IndexOf('/');
        var firstSegment = firstSeparator < 0 ? relativePath : relativePath[..firstSeparator];
        return PreservedRootDirectories.Contains(firstSegment);
    }

    private static void ValidateFilePathSet(IReadOnlyList<CheckpointFileDocument> files)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstDescendantByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var segments = file.RelativePath.Split('/');
            for (var index = 1; index < segments.Length; index++)
            {
                var ancestor = string.Join('/', segments.Take(index));
                if (seenPaths.Contains(ancestor))
                    throw new InvalidDataException(
                        $"Mission checkpoint paths '{file.RelativePath}' and '{ancestor}' overlap as a file and directory.");
                firstDescendantByPath.TryAdd(ancestor, file.RelativePath);
            }
            if (firstDescendantByPath.TryGetValue(file.RelativePath, out var descendant))
                throw new InvalidDataException(
                    $"Mission checkpoint paths '{file.RelativePath}' and '{descendant}' overlap as a file and directory.");
            if (!seenPaths.Add(file.RelativePath))
                throw new InvalidDataException($"Mission checkpoint repeats file '{file.RelativePath}'.");
        }
    }

    private static string NormalizeRelativePath(string? path, string context)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException($"Mission checkpoint {context} path is empty.");
        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized)
            || normalized.Contains(':'))
            throw new InvalidDataException($"Mission checkpoint {context} path must be project-relative.");
        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new InvalidDataException($"Mission checkpoint {context} path contains an unsafe segment.");
        return string.Join('/', segments);
    }

    private static string ResolveWithin(string root, string relativePath)
    {
        var fullRoot = NormalizeRoot(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException($"Mission checkpoint path '{relativePath}' escapes its destination.");
        return fullPath;
    }

    private static void EnsureOutsideProject(string projectRoot, string path)
    {
        var relative = Path.GetRelativePath(projectRoot, path);
        if (!Path.IsPathRooted(relative) && (relative == "."
            || relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ArgumentException("Mission checkpoints must be stored outside the project directory.", nameof(path));
    }

    private static void ValidateMissionId(string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId) || missionId.Length > 64
            || missionId[0] is < 'a' or > 'z'
            || missionId.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentException("Mission ID must use lowercase letters, digits, and hyphens.", nameof(missionId));
    }

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string CreateProjectKey(string projectRoot) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(projectRoot.ToUpperInvariant())))[..24];

    private static void EnsureNotReparsePoint(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Mission checkpoint does not support reparse points in {description} '{path}'.");
    }

    private static (long Length, string Sha256) CopyAndHash(Stream source, Stream destination)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            length = checked(length + read);
        }
        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private sealed record ProjectFileSource(string RelativePath, string FullPath);

    private sealed class ValidatedCheckpoint : IDisposable
    {
        public ValidatedCheckpoint(ZipArchive archive, CheckpointDocument document,
            Dictionary<string, ZipArchiveEntry> entries)
        {
            Archive = archive;
            Document = document;
            Entries = entries;
        }

        public ZipArchive Archive { get; }
        public CheckpointDocument Document { get; }
        public Dictionary<string, ZipArchiveEntry> Entries { get; }
        public void Dispose() => Archive.Dispose();
    }

    private sealed class CheckpointDocument
    {
        public CheckpointDocument() { }
        public int Version { get; set; }
        public Guid CheckpointId { get; set; }
        public string MissionId { get; set; } = string.Empty;
        public string ProjectKey { get; set; } = string.Empty;
        public string ProjectFileRelativePath { get; set; } = string.Empty;
        public DateTimeOffset CapturedUtc { get; set; }
        public List<CheckpointFileDocument>? Files { get; set; }
    }

    private sealed class CheckpointFileDocument
    {
        public CheckpointFileDocument() { }
        public string RelativePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}

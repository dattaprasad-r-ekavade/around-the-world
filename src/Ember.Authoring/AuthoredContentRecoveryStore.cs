using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Authoring;

public sealed record AuthoredRecoveryFile(string RelativePath, byte[] Content);

public sealed record AuthoredRecoverySnapshot(
    Guid SnapshotId,
    DateTimeOffset CapturedUtc,
    IReadOnlyList<AuthoredRecoveryFile> Files);

/// <summary>Stores checksummed authoring snapshots outside the project and player-save locations.</summary>
public static class AuthoredContentRecoveryStore
{
    public const int CurrentVersion = 1;
    public const string SnapshotFileName = "authored-recovery.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string GetDefaultStorageDirectory(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("The local application-data directory is unavailable.");

        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        var projectKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRoot.ToUpperInvariant())))[..24];
        return Path.Combine(localApplicationData, "Ember", "CharacterStudio", "AuthoringRecovery", projectKey);
    }

    public static AuthoredRecoverySnapshot SaveLatest(string projectRoot, string recoveryDirectory,
        IEnumerable<AuthoredRecoveryFile> files, DateTimeOffset? capturedUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        ArgumentNullException.ThrowIfNull(files);

        var root = Path.GetFullPath(projectRoot);
        var storage = Path.GetFullPath(recoveryDirectory);
        EnsureOutsideProject(root, storage);

        var snapshotFiles = new List<AuthoredRecoveryFile>();
        var entries = new List<SnapshotFileDocument>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (file is null) throw new ArgumentException("Snapshot file cannot be null.", nameof(files));
            if (file.Content is null) throw new ArgumentException("Snapshot file content cannot be null.", nameof(files));
            var relativePath = NormalizeRelativePath(file.RelativePath);
            ValidateProjectRelativePath(root, relativePath);
            if (!seenPaths.Add(relativePath))
                throw new ArgumentException($"Snapshot repeats authored file '{relativePath}'.", nameof(files));

            var content = (byte[])file.Content.Clone();
            snapshotFiles.Add(new AuthoredRecoveryFile(relativePath, content));
            entries.Add(new SnapshotFileDocument
            {
                RelativePath = relativePath,
                Sha256 = Convert.ToHexString(SHA256.HashData(content)),
                ContentBase64 = Convert.ToBase64String(content)
            });
        }

        if (entries.Count == 0)
            throw new ArgumentException("An authored recovery snapshot must contain at least one file.", nameof(files));

        var snapshot = new AuthoredRecoverySnapshot(Guid.NewGuid(),
            (capturedUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(), snapshotFiles.AsReadOnly());
        var document = new SnapshotDocument
        {
            Version = CurrentVersion,
            SnapshotId = snapshot.SnapshotId,
            CapturedUtc = snapshot.CapturedUtc,
            Files = entries
        };

        Directory.CreateDirectory(storage);
        var destination = Path.Combine(storage, SnapshotFileName);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return snapshot;
    }

    public static AuthoredRecoverySnapshot LoadLatest(string projectRoot, string recoveryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        var root = Path.GetFullPath(projectRoot);
        var storage = Path.GetFullPath(recoveryDirectory);
        EnsureOutsideProject(root, storage);
        var snapshotPath = Path.Combine(storage, SnapshotFileName);

        SnapshotDocument document;
        try
        {
            using var stream = File.OpenRead(snapshotPath);
            document = JsonSerializer.Deserialize<SnapshotDocument>(stream, JsonOptions)
                ?? throw new InvalidDataException("Authored recovery snapshot is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Authored recovery snapshot JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported authored recovery snapshot version {document.Version}; expected {CurrentVersion}.");
        if (document.SnapshotId == Guid.Empty)
            throw new InvalidDataException("Authored recovery snapshot ID cannot be empty.");
        if (document.Files is null || document.Files.Count == 0)
            throw new InvalidDataException("Authored recovery snapshot file list is empty.");

        var files = new List<AuthoredRecoveryFile>(document.Files.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.Files)
        {
            if (entry is null) throw new InvalidDataException("Authored recovery snapshot contains a null file entry.");
            string relativePath;
            try
            {
                relativePath = NormalizeRelativePath(entry.RelativePath);
                ValidateProjectRelativePath(root, relativePath);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                throw new InvalidDataException("Authored recovery snapshot contains an unsafe file path.", exception);
            }
            if (!seenPaths.Add(relativePath))
                throw new InvalidDataException($"Authored recovery snapshot repeats file '{relativePath}'.");

            byte[] content;
            byte[] expectedChecksum;
            try
            {
                content = Convert.FromBase64String(entry.ContentBase64
                    ?? throw new FormatException("File content is missing."));
                expectedChecksum = Convert.FromHexString(entry.Sha256
                    ?? throw new FormatException("File checksum is missing."));
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Authored recovery snapshot file '{relativePath}' is malformed.", exception);
            }

            var actualChecksum = SHA256.HashData(content);
            if (expectedChecksum.Length != actualChecksum.Length
                || !CryptographicOperations.FixedTimeEquals(expectedChecksum, actualChecksum))
                throw new InvalidDataException($"Authored recovery snapshot checksum failed for '{relativePath}'.");
            files.Add(new AuthoredRecoveryFile(relativePath, content));
        }

        return new AuthoredRecoverySnapshot(document.SnapshotId, document.CapturedUtc.ToUniversalTime(),
            files.AsReadOnly());
    }

    private static void EnsureOutsideProject(string projectRoot, string storageDirectory)
    {
        var relative = Path.GetRelativePath(projectRoot, storageDirectory);
        var separator = Path.DirectorySeparatorChar.ToString();
        if (!Path.IsPathRooted(relative) && (relative == "."
            || relative != ".." && !relative.StartsWith(".." + separator, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ArgumentException("Authored recovery storage must be outside the project root.", nameof(storageDirectory));
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Authored file path is required.", nameof(path));
        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized)
            || normalized.Contains(':'))
            throw new ArgumentException("Authored file path must be project-relative.", nameof(path));
        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            throw new ArgumentException("Authored file path cannot contain empty, '.' or '..' segments.", nameof(path));
        return string.Join('/', segments);
    }

    private static void ValidateProjectRelativePath(string projectRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(projectRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Authored file path escapes the project root.", nameof(relativePath));
    }

    private sealed class SnapshotDocument
    {
        public int Version { get; init; }
        public Guid SnapshotId { get; init; }
        public DateTimeOffset CapturedUtc { get; init; }
        public List<SnapshotFileDocument>? Files { get; init; }
    }

    private sealed class SnapshotFileDocument
    {
        public string RelativePath { get; init; } = string.Empty;
        public string? Sha256 { get; init; }
        public string? ContentBase64 { get; init; }
    }
}

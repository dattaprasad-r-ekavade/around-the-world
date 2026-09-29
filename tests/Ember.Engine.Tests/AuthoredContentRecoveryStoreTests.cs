using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ember.Authoring;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class AuthoredContentRecoveryStoreTests
{
    [Fact]
    public void SnapshotRoundTripsBytesOutsideProjectAndLeavesPlayerSaveUntouched()
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "project");
            var recoveryDirectory = Path.Combine(directory, "local", "Ember", "CharacterStudio", "AuthoringRecovery");
            var playerSavePath = Path.Combine(directory, "local", "Ember", "RpgSlice", "world-save.json");
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(playerSavePath)!);
            var playerSave = "valid-player-save";
            File.WriteAllText(playerSavePath, playerSave);
            var capturedAt = new DateTimeOffset(2026, 9, 26, 12, 30, 0, TimeSpan.FromHours(5.5));
            var files = new[]
            {
                new AuthoredRecoveryFile("Scenes/Exterior.json", Encoding.UTF8.GetBytes("{\"version\":7}")),
                new AuthoredRecoveryFile("RpgContent.json", [0, 1, 2, 255])
            };

            var saved = AuthoredContentRecoveryStore.SaveLatest(projectRoot, recoveryDirectory, files, capturedAt);
            var loaded = AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory);
            var defaultStorage = AuthoredContentRecoveryStore.GetDefaultStorageDirectory(projectRoot);

            Assert.Equal(AuthoredContentRecoveryStore.CurrentVersion, ReadVersion(
                Path.Combine(recoveryDirectory, AuthoredContentRecoveryStore.SnapshotFileName)));
            Assert.Equal(saved.SnapshotId, loaded.SnapshotId);
            Assert.Equal(capturedAt.ToUniversalTime(), loaded.CapturedUtc);
            Assert.Equal(2, loaded.Files.Count);
            Assert.Equal(files.Select(file => file.RelativePath), loaded.Files.Select(file => file.RelativePath));
            for (var index = 0; index < files.Length; index++)
                Assert.Equal(files[index].Content, loaded.Files[index].Content);
            Assert.Contains("AuthoringRecovery", defaultStorage, StringComparison.OrdinalIgnoreCase);
            Assert.False(IsWithin(projectRoot, defaultStorage));
            Assert.Equal(playerSave, File.ReadAllText(playerSavePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SnapshotRejectsCorruptedContentAndUnsupportedVersion()
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "project");
            var recoveryDirectory = Path.Combine(directory, "recovery");
            Directory.CreateDirectory(projectRoot);
            AuthoredContentRecoveryStore.SaveLatest(projectRoot, recoveryDirectory,
            [new AuthoredRecoveryFile("Scenes/Exterior.json", Encoding.UTF8.GetBytes("valid scene"))]);
            var snapshotPath = Path.Combine(recoveryDirectory, AuthoredContentRecoveryStore.SnapshotFileName);

            var corrupted = JsonNode.Parse(File.ReadAllText(snapshotPath))!;
            corrupted["files"]![0]!["contentBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("changed scene"));
            File.WriteAllText(snapshotPath, corrupted.ToJsonString());
            var checksum = Assert.Throws<InvalidDataException>(() =>
                AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory));
            Assert.Contains("checksum", checksum.Message, StringComparison.OrdinalIgnoreCase);

            var unsupported = JsonNode.Parse(File.ReadAllText(snapshotPath))!;
            unsupported["version"] = AuthoredContentRecoveryStore.CurrentVersion + 1;
            File.WriteAllText(snapshotPath, unsupported.ToJsonString());
            var version = Assert.Throws<InvalidDataException>(() =>
                AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory));
            Assert.Contains("Unsupported authored recovery snapshot version", version.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SnapshotRejectsUnsafeAuthoredPathsAndStorageInsideProject()
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "project");
            Directory.CreateDirectory(projectRoot);
            var file = new AuthoredRecoveryFile("../RpgSlice/world-save.json", [1]);

            Assert.Throws<ArgumentException>(() => AuthoredContentRecoveryStore.SaveLatest(
                projectRoot, Path.Combine(directory, "recovery"), [file]));
            Assert.Throws<ArgumentException>(() => AuthoredContentRecoveryStore.SaveLatest(
                projectRoot, Path.Combine(projectRoot, "recovery"),
                [new AuthoredRecoveryFile("Scenes/Exterior.json", [1])]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedSnapshotReplacementPreservesPreviousSnapshotAndCleansTemporaryFile()
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "project");
            var recoveryDirectory = Path.Combine(directory, "recovery");
            Directory.CreateDirectory(projectRoot);
            var originalContent = Encoding.UTF8.GetBytes("saved recovery version one");
            var original = AuthoredContentRecoveryStore.SaveLatest(projectRoot, recoveryDirectory,
                [new AuthoredRecoveryFile("Scenes/Start.json", originalContent)]);
            var snapshotPath = Path.Combine(recoveryDirectory, AuthoredContentRecoveryStore.SnapshotFileName);
            var previousSnapshot = File.ReadAllBytes(snapshotPath);

            using (new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = Record.Exception(() => AuthoredContentRecoveryStore.SaveLatest(
                    projectRoot, recoveryDirectory,
                    [new AuthoredRecoveryFile("Scenes/Start.json", Encoding.UTF8.GetBytes("new recovery version"))]));

                Assert.NotNull(failure);
                Assert.Equal(previousSnapshot, File.ReadAllBytes(snapshotPath));
                Assert.Empty(Directory.GetFiles(recoveryDirectory, "*.tmp", SearchOption.TopDirectoryOnly));
            }

            var loaded = AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory);
            Assert.Equal(original.SnapshotId, loaded.SnapshotId);
            Assert.Equal(originalContent, Assert.Single(loaded.Files).Content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("World", "World/world.json")]
    [InlineData("World/world.json", "World")]
    public void SnapshotRejectsFileAndDirectoryPathCollisionsBeforeWriting(string firstPath, string secondPath)
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "project");
            var recoveryDirectory = Path.Combine(directory, "recovery");
            Directory.CreateDirectory(projectRoot);

            var error = Assert.Throws<ArgumentException>(() => AuthoredContentRecoveryStore.SaveLatest(
                projectRoot, recoveryDirectory,
                [
                    new AuthoredRecoveryFile(firstPath, [1]),
                    new AuthoredRecoveryFile(secondPath, [2])
                ]));

            Assert.Contains("overlap as a file and directory", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(recoveryDirectory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SnapshotLoadRejectsConflictingPathsEvenWhenChecksumsAreValid()
    {
        var directory = TemporaryDirectory();
        try
        {
            var projectRoot = Path.Combine(directory, "project");
            var recoveryDirectory = Path.Combine(directory, "recovery");
            Directory.CreateDirectory(projectRoot);
            AuthoredContentRecoveryStore.SaveLatest(projectRoot, recoveryDirectory,
                [new AuthoredRecoveryFile("World/Scenes/Start.json", Encoding.UTF8.GetBytes("scene"))]);
            var snapshotPath = Path.Combine(recoveryDirectory, AuthoredContentRecoveryStore.SnapshotFileName);
            var document = JsonNode.Parse(File.ReadAllText(snapshotPath))!;
            var conflictingContent = Encoding.UTF8.GetBytes("conflicting file");
            document["files"]!.AsArray().Add(new JsonObject
            {
                ["relativePath"] = "World",
                ["sha256"] = Convert.ToHexString(SHA256.HashData(conflictingContent)),
                ["contentBase64"] = Convert.ToBase64String(conflictingContent)
            });
            File.WriteAllText(snapshotPath, document.ToJsonString());

            var error = Assert.Throws<InvalidDataException>(() =>
                AuthoredContentRecoveryStore.LoadLatest(projectRoot, recoveryDirectory));

            Assert.Contains("overlap as a file and directory", error.Message, StringComparison.Ordinal);
            Assert.Contains("World/Scenes/Start.json", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int ReadVersion(string snapshotPath)
    {
        var document = JsonNode.Parse(File.ReadAllText(snapshotPath))!;
        return (int)document["version"]!;
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-authored-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

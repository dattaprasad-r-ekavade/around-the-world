using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Ember.Authoring;
using Ember.Project;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class MissionProjectCheckpointStoreTests
{
    [Fact]
    public void GetOrCaptureKeepsTheOriginalMissionStartingPoint()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(temporaryRoot, "Project"));
            var checkpointPath = Path.Combine(temporaryRoot, "Checkpoints", "first-creation.zip");
            var first = MissionProjectCheckpointStore.GetOrCapture(project, "first-creation", checkpointPath);
            var scenePath = project.ResolveStartupScenePath();
            var startingScene = File.ReadAllBytes(scenePath);
            File.WriteAllText(scenePath, "changed after entering mission");

            var second = MissionProjectCheckpointStore.GetOrCapture(project, "first-creation", checkpointPath);
            var rewind = MissionProjectCheckpointStore.Restore(project, "first-creation", checkpointPath);

            Assert.Equal(first.CheckpointId, second.CheckpointId);
            Assert.Equal(first.CheckpointId, rewind.CheckpointId);
            Assert.Equal(startingScene, File.ReadAllBytes(scenePath));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void RewindRestoresAuthoredFilesAndPreservesLearningProgressAndGitMetadata()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(temporaryRoot, "Project"));
            var checkpointPath = Path.Combine(temporaryRoot, "Checkpoints", "first-creation.zip");
            var scenePath = project.ResolveStartupScenePath();
            var assetPath = project.ResolveContentPath("Assets/prop.glb");
            var progressPath = project.ResolveContentPath(ProjectLearningProgressStore.RelativePath);
            var gitHeadPath = Path.Combine(project.RootDirectory, ".git", "HEAD");
            var outsidePath = Path.Combine(temporaryRoot, "outside.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(progressPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(gitHeadPath)!);
            File.WriteAllBytes(assetPath, [1, 3, 5, 7]);
            File.WriteAllText(progressPath, "before mission");
            File.WriteAllText(gitHeadPath, "before mission metadata");
            File.WriteAllText(outsidePath, "outside project");
            var originalScene = File.ReadAllBytes(scenePath);
            var originalAsset = File.ReadAllBytes(assetPath);

            var checkpoint = MissionProjectCheckpointStore.Capture(project, "first-creation", checkpointPath);

            var changedScene = SceneFile.Load(scenePath);
            changedScene.Add(new SceneObject(Guid.NewGuid(), "Mission object"));
            SceneFile.SaveAtomic(changedScene, scenePath);
            File.Delete(assetPath);
            File.WriteAllText(project.ResolveContentPath("Assets/mission-created.txt"), "mission content");
            File.WriteAllText(progressPath, "mission progress to retain");
            File.WriteAllText(gitHeadPath, "current git metadata to retain");
            File.WriteAllText(outsidePath, "outside project unchanged");

            var result = MissionProjectCheckpointStore.Restore(project, "first-creation", checkpointPath);

            Assert.Equal(checkpoint.CheckpointId, result.CheckpointId);
            Assert.Null(result.RetainedBackupPath);
            Assert.Equal(originalScene, File.ReadAllBytes(scenePath));
            Assert.Equal(originalAsset, File.ReadAllBytes(assetPath));
            Assert.False(File.Exists(project.ResolveContentPath("Assets/mission-created.txt")));
            Assert.Equal("mission progress to retain", File.ReadAllText(progressPath));
            Assert.Equal("current git metadata to retain", File.ReadAllText(gitHeadPath));
            Assert.Equal("outside project unchanged", File.ReadAllText(outsidePath));
            Assert.Equal(project.FilePath, EngineProjectFile.Load(project.FilePath).FilePath);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void WrongProjectOrMissionCheckpointIsRejectedBeforeChangingTheProject()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var source = EngineProjectWorkspace.CreateEmpty(Path.Combine(temporaryRoot, "Source"));
            var other = EngineProjectWorkspace.CreateEmpty(Path.Combine(temporaryRoot, "Other"));
            var checkpointPath = Path.Combine(temporaryRoot, "Checkpoints", "first-creation.zip");
            MissionProjectCheckpointStore.Capture(source, "first-creation", checkpointPath);
            var otherScenePath = other.ResolveStartupScenePath();
            File.WriteAllText(otherScenePath, "other project sentinel");
            var originalOtherScene = File.ReadAllBytes(otherScenePath);

            Assert.Throws<InvalidDataException>(() =>
                MissionProjectCheckpointStore.Restore(other, "first-creation", checkpointPath));
            Assert.Equal(originalOtherScene, File.ReadAllBytes(otherScenePath));

            var sourceScenePath = source.ResolveStartupScenePath();
            File.WriteAllText(sourceScenePath, "source project sentinel");
            var originalSourceScene = File.ReadAllBytes(sourceScenePath);
            Assert.Throws<InvalidDataException>(() =>
                MissionProjectCheckpointStore.Restore(source, "move-two-objects", checkpointPath));
            Assert.Equal(originalSourceScene, File.ReadAllBytes(sourceScenePath));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void CorruptCheckpointFileIsRejectedBeforeReplacingTheProject()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(temporaryRoot, "Project"));
            var checkpointPath = Path.Combine(temporaryRoot, "Checkpoints", "first-creation.zip");
            MissionProjectCheckpointStore.Capture(project, "first-creation", checkpointPath);
            var scenePath = project.ResolveStartupScenePath();
            File.WriteAllText(scenePath, "live project sentinel");
            var originalLiveFile = File.ReadAllBytes(scenePath);

            using (var archive = ZipFile.Open(checkpointPath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("Scenes/Main.json")
                    ?? throw new InvalidDataException("Checkpoint fixture has no startup scene entry.");
                var bytes = ReadEntry(entry);
                entry.Delete();
                bytes[0] ^= 0x01;
                var replacement = archive.CreateEntry("Scenes/Main.json", CompressionLevel.NoCompression);
                using var destination = replacement.Open();
                destination.Write(bytes, 0, bytes.Length);
            }

            Assert.Throws<InvalidDataException>(() =>
                MissionProjectCheckpointStore.Restore(project, "first-creation", checkpointPath));
            Assert.Equal(originalLiveFile, File.ReadAllBytes(scenePath));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void CheckpointMustBeStoredOutsideTheProjectDirectory()
    {
        var temporaryRoot = CreateTemporaryDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(temporaryRoot, "Project"));
            var checkpointPath = project.ResolveContentPath("mission.zip");

            Assert.Throws<ArgumentException>(() =>
                MissionProjectCheckpointStore.Capture(project, "first-creation", checkpointPath));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var destination = new MemoryStream();
        source.CopyTo(destination);
        return destination.ToArray();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "EmberMissionCheckpointTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

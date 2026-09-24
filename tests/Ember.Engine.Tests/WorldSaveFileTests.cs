using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldSaveFileTests
{
    [Fact]
    public void SaveRestartRestoresPlayerChangesTombstonesAndRuntimeObjects()
    {
        var directory = TemporaryDirectory();
        try
        {
            var cellId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var firstObjectId = Guid.NewGuid();
            var deletedObjectId = Guid.NewGuid();
            var scenePath = Path.Combine(directory, "authored-cell.json");
            var authored = new SceneGraph();
            authored.Add(CreateAssetObject(firstObjectId, "Moved prop", assetId, Vector3.Zero));
            authored.Add(CreateAssetObject(deletedObjectId, "Removed prop", assetId, Vector3.Right));
            SceneFile.SaveAtomic(authored, scenePath);

            var identities = new WorldInstanceIdentityMap();
            var activeScene = SceneFile.Load(scenePath);
            var identityByObject = identities.CreateCellIdentities(cellId, activeScene);
            var changes = new WorldCellChangeStore();
            changes.SetTransform(cellId, identityByObject[firstObjectId], new Transform
            {
                Position = new Vector3(12f, 3f, -5f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, 0f, 0f),
                Scale = new Vector3(1.5f, 1f, 0.75f)
            });
            changes.SetEnabled(cellId, identityByObject[firstObjectId], false);
            changes.MarkDeleted(cellId, identityByObject[deletedObjectId]);
            var runtimeObjects = new WorldRuntimeObjectStore();
            var runtime = runtimeObjects.Spawn(cellId, activeScene,
                CreateAssetObject(Guid.NewGuid(), "Dropped key", assetId, new Vector3(4f, 1f, 2f)), identities);
            var player = new WorldPlayerLocation(cellId, new Vector3(3f, 1.2f, 8f),
                Quaternion.CreateFromYawPitchRoll(-0.7f, 0f, 0f));
            var snapshot = WorldSaveSnapshot.Capture(player, identities, changes, runtimeObjects);
            var savePath = Path.Combine(directory, "world-save.json");

            WorldSaveFile.SaveAtomic(savePath, snapshot);
            var reopened = WorldSaveFile.Load(savePath);

            Assert.Equal(WorldSaveFile.CurrentVersion, ReadVersion(savePath));
            Assert.Equal(player, reopened.PlayerLocation);
            Assert.Equal(3, reopened.Identities.Count);
            Assert.Equal(2, reopened.Changes.Count);
            Assert.Single(reopened.RuntimeObjects);

            var restoredIdentities = new WorldInstanceIdentityMap();
            var restoredChanges = new WorldCellChangeStore();
            var restoredRuntimeObjects = new WorldRuntimeObjectStore();
            reopened.Restore(restoredIdentities, restoredChanges, restoredRuntimeObjects);
            var reloadedScene = SceneFile.Load(scenePath);
            var reloadedIdentities = restoredIdentities.CreateCellIdentities(cellId, reloadedScene);
            Assert.Equal(1, restoredRuntimeObjects.Restore(cellId, reloadedScene, restoredIdentities));
            Assert.Equal(2, restoredChanges.Apply(cellId, reloadedScene, reloadedIdentities));

            var moved = reloadedScene.Find(firstObjectId)!;
            Assert.False(moved.Enabled);
            Assert.Equal(new Vector3(12f, 3f, -5f), moved.Transform.Position);
            Assert.Equal(new Vector3(1.5f, 1f, 0.75f), moved.Transform.Scale);
            Assert.Null(reloadedScene.Find(deletedObjectId));
            Assert.NotNull(reloadedScene.Find(runtime.SceneObjectId));
            Assert.True(restoredIdentities.TryGet(cellId, runtime.SceneObjectId, out var restoredRuntimeId));
            Assert.Equal(runtime.InstanceId, restoredRuntimeId);
            Assert.Equal(player.CellId, reopened.PlayerLocation.CellId);
            Assert.Equal(player.Position, reopened.PlayerLocation.Position);
            Assert.Equal(player.Facing, reopened.PlayerLocation.Facing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedReplacementLeavesPreviousWorldSaveUntouched()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "world-save.json");
            var original = new WorldSaveSnapshot(
                new WorldPlayerLocation(Guid.NewGuid(), Vector3.One, Quaternion.Identity), [], [], []);
            var replacement = new WorldSaveSnapshot(
                new WorldPlayerLocation(Guid.NewGuid(), new Vector3(99f, 0f, 0f), Quaternion.Identity), [], [], []);
            WorldSaveFile.SaveAtomic(path, original);
            var originalText = File.ReadAllText(path);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => WorldSaveFile.SaveAtomic(path, replacement));

            Assert.Equal(originalText, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            Assert.Equal(original.PlayerLocation, WorldSaveFile.Load(path).PlayerLocation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnsupportedWorldSaveVersionIsRejected()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "old-save.json");
        try
        {
            File.WriteAllText(path,
                "{\"Version\":99,\"PlayerLocation\":{},\"InstanceIdentities\":[],\"CellChanges\":[],\"RuntimeObjects\":[]}");

            var exception = Assert.Throws<InvalidDataException>(() => WorldSaveFile.Load(path));
            Assert.Contains("Unsupported world-save version", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int ReadVersion(string path)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Version").GetInt32();
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-world-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static SceneObject CreateAssetObject(Guid id, string name, Guid assetId, Vector3 position) =>
        new(id, name)
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/SharedProp.glb"),
            Transform = new Transform { Position = position }
        };
}

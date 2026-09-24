using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldInstanceTransferTests
{
    [Fact]
    public void TransferMovesOneRuntimeInstanceAndItsIdentityBetweenCells()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-instance-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourceCellId = Guid.NewGuid();
            var destinationCellId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var sourceObjectId = Guid.NewGuid();
            var sourcePath = Path.Combine(directory, "source.json");
            var destinationPath = Path.Combine(directory, "destination.json");
            var authoredSource = new SceneGraph();
            authoredSource.Add(new SceneObject(sourceObjectId, "Source marker"));
            var authoredDestination = new SceneGraph();
            authoredDestination.Add(new SceneObject(Guid.NewGuid(), "Destination marker"));
            SceneFile.SaveAtomic(authoredSource, sourcePath);
            SceneFile.SaveAtomic(authoredDestination, destinationPath);

            var identities = new WorldInstanceIdentityMap();
            var sourceScene = SceneFile.Load(sourcePath);
            var destinationScene = SceneFile.Load(destinationPath);
            identities.CreateCellIdentities(sourceCellId, sourceScene);
            identities.CreateCellIdentities(destinationCellId, destinationScene);
            var store = new WorldRuntimeObjectStore();
            var spawned = store.Spawn(sourceCellId, sourceScene, new SceneObject(Guid.NewGuid(), "Dropped relic")
            {
                GltfAsset = new GltfAssetReference(assetId, "Assets/Relic.glb"),
                Transform = new Transform { Position = new Vector3(31f, 1f, 8f) }
            }, identities);
            var transferredTransform = new Transform
            {
                Position = new Vector3(-29f, 1f, 8f),
                Rotation = Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0f, 0f)
            };

            var moved = store.Transfer(sourceCellId, destinationCellId, spawned.InstanceId,
                sourceScene, destinationScene, identities, transferredTransform);

            Assert.Equal(destinationCellId, moved.CellId);
            Assert.Equal(spawned.SceneObjectId, moved.SceneObjectId);
            Assert.Equal(spawned.InstanceId, moved.InstanceId);
            Assert.Null(sourceScene.Find(spawned.SceneObjectId));
            Assert.NotNull(destinationScene.Find(spawned.SceneObjectId));
            Assert.Equal(new Vector3(-29f, 1f, 8f), destinationScene.Find(spawned.SceneObjectId)!.Transform.Position);
            Assert.False(identities.TryGet(sourceCellId, spawned.SceneObjectId, out _));
            Assert.True(identities.TryGet(destinationCellId, spawned.SceneObjectId, out var movedId));
            Assert.Equal(spawned.InstanceId, movedId);
            Assert.Equal(1, store.Count);

            var reloadedSource = SceneFile.Load(sourcePath);
            var reloadedDestination = SceneFile.Load(destinationPath);
            identities.CreateCellIdentities(sourceCellId, reloadedSource);
            identities.CreateCellIdentities(destinationCellId, reloadedDestination);
            Assert.Equal(0, store.Restore(sourceCellId, reloadedSource, identities));
            Assert.Equal(1, store.Restore(destinationCellId, reloadedDestination, identities));
            Assert.Null(reloadedSource.Find(spawned.SceneObjectId));
            Assert.NotNull(reloadedDestination.Find(spawned.SceneObjectId));
            Assert.Equal(moved.InstanceId,
                identities.GetOrCreate(destinationCellId, reloadedDestination.Find(moved.SceneObjectId)!));
            Assert.Equal(0, store.Restore(sourceCellId, reloadedSource, identities));
            Assert.Equal(0, store.Restore(destinationCellId, reloadedDestination, identities));
            Assert.Equal(1, store.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DestinationCollisionLeavesSourceOwnershipUntouched()
    {
        var sourceCellId = Guid.NewGuid();
        var destinationCellId = Guid.NewGuid();
        var sourceScene = new SceneGraph();
        var destinationScene = new SceneGraph();
        var identities = new WorldInstanceIdentityMap();
        var store = new WorldRuntimeObjectStore();
        var spawned = store.Spawn(sourceCellId, sourceScene, new SceneObject(Guid.NewGuid(), "Runtime item"), identities);
        destinationScene.Add(new SceneObject(spawned.SceneObjectId, "Collision"));

        Assert.Throws<InvalidOperationException>(() => store.Transfer(sourceCellId, destinationCellId,
            spawned.InstanceId, sourceScene, destinationScene, identities, new Transform()));

        Assert.NotNull(sourceScene.Find(spawned.SceneObjectId));
        Assert.Equal("Collision", destinationScene.Find(spawned.SceneObjectId)!.Name);
        Assert.True(identities.TryGet(sourceCellId, spawned.SceneObjectId, out var sourceId));
        Assert.Equal(spawned.InstanceId, sourceId);
        Assert.False(identities.TryGet(destinationCellId, spawned.SceneObjectId, out _));
        Assert.Equal(1, store.Count);
    }
}

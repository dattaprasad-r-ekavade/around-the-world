using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellDeletionTombstoneTests
{
    [Fact]
    public void DeletedAuthoredInstanceDoesNotReturnAfterCellReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-cell-tombstones-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var deletedObjectId = Guid.NewGuid();
            var survivorObjectId = Guid.NewGuid();
            var path = Path.Combine(directory, "scene.json");
            var authoredScene = new SceneGraph();
            authoredScene.Add(CreateAssetObject(deletedObjectId, assetId, Vector3.Zero));
            authoredScene.Add(CreateAssetObject(survivorObjectId, assetId, Vector3.Right));
            SceneFile.SaveAtomic(authoredScene, path);

            var identities = new WorldInstanceIdentityMap();
            var originalIds = identities.CreateCellIdentities(cellId, SceneFile.Load(path));
            var deletedInstanceId = originalIds[deletedObjectId];
            var survivorInstanceId = originalIds[survivorObjectId];
            var changes = new WorldCellChangeStore();
            changes.MarkDeleted(cellId, deletedInstanceId);

            var reloadedScene = SceneFile.Load(path);
            var reloadedIds = identities.CreateCellIdentities(cellId, reloadedScene);
            Assert.Equal(1, changes.Apply(cellId, reloadedScene, reloadedIds));

            Assert.Null(reloadedScene.Find(deletedObjectId));
            Assert.NotNull(reloadedScene.Find(survivorObjectId));
            Assert.True(changes.IsDeleted(cellId, deletedInstanceId));
            Assert.False(changes.IsDeleted(cellId, survivorInstanceId));

            Assert.Throws<InvalidOperationException>(() =>
                changes.SetEnabled(cellId, deletedInstanceId, true));

            var nextReload = SceneFile.Load(path);
            Assert.Equal(1, changes.Apply(cellId, nextReload,
                identities.CreateCellIdentities(cellId, nextReload)));
            Assert.Null(nextReload.Find(deletedObjectId));
            Assert.NotNull(nextReload.Find(survivorObjectId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SceneObject CreateAssetObject(Guid id, Guid assetId, Vector3 position) =>
        new(id, "Shared asset instance")
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/SharedProp.glb"),
            Transform = new Transform { Position = position }
        };
}

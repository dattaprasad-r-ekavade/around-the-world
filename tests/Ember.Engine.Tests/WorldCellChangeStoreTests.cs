using System;
using System.Collections.Generic;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellChangeStoreTests
{
    [Fact]
    public void TransformAndEnabledOverridesSurviveCellReloadByWorldInstanceId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-cell-changes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var firstObjectId = Guid.NewGuid();
            var secondObjectId = Guid.NewGuid();
            var path = Path.Combine(directory, "scene.json");
            var authoredScene = new SceneGraph();
            authoredScene.Add(CreateAssetObject(firstObjectId, "Prop A", assetId, Vector3.Zero));
            authoredScene.Add(CreateAssetObject(secondObjectId, "Prop B", assetId, new Vector3(5f, 0f, 0f)));
            SceneFile.SaveAtomic(authoredScene, path);

            var identities = new WorldInstanceIdentityMap();
            var firstIds = identities.CreateCellIdentities(cellId, SceneFile.Load(path));
            var firstInstanceId = firstIds[firstObjectId];
            var secondInstanceId = firstIds[secondObjectId];
            var changes = new WorldCellChangeStore();
            var moved = new Transform
            {
                Position = new Vector3(9f, 2f, -4f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0.8f, 0.1f, 0f),
                Scale = new Vector3(2f, 1f, 0.5f)
            };
            changes.SetTransform(cellId, firstInstanceId, moved);
            changes.SetEnabled(cellId, firstInstanceId, false);
            moved.Position = Vector3.Zero;

            var reloadedScene = SceneFile.Load(path);
            var reloadedIds = identities.CreateCellIdentities(cellId, reloadedScene);
            Assert.Equal(1, changes.Apply(cellId, reloadedScene, reloadedIds));

            var restored = reloadedScene.Find(firstObjectId)!;
            Assert.False(restored.Enabled);
            Assert.Equal(new Vector3(9f, 2f, -4f), restored.Transform.Position);
            Assert.Equal(new Vector3(2f, 1f, 0.5f), restored.Transform.Scale);
            Assert.Equal(Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.8f, 0.1f, 0f)), restored.Transform.Rotation);
            Assert.True(reloadedScene.Find(secondObjectId)!.Enabled);
            Assert.Equal(new Vector3(5f, 0f, 0f), reloadedScene.Find(secondObjectId)!.Transform.Position);
            Assert.NotEqual(firstInstanceId, secondInstanceId);
            Assert.Equal(1, changes.CellCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ChangesRemainScopedToTheirCell()
    {
        var firstCellId = Guid.NewGuid();
        var secondCellId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var firstScene = new SceneGraph();
        firstScene.Add(new SceneObject(objectId, "First cell object"));
        var secondScene = new SceneGraph();
        secondScene.Add(new SceneObject(objectId, "Second cell object"));
        var identities = new WorldInstanceIdentityMap();
        var firstIds = identities.CreateCellIdentities(firstCellId, firstScene);
        var secondIds = identities.CreateCellIdentities(secondCellId, secondScene);
        var changes = new WorldCellChangeStore();
        changes.SetEnabled(firstCellId, firstIds[objectId], false);

        Assert.Equal(0, changes.Apply(secondCellId, secondScene, secondIds));
        Assert.True(secondScene.Find(objectId)!.Enabled);
        Assert.Equal(1, changes.Apply(firstCellId, firstScene, firstIds));
        Assert.False(firstScene.Find(objectId)!.Enabled);
    }

    [Fact]
    public void InvalidTransformOverrideIsRejected()
    {
        var changes = new WorldCellChangeStore();
        Assert.Throws<ArgumentException>(() => changes.SetTransform(Guid.NewGuid(),
            new WorldInstanceId(Guid.NewGuid()), new Transform { Rotation = new Quaternion(0f, 0f, 0f, 0f) }));
    }

    private static SceneObject CreateAssetObject(Guid id, string name, Guid assetId, Vector3 position) =>
        new(id, name)
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/SharedProp.glb"),
            Transform = new Transform { Position = position }
        };
}

using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldRuntimeObjectStoreTests
{
    [Fact]
    public void RuntimeSpawnKeepsItsSceneAndWorldIdsAcrossReloadWithoutDuplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-runtime-objects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var authoredId = Guid.NewGuid();
            var scenePath = Path.Combine(directory, "cell.json");
            var authoredScene = new SceneGraph();
            authoredScene.Add(new SceneObject(authoredId, "Authored prop")
            {
                GltfAsset = new GltfAssetReference(assetId, "Assets/Prop.glb")
            });
            SceneFile.SaveAtomic(authoredScene, scenePath);

            var identityMap = new WorldInstanceIdentityMap();
            var liveScene = SceneFile.Load(scenePath);
            identityMap.CreateCellIdentities(cellId, liveScene);
            var store = new WorldRuntimeObjectStore();
            var spawned = store.Spawn(cellId, liveScene,
                new SceneObject(Guid.NewGuid(), "Dropped item")
                {
                    GltfAsset = new GltfAssetReference(assetId, "Assets/Prop.glb"),
                    Transform = new Transform { Position = new Vector3(8f, 1f, 2f) }
                }, identityMap);

            Assert.NotEqual(cellId, spawned.InstanceId.Value);
            Assert.NotEqual(assetId, spawned.InstanceId.Value);
            Assert.NotEqual(authoredId, spawned.SceneObjectId);
            Assert.Equal(2, liveScene.Objects.Count);
            Assert.Equal(1, store.Count);

            var reloadedScene = SceneFile.Load(scenePath);
            identityMap.CreateCellIdentities(cellId, reloadedScene);
            Assert.Equal(1, store.Restore(cellId, reloadedScene, identityMap));
            Assert.Equal(2, reloadedScene.Objects.Count);
            Assert.Equal(spawned.InstanceId, identityMap.GetOrCreate(cellId, reloadedScene.Find(spawned.SceneObjectId)!));
            Assert.Equal(new Vector3(8f, 1f, 2f), reloadedScene.Find(spawned.SceneObjectId)!.Transform.Position);

            Assert.Equal(0, store.Restore(cellId, reloadedScene, identityMap));
            Assert.Equal(2, reloadedScene.Objects.Count);
            Assert.Equal(spawned.InstanceId, identityMap.GetOrCreate(cellId, reloadedScene.Find(spawned.SceneObjectId)!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RestoreRejectsRuntimeSceneIdCollision()
    {
        var scene = new SceneGraph();
        var cellId = Guid.NewGuid();
        var identities = new WorldInstanceIdentityMap();
        var store = new WorldRuntimeObjectStore();
        var spawned = store.Spawn(cellId, scene, new SceneObject(Guid.NewGuid(), "Runtime"), identities);

        var conflictingScene = new SceneGraph();
        conflictingScene.Add(new SceneObject(spawned.SceneObjectId, "Wrong object"));
        var conflictingIdentities = new WorldInstanceIdentityMap();
        conflictingIdentities.Register(cellId, spawned.SceneObjectId, new WorldInstanceId(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => store.Restore(cellId, conflictingScene, conflictingIdentities));
    }
}

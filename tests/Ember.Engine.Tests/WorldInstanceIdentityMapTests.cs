using System;
using System.Collections.Generic;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldInstanceIdentityMapTests
{
    [Fact]
    public void TwoInstancesOfOneAssetKeepDistinctWorldIdsAcrossCellReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-instance-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var firstObjectId = Guid.NewGuid();
            var secondObjectId = Guid.NewGuid();
            var scenePath = Path.Combine(directory, "cell.json");
            var authoredScene = new SceneGraph();
            authoredScene.Add(CreateAssetObject(firstObjectId, assetId, new Vector3(1f, 0f, 0f)));
            authoredScene.Add(CreateAssetObject(secondObjectId, assetId, new Vector3(4f, 0f, 0f)));
            SceneFile.SaveAtomic(authoredScene, scenePath);

            var identityMap = new WorldInstanceIdentityMap();
            var firstLoad = identityMap.CreateCellIdentities(cellId, SceneFile.Load(scenePath));
            var firstIdentity = firstLoad[firstObjectId];
            var secondIdentity = firstLoad[secondObjectId];

            Assert.NotEqual(firstIdentity, secondIdentity);
            Assert.NotEqual(cellId, firstIdentity.Value);
            Assert.NotEqual(cellId, secondIdentity.Value);
            Assert.NotEqual(assetId, firstIdentity.Value);
            Assert.NotEqual(assetId, secondIdentity.Value);
            Assert.NotEqual(firstObjectId, firstIdentity.Value);
            Assert.NotEqual(secondObjectId, secondIdentity.Value);
            Assert.Equal(2, identityMap.Count);

            var reloadedScene = SceneFile.Load(scenePath);
            var secondLoad = identityMap.CreateCellIdentities(cellId, reloadedScene);
            Assert.Equal(firstIdentity, secondLoad[firstObjectId]);
            Assert.Equal(secondIdentity, secondLoad[secondObjectId]);
            Assert.Equal(2, identityMap.Count);

            var sameDefinitionInAnotherCell = identityMap.GetOrCreate(Guid.NewGuid(), reloadedScene.Find(firstObjectId)!);
            Assert.NotEqual(firstIdentity, sameDefinitionInAnotherCell);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EmptyInstanceIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new WorldInstanceId(Guid.Empty));
    }

    private static SceneObject CreateAssetObject(Guid objectId, Guid assetId, Vector3 position) =>
        new(objectId, "Placed asset")
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/SharedProp.glb"),
            Transform = new Transform { Position = position }
        };
}

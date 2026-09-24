using System;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class GltfStaticMeshLodTests
{
    [Fact]
    public void RepresentationSelectionHoldsItsTierInsideTheHysteresisBand()
    {
        var lod = CreateLod(enterFar: 50f, exitFar: 40f);
        var isFar = false;

        isFar = lod.SelectFar(isFar, 50f);
        Assert.True(isFar);
        isFar = lod.SelectFar(isFar, 45f);
        Assert.True(isFar);
        isFar = lod.SelectFar(isFar, 40f);
        Assert.False(isFar);
        isFar = lod.SelectFar(isFar, 49f);
        Assert.False(isFar);
        Assert.Equal(10f, lod.HysteresisDistance);
    }

    [Fact]
    public void InvalidThresholdsAndDistancesAreRejected()
    {
        var near = new GltfAssetReference(Guid.NewGuid(), "Assets/near.glb");
        var far = new GltfAssetReference(Guid.NewGuid(), "Assets/far.glb");

        Assert.Throws<ArgumentOutOfRangeException>(() => new GltfStaticMeshLod(near, far, 40f, 40f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GltfStaticMeshLod(near, far, float.NaN, 10f));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateLod().SelectFar(false, float.PositiveInfinity));
    }

    private static GltfStaticMeshLod CreateLod(float enterFar = 50f, float exitFar = 40f) =>
        new(new GltfAssetReference(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Assets/near.glb"),
            new GltfAssetReference(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Assets/far.glb"),
            enterFar, exitFar);
}

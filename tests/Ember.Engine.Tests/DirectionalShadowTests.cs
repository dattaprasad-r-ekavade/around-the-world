using System;
using Ember.Assets;
using Ember.Render;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class DirectionalShadowTests
{
    [Fact]
    public void OrthographicLightCameraContainsEverySceneBoundsCorner()
    {
        var bounds = new Bounds3(new Vector3(-7f, -2f, -4f), new Vector3(7f, 6f, 4f));
        var matrix = DirectionalShadowCamera.CreateViewProjection(bounds, new Vector3(-0.4f, -1f, -0.25f));

        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector4(
                (corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z,
                1f);
            var clip = Vector4.Transform(point, matrix);
            var x = clip.X / clip.W;
            var y = clip.Y / clip.W;
            var z = clip.Z / clip.W;
            Assert.InRange(x, -1f, 1f);
            Assert.InRange(y, -1f, 1f);
            Assert.InRange(z, 0f, 1f);
        }
    }

    [Fact]
    public void LightCameraRejectsZeroOrNonfiniteDirection()
    {
        var bounds = new Bounds3(Vector3.Zero, Vector3.One);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DirectionalShadowCamera.CreateViewProjection(bounds, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DirectionalShadowCamera.CreateViewProjection(bounds, new Vector3(float.NaN, 0f, 0f)));
    }

    [Fact]
    public void ShadowMapResolutionTracksViewportWithAClamp()
    {
        Assert.Equal(512, DirectionalShadowMap.ComputeSize(800, 600));
        Assert.Equal(1024, DirectionalShadowMap.ComputeSize(1280, 720));
        Assert.Equal(2048, DirectionalShadowMap.ComputeSize(3840, 2160));
    }

    [Fact]
    public void StaticCullerKeepsVisibleBoundsAndRejectsBoundsBeyondTheCamera()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(16f / 9f, far: 200f);
        camera.Reset(Vector3.Zero, distance: 10f, yaw: 0f, pitch: 0f);
        var frustum = new BoundingFrustum(camera.View * camera.Projection);
        var localBounds = new Bounds3(new Vector3(-1f), Vector3.One);
        var visibleWorld = Matrix.CreateTranslation(0f, 0f, 0f);
        var outsideWorld = Matrix.CreateTranslation(500f, 0f, 0f);

        Assert.True(StaticSceneCuller.IsVisible(localBounds, visibleWorld, frustum));
        Assert.False(StaticSceneCuller.IsVisible(localBounds, outsideWorld, frustum));
    }

    [Fact]
    public void StaticCullerUsesTransformedBoundsAndRetainsUnboundedMeshes()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(1f, far: 100f);
        camera.Reset(Vector3.Zero, distance: 8f, yaw: 0f, pitch: 0f);
        var frustum = new BoundingFrustum(camera.View * camera.Projection);
        var localBounds = new Bounds3(new Vector3(-0.5f), new Vector3(0.5f));
        var scaledAndTranslated = Matrix.CreateScale(2f) * Matrix.CreateTranslation(0f, 0f, 1f);

        Assert.True(StaticSceneCuller.IsVisible(localBounds, scaledAndTranslated, frustum));
        Assert.True(StaticSceneCuller.IsVisible(null, Matrix.Identity, frustum));
    }
}

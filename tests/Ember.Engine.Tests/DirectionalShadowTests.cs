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
    public void CameraShadowFitContainsVisibleFrustumWithinTheShadowDistance()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(16f / 9f, far: 1000f);
        camera.Reset(new Vector3(4f, 2f, -3f), distance: 12f, yaw: 0.6f, pitch: -0.2f);
        var sceneBounds = new Bounds3(new Vector3(-80f), new Vector3(80f));
        const float shadowDistance = 45f;
        var shadow = DirectionalShadowCamera.CreateViewProjection(sceneBounds,
            camera.View, camera.Projection, new Vector3(-0.4f, -1f, -0.25f),
            shadowMapSize: 1024, shadowDistance: shadowDistance);
        var receiverCorners = GetClippedFrustumCorners(camera.View, camera.Projection, shadowDistance);

        foreach (var corner in receiverCorners)
        {
            var clip = Vector4.Transform(new Vector4(corner, 1f), shadow);
            Assert.True(clip.W > 0f);
            Assert.InRange(clip.X / clip.W, -1.0001f, 1.0001f);
            Assert.InRange(clip.Y / clip.W, -1.0001f, 1.0001f);
            Assert.InRange(clip.Z / clip.W, -0.0001f, 1.0001f);
        }
    }

    [Fact]
    public void CameraShadowFitDoesNotMoveForSubTexelCameraTranslation()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(16f / 9f, far: 1000f);
        camera.Reset(new Vector3(4f, 2f, -3f), distance: 12f, yaw: 0.6f, pitch: -0.2f);
        var sceneBounds = new Bounds3(new Vector3(-80f), new Vector3(80f));
        var lightDirection = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.25f));
        const int shadowMapSize = 1024;
        const float shadowDistance = 45f;
        var first = DirectionalShadowCamera.CreateViewProjection(sceneBounds,
            camera.View, camera.Projection, lightDirection, shadowMapSize, shadowDistance);

        var inverseView = Matrix.Invert(camera.View);
        var cameraPosition = Vector3.Transform(Vector3.Zero, inverseView);
        var cameraForward = Vector3.TransformNormal(Vector3.Forward, inverseView);
        var cameraTarget = cameraPosition + cameraForward;
        var offset = Vector3.Normalize(Vector3.Cross(lightDirection, Vector3.Up)) * 0.00001f;
        var movedView = Matrix.CreateLookAt(cameraPosition + offset, cameraTarget + offset, Vector3.Up);
        var second = DirectionalShadowCamera.CreateViewProjection(sceneBounds,
            movedView, camera.Projection, lightDirection, shadowMapSize, shadowDistance);

        AssertMatrixNear(first, second, 0.0001f);
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

    private static Vector3[] GetClippedFrustumCorners(Matrix view, Matrix projection, float distance)
    {
        var inverseViewProjection = Matrix.Invert(view * projection);
        var cameraPosition = Vector3.Transform(Vector3.Zero, Matrix.Invert(view));
        var points = new Vector3[16];
        var index = 0;
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
        {
            var screenX = x == 0 ? -1f : 1f;
            var screenY = y == 0 ? -1f : 1f;
            var near = Unproject(new Vector3(screenX, screenY, 0f), inverseViewProjection);
            var far = Unproject(new Vector3(screenX, screenY, 1f), inverseViewProjection);
            var ray = Vector3.Normalize(far - cameraPosition);
            var clipped = cameraPosition + ray * MathF.Min(distance,
                Vector3.Distance(cameraPosition, far));
            points[index++] = near;
            points[index++] = clipped;
        }

        return points;
    }

    private static Vector3 Unproject(Vector3 point, Matrix inverseViewProjection)
    {
        var world = Vector4.Transform(new Vector4(point, 1f), inverseViewProjection);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }

    private static void AssertMatrixNear(Matrix expected, Matrix actual, float tolerance)
    {
        var expectedValues = new[]
        {
            expected.M11, expected.M12, expected.M13, expected.M14,
            expected.M21, expected.M22, expected.M23, expected.M24,
            expected.M31, expected.M32, expected.M33, expected.M34,
            expected.M41, expected.M42, expected.M43, expected.M44
        };
        var actualValues = new[]
        {
            actual.M11, actual.M12, actual.M13, actual.M14,
            actual.M21, actual.M22, actual.M23, actual.M24,
            actual.M31, actual.M32, actual.M33, actual.M34,
            actual.M41, actual.M42, actual.M43, actual.M44
        };
        for (var i = 0; i < expectedValues.Length; i++)
            Assert.InRange(MathF.Abs(expectedValues[i] - actualValues[i]), 0f, tolerance);
    }
}

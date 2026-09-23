using System;
using System.IO;
using Ember;
using Ember.Assets;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class MeshBoundsTests
{
    [Fact]
    public void ImportedMeshBoundsMatchAuthoredVertices()
    {
        var model = ModelRoot.Load(FixturePath());
        var mesh = GltfPrimitiveImporter.Import(model.LogicalMeshes[0].Primitives[0]);

        Assert.NotNull(mesh.LocalBounds);
        AssertClose(new Vector3(0.2f, 0.2f, 0f), mesh.LocalBounds!.Value.Min);
        AssertClose(new Vector3(1.2f, 1.2f, 0f), mesh.LocalBounds.Value.Max);
        Assert.All(mesh.Vertices, vertex => Assert.True(mesh.LocalBounds.Value.Contains(vertex.Position)));
    }

    [Fact]
    public void TransformedBoundsContainEveryRotatedAndScaledVertex()
    {
        var local = new Bounds3(new Vector3(-1f, -0.5f, -2f), new Vector3(1f, 0.5f, 2f));
        var world = Matrix.CreateScale(2f, 0.5f, 1.5f)
            * Matrix.CreateRotationY(MathHelper.PiOver4)
            * Matrix.CreateTranslation(3f, -2f, 5f);
        var transformed = local.Transform(world);

        for (var corner = 0; corner < 8; corner++)
        {
            var point = Vector3.Transform(new Vector3(
                (corner & 1) == 0 ? local.Min.X : local.Max.X,
                (corner & 2) == 0 ? local.Min.Y : local.Max.Y,
                (corner & 4) == 0 ? local.Min.Z : local.Max.Z), world);
            Assert.True(transformed.Contains(point, 0.0001f), $"Transformed bounds do not contain corner {point}.");
        }
    }

    [Fact]
    public void OrbitCameraFramesAllCornersAfterBoundsTransform()
    {
        var camera = new OrbitCamera { MinDistance = 0.25f, MaxDistance = 100f };
        camera.SetProjection(16f / 9f, fieldOfViewDegrees: 60f);
        camera.Reset(Vector3.Zero, distance: 6f, yaw: 0.7f, pitch: -0.3f);
        var local = new Bounds3(new Vector3(-1f, -0.5f, -2f), new Vector3(1f, 0.5f, 2f));
        var world = Matrix.CreateScale(2f, 0.5f, 1.5f)
            * Matrix.CreateRotationY(MathHelper.PiOver4)
            * Matrix.CreateTranslation(3f, -2f, 5f);
        var bounds = local.Transform(world);

        camera.Frame(bounds, padding: 1.1f);
        Assert.Equal(bounds.Center, camera.Target);
        Assert.True(camera.Distance > 0f);

        var viewProjection = camera.View * camera.Projection;
        for (var corner = 0; corner < 8; corner++)
        {
            var localPoint = new Vector3(
                (corner & 1) == 0 ? local.Min.X : local.Max.X,
                (corner & 2) == 0 ? local.Min.Y : local.Max.Y,
                (corner & 4) == 0 ? local.Min.Z : local.Max.Z);
            var point = Vector3.Transform(localPoint, world);
            var projected = Vector4.Transform(new Vector4(point, 1f), viewProjection);
            Assert.True(MathF.Abs(projected.X / projected.W) <= 1f, $"Corner {point} fell outside horizontal view.");
            Assert.True(MathF.Abs(projected.Y / projected.W) <= 1f, $"Corner {point} fell outside vertical view.");
        }
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(Vector3.Distance(expected, actual), 0f, 0.00001f);
    }
}

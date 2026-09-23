using System;
using System.IO;
using System.Linq;
using Ember.Assets;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class GltfSceneImporterTests
{
    [Fact]
    public void ImportsRotatedScaledGlbChildIntoSceneHierarchy()
    {
        var model = ModelRoot.Load(FixturePath());
        var parentNode = model.DefaultScene!.CreateNode("TransformParent");
        parentNode.LocalTransform = parentNode.LocalTransform
            .WithScale(new System.Numerics.Vector3(2f, 3f, 0.5f))
            .WithRotation(System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, MathF.PI / 2f))
            .WithTranslation(new System.Numerics.Vector3(1f, 2f, 3f));
        var childNode = parentNode.CreateNode("TransformChild");
        childNode.Mesh = model.LogicalMeshes[0];
        childNode.LocalTransform = childNode.LocalTransform
            .WithScale(new System.Numerics.Vector3(0.5f, 1.5f, 2f))
            .WithRotation(System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, MathF.PI / 4f))
            .WithTranslation(new System.Numerics.Vector3(0f, 1f, -2f));

        var path = Path.Combine(Path.GetTempPath(), $"ember-hierarchy-{Guid.NewGuid():N}.glb");
        try
        {
            using (var output = File.Create(path)) model.WriteGLB(output, new WriteSettings());
            var imported = GltfSceneImporter.Load(path);
            var parent = imported.Scene.Objects.Single(item => item.Name == "TransformParent");
            var child = imported.Scene.Objects.Single(item => item.Name == "TransformChild");
            Assert.Equal(parent.Id, child.ParentId);
            Assert.True(imported.MeshesByNodeId.TryGetValue(child.Id, out var parts));
            Assert.Single(parts!);

            var expectedParent = new Ember.Scene.Transform
            {
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2),
                Scale = new Vector3(2f, 3f, 0.5f)
            };
            var expectedChild = new Ember.Scene.Transform
            {
                Position = new Vector3(0f, 1f, -2f),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.Backward, MathHelper.PiOver4),
                Scale = new Vector3(0.5f, 1.5f, 2f)
            };
            AssertMatrixClose(expectedChild.LocalMatrix * expectedParent.LocalMatrix,
                imported.Scene.GetWorldMatrix(child.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ImportsOpaqueBaseColorFactorsPngTextureAndUntexturedMesh()
    {
        var imported = GltfSceneImporter.Load(FixturePath());
        Assert.Equal(5, imported.Scene.Objects.Count);

        var texturedObject = imported.Scene.Objects.Single(item => item.Name == "TopRightObj");
        var texturedPart = Assert.Single(imported.MeshesByNodeId[texturedObject.Id]);
        AssertClose(new Vector4(0.8f, 0.08f, 0f, 1f), texturedPart.Material.BaseColorFactor);
        Assert.Equal("image/png", texturedPart.Material.BaseColorImageMimeType);
        Assert.True(texturedPart.Material.BaseColorImage.Length > 0);
        Assert.True(texturedPart.Material.DoubleSided);

        var backPlane = imported.Scene.Objects.Single(item => item.Name == "BackPlane");
        var backPlanePart = Assert.Single(imported.MeshesByNodeId[backPlane.Id]);
        Assert.False(backPlanePart.Material.HasBaseColorImage);
        AssertClose(new Vector4(0.16f, 0.16f, 0.16f, 1f), backPlanePart.Material.BaseColorFactor);
        Assert.Equal(Vector2.Zero, backPlanePart.Mesh.Vertices[0].TextureCoordinate0);
    }

    [Fact]
    public void RejectsNonOpaqueMaterialsWithActionableMessage()
    {
        var model = ModelRoot.Load(FixturePath());
        model.LogicalMaterials[4].Alpha = AlphaMode.BLEND;

        var exception = Assert.Throws<NotSupportedException>(() => GltfSceneImporter.Import(model));
        Assert.Contains("only OPAQUE is supported", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TopRightMat", exception.Message, StringComparison.Ordinal);
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");

    private static void AssertClose(Vector4 expected, Vector4 actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0f, 0.00001f);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0f, 0.00001f);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0f, 0.00001f);
        Assert.InRange(Math.Abs(expected.W - actual.W), 0f, 0.00001f);
    }

    private static void AssertMatrixClose(Matrix expected, Matrix actual)
    {
        Assert.InRange(Math.Abs(expected.M11 - actual.M11), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M12 - actual.M12), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M13 - actual.M13), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M21 - actual.M21), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M22 - actual.M22), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M23 - actual.M23), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M31 - actual.M31), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M32 - actual.M32), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M33 - actual.M33), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M41 - actual.M41), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M42 - actual.M42), 0f, 0.0001f);
        Assert.InRange(Math.Abs(expected.M43 - actual.M43), 0f, 0.0001f);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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

    [Fact]
    public void RejectsRequiredExtensionsBeforeImportingScene()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-required-extension-{Guid.NewGuid():N}.gltf");
        File.WriteAllText(path, """
            {
              "asset": { "version": "2.0" },
              "extensionsUsed": [ "KHR_mesh_quantization" ],
              "extensionsRequired": [ "KHR_mesh_quantization" ],
              "scene": 0,
              "scenes": [ {} ]
            }
            """);

        try
        {
            var exception = Assert.Throws<NotSupportedException>(() => GltfSceneImporter.Load(path));
            Assert.Contains("KHR_mesh_quantization", exception.Message, StringComparison.Ordinal);
            Assert.Contains("does not support", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsUnimportedVertexAttributesWithTheirName()
    {
        var model = ModelRoot.Load(FixturePath());
        var primitive = model.LogicalMeshes[0].Primitives[0];
        primitive.SetVertexAccessor("COLOR_0", primitive.GetVertexAccessor("POSITION"));

        var exception = Assert.Throws<NotSupportedException>(() => GltfSceneImporter.Import(model));
        Assert.Contains("COLOR_0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("POSITION, NORMAL, and TEXCOORD_0 only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMorphTargetsInStaticMeshImporter()
    {
        var model = ModelRoot.Load(FixturePath());
        var primitive = model.LogicalMeshes[0].Primitives[0];
        primitive.SetMorphTargetAccessors(0, new Dictionary<string, Accessor>
        {
            ["POSITION"] = primitive.GetVertexAccessor("POSITION")
        });

        var exception = Assert.Throws<NotSupportedException>(() => GltfSceneImporter.Import(model));
        Assert.Contains("Morph targets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FoxFixturePinsRestPoseHierarchyJointCountAndClipDurations()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = Assert.Single(model.LogicalSkins);
        Assert.Equal(24, skin.JointsCount);
        Assert.Equal(24, skin.InverseBindMatrices.Count);
        Assert.Equal("_rootJoint", skin.Skeleton!.Name);
        Assert.Equal("_rootJoint", skin.Joints[0].Name);
        Assert.Equal("b_Root_00", skin.Joints[1].Name);
        Assert.Equal("b_Hip_01", skin.Joints[2].Name);
        Assert.Equal("b_Spine01_02", skin.Joints[3].Name);
        Assert.Equal("b_Root_00", skin.Joints[2].VisualParent!.Name);
        Assert.Equal(System.Numerics.Vector3.Zero, skin.Joints[0].LocalTransform.Translation);
        AssertMatrixClose(Matrix.Identity, skin.InverseBindMatrices[0]);

        var foxNode = model.LogicalNodes.Single(node => node.Name == "fox");
        Assert.Equal(skin.LogicalIndex, foxNode.Skin!.LogicalIndex);
        var primitive = Assert.Single(foxNode.Mesh!.Primitives);
        Assert.Contains("JOINTS_0", primitive.VertexAccessors.Keys);
        Assert.Contains("WEIGHTS_0", primitive.VertexAccessors.Keys);

        Assert.Collection(model.LogicalAnimations,
            animation => AssertAnimation(animation, "Survey", 3.416667f),
            animation => AssertAnimation(animation, "Walk", 0.7083333f),
            animation => AssertAnimation(animation, "Run", 1.1583333f));
    }

    [Fact]
    public void ImportsFoxSkinInSkinOrderWithRestHierarchyBindMatricesAndMeshNodeTransform()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var originalMeshNode = model.LogicalNodes.Single(node => node.Name == "fox");
        var sourceMesh = originalMeshNode.Mesh!;
        var sourceSkin = originalMeshNode.Skin!;
        originalMeshNode.Mesh = null;
        originalMeshNode.Skin = null;
        var meshParent = model.DefaultScene!.CreateNode("MeshParent");
        meshParent.LocalTransform = meshParent.LocalTransform
            .WithTranslation(new System.Numerics.Vector3(4f, 5f, 6f));
        var meshNode = meshParent.CreateNode("fox-transformed");
        meshNode.Mesh = sourceMesh;
        meshNode.Skin = sourceSkin;

        var skin = GltfSkinData.Import(model, meshNode);

        Assert.Equal(meshNode.Skin!.LogicalIndex, skin.SkinIndex);
        Assert.Equal(27, skin.Nodes.Count);
        Assert.Equal(24, skin.JointNodeIndices.Count);
        Assert.Equal(24, skin.InverseBindMatrices.Count);
        Assert.Equal("fox-transformed", skin.Nodes[skin.MeshNodeIndex].Name);
        Assert.Equal(Vector3.Zero, skin.Nodes[skin.MeshNodeIndex].RestTransform.Position);
        Assert.Equal(4f, skin.MeshNodeRestWorldMatrix.M41);
        Assert.Equal(5f, skin.MeshNodeRestWorldMatrix.M42);
        Assert.Equal(6f, skin.MeshNodeRestWorldMatrix.M43);
        AssertMatrixClose(Matrix.Identity, skin.InverseBindMatrices[0]);

        var hipIndex = skin.JointNodeIndices[2];
        var hip = skin.Nodes[hipIndex];
        Assert.Equal("b_Hip_01", hip.Name);
        Assert.True(hip.IsJoint);
        Assert.NotNull(hip.ParentNodeIndex);
        Assert.Equal("b_Root_00", skin.Nodes[hip.ParentNodeIndex!.Value].Name);
        Assert.InRange(MathF.Abs(hip.RestTransform.Position.Y - 26.7484f), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(hip.RestTransform.Position.Z - 42.9382f), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(skin.InverseBindMatrices[2].M41 - (-30.63603f)), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(skin.InverseBindMatrices[2].M42 - (-40.25664f)), 0f, 0.0001f);
    }

    [Fact]
    public void ImportsAndNormalizesFourFoxInfluencesPerVertex()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var primitive = model.LogicalNodes.Single(node => node.Name == "fox").Mesh!.Primitives[0];
        var weightAccessor = primitive.GetVertexAccessor("WEIGHTS_0");
        weightAccessor.AsVector4Array()[0] = new System.Numerics.Vector4(2f, 1f, 0f, 0f);

        var weights = GltfSkinWeightData.Import(primitive, expectedVertexCount: 1728, jointCount: 24);

        Assert.Equal(1728, weights.VertexCount);
        var first = weights.Vertices[0];
        Assert.InRange(MathF.Abs(first.Weights.X - (2f / 3f)), 0f, 0.00001f);
        Assert.InRange(MathF.Abs(first.Weights.Y - (1f / 3f)), 0f, 0.00001f);
        Assert.Equal(1f, first.Weights.X + first.Weights.Y + first.Weights.Z + first.Weights.W);
        for (var vertex = 0; vertex < weights.VertexCount; vertex++)
        {
            var influence = weights.Vertices[vertex];
            var sum = influence.Weights.X + influence.Weights.Y + influence.Weights.Z + influence.Weights.W;
            Assert.InRange(MathF.Abs(sum - 1f), 0f, 0.00001f);
            for (var slot = 0; slot < 4; slot++)
                Assert.InRange(influence.GetJointIndex(slot), 0, 23);
        }
    }

    [Fact]
    public void RejectsOutOfRangeJointReferencesAndInfluenceSetsAboveFour()
    {
        var outOfRangeModel = ModelRoot.Load(FoxFixturePath());
        var outOfRangePrimitive = outOfRangeModel.LogicalNodes.Single(node => node.Name == "fox").Mesh!.Primitives[0];
        outOfRangePrimitive.GetVertexAccessor("JOINTS_0").AsVector4Array()[0] =
            new System.Numerics.Vector4(24f, 0f, 0f, 0f);

        var indexException = Assert.Throws<InvalidDataException>(() =>
            GltfSkinWeightData.Import(outOfRangePrimitive, expectedVertexCount: 1728, jointCount: 24));
        Assert.Contains("references joint 24", indexException.Message, StringComparison.Ordinal);

        var extraSetModel = ModelRoot.Load(FoxFixturePath());
        var extraSetPrimitive = extraSetModel.LogicalNodes.Single(node => node.Name == "fox").Mesh!.Primitives[0];
        extraSetPrimitive.SetVertexAccessor("JOINTS_1", extraSetPrimitive.GetVertexAccessor("JOINTS_0"));

        var extraSetException = Assert.Throws<NotSupportedException>(() =>
            GltfSkinWeightData.Import(extraSetPrimitive, expectedVertexCount: 1728, jointCount: 24));
        Assert.Contains("four joint influences", extraSetException.Message, StringComparison.Ordinal);
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");
    private static string FoxFixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "Fox.glb");

    private static void AssertAnimation(Animation animation, string name, float expectedDuration)
    {
        Assert.Equal(name, animation.Name);
        Assert.InRange(MathF.Abs(expectedDuration - animation.Duration), 0f, 0.00001f);
    }

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

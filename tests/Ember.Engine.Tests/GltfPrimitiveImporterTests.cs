using System;
using System.IO;
using System.Linq;
using Ember.Assets;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class GltfPrimitiveImporterTests
{
    [Fact]
    public void SharpGltfReadsFixtureNodesMeshesAndAuthoredTransforms()
    {
        var model = ModelRoot.Load(FixturePath());

        Assert.Single(model.LogicalScenes);
        Assert.Equal(5, model.LogicalNodes.Count);
        Assert.Equal(5, model.LogicalMeshes.Count);
        Assert.Equal(new[] { "BackPlane", "BottomLeftObj", "BottomRightObj", "TopLeftObj", "TopRightObj" },
            model.LogicalNodes.Select(node => node.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        foreach (var node in model.LogicalNodes)
        {
            Assert.NotNull(node.Mesh);
            Assert.Single(node.Mesh!.Primitives);
            Assert.Equal(System.Numerics.Matrix4x4.Identity, node.LocalMatrix);
            Assert.Equal(System.Numerics.Matrix4x4.Identity, node.WorldMatrix);
        }

        var primitive = model.LogicalMeshes[0].Primitives[0];
        Assert.Equal(PrimitiveType.TRIANGLES, primitive.DrawPrimitiveType);
        Assert.Equal(4, primitive.GetVertexAccessor("POSITION").Count);
        Assert.Equal(6, primitive.GetIndices().Count);
        Assert.Contains("TEXCOORD_0", primitive.VertexAccessors.Keys);
    }

    [Fact]
    public void ImportsPositionsNormalsUv0AndTriangleIndicesIntoEngineData()
    {
        var model = ModelRoot.Load(FixturePath());
        var mesh = GltfPrimitiveImporter.Import(model.LogicalMeshes[0].Primitives[0]);

        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 1, 0 }, mesh.TriangleIndices);
        AssertClose(new Vector3(1.2f, 0.2f, 0f), mesh.Vertices[0].Position);
        AssertClose(new Vector3(0f, 0f, 1f), mesh.Vertices[0].Normal);
        AssertClose(new Vector2(1f, 0.4f), mesh.Vertices[0].TextureCoordinate0);
        AssertClose(new Vector3(0.2f, 1.2f, 0f), mesh.Vertices[1].Position);
        AssertClose(new Vector2(0.6f, 0f), mesh.Vertices[1].TextureCoordinate0);
    }

    [Fact]
    public void RejectsNonTriangleTopologyAndMissingRequiredAttributes()
    {
        var model = ModelRoot.Load(FixturePath());
        var linePrimitive = model.LogicalMeshes[0].Primitives[0];
        linePrimitive.DrawPrimitiveType = PrimitiveType.LINES;
        Assert.Throws<NotSupportedException>(() => GltfPrimitiveImporter.Import(linePrimitive));

        var missingUv = ModelRoot.Load(FixturePath()).LogicalMeshes[3].Primitives[0];
        var exception = Assert.Throws<NotSupportedException>(() => GltfPrimitiveImporter.Import(missingUv));
        Assert.Contains("TEXCOORD_0", exception.Message, StringComparison.Ordinal);
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0f, 0.00001f);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0f, 0.00001f);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0f, 0.00001f);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0f, 0.00001f);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0f, 0.00001f);
    }
}

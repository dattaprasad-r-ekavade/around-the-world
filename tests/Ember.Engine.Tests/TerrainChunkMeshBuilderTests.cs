using System;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class TerrainChunkMeshBuilderTests
{
    [Fact]
    public void AdjacentChunksShareExactBorderPositionsMaterialsAndTextureCoordinates()
    {
        var source = new SlopedTerrainSource();
        var settings = new TerrainChunkSettings(32f, 4f, 8f);
        var left = TerrainChunkMeshBuilder.Build(source, 0, -1, settings);
        var right = TerrainChunkMeshBuilder.Build(source, 1, -1, settings);

        Assert.Equal(9, left.VertexStride);
        Assert.Equal(81, left.Vertices.Count);
        Assert.Equal(8 * 8 * 6, left.TriangleIndices.Count);
        for (var row = 0; row < left.VertexStride; row++)
        {
            var leftEdge = left.Vertices[row * left.VertexStride + left.VertexStride - 1];
            var rightEdge = right.Vertices[row * right.VertexStride];
            Assert.Equal(leftEdge.Position, rightEdge.Position);
            Assert.Equal(leftEdge.Tint, rightEdge.Tint);
            Assert.Equal(leftEdge.TextureCoordinate, rightEdge.TextureCoordinate);
        }
    }

    [Fact]
    public void GridBuildIsDeterministicAndRequiresAlignedChunkSpacing()
    {
        var settings = new TerrainChunkSettings(32f, 4f);
        var first = TerrainChunkMeshBuilder.Build(new SlopedTerrainSource(), -2, 3, settings);
        var second = TerrainChunkMeshBuilder.Build(new SlopedTerrainSource(), -2, 3, settings);
        Assert.Equal(first.Vertices, second.Vertices);
        Assert.Equal(first.TriangleIndices, second.TriangleIndices);

        Assert.Throws<ArgumentException>(() =>
            TerrainChunkMeshBuilder.Build(new SlopedTerrainSource(), 0, 0, new TerrainChunkSettings(30f, 4f)));
    }

    private sealed class SlopedTerrainSource : ITerrainHeightMaterialSource
    {
        public TerrainSurfaceSample Sample(float worldX, float worldZ)
        {
            var height = MathF.Sin(worldX * 0.01f) + MathF.Cos(worldZ * 0.02f);
            var red = (byte)(80 + (MathF.Abs(worldX) % 100f));
            var green = (byte)(70 + (MathF.Abs(worldZ) % 100f));
            return new TerrainSurfaceSample(height, new Color(red, green, 45));
        }
    }
}

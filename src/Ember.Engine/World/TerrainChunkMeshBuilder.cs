using System;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;

namespace Ember.World;

/// <summary>Game-owned source for world-space terrain height and vertex material tint.</summary>
public interface ITerrainHeightMaterialSource
{
    TerrainSurfaceSample Sample(float worldX, float worldZ);
}

public readonly record struct TerrainSurfaceSample(float Height, Color Tint);

public readonly record struct TerrainChunkVertex(
    Vector3 Position, Vector3 Normal, Color Tint, Vector2 TextureCoordinate);

/// <summary>Sampling and texturing settings shared by terrain mesh consumers.</summary>
public sealed record TerrainChunkSettings(float ChunkSize, float VertexSpacing, float TextureRepeatMetres = 6f)
{
    public int GetVertexStride()
    {
        if (!float.IsFinite(ChunkSize) || ChunkSize <= 0f
            || !float.IsFinite(VertexSpacing) || VertexSpacing <= 0f
            || !float.IsFinite(TextureRepeatMetres) || TextureRepeatMetres <= 0f)
            throw new ArgumentOutOfRangeException(nameof(ChunkSize), "Terrain chunk dimensions and texture scale must be finite and positive.");

        var intervals = ChunkSize / VertexSpacing;
        var roundedIntervals = MathF.Round(intervals);
        if (!float.IsFinite(intervals) || roundedIntervals < 1f
            || MathF.Abs(intervals - roundedIntervals) > 1e-4f)
            throw new ArgumentException("Terrain chunk size must be an integer multiple of vertex spacing.");
        if (roundedIntervals > 2047f)
            throw new ArgumentOutOfRangeException(nameof(VertexSpacing), "Terrain chunks may contain at most 2048 vertices per side.");
        return (int)roundedIntervals + 1;
    }
}

public sealed class TerrainChunkMeshData
{
    internal TerrainChunkMeshData(int chunkX, int chunkZ, int vertexStride,
        TerrainChunkVertex[] vertices, int[] triangleIndices)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        VertexStride = vertexStride;
        Vertices = Array.AsReadOnly(vertices);
        TriangleIndices = Array.AsReadOnly(triangleIndices);
    }

    public int ChunkX { get; }
    public int ChunkZ { get; }
    public int VertexStride { get; }
    public ReadOnlyCollection<TerrainChunkVertex> Vertices { get; }
    public ReadOnlyCollection<int> TriangleIndices { get; }
}

/// <summary>Builds deterministic grid meshes whose border vertices sample identical world positions.</summary>
public static class TerrainChunkMeshBuilder
{
    public static TerrainChunkMeshData Build(ITerrainHeightMaterialSource source,
        int chunkX, int chunkZ, TerrainChunkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        var stride = settings.GetVertexStride();
        return new TerrainChunkMeshData(chunkX, chunkZ, stride,
            BuildVertices(source, chunkX, chunkZ, settings), BuildTriangleIndices(stride));
    }

    public static TerrainChunkVertex[] BuildVertices(ITerrainHeightMaterialSource source,
        int chunkX, int chunkZ, TerrainChunkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        var stride = settings.GetVertexStride();
        var vertices = new TerrainChunkVertex[stride * stride];
        var firstGridX = (long)chunkX * (stride - 1L);
        var firstGridZ = (long)chunkZ * (stride - 1L);
        for (var z = 0; z < stride; z++)
        for (var x = 0; x < stride; x++)
        {
            var worldX = (float)((firstGridX + x) * (double)settings.VertexSpacing);
            var worldZ = (float)((firstGridZ + z) * (double)settings.VertexSpacing);
            if (!float.IsFinite(worldX) || !float.IsFinite(worldZ))
                throw new ArgumentOutOfRangeException(nameof(chunkX), "Terrain chunk coordinates exceed finite world space.");
            var sample = source.Sample(worldX, worldZ);
            if (!float.IsFinite(sample.Height))
                throw new InvalidOperationException($"Terrain source returned a nonfinite height at ({worldX}, {worldZ}).");
            var spacing = settings.VertexSpacing;
            var left = SampleHeight(source, worldX - spacing, worldZ);
            var right = SampleHeight(source, worldX + spacing, worldZ);
            var back = SampleHeight(source, worldX, worldZ - spacing);
            var front = SampleHeight(source, worldX, worldZ + spacing);
            var tangentX = new Vector3(spacing * 2f, right - left, 0f);
            var tangentZ = new Vector3(0f, front - back, spacing * 2f);
            var normal = Vector3.Cross(tangentZ, tangentX);
            normal.Normalize();
            vertices[z * stride + x] = new TerrainChunkVertex(
                new Vector3(worldX, sample.Height, worldZ), normal, sample.Tint,
                new Vector2(worldX / settings.TextureRepeatMetres, worldZ / settings.TextureRepeatMetres));
        }

        return vertices;
    }

    public static int[] BuildTriangleIndices(int vertexStride)
    {
        if (vertexStride < 2 || vertexStride > 2048)
            throw new ArgumentOutOfRangeException(nameof(vertexStride));
        var indices = new int[(vertexStride - 1) * (vertexStride - 1) * 6];
        var index = 0;
        for (var z = 0; z < vertexStride - 1; z++)
        for (var x = 0; x < vertexStride - 1; x++)
        {
            var corner = z * vertexStride + x;
            indices[index++] = corner;
            indices[index++] = corner + 1;
            indices[index++] = corner + vertexStride;
            indices[index++] = corner + 1;
            indices[index++] = corner + vertexStride + 1;
            indices[index++] = corner + vertexStride;
        }

        return indices;
    }

    private static float SampleHeight(ITerrainHeightMaterialSource source, float worldX, float worldZ)
    {
        var height = source.Sample(worldX, worldZ).Height;
        if (!float.IsFinite(height))
            throw new InvalidOperationException($"Terrain source returned a nonfinite height at ({worldX}, {worldZ}).");
        return height;
    }
}

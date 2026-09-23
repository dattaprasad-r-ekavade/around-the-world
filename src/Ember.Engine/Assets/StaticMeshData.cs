using System;
using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;

namespace Ember.Assets;

public readonly struct StaticMeshVertex
{
    public StaticMeshVertex(Vector3 position, Vector3 normal, Vector2 textureCoordinate0)
    {
        Position = position;
        Normal = normal;
        TextureCoordinate0 = textureCoordinate0;
    }

    public Vector3 Position { get; }
    public Vector3 Normal { get; }
    public Vector2 TextureCoordinate0 { get; }
}

public sealed class StaticMeshData
{
    private readonly ReadOnlyCollection<StaticMeshVertex> _vertices;
    private readonly ReadOnlyCollection<int> _triangleIndices;

    internal StaticMeshData(StaticMeshVertex[] vertices, int[] triangleIndices)
    {
        if (vertices is null) throw new ArgumentNullException(nameof(vertices));
        if (triangleIndices is null) throw new ArgumentNullException(nameof(triangleIndices));
        if (triangleIndices.Length % 3 != 0)
            throw new ArgumentException("Triangle index count must be divisible by three.", nameof(triangleIndices));

        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) || !IsFinite(vertex.TextureCoordinate0))
                throw new ArgumentException($"Vertex {i} contains a nonfinite value.", nameof(vertices));
        }

        for (var i = 0; i < triangleIndices.Length; i++)
        {
            if ((uint)triangleIndices[i] >= (uint)vertices.Length)
                throw new ArgumentOutOfRangeException(nameof(triangleIndices), $"Index {i} refers to missing vertex {triangleIndices[i]}.");
        }

        _vertices = Array.AsReadOnly((StaticMeshVertex[])vertices.Clone());
        _triangleIndices = Array.AsReadOnly((int[])triangleIndices.Clone());
    }

    public ReadOnlyCollection<StaticMeshVertex> Vertices => _vertices;
    public ReadOnlyCollection<int> TriangleIndices => _triangleIndices;

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

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

/// <summary>An axis-aligned 3D box. Transform uses all eight corners to enclose rotated bounds.</summary>
public readonly struct Bounds3
{
    public Bounds3(Vector3 min, Vector3 max)
    {
        if (!IsFinite(min) || !IsFinite(max))
            throw new ArgumentException("Bounds must contain finite coordinates.");
        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            throw new ArgumentException("Bounds minimum must not exceed its maximum.");

        Min = min;
        Max = max;
    }

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    public Bounds3 Encapsulate(Bounds3 other) => new(
        Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public Bounds3 Transform(Matrix matrix)
    {
        var first = Vector3.Transform(new Vector3(Min.X, Min.Y, Min.Z), matrix);
        var min = first;
        var max = first;
        for (var corner = 1; corner < 8; corner++)
        {
            var point = Vector3.Transform(new Vector3(
                (corner & 1) == 0 ? Min.X : Max.X,
                (corner & 2) == 0 ? Min.Y : Max.Y,
                (corner & 4) == 0 ? Min.Z : Max.Z), matrix);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        return new Bounds3(min, max);
    }

    public bool Contains(Vector3 point, float tolerance = 0f) =>
        point.X >= Min.X - tolerance && point.X <= Max.X + tolerance
        && point.Y >= Min.Y - tolerance && point.Y <= Max.Y + tolerance
        && point.Z >= Min.Z - tolerance && point.Z <= Max.Z + tolerance;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
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

        Bounds3? localBounds = null;
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) || !IsFinite(vertex.TextureCoordinate0))
                throw new ArgumentException($"Vertex {i} contains a nonfinite value.", nameof(vertices));

            var pointBounds = new Bounds3(vertex.Position, vertex.Position);
            localBounds = localBounds is { } current ? current.Encapsulate(pointBounds) : pointBounds;
        }

        for (var i = 0; i < triangleIndices.Length; i++)
        {
            if ((uint)triangleIndices[i] >= (uint)vertices.Length)
                throw new ArgumentOutOfRangeException(nameof(triangleIndices), $"Index {i} refers to missing vertex {triangleIndices[i]}.");
        }

        _vertices = Array.AsReadOnly((StaticMeshVertex[])vertices.Clone());
        _triangleIndices = Array.AsReadOnly((int[])triangleIndices.Clone());
        LocalBounds = localBounds;
    }

    public ReadOnlyCollection<StaticMeshVertex> Vertices => _vertices;
    public ReadOnlyCollection<int> TriangleIndices => _triangleIndices;
    public Bounds3? LocalBounds { get; }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

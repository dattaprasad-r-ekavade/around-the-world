using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Scene;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public readonly struct GltfSkinnedVertex
{
    internal GltfSkinnedVertex(Vector3 position, Vector3 normal, Vector2 textureCoordinate0,
        GltfVertexJointWeights jointWeights)
    {
        Position = position;
        Normal = normal;
        TextureCoordinate0 = textureCoordinate0;
        JointWeights = jointWeights;
    }

    public Vector3 Position { get; }
    public Vector3 Normal { get; }
    public Vector2 TextureCoordinate0 { get; }
    public GltfVertexJointWeights JointWeights { get; }
}

/// <summary>Immutable CPU vertex data for one skinned triangle primitive.</summary>
public sealed class GltfSkinnedMeshData
{
    private readonly IReadOnlyList<GltfSkinnedVertex> _vertices;
    private readonly IReadOnlyList<int> _triangleIndices;

    private GltfSkinnedMeshData(GltfSkinnedVertex[] vertices, int[] triangleIndices, Bounds3 bounds)
    {
        _vertices = Array.AsReadOnly(vertices);
        _triangleIndices = Array.AsReadOnly(triangleIndices);
        LocalBounds = bounds;
    }

    public IReadOnlyList<GltfSkinnedVertex> Vertices => _vertices;
    public IReadOnlyList<int> TriangleIndices => _triangleIndices;
    public Bounds3 LocalBounds { get; }

    public static GltfSkinnedMeshData Import(MeshPrimitive primitive, GltfSkinData skin, bool hasBaseColorImage)
    {
        if (primitive is null) throw new ArgumentNullException(nameof(primitive));
        if (skin is null) throw new ArgumentNullException(nameof(skin));
        if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
            throw new NotSupportedException($"Primitive topology '{primitive.DrawPrimitiveType}' is not supported; only TRIANGLES is supported.");

        var unsupportedAttribute = primitive.VertexAccessors.Keys.FirstOrDefault(semantic =>
            semantic != "POSITION" && semantic != "NORMAL" && semantic != "TEXCOORD_0"
            && semantic != "JOINTS_0" && semantic != "WEIGHTS_0");
        if (unsupportedAttribute is not null)
            throw new NotSupportedException($"Skinned primitive vertex attribute '{unsupportedAttribute}' is not supported.");

        if (!primitive.VertexAccessors.TryGetValue("POSITION", out var positionAccessor))
            throw new NotSupportedException("Skinned primitive is missing required vertex attribute 'POSITION'.");
        if (!primitive.VertexAccessors.TryGetValue("TEXCOORD_0", out var textureCoordinateAccessor)
            && hasBaseColorImage)
        {
            throw new NotSupportedException("A textured skinned primitive must provide TEXCOORD_0.");
        }
        if (textureCoordinateAccessor is not null && textureCoordinateAccessor.Count != positionAccessor.Count)
            throw new InvalidDataException("POSITION and TEXCOORD_0 must have the same vertex count when present.");

        var positions = positionAccessor.AsVector3Array();
        var textureCoordinates = textureCoordinateAccessor?.AsVector2Array();
        var weights = GltfSkinWeightData.Import(primitive, positions.Count, skin.JointNodeIndices.Count);
        var indices = ReadTriangleIndices(primitive, positions.Count);
        var normals = primitive.VertexAccessors.TryGetValue("NORMAL", out var normalAccessor)
            ? ReadNormals(normalAccessor, positions.Count)
            : CalculateNormals(positions, indices);

        var vertices = new GltfSkinnedVertex[positions.Count];
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < vertices.Length; i++)
        {
            var sourcePosition = positions[i];
            var position = new Vector3(sourcePosition.X, sourcePosition.Y, sourcePosition.Z);
            if (!IsFinite(position)) throw new InvalidDataException($"Skinned POSITION vertex {i} contains a nonfinite value.");
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);

            var sourceUv = textureCoordinates is null ? default : textureCoordinates[i];
            var uv = new Vector2(sourceUv.X, sourceUv.Y);
            if (!IsFinite(uv)) throw new InvalidDataException($"Skinned TEXCOORD_0 vertex {i} contains a nonfinite value.");
            vertices[i] = new GltfSkinnedVertex(position, normals[i], uv, weights.Vertices[i]);
        }

        return new GltfSkinnedMeshData(vertices, indices, new Bounds3(minimum, maximum));
    }

    private static int[] ReadTriangleIndices(MeshPrimitive primitive, int vertexCount)
    {
        if (vertexCount == 0) throw new InvalidDataException("Skinned primitive has no vertices.");

        int[] indices;
        if (primitive.IndexAccessor is null)
        {
            indices = Enumerable.Range(0, vertexCount).ToArray();
        }
        else
        {
            var sourceIndices = primitive.GetIndices();
            indices = new int[sourceIndices.Count];
            for (var i = 0; i < indices.Length; i++)
            {
                if (sourceIndices[i] > int.MaxValue)
                    throw new InvalidDataException($"Triangle index {i} exceeds Ember's supported index range.");
                indices[i] = (int)sourceIndices[i];
            }
        }

        if (indices.Length == 0 || indices.Length % 3 != 0)
            throw new InvalidDataException($"Triangle index count must be a nonzero multiple of three; found {indices.Length}.");
        for (var i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= (uint)vertexCount)
                throw new InvalidDataException($"Triangle index {i} refers to missing vertex {indices[i]}.");
        }

        return indices;
    }

    private static Vector3[] ReadNormals(Accessor accessor, int vertexCount)
    {
        if (accessor.Count != vertexCount)
            throw new InvalidDataException($"NORMAL must contain {vertexCount} entries; found {accessor.Count}.");

        var source = accessor.AsVector3Array();
        var normals = new Vector3[vertexCount];
        for (var i = 0; i < normals.Length; i++)
        {
            var normal = source[i];
            normals[i] = NormalizeNormal(new Vector3(normal.X, normal.Y, normal.Z), i);
        }

        return normals;
    }

    private static Vector3[] CalculateNormals(IReadOnlyList<System.Numerics.Vector3> positions, int[] indices)
    {
        var normals = new Vector3[positions.Count];
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = ToXna(positions[indices[i]]);
            var b = ToXna(positions[indices[i + 1]]);
            var c = ToXna(positions[indices[i + 2]]);
            var faceNormal = Vector3.Cross(b - a, c - a);
            if (faceNormal.LengthSquared() <= 0.0000001f) continue;

            normals[indices[i]] += faceNormal;
            normals[indices[i + 1]] += faceNormal;
            normals[indices[i + 2]] += faceNormal;
        }

        for (var i = 0; i < normals.Length; i++)
            normals[i] = normals[i].LengthSquared() <= 0.0000001f
                ? Vector3.Up
                : Vector3.Normalize(normals[i]);
        return normals;
    }

    private static Vector3 NormalizeNormal(Vector3 value, int index)
    {
        if (!IsFinite(value)) throw new InvalidDataException($"NORMAL vertex {index} contains a nonfinite value.");
        if (value.LengthSquared() <= 0.0000001f)
            throw new InvalidDataException($"NORMAL vertex {index} has zero length.");
        return Vector3.Normalize(value);
    }

    private static Vector3 ToXna(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

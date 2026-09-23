using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public readonly record struct GltfVertexJointWeights(
    int Joint0, int Joint1, int Joint2, int Joint3, Vector4 Weights)
{
    public int GetJointIndex(int influence) => influence switch
    {
        0 => Joint0,
        1 => Joint1,
        2 => Joint2,
        3 => Joint3,
        _ => throw new ArgumentOutOfRangeException(nameof(influence), "Ember stores four influences per vertex.")
    };

    public float GetWeight(int influence) => influence switch
    {
        0 => Weights.X,
        1 => Weights.Y,
        2 => Weights.Z,
        3 => Weights.W,
        _ => throw new ArgumentOutOfRangeException(nameof(influence), "Ember stores four influences per vertex.")
    };
}

/// <summary>Immutable, normalized JOINTS_0/WEIGHTS_0 data: at most four influences per vertex.</summary>
public sealed class GltfSkinWeightData
{
    private GltfSkinWeightData(IReadOnlyList<GltfVertexJointWeights> vertices)
    {
        Vertices = vertices;
    }

    public IReadOnlyList<GltfVertexJointWeights> Vertices { get; }
    public int VertexCount => Vertices.Count;

    public static GltfSkinWeightData Import(MeshPrimitive primitive, int expectedVertexCount, int jointCount)
    {
        if (primitive is null) throw new ArgumentNullException(nameof(primitive));
        if (expectedVertexCount < 0) throw new ArgumentOutOfRangeException(nameof(expectedVertexCount));
        if (jointCount <= 0) throw new ArgumentOutOfRangeException(nameof(jointCount));

        var extraSet = primitive.VertexAccessors.Keys.FirstOrDefault(semantic =>
            (semantic.StartsWith("JOINTS_", StringComparison.Ordinal) && semantic != "JOINTS_0")
            || (semantic.StartsWith("WEIGHTS_", StringComparison.Ordinal) && semantic != "WEIGHTS_0"));
        if (extraSet is not null)
            throw new NotSupportedException(
                $"Vertex attribute '{extraSet}' exceeds Ember's limit of four joint influences per vertex (JOINTS_0/WEIGHTS_0 only).");

        primitive.VertexAccessors.TryGetValue("JOINTS_0", out var jointAccessor);
        primitive.VertexAccessors.TryGetValue("WEIGHTS_0", out var weightAccessor);
        if ((jointAccessor is null) != (weightAccessor is null))
            throw new InvalidDataException("A skinned primitive must provide both JOINTS_0 and WEIGHTS_0.");
        if (jointAccessor is null || weightAccessor is null)
            throw new NotSupportedException("The primitive has no JOINTS_0/WEIGHTS_0 skin influences.");

        ValidateJointAccessor(jointAccessor);
        ValidateWeightAccessor(weightAccessor);
        if (jointAccessor.Count != expectedVertexCount || weightAccessor.Count != expectedVertexCount)
            throw new InvalidDataException(
                $"JOINTS_0 and WEIGHTS_0 must each contain {expectedVertexCount} entries; found {jointAccessor.Count} and {weightAccessor.Count}.");

        var jointValues = jointAccessor.AsVector4Array();
        var weightValues = weightAccessor.AsVector4Array();
        var vertices = new GltfVertexJointWeights[expectedVertexCount];
        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            var rawJoints = jointValues[vertexIndex];
            var joints = new int[4];
            for (var influence = 0; influence < 4; influence++)
            {
                var value = GetComponent(rawJoints, influence);
                if (!float.IsFinite(value) || value < 0f || value > int.MaxValue || value != MathF.Truncate(value))
                    throw new InvalidDataException(
                        $"JOINTS_0 vertex {vertexIndex}, influence {influence} must be a nonnegative integer.");

                joints[influence] = (int)value;
                if (joints[influence] >= jointCount)
                    throw new InvalidDataException(
                        $"JOINTS_0 vertex {vertexIndex}, influence {influence} references joint {joints[influence]}, but the skin has {jointCount} joints.");
            }

            var weights = weightValues[vertexIndex];
            if (!float.IsFinite(weights.X) || !float.IsFinite(weights.Y)
                || !float.IsFinite(weights.Z) || !float.IsFinite(weights.W)
                || weights.X < 0f || weights.Y < 0f || weights.Z < 0f || weights.W < 0f)
            {
                throw new InvalidDataException($"WEIGHTS_0 vertex {vertexIndex} must contain finite, nonnegative values.");
            }

            var total = (double)weights.X + weights.Y + weights.Z + weights.W;
            if (total <= 0d)
                throw new InvalidDataException($"WEIGHTS_0 vertex {vertexIndex} has no positive influence weight.");

            var normalized = new Vector4(
                (float)(weights.X / total),
                (float)(weights.Y / total),
                (float)(weights.Z / total),
                (float)(weights.W / total));
            vertices[vertexIndex] = new GltfVertexJointWeights(
                joints[0], joints[1], joints[2], joints[3], normalized);
        }

        return new GltfSkinWeightData(Array.AsReadOnly(vertices));
    }

    private static void ValidateJointAccessor(Accessor accessor)
    {
        if (accessor.Dimensions != DimensionType.VEC4
            || (accessor.Encoding != EncodingType.UNSIGNED_BYTE && accessor.Encoding != EncodingType.UNSIGNED_SHORT)
            || accessor.Normalized)
        {
            throw new NotSupportedException(
                "JOINTS_0 must be a non-normalized VEC4 of UNSIGNED_BYTE or UNSIGNED_SHORT values.");
        }
    }

    private static void ValidateWeightAccessor(Accessor accessor)
    {
        var validEncoding = (accessor.Encoding == EncodingType.FLOAT && !accessor.Normalized)
            || (accessor.Normalized
                && (accessor.Encoding == EncodingType.UNSIGNED_BYTE || accessor.Encoding == EncodingType.UNSIGNED_SHORT));
        if (accessor.Dimensions != DimensionType.VEC4 || !validEncoding)
        {
            throw new NotSupportedException(
                "WEIGHTS_0 must be a VEC4 of FLOAT or normalized UNSIGNED_BYTE/UNSIGNED_SHORT values.");
        }
    }

    private static float GetComponent(Vector4 value, int component) => component switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        3 => value.W,
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };
}

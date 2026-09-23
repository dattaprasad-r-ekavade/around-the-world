using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public static class GltfPrimitiveImporter
{
    public static StaticMeshData Import(MeshPrimitive primitive, bool allowMissingTextureCoordinates = false)
    {
        if (primitive is null) throw new ArgumentNullException(nameof(primitive));
        if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
            throw new NotSupportedException($"Primitive topology '{primitive.DrawPrimitiveType}' is not supported; only TRIANGLES is supported.");

        var positionAccessor = GetRequiredAccessor(primitive, "POSITION");
        var normalAccessor = GetRequiredAccessor(primitive, "NORMAL");
        primitive.VertexAccessors.TryGetValue("TEXCOORD_0", out var textureCoordinateAccessor);
        if (textureCoordinateAccessor is null && !allowMissingTextureCoordinates)
            throw new NotSupportedException("Primitive is missing required vertex attribute 'TEXCOORD_0'.");

        var positions = positionAccessor.AsVector3Array();
        var normals = normalAccessor.AsVector3Array();
        var textureCoordinates = textureCoordinateAccessor?.AsVector2Array();
        if (normals.Count != positions.Count || (textureCoordinates is not null && textureCoordinates.Count != positions.Count))
            throw new InvalidDataException("POSITION, NORMAL, and TEXCOORD_0 must have the same vertex count when present.");

        var vertices = new StaticMeshVertex[positions.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var position = positions[i];
            var normal = normals[i];
            var textureCoordinate = textureCoordinates is null ? default : textureCoordinates[i];
            vertices[i] = new StaticMeshVertex(
                new Vector3(position.X, position.Y, position.Z),
                new Vector3(normal.X, normal.Y, normal.Z),
                new Vector2(textureCoordinate.X, textureCoordinate.Y));
        }

        int[] indices;
        if (primitive.IndexAccessor is null)
        {
            indices = Enumerable.Range(0, vertices.Length).ToArray();
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

        if (indices.Length % 3 != 0)
            throw new InvalidDataException($"Triangle index count must be divisible by three; found {indices.Length}.");

        try
        {
            return new StaticMeshData(vertices, indices);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The primitive contains invalid mesh data.", exception);
        }
    }

    private static Accessor GetRequiredAccessor(MeshPrimitive primitive, string semantic)
    {
        if (!primitive.VertexAccessors.TryGetValue(semantic, out var accessor))
            throw new NotSupportedException($"Primitive is missing required vertex attribute '{semantic}'.");

        return accessor;
    }
}

using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public enum GltfAlphaMode
{
    Opaque,
    Mask
}

public sealed class GltfMaterialData
{
    private readonly byte[]? _baseColorImage;

    private GltfMaterialData(string name, Vector4 baseColorFactor, bool doubleSided,
        GltfAlphaMode alphaMode, float alphaCutoff, int? imageIndex,
        string? imageMimeType, byte[]? baseColorImage)
    {
        Name = name;
        BaseColorFactor = baseColorFactor;
        DoubleSided = doubleSided;
        AlphaMode = alphaMode;
        AlphaCutoff = alphaCutoff;
        BaseColorImageIndex = imageIndex;
        BaseColorImageMimeType = imageMimeType;
        _baseColorImage = baseColorImage;
    }

    public string Name { get; }
    public Vector4 BaseColorFactor { get; }
    public bool DoubleSided { get; }
    public GltfAlphaMode AlphaMode { get; }
    public float AlphaCutoff { get; }
    public int? BaseColorImageIndex { get; }
    public string? BaseColorImageMimeType { get; }
    public bool HasBaseColorImage => _baseColorImage is not null;
    public ReadOnlyMemory<byte> BaseColorImage => _baseColorImage ?? Array.Empty<byte>();

    public static GltfMaterialData Import(Material? material)
    {
        if (material is null)
            return new GltfMaterialData("Default", Vector4.One, doubleSided: false,
                GltfAlphaMode.Opaque, 0.5f, null, null, null);

        if (material.Alpha == SharpGLTF.Schema2.AlphaMode.BLEND)
            throw new NotSupportedException(
                $"Material '{DisplayName(material)}' uses BLEND alpha mode; OPAQUE and MASK are supported, but BLEND is not.");

        var alphaMode = material.Alpha == SharpGLTF.Schema2.AlphaMode.MASK
            ? GltfAlphaMode.Mask
            : GltfAlphaMode.Opaque;
        var alphaCutoff = material.AlphaCutoff;
        if (alphaMode == GltfAlphaMode.Mask
            && (!float.IsFinite(alphaCutoff) || alphaCutoff < 0f || alphaCutoff > 1f))
            throw new InvalidDataException(
                $"Material '{DisplayName(material)}' has alpha cutoff {alphaCutoff}; MASK cutoff must be between 0 and 1.");

        var channelValue = material.FindChannel("BaseColor");
        if (!channelValue.HasValue)
            throw new NotSupportedException($"Material '{DisplayName(material)}' has no supported BaseColor channel.");

        var channel = channelValue.Value;
        var factor = channel.Color;
        ValidateFactor(factor, material);

        int? imageIndex = null;
        string? mimeType = null;
        byte[]? imageBytes = null;
        if (channel.Texture is { } texture)
        {
            if (channel.TextureCoordinate != 0)
                throw new NotSupportedException($"Material '{DisplayName(material)}' uses TEXCOORD_{channel.TextureCoordinate}; only TEXCOORD_0 is supported.");

            if (channel.TextureTransform is { } transform
                && (transform.Offset != System.Numerics.Vector2.Zero
                    || transform.Scale != System.Numerics.Vector2.One
                    || MathF.Abs(transform.Rotation) > 0.000001f
                    || transform.TextureCoordinateOverride.HasValue))
            {
                throw new NotSupportedException($"Material '{DisplayName(material)}' uses a base-color texture transform, which is not supported.");
            }

            var image = texture.PrimaryImage
                ?? throw new NotSupportedException($"Material '{DisplayName(material)}' has no primary base-color image.");
            var content = image.Content;
            if (content.IsEmpty || (!content.IsPng && !content.IsJpg))
                throw new NotSupportedException($"Material '{DisplayName(material)}' uses an unsupported base-color image format '{content.MimeType}'. PNG and JPEG are supported.");

            imageIndex = image.LogicalIndex;
            mimeType = content.MimeType;
            imageBytes = content.Content.ToArray();
        }

        return new GltfMaterialData(
            DisplayName(material),
            new Vector4(factor.X, factor.Y, factor.Z, factor.W),
            material.DoubleSided,
            alphaMode,
            alphaCutoff,
            imageIndex,
            mimeType,
            imageBytes);
    }

    private static string DisplayName(Material material) =>
        string.IsNullOrWhiteSpace(material.Name) ? $"Material {material.LogicalIndex}" : material.Name;

    private static void ValidateFactor(System.Numerics.Vector4 factor, Material material)
    {
        if (!float.IsFinite(factor.X) || !float.IsFinite(factor.Y)
            || !float.IsFinite(factor.Z) || !float.IsFinite(factor.W))
        {
            throw new InvalidDataException($"Material '{DisplayName(material)}' has a nonfinite base-color factor.");
        }
    }
}

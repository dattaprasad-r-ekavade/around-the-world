using System;
using System.Collections.Generic;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>A stable scene reference to one GLB file, stored as project-relative metadata.</summary>
public sealed class GltfAssetReference
{
    public GltfAssetReference(Guid assetId, string sourcePath)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset ID cannot be empty.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("GLB source path is required.", nameof(sourcePath));

        var normalizedPath = sourcePath.Trim().Replace('\\', '/');
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal)
            || (normalizedPath.Length >= 2 && char.IsLetter(normalizedPath[0]) && normalizedPath[1] == ':'))
            throw new ArgumentException("GLB source path must be relative to the project root.", nameof(sourcePath));

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == "..")
                throw new ArgumentException("GLB source path cannot move above the project root.", nameof(sourcePath));
            if (segment != ".") safeSegments.Add(segment);
        }

        if (safeSegments.Count == 0)
            throw new ArgumentException("GLB source path is required.", nameof(sourcePath));

        AssetId = assetId;
        SourcePath = string.Join('/', safeSegments);
        if (!SourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .glb asset paths are supported by this reference.", nameof(sourcePath));
    }

    public Guid AssetId { get; }
    public string SourcePath { get; }
}

/// <summary>A stable scene object identity and its local authored state.</summary>
public sealed class SceneObject
{
    public SceneObject(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scene object name is required.", nameof(name));

        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public Transform Transform { get; set; } = new();
    public Guid? ParentId { get; internal set; }
    public GltfAssetReference? GltfAsset { get; set; }
    public GltfCharacterSettings? CharacterSettings { get; set; }
    public WorldDoorComponent? Door { get; set; }
    public WorldSpawnComponent? SpawnPoint { get; set; }
}

/// <summary>Per-instance skeletal clip, playback, and attachment settings saved with a scene object.</summary>
public sealed class GltfCharacterSettings
{
    public string? ClipName { get; set; }
    public float Time { get; set; }
    public float Speed { get; set; } = 1f;
    public bool Loop { get; set; } = true;
    public bool IsPlaying { get; set; }
    public string? CrossfadeClipName { get; set; }
    public float BlendAmount { get; set; } = 0.5f;
    public List<GltfBoneAttachmentReference> Attachments { get; } = new();
}

/// <summary>Stable scene reference to a character joint and a prop's joint-local transform.</summary>
public sealed class GltfBoneAttachmentReference
{
    public GltfBoneAttachmentReference(Guid id, string boneName, Matrix localOffset)
    {
        if (id == Guid.Empty) throw new ArgumentException("Attachment ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(boneName)) throw new ArgumentException("Bone name is required.", nameof(boneName));
        if (!IsFinite(localOffset)) throw new ArgumentException("Attachment offset must be finite.", nameof(localOffset));

        Id = id;
        BoneName = boneName;
        LocalOffset = localOffset;
    }

    public Guid Id { get; }
    public string BoneName { get; }
    public Matrix LocalOffset { get; }

    private static bool IsFinite(Matrix matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}

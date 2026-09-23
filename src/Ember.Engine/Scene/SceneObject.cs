using System;
using System.Collections.Generic;

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
}

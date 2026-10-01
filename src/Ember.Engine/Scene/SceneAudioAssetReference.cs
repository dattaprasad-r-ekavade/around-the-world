using System;
using System.Collections.Generic;

namespace Ember.Scene;

/// <summary>A stable scene reference to an audio file stored relative to its project root.</summary>
public sealed class SceneAudioAssetReference
{
    public SceneAudioAssetReference(Guid assetId, string sourcePath)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset ID cannot be empty.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Audio source path is required.", nameof(sourcePath));

        var normalizedPath = sourcePath.Trim().Replace('\\', '/');
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal)
            || (normalizedPath.Length >= 2 && char.IsLetter(normalizedPath[0]) && normalizedPath[1] == ':'))
            throw new ArgumentException("Audio source path must be relative to the project root.", nameof(sourcePath));

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == "..")
                throw new ArgumentException("Audio source path cannot move above the project root.", nameof(sourcePath));
            if (segment != ".") safeSegments.Add(segment);
        }

        if (safeSegments.Count == 0)
            throw new ArgumentException("Audio source path is required.", nameof(sourcePath));

        AssetId = assetId;
        SourcePath = string.Join('/', safeSegments);
    }

    public Guid AssetId { get; }
    public string SourcePath { get; }
}

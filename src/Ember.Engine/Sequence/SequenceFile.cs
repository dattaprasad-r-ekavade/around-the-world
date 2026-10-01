using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.Assets;
using Ember.Scene;

namespace Ember.Sequence;

/// <summary>Versioned JSON persistence for absolute-time character and camera sequences.</summary>
public static class SequenceFile
{
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void SaveAtomic(SceneSequence sequence, SceneGraph scene, string path)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A sequence path is required.", nameof(path));

        var document = ToDocument(sequence, scene);
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, document, JsonOptions));
    }

    public static SceneSequence Load(string path, SceneGraph scene,
        IReadOnlyDictionary<Guid, IReadOnlyList<GltfAnimationClipData>> clipsByAssetId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(clipsByAssetId);

        var document = ReadDocument(path);

        try
        {
            var characters = document.CharacterTracks!.Select((track, index) =>
            {
                if (track is null) throw new InvalidDataException($"Character track {index} is null.");
                if (track.Id == Guid.Empty) throw new InvalidDataException($"Character track {index} has an empty ID.");
                if (track.AssetId == Guid.Empty) throw new InvalidDataException($"Character track {track.Id} has an empty asset ID.");
                if (string.IsNullOrWhiteSpace(track.ClipName))
                    throw new InvalidDataException($"Character track {track.Id} has no clip name.");

                var target = scene.Find(track.TargetObjectId)
                    ?? throw new InvalidDataException($"Character track {track.Id} refers to missing scene object {track.TargetObjectId}.");
                if (target.GltfAsset is null || target.GltfAsset.AssetId != track.AssetId)
                    throw new InvalidDataException(
                        $"Character track {track.Id} refers to asset {track.AssetId}, which is not assigned to scene object {track.TargetObjectId}.");
                if (!clipsByAssetId.TryGetValue(track.AssetId, out var clips))
                    throw new InvalidDataException($"Character track {track.Id} refers to unloaded asset {track.AssetId}.");
                if (clips is null)
                    throw new InvalidDataException($"Character track {track.Id} has no loaded clip catalog for asset {track.AssetId}.");

                var matches = clips.Where(clip => string.Equals(clip.Name, track.ClipName, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException(matches.Length == 0
                        ? $"Character track {track.Id} refers to missing clip '{track.ClipName}' in asset {track.AssetId}."
                        : $"Character track {track.Id} refers to ambiguous clip name '{track.ClipName}' in asset {track.AssetId}.");

                return new CharacterClipTrack(track.TargetObjectId, matches[0], track.StartTime,
                    track.PlaybackSpeed, track.Loop, track.Id);
            }).ToArray();

            var cameras = document.CameraTracks!.Select((track, index) =>
            {
                if (track is null) throw new InvalidDataException($"Camera track {index} is null.");
                if (track.Keys is null) throw new InvalidDataException($"Camera track {track.Id} has no key list.");
                var keys = track.Keys.Select((key, keyIndex) =>
                {
                    if (key is null) throw new InvalidDataException($"Camera track {track.Id} key {keyIndex} is null.");
                    return new SequenceCameraKeyframe(key.Time,
                        new SequenceCameraTransform(
                            new Microsoft.Xna.Framework.Vector3(key.PositionX, key.PositionY, key.PositionZ),
                            new Microsoft.Xna.Framework.Quaternion(key.RotationX, key.RotationY, key.RotationZ, key.RotationW),
                            key.FieldOfViewDegrees));
                }).ToArray();
                return new SequenceCameraTrack(track.Id, track.Name ?? string.Empty, keys);
            }).ToArray();

            SequenceCameraCutTrack? cuts = null;
            if (document.CameraCuts is not null)
            {
                var cutKeys = document.CameraCuts.Select((key, index) =>
                {
                    if (key is null) throw new InvalidDataException($"Camera cut {index} is null.");
                    return new SequenceCameraCutKeyframe(key.Time, key.CameraId);
                });
                cuts = new SequenceCameraCutTrack(cutKeys);
            }

            var triggerObjectId = document.Version >= 2 ? document.TriggerObjectId : null;
            ValidateCutsceneTrigger(scene, triggerObjectId);
            return new SceneSequence(document.Name!, document.Duration, characters, cameras, cuts,
                triggerObjectId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Sequence data is invalid: {exception.Message}", exception);
        }
    }

    internal static FileDependencies ReadDependencies(string path)
    {
        var document = ReadDocument(path);
        var tracks = document.CharacterTracks!.Select((track, index) =>
        {
            if (track is null) throw new InvalidDataException($"Character track {index} is null.");
            if (track.Id == Guid.Empty) throw new InvalidDataException($"Character track {index} has an empty ID.");
            if (track.AssetId == Guid.Empty) throw new InvalidDataException($"Character track {track.Id} has an empty asset ID.");
            if (string.IsNullOrWhiteSpace(track.ClipName))
                throw new InvalidDataException($"Character track {track.Id} has no clip name.");
            return new CharacterDependency(track.Id, track.TargetObjectId, track.AssetId, track.ClipName);
        }).ToArray();
        return new FileDependencies(document.Version >= 2 ? document.TriggerObjectId : null, tracks);
    }

    private static SequenceDocument ReadDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A sequence path is required.", nameof(path));
        SequenceDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SequenceDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Sequence JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Sequence JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version < 1 || document.Version > CurrentVersion)
            throw new InvalidDataException($"Unsupported sequence version {document.Version}; supported versions are 1–{CurrentVersion}.");
        if (document.Name is null) throw new InvalidDataException("Sequence name is missing.");
        if (document.CharacterTracks is null) throw new InvalidDataException("Sequence character-track list is missing.");
        if (document.CameraTracks is null) throw new InvalidDataException("Sequence camera-track list is missing.");
        return document;
    }

    private static SequenceDocument ToDocument(SceneSequence sequence, SceneGraph scene)
    {
        ValidateCutsceneTrigger(scene, sequence.TriggerObjectId);
        var characters = sequence.CharacterTracks.Select(track =>
        {
            var target = scene.Find(track.TargetObjectId)
                ?? throw new InvalidDataException($"Character track {track.Id} refers to missing scene object {track.TargetObjectId}.");
            var asset = target.GltfAsset
                ?? throw new InvalidDataException($"Character track {track.Id} target {track.TargetObjectId} has no GLB asset reference.");
            return new CharacterTrackDocument
            {
                Id = track.Id,
                TargetObjectId = track.TargetObjectId,
                AssetId = asset.AssetId,
                ClipName = track.Clip.Name,
                StartTime = track.StartTime,
                PlaybackSpeed = track.PlaybackSpeed,
                Loop = track.Loop
            };
        }).ToArray();

        var cameras = sequence.CameraTracks.Select(track => new CameraTrackDocument
        {
            Id = track.Id,
            Name = track.Name,
            Keys = track.Keys.Select(key => new CameraKeyDocument
            {
                Time = key.Time,
                PositionX = key.Transform.Position.X,
                PositionY = key.Transform.Position.Y,
                PositionZ = key.Transform.Position.Z,
                RotationX = key.Transform.Rotation.X,
                RotationY = key.Transform.Rotation.Y,
                RotationZ = key.Transform.Rotation.Z,
                RotationW = key.Transform.Rotation.W,
                FieldOfViewDegrees = key.Transform.FieldOfViewDegrees
            }).ToArray()
        }).ToArray();

        return new SequenceDocument
        {
            Version = CurrentVersion,
            Name = sequence.Name,
            Duration = sequence.Duration,
            TriggerObjectId = sequence.TriggerObjectId,
            CharacterTracks = characters,
            CameraTracks = cameras,
            CameraCuts = sequence.CameraCuts?.Keys.Select(key => new CameraCutDocument
            {
                Time = key.Time,
                CameraId = key.CameraId
            }).ToArray()
        };
    }

    private static void ValidateCutsceneTrigger(SceneGraph scene, Guid? triggerObjectId)
    {
        if (triggerObjectId is not { } id) return;
        var trigger = scene.Find(id)
            ?? throw new InvalidDataException($"Sequence cutscene trigger refers to missing scene object {id}.");
        if (!trigger.Enabled)
            throw new InvalidDataException($"Sequence cutscene trigger '{trigger.Name}' ({id}) must be enabled.");
        if (trigger.BoxCollider is not { IsTrigger: true })
            throw new InvalidDataException(
                $"Sequence cutscene trigger '{trigger.Name}' ({id}) must have a trigger box collider.");
        if (trigger.TriggerAction?.Kind != SceneTriggerActionKind.ReachGoal)
            throw new InvalidDataException(
                $"Sequence cutscene trigger '{trigger.Name}' ({id}) must use the Reach goal action.");
    }

    private sealed class SequenceDocument
    {
        public int Version { get; set; }
        public string? Name { get; set; }
        public float Duration { get; set; }
        public Guid? TriggerObjectId { get; set; }
        public CharacterTrackDocument?[]? CharacterTracks { get; set; }
        public CameraTrackDocument?[]? CameraTracks { get; set; }
        public CameraCutDocument?[]? CameraCuts { get; set; }
    }

    private sealed class CharacterTrackDocument
    {
        public Guid Id { get; set; }
        public Guid TargetObjectId { get; set; }
        public Guid AssetId { get; set; }
        public string? ClipName { get; set; }
        public float StartTime { get; set; }
        public float PlaybackSpeed { get; set; }
        public bool Loop { get; set; }
    }

    private sealed class CameraTrackDocument
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public CameraKeyDocument?[]? Keys { get; set; }
    }

    private sealed class CameraKeyDocument
    {
        public float Time { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }
        public float FieldOfViewDegrees { get; set; }
    }

    private sealed class CameraCutDocument
    {
        public float Time { get; set; }
        public Guid CameraId { get; set; }
    }

    internal sealed record FileDependencies(Guid? TriggerObjectId,
        IReadOnlyList<CharacterDependency> CharacterTracks);
    internal sealed record CharacterDependency(Guid TrackId, Guid TargetObjectId, Guid AssetId, string ClipName);
}

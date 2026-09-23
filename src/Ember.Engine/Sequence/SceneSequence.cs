using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ember.Assets;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Sequence;

/// <summary>One character's imported clip, placed on an absolute scene-sequence timeline.</summary>
public sealed class CharacterClipTrack
{
    public CharacterClipTrack(Guid targetObjectId, GltfAnimationClipData clip,
        float startTime = 0f, float playbackSpeed = 1f, bool loop = false, Guid? id = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Character track ID cannot be empty.", nameof(id));
        if (targetObjectId == Guid.Empty) throw new ArgumentException("Target scene-object ID cannot be empty.", nameof(targetObjectId));
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        if (!float.IsFinite(startTime) || startTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(startTime), "Track start time must be finite and nonnegative.");
        if (!float.IsFinite(playbackSpeed) || playbackSpeed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(playbackSpeed), "Track speed must be finite and positive.");

        Id = id ?? Guid.NewGuid();
        TargetObjectId = targetObjectId;
        StartTime = startTime;
        PlaybackSpeed = playbackSpeed;
        Loop = loop;
    }

    public Guid Id { get; }
    public Guid TargetObjectId { get; }
    public GltfAnimationClipData Clip { get; }
    public float StartTime { get; }
    public float PlaybackSpeed { get; }
    public bool Loop { get; }

    internal void Evaluate(GltfSkinPose pose, float sequenceTime)
    {
        if (sequenceTime < StartTime) return;
        var clipTime = (double)(sequenceTime - StartTime) * PlaybackSpeed;
        if (Clip.Duration > 0f)
        {
            if (Loop)
            {
                clipTime %= Clip.Duration;
            }
            else
            {
                clipTime = Math.Min(clipTime, Clip.Duration);
            }
        }
        else
        {
            clipTime = 0d;
        }

        Clip.Evaluate(pose, (float)clipTime);
    }
}

public readonly record struct SequenceCameraTransform(Vector3 Position, Quaternion Rotation, float FieldOfViewDegrees);
public readonly record struct SequenceCameraKeyframe(float Time, SequenceCameraTransform Transform);
public readonly record struct SequenceCameraCutKeyframe(float Time, Guid CameraId);

/// <summary>Absolute-time position/orientation/FOV keys for one stable camera track.</summary>
public sealed class SequenceCameraTrack
{
    public SequenceCameraTrack(Guid id, string name, IEnumerable<SequenceCameraKeyframe> keys)
    {
        if (id == Guid.Empty) throw new ArgumentException("Camera track ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Camera track name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(keys);
        var ordered = keys.ToArray();
        if (ordered.Length == 0) throw new ArgumentException("A camera track needs at least one key.", nameof(keys));

        for (var index = 0; index < ordered.Length; index++)
        {
            var key = ordered[index];
            if (!float.IsFinite(key.Time) || key.Time < 0f
                || (index > 0 && key.Time <= ordered[index - 1].Time))
                throw new ArgumentException("Camera key times must be finite, nonnegative, and strictly increasing.", nameof(keys));
            ordered[index] = key with { Transform = ValidateTransform(key.Transform, nameof(keys)) };
        }

        Id = id;
        Name = name.Trim();
        Keys = Array.AsReadOnly(ordered);
    }

    public Guid Id { get; }
    public string Name { get; }
    public ReadOnlyCollection<SequenceCameraKeyframe> Keys { get; }

    public SequenceCameraTransform Sample(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (time <= Keys[0].Time) return Keys[0].Transform;
        if (time >= Keys[^1].Time) return Keys[^1].Transform;

        var upper = 1;
        while (Keys[upper].Time < time) upper++;
        var start = Keys[upper - 1];
        var end = Keys[upper];
        var amount = (time - start.Time) / (end.Time - start.Time);
        var endRotation = end.Transform.Rotation;
        if (Quaternion.Dot(start.Transform.Rotation, endRotation) < 0f)
            endRotation = new Quaternion(-endRotation.X, -endRotation.Y, -endRotation.Z, -endRotation.W);

        return new SequenceCameraTransform(
            Vector3.Lerp(start.Transform.Position, end.Transform.Position, amount),
            Quaternion.Normalize(Quaternion.Slerp(start.Transform.Rotation, endRotation, amount)),
            MathHelper.Lerp(start.Transform.FieldOfViewDegrees, end.Transform.FieldOfViewDegrees, amount));
    }

    private static SequenceCameraTransform ValidateTransform(SequenceCameraTransform value, string parameterName)
    {
        if (!IsFinite(value.Position) || !IsFinite(value.Rotation)
            || !float.IsFinite(value.FieldOfViewDegrees)
            || value.FieldOfViewDegrees <= 1f || value.FieldOfViewDegrees >= 179f)
            throw new ArgumentException("Camera keys need finite transforms and a field of view between 1 and 179 degrees.", parameterName);
        var lengthSquared = value.Rotation.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 0.0000001f)
            throw new ArgumentException("Camera rotation cannot be a zero-length quaternion.", parameterName);
        return value with { Rotation = Quaternion.Normalize(value.Rotation) };
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>Time-ordered camera cuts that refer to camera-track IDs, never list positions.</summary>
public sealed class SequenceCameraCutTrack
{
    public SequenceCameraCutTrack(IEnumerable<SequenceCameraCutKeyframe> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var ordered = keys.ToArray();
        if (ordered.Length == 0) throw new ArgumentException("A camera-cut track needs at least one cut.", nameof(keys));
        for (var index = 0; index < ordered.Length; index++)
        {
            var key = ordered[index];
            if (!float.IsFinite(key.Time) || key.Time < 0f || key.CameraId == Guid.Empty
                || (index > 0 && key.Time <= ordered[index - 1].Time))
                throw new ArgumentException("Camera cuts need valid IDs and finite, nonnegative, strictly increasing times.", nameof(keys));
        }
        Keys = Array.AsReadOnly(ordered);
    }

    public ReadOnlyCollection<SequenceCameraCutKeyframe> Keys { get; }

    public Guid Sample(float time, Guid fallbackCameraId)
    {
        var selected = fallbackCameraId;
        foreach (var key in Keys)
        {
            if (key.Time > time) break;
            selected = key.CameraId;
        }
        return selected;
    }
}

/// <summary>One immutable character-and-camera sequence evaluated from an absolute timeline time.</summary>
public sealed class SceneSequence
{
    private readonly ReadOnlyDictionary<Guid, SequenceCameraTrack> _cameraById;

    public SceneSequence(string name, float duration,
        IEnumerable<CharacterClipTrack> characterTracks,
        IEnumerable<SequenceCameraTrack>? cameraTracks = null,
        SequenceCameraCutTrack? cameraCuts = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Sequence name is required.", nameof(name));
        if (!float.IsFinite(duration) || duration <= 0f)
            throw new ArgumentOutOfRangeException(nameof(duration), "Sequence duration must be finite and positive.");
        ArgumentNullException.ThrowIfNull(characterTracks);
        var characters = characterTracks.ToArray();
        if (characters.Any(track => track is null)) throw new ArgumentException("Character tracks cannot contain null entries.", nameof(characterTracks));
        if (characters.Any(track => track.StartTime > duration))
            throw new ArgumentException("A character track cannot start after the sequence ends.", nameof(characterTracks));
        if (characters.Select(track => track.Id).Distinct().Count() != characters.Length)
            throw new ArgumentException("Character track IDs must be unique.", nameof(characterTracks));
        if (characters.Select(track => track.TargetObjectId).Distinct().Count() != characters.Length)
            throw new ArgumentException("A sequence can contain at most one character clip track per scene object.", nameof(characterTracks));

        var cameras = (cameraTracks ?? Array.Empty<SequenceCameraTrack>()).ToArray();
        if (cameras.Any(track => track is null)) throw new ArgumentException("Camera tracks cannot contain null entries.", nameof(cameraTracks));
        if (cameras.Select(track => track.Id).Distinct().Count() != cameras.Length)
            throw new ArgumentException("Camera track IDs must be unique.", nameof(cameraTracks));
        if (cameras.SelectMany(track => track.Keys).Any(key => key.Time > duration))
            throw new ArgumentException("Camera keys cannot occur after the sequence ends.", nameof(cameraTracks));
        if (cameraCuts is not null)
        {
            if (cameras.Length == 0) throw new ArgumentException("Camera cuts require at least one camera track.", nameof(cameraCuts));
            if (cameraCuts.Keys.Any(key => key.Time > duration || cameras.All(camera => camera.Id != key.CameraId)))
                throw new ArgumentException("Every camera cut must fall within the sequence and reference a camera track ID.", nameof(cameraCuts));
        }

        Name = name.Trim();
        Duration = duration;
        CharacterTracks = Array.AsReadOnly(characters);
        CameraTracks = Array.AsReadOnly(cameras);
        CameraCuts = cameraCuts;
        _cameraById = new ReadOnlyDictionary<Guid, SequenceCameraTrack>(cameras.ToDictionary(camera => camera.Id));
    }

    public string Name { get; }
    public float Duration { get; }
    public ReadOnlyCollection<CharacterClipTrack> CharacterTracks { get; }
    public ReadOnlyCollection<SequenceCameraTrack> CameraTracks { get; }
    public SequenceCameraCutTrack? CameraCuts { get; }

    /// <summary>Resets tracked poses to rest, evaluates clips at this absolute time, and samples the active camera.</summary>
    public SceneSequenceFrame Evaluate(float time, SceneGraph scene,
        IReadOnlyDictionary<Guid, GltfSkinPose> characterPoses)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(characterPoses);
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time), "Sequence time must be finite.");
        var clampedTime = Math.Clamp(time, 0f, Duration);

        foreach (var track in CharacterTracks)
        {
            if (scene.Find(track.TargetObjectId) is null)
                throw new InvalidOperationException($"Sequence character object {track.TargetObjectId} is not present in the scene.");
            if (!characterPoses.TryGetValue(track.TargetObjectId, out var pose))
                throw new InvalidOperationException($"No character pose is registered for scene object {track.TargetObjectId}.");
            if (pose is null) throw new ArgumentException($"Character pose for scene object {track.TargetObjectId} is null.", nameof(characterPoses));
        }

        foreach (var track in CharacterTracks)
        {
            var pose = characterPoses[track.TargetObjectId];
            pose.ResetToRestPose();
            pose.ComputeSkinMatrices();
            track.Evaluate(pose, clampedTime);
        }

        return SampleCameraAt(clampedTime);
    }

    /// <summary>Samples only the camera/cut tracks without changing any character pose.</summary>
    public SceneSequenceFrame SampleCamera(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time), "Sequence time must be finite.");
        return SampleCameraAt(Math.Clamp(time, 0f, Duration));
    }

    private SceneSequenceFrame SampleCameraAt(float clampedTime)
    {
        if (CameraTracks.Count == 0) return new SceneSequenceFrame(clampedTime, null, null, null);
        var cameraId = CameraCuts?.Sample(clampedTime, CameraTracks[0].Id) ?? CameraTracks[0].Id;
        var activeCamera = _cameraById[cameraId];
        return new SceneSequenceFrame(clampedTime, activeCamera.Id, activeCamera.Name, activeCamera.Sample(clampedTime));
    }
}

public readonly record struct SceneSequenceFrame(
    float Time, Guid? CameraId, string? CameraName, SequenceCameraTransform? CameraTransform);

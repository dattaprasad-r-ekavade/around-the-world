using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Scene;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public enum GltfAnimationTargetPath
{
    Translation,
    Rotation,
    Scale
}

public enum GltfAnimationInterpolation
{
    Step,
    Linear
}

public readonly record struct GltfVector3AnimationKey(float Time, Vector3 Value);
public readonly record struct GltfQuaternionAnimationKey(float Time, Quaternion Value);

/// <summary>One immutable transform channel mapped to a node in a skinned character pose.</summary>
public sealed class GltfAnimationTrackData
{
    private GltfAnimationTrackData(int nodeIndex, string nodeName, GltfAnimationTargetPath path,
        GltfAnimationInterpolation interpolation, GltfVector3AnimationKey[] vector3Keys,
        GltfQuaternionAnimationKey[] quaternionKeys)
    {
        NodeIndex = nodeIndex;
        NodeName = nodeName;
        Path = path;
        Interpolation = interpolation;
        Vector3Keys = Array.AsReadOnly(vector3Keys);
        QuaternionKeys = Array.AsReadOnly(quaternionKeys);
    }

    public int NodeIndex { get; }
    public string NodeName { get; }
    public GltfAnimationTargetPath Path { get; }
    public GltfAnimationInterpolation Interpolation { get; }
    public IReadOnlyList<GltfVector3AnimationKey> Vector3Keys { get; }
    public IReadOnlyList<GltfQuaternionAnimationKey> QuaternionKeys { get; }

    internal float LastKeyTime => Path == GltfAnimationTargetPath.Rotation
        ? QuaternionKeys[^1].Time
        : Vector3Keys[^1].Time;

    internal static GltfAnimationTrackData CreateVector3(int nodeIndex, string nodeName,
        GltfAnimationTargetPath path, GltfAnimationInterpolation interpolation,
        GltfVector3AnimationKey[] keys) =>
        new(nodeIndex, nodeName, path, interpolation, keys, Array.Empty<GltfQuaternionAnimationKey>());

    internal static GltfAnimationTrackData CreateQuaternion(int nodeIndex, string nodeName,
        GltfAnimationInterpolation interpolation, GltfQuaternionAnimationKey[] keys) =>
        new(nodeIndex, nodeName, GltfAnimationTargetPath.Rotation, interpolation,
            Array.Empty<GltfVector3AnimationKey>(), keys);

    internal void Apply(GltfSkinPose pose, float time)
    {
        var local = pose.GetLocalTransform(NodeIndex);
        switch (Path)
        {
            case GltfAnimationTargetPath.Translation:
                pose.SetLocalTransform(NodeIndex, local with { Position = SampleVector3(Vector3Keys, time, Interpolation) });
                break;
            case GltfAnimationTargetPath.Rotation:
                pose.SetLocalTransform(NodeIndex, local with { Rotation = SampleQuaternion(QuaternionKeys, time, Interpolation) });
                break;
            case GltfAnimationTargetPath.Scale:
                pose.SetLocalTransform(NodeIndex, local with { Scale = SampleVector3(Vector3Keys, time, Interpolation) });
                break;
            default:
                throw new InvalidOperationException($"Unknown animation target path '{Path}'.");
        }
    }

    private static Vector3 SampleVector3(IReadOnlyList<GltfVector3AnimationKey> keys, float time,
        GltfAnimationInterpolation interpolation)
    {
        var upper = FindFirstKeyAfter(keys, time);
        if (upper == 0) return keys[0].Value;
        if (upper == keys.Count) return keys[^1].Value;

        var lower = upper - 1;
        var start = keys[lower];
        if (interpolation == GltfAnimationInterpolation.Step) return start.Value;

        var end = keys[upper];
        var amount = (time - start.Time) / (end.Time - start.Time);
        return Vector3.Lerp(start.Value, end.Value, amount);
    }

    private static Quaternion SampleQuaternion(IReadOnlyList<GltfQuaternionAnimationKey> keys, float time,
        GltfAnimationInterpolation interpolation)
    {
        var upper = FindFirstKeyAfter(keys, time);
        if (upper == 0) return keys[0].Value;
        if (upper == keys.Count) return keys[^1].Value;

        var lower = upper - 1;
        var start = keys[lower];
        if (interpolation == GltfAnimationInterpolation.Step) return start.Value;

        var end = keys[upper];
        var amount = (time - start.Time) / (end.Time - start.Time);
        var endRotation = end.Value;
        if (Quaternion.Dot(start.Value, endRotation) < 0f)
            endRotation = new Quaternion(-endRotation.X, -endRotation.Y, -endRotation.Z, -endRotation.W);
        return Quaternion.Normalize(Quaternion.Slerp(start.Value, endRotation, amount));
    }

    private static int FindFirstKeyAfter(IReadOnlyList<GltfVector3AnimationKey> keys, float time)
    {
        var low = 0;
        var high = keys.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (keys[middle].Time <= time) low = middle + 1;
            else high = middle;
        }

        return low;
    }

    private static int FindFirstKeyAfter(IReadOnlyList<GltfQuaternionAnimationKey> keys, float time)
    {
        var low = 0;
        var high = keys.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (keys[middle].Time <= time) low = middle + 1;
            else high = middle;
        }

        return low;
    }
}

/// <summary>An immutable STEP/LINEAR transform clip for one imported skin hierarchy.</summary>
public sealed class GltfAnimationClipData
{
    private readonly GltfSkinData _skin;

    private GltfAnimationClipData(GltfSkinData skin, string name,
        IReadOnlyList<GltfAnimationTrackData> tracks, float duration)
    {
        _skin = skin;
        Name = name;
        Tracks = tracks;
        Duration = duration;
    }

    public string Name { get; }
    public float Duration { get; }
    public IReadOnlyList<GltfAnimationTrackData> Tracks { get; }

    public static GltfAnimationClipData Import(Animation animation, GltfSkinData skin)
    {
        if (animation is null) throw new ArgumentNullException(nameof(animation));
        if (skin is null) throw new ArgumentNullException(nameof(skin));

        var nodeIndexBySourceIndex = new Dictionary<int, int>(skin.Nodes.Count);
        for (var i = 0; i < skin.Nodes.Count; i++)
            nodeIndexBySourceIndex.Add(skin.Nodes[i].SourceNodeIndex, i);

        var tracks = new List<GltfAnimationTrackData>(animation.Channels.Count);
        var occupiedTargets = new HashSet<(int NodeIndex, GltfAnimationTargetPath Path)>();
        foreach (var channel in animation.Channels)
        {
            var target = channel.TargetNode
                ?? throw new NotSupportedException(
                    $"Animation '{DisplayName(animation)}' channel {channel.LogicalIndex} does not target a node transform.");
            if (!nodeIndexBySourceIndex.TryGetValue(target.LogicalIndex, out var nodeIndex))
                throw new NotSupportedException(
                    $"Animation '{DisplayName(animation)}' targets node '{DisplayName(target)}' outside the imported skin hierarchy.");

            var nodeName = skin.Nodes[nodeIndex].Name;
            switch (channel.TargetNodePath)
            {
                case PropertyPath.translation:
                    AddVector3Track(channel.GetTranslationSampler(), nodeIndex, nodeName,
                        GltfAnimationTargetPath.Translation);
                    break;
                case PropertyPath.scale:
                    AddVector3Track(channel.GetScaleSampler(), nodeIndex, nodeName,
                        GltfAnimationTargetPath.Scale);
                    break;
                case PropertyPath.rotation:
                    AddRotationTrack(channel.GetRotationSampler(), nodeIndex, nodeName);
                    break;
                case PropertyPath.weights:
                    throw new NotSupportedException(
                        $"Animation '{DisplayName(animation)}' contains morph-weight animation on node '{nodeName}'.");
                default:
                    throw new NotSupportedException(
                        $"Animation '{DisplayName(animation)}' contains unsupported target path '{channel.TargetNodePath}'.");
            }

            void AddVector3Track(IAnimationSampler<System.Numerics.Vector3>? sampler, int targetNodeIndex,
                string targetNodeName, GltfAnimationTargetPath targetPath)
            {
                if (sampler is null)
                    throw new InvalidDataException(
                        $"Animation '{DisplayName(animation)}' channel {channel.LogicalIndex} has no {targetPath} sampler.");
                var interpolation = GetInterpolation(sampler.InterpolationMode, animation, targetNodeName, targetPath);
                var sourceKeys = sampler.GetLinearKeys().ToArray();
                var keys = new GltfVector3AnimationKey[sourceKeys.Length];
                for (var keyIndex = 0; keyIndex < sourceKeys.Length; keyIndex++)
                {
                    var (time, value) = sourceKeys[keyIndex];
                    ValidateKeyTime(keys, keyIndex, time, animation, targetNodeName, targetPath);
                    var converted = new Vector3(value.X, value.Y, value.Z);
                    if (!IsFinite(converted))
                        throw new InvalidDataException(
                            $"Animation '{DisplayName(animation)}' has a nonfinite {targetPath} value for node '{targetNodeName}'.");
                    keys[keyIndex] = new GltfVector3AnimationKey(time, converted);
                }

                RequireKeys(keys.Length, animation, targetNodeName, targetPath);
                AddTrack(GltfAnimationTrackData.CreateVector3(targetNodeIndex, targetNodeName,
                    targetPath, interpolation, keys));
            }

            void AddRotationTrack(IAnimationSampler<System.Numerics.Quaternion>? sampler,
                int targetNodeIndex, string targetNodeName)
            {
                if (sampler is null)
                    throw new InvalidDataException(
                        $"Animation '{DisplayName(animation)}' channel {channel.LogicalIndex} has no rotation sampler.");
                var interpolation = GetInterpolation(sampler.InterpolationMode, animation, targetNodeName,
                    GltfAnimationTargetPath.Rotation);
                var sourceKeys = sampler.GetLinearKeys().ToArray();
                var keys = new GltfQuaternionAnimationKey[sourceKeys.Length];
                for (var keyIndex = 0; keyIndex < sourceKeys.Length; keyIndex++)
                {
                    var (time, value) = sourceKeys[keyIndex];
                    ValidateKeyTime(keys, keyIndex, time, animation, targetNodeName,
                        GltfAnimationTargetPath.Rotation);
                    var converted = new Quaternion(value.X, value.Y, value.Z, value.W);
                    var lengthSquared = converted.LengthSquared();
                    if (!IsFinite(converted) || !float.IsFinite(lengthSquared) || lengthSquared < 0.0000001f)
                        throw new InvalidDataException(
                            $"Animation '{DisplayName(animation)}' has an invalid rotation value for node '{targetNodeName}'.");
                    keys[keyIndex] = new GltfQuaternionAnimationKey(time, Quaternion.Normalize(converted));
                }

                RequireKeys(keys.Length, animation, targetNodeName, GltfAnimationTargetPath.Rotation);
                AddTrack(GltfAnimationTrackData.CreateQuaternion(targetNodeIndex, targetNodeName,
                    interpolation, keys));
            }

            void AddTrack(GltfAnimationTrackData track)
            {
                if (!occupiedTargets.Add((track.NodeIndex, track.Path)))
                    throw new InvalidDataException(
                        $"Animation '{DisplayName(animation)}' contains duplicate {track.Path} channels for node '{track.NodeName}'.");
                tracks.Add(track);
            }
        }

        if (tracks.Count == 0)
            throw new NotSupportedException($"Animation '{DisplayName(animation)}' contains no supported transform channels.");

        tracks.Sort((left, right) =>
        {
            var nodeOrder = left.NodeIndex.CompareTo(right.NodeIndex);
            return nodeOrder != 0 ? nodeOrder : left.Path.CompareTo(right.Path);
        });
        var duration = tracks.Max(track => track.LastKeyTime);
        return new GltfAnimationClipData(skin, DisplayName(animation), Array.AsReadOnly(tracks.ToArray()), duration);
    }

    /// <summary>Evaluates an absolute clip time; every unanimated property returns to its rest value.</summary>
    public void Evaluate(GltfSkinPose pose, float time)
    {
        if (pose is null) throw new ArgumentNullException(nameof(pose));
        if (!pose.UsesSkin(_skin))
            throw new ArgumentException("The pose was created from a different skin than this animation clip.", nameof(pose));
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time), "Animation time must be finite.");

        pose.ResetToRestPose();
        foreach (var track in Tracks) track.Apply(pose, time);
        pose.ComputeSkinMatrices();
    }

    private static GltfAnimationInterpolation GetInterpolation(AnimationInterpolationMode mode,
        Animation animation, string nodeName, GltfAnimationTargetPath path) => mode switch
    {
        AnimationInterpolationMode.STEP => GltfAnimationInterpolation.Step,
        AnimationInterpolationMode.LINEAR => GltfAnimationInterpolation.Linear,
        _ => throw new NotSupportedException(
            $"Animation '{DisplayName(animation)}' uses unsupported {mode} interpolation for {path} on node '{nodeName}'.")
    };

    private static void ValidateKeyTime(GltfVector3AnimationKey[] keys, int index, float time,
        Animation animation, string nodeName, GltfAnimationTargetPath path)
    {
        if (!float.IsFinite(time) || time < 0f || (index > 0 && time <= keys[index - 1].Time))
            throw new InvalidDataException(
                $"Animation '{DisplayName(animation)}' has a nonfinite, negative, or unordered key time for {path} on node '{nodeName}'.");
    }

    private static void ValidateKeyTime(GltfQuaternionAnimationKey[] keys, int index, float time,
        Animation animation, string nodeName, GltfAnimationTargetPath path)
    {
        if (!float.IsFinite(time) || time < 0f || (index > 0 && time <= keys[index - 1].Time))
            throw new InvalidDataException(
                $"Animation '{DisplayName(animation)}' has a nonfinite, negative, or unordered key time for {path} on node '{nodeName}'.");
    }

    private static void RequireKeys(int count, Animation animation, string nodeName,
        GltfAnimationTargetPath path)
    {
        if (count == 0)
            throw new InvalidDataException(
                $"Animation '{DisplayName(animation)}' has no keys for {path} on node '{nodeName}'.");
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static string DisplayName(Animation animation) =>
        string.IsNullOrWhiteSpace(animation.Name) ? $"Animation {animation.LogicalIndex}" : animation.Name;

    private static string DisplayName(Node node) =>
        string.IsNullOrWhiteSpace(node.Name) ? $"Node {node.LogicalIndex}" : node.Name;
}

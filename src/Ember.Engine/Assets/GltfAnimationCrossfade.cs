using System;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Assets;

/// <summary>Blends two sampled clips into one character's local pose.</summary>
public sealed class GltfAnimationCrossfade
{
    private readonly GltfAnimationClipData _fromClip;
    private readonly GltfAnimationClipData _toClip;
    private readonly GltfSkinData _skin;
    private readonly GltfSkinPose _fromPose;
    private readonly GltfSkinPose _toPose;

    public GltfAnimationCrossfade(GltfAnimationClipData fromClip, GltfAnimationClipData toClip)
    {
        _fromClip = fromClip ?? throw new ArgumentNullException(nameof(fromClip));
        _toClip = toClip ?? throw new ArgumentNullException(nameof(toClip));
        if (!ReferenceEquals(fromClip.Skin, toClip.Skin))
            throw new ArgumentException("Both clips must belong to the same imported skin.", nameof(toClip));

        _skin = fromClip.Skin;
        _fromPose = _skin.CreatePose();
        _toPose = _skin.CreatePose();
    }

    public GltfAnimationClipData FromClip => _fromClip;
    public GltfAnimationClipData ToClip => _toClip;

    /// <summary>Samples both clips and blends local transforms at an amount from zero to one.</summary>
    public void Evaluate(GltfSkinPose pose, float fromTime, float toTime, float amount)
    {
        if (pose is null) throw new ArgumentNullException(nameof(pose));
        if (!pose.UsesSkin(_skin))
            throw new ArgumentException("The pose was created from a different skin than these clips.", nameof(pose));
        if (!float.IsFinite(amount) || amount < 0f || amount > 1f)
            throw new ArgumentOutOfRangeException(nameof(amount), "Crossfade amount must be between zero and one.");

        _fromClip.Evaluate(_fromPose, fromTime);
        _toClip.Evaluate(_toPose, toTime);
        if (amount == 0f)
        {
            pose.CopyLocalTransformsFrom(_fromPose);
            pose.ComputeSkinMatrices();
            return;
        }
        if (amount == 1f)
        {
            pose.CopyLocalTransformsFrom(_toPose);
            pose.ComputeSkinMatrices();
            return;
        }

        for (var nodeIndex = 0; nodeIndex < pose.NodeCount; nodeIndex++)
        {
            var from = _fromPose.GetLocalTransform(nodeIndex);
            var to = _toPose.GetLocalTransform(nodeIndex);
            var rotation = SlerpShortest(from.Rotation, to.Rotation, amount);
            pose.SetLocalTransform(nodeIndex, new GltfLocalTransform(
                Vector3.Lerp(from.Position, to.Position, amount),
                rotation,
                Vector3.Lerp(from.Scale, to.Scale, amount)));
        }

        pose.ComputeSkinMatrices();
    }

    private static Quaternion SlerpShortest(Quaternion from, Quaternion to, float amount)
    {
        if (Quaternion.Dot(from, to) < 0f)
            to = new Quaternion(-to.X, -to.Y, -to.Z, -to.W);
        return Quaternion.Normalize(Quaternion.Slerp(from, to, amount));
    }
}

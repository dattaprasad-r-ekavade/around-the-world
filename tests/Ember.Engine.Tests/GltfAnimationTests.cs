using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Assets;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class GltfAnimationTests
{
    [Fact]
    public void ImportsFoxStepAndLinearTransformTracksWithFixtureDurations()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var walk = Assert.Single(model.LogicalAnimations, animation => animation.Name == "Walk");
        var hip = model.LogicalNodes.Single(node => node.Name == "b_Hip_01");
        var root = model.LogicalNodes.Single(node => node.Name == "b_Root_00");
        walk.CreateScaleChannel(hip, new SortedDictionary<float, System.Numerics.Vector3>
        {
            [0f] = new(1f, 1f, 1f),
            [walk.Duration] = new(2f, 3f, 4f)
        }, linear: true);
        walk.CreateScaleChannel(root, new SortedDictionary<float, System.Numerics.Vector3>
        {
            [0f] = new(1f, 1f, 1f),
            [walk.Duration] = new(3f, 2f, 1f)
        }, linear: false);

        var clip = GltfAnimationClipData.Import(walk, skin);

        Assert.Equal("Walk", clip.Name);
        Assert.InRange(MathF.Abs(0.7083333f - clip.Duration), 0f, 0.00001f);
        Assert.Equal(23, clip.Tracks.Count);
        var hipNodeIndex = skin.Nodes.ToList().FindIndex(node => node.SourceNodeIndex == hip.LogicalIndex);
        var translation = Assert.Single(clip.Tracks, track => track.NodeIndex == hipNodeIndex
            && track.Path == GltfAnimationTargetPath.Translation);
        Assert.Equal(GltfAnimationInterpolation.Linear, translation.Interpolation);
        Assert.Equal(translation.Vector3Keys.Count, walk.FindTranslationChannel(hip)!
            .GetTranslationSampler()!.GetLinearKeys().Count());
        var sourceTranslationKeys = walk.FindTranslationChannel(hip)!
            .GetTranslationSampler()!.GetLinearKeys().ToArray();
        for (var i = 0; i < sourceTranslationKeys.Length; i++)
        {
            Assert.Equal(sourceTranslationKeys[i].Item1, translation.Vector3Keys[i].Time);
            AssertVectorClose(new Vector3(sourceTranslationKeys[i].Item2.X,
                sourceTranslationKeys[i].Item2.Y, sourceTranslationKeys[i].Item2.Z), translation.Vector3Keys[i].Value);
        }

        Assert.Equal(0f, translation.Vector3Keys[0].Time);
        Assert.Equal(clip.Duration, translation.Vector3Keys[^1].Time);

        var hipRotation = Assert.Single(clip.Tracks, track => track.NodeIndex == hipNodeIndex
            && track.Path == GltfAnimationTargetPath.Rotation);
        var sourceRotationKeys = walk.FindRotationChannel(hip)!
            .GetRotationSampler()!.GetLinearKeys().ToArray();
        Assert.Equal(sourceRotationKeys.Length, hipRotation.QuaternionKeys.Count);
        for (var i = 0; i < sourceRotationKeys.Length; i++)
        {
            Assert.Equal(sourceRotationKeys[i].Item1, hipRotation.QuaternionKeys[i].Time);
            var source = sourceRotationKeys[i].Item2;
            var expected = Quaternion.Normalize(new Quaternion(source.X, source.Y, source.Z, source.W));
            Assert.InRange(MathF.Abs(MathF.Abs(Quaternion.Dot(expected,
                hipRotation.QuaternionKeys[i].Value)) - 1f), 0f, 0.0001f);
        }

        var scale = Assert.Single(clip.Tracks, track => track.NodeIndex == hipNodeIndex
            && track.Path == GltfAnimationTargetPath.Scale);
        Assert.Equal(GltfAnimationInterpolation.Linear, scale.Interpolation);
        AssertVectorClose(new Vector3(1f), scale.Vector3Keys[0].Value);
        AssertVectorClose(new Vector3(2f, 3f, 4f), scale.Vector3Keys[^1].Value);

        var stepClip = GltfAnimationClipData.Import(walk, skin);
        var rootNodeIndex = skin.Nodes.ToList().FindIndex(node => node.SourceNodeIndex == root.LogicalIndex);
        var stepScale = Assert.Single(stepClip.Tracks, track => track.NodeIndex == rootNodeIndex
            && track.Path == GltfAnimationTargetPath.Scale);
        Assert.Equal(GltfAnimationInterpolation.Step, stepScale.Interpolation);

        var pose = skin.CreatePose();
        stepClip.Evaluate(pose, (stepScale.Vector3Keys[0].Time + stepScale.Vector3Keys[1].Time) * 0.5f);
        AssertVectorClose(new Vector3(1f), pose.GetLocalTransform(rootNodeIndex).Scale);
        stepClip.Evaluate(pose, clip.Duration);
        AssertVectorClose(new Vector3(3f, 2f, 1f), pose.GetLocalTransform(rootNodeIndex).Scale);
    }

    [Fact]
    public void RejectsCubicSplineInterpolation()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var walk = model.LogicalAnimations.Single(animation => animation.Name == "Walk");
        var meshNode = model.LogicalNodes.Single(node => node.Name == "fox");
        walk.CreateRotationChannel(meshNode, new SortedDictionary<float,
            (System.Numerics.Quaternion InTangent, System.Numerics.Quaternion Value, System.Numerics.Quaternion OutTangent)>
        {
            [0f] = (default, System.Numerics.Quaternion.Identity, default),
            [walk.Duration] = (default, System.Numerics.Quaternion.Identity, default)
        });

        var exception = Assert.Throws<NotSupportedException>(() => GltfAnimationClipData.Import(walk, skin));
        Assert.Contains("CUBICSPLINE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluatesFoxClipAtAbsoluteTimesAndRestoresUnanimatedNodes()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var sourceClip = model.LogicalAnimations.Single(animation => animation.Name == "Walk");
        var clip = GltfAnimationClipData.Import(sourceClip, skin);
        var pose = skin.CreatePose();
        var times = new[] { 0f, clip.Duration * 0.5f, clip.Duration };

        foreach (var time in times)
        {
            clip.Evaluate(pose, time);
            for (var i = 0; i < skin.Nodes.Count; i++)
            {
                var sourceNode = model.LogicalNodes.Single(node => node.LogicalIndex == skin.Nodes[i].SourceNodeIndex);
                var expected = sourceNode.GetLocalTransform(sourceClip, time);
                var actual = pose.GetLocalTransform(i);
                AssertVectorClose(new Vector3(expected.Translation.X, expected.Translation.Y, expected.Translation.Z),
                    actual.Position);
                AssertVectorClose(new Vector3(expected.Scale.X, expected.Scale.Y, expected.Scale.Z), actual.Scale);
                var expectedRotation = new Quaternion(expected.Rotation.X, expected.Rotation.Y,
                    expected.Rotation.Z, expected.Rotation.W);
                Assert.InRange(MathF.Abs(MathF.Abs(Quaternion.Dot(expectedRotation, actual.Rotation)) - 1f), 0f, 0.0001f);
            }
        }

        var rootIndex = skin.Nodes.ToList().FindIndex(node => node.Name == "b_Root_00");
        Assert.True(rootIndex >= 0);
        Assert.Equal(skin.Nodes[rootIndex].RestTransform, pose.GetLocalTransform(rootIndex));
        Assert.Equal(skin.JointNodeIndices.Count, pose.ComputeSkinMatrices().Count);

        var unrelatedPose = GltfSkinData.Import(model, model.LogicalNodes.Single(node => node.Name == "fox")).CreatePose();
        Assert.Throws<ArgumentException>(() => clip.Evaluate(unrelatedPose, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Evaluate(pose, float.NaN));
    }

    [Fact]
    public void RotationInterpolationUsesTheShortestQuaternionPath()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var animation = model.LogicalAnimations.Single(clip => clip.Name == "Walk");
        var meshNode = model.LogicalNodes.Single(node => node.Name == "fox");
        animation.CreateRotationChannel(meshNode, new SortedDictionary<float, System.Numerics.Quaternion>
        {
            [0f] = System.Numerics.Quaternion.Identity,
            [animation.Duration] = new System.Numerics.Quaternion(0f, 0f, 0f, -1f)
        }, linear: true);
        var meshTranslation = meshNode.LocalTransform.Translation;
        animation.CreateTranslationChannel(meshNode, new SortedDictionary<float, System.Numerics.Vector3>
        {
            [0f] = meshTranslation,
            [animation.Duration] = meshTranslation + new System.Numerics.Vector3(2f, 0f, 0f)
        }, linear: true);

        var clip = GltfAnimationClipData.Import(animation, skin);
        var sourceNodeIndex = skin.MeshNodeIndex;
        var track = Assert.Single(clip.Tracks, item => item.NodeIndex == sourceNodeIndex
            && item.Path == GltfAnimationTargetPath.Rotation);
        var sampleTime = (track.QuaternionKeys[0].Time + track.QuaternionKeys[1].Time) * 0.5f;
        var pose = skin.CreatePose();
        clip.Evaluate(pose, sampleTime);

        Assert.InRange(MathF.Abs(MathF.Abs(pose.GetLocalTransform(sourceNodeIndex).Rotation.W) - 1f), 0f, 0.0001f);
        AssertMatrixClose(ToXnaMatrix(meshNode.GetWorldMatrix(animation, sampleTime)), pose.MeshNodeWorldMatrix);
    }

    [Fact]
    public void PlaybackSeekMatchesSequentialTimeAndLoopPauseSpeedControlsWork()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var sourceClip = model.LogicalAnimations.Single(animation => animation.Name == "Walk");
        var clip = GltfAnimationClipData.Import(sourceClip, skin);
        var seekPose = skin.CreatePose();
        var sequentialPose = skin.CreatePose();
        var targetTime = clip.Duration * 0.6f;

        var seekPlayback = new GltfAnimationPlayback(clip, loop: false);
        seekPlayback.Seek(targetTime);
        clip.Evaluate(seekPose, seekPlayback.Time);

        var sequentialPlayback = new GltfAnimationPlayback(clip, loop: false);
        sequentialPlayback.Play();
        for (var i = 0; i < 6; i++) sequentialPlayback.Advance(targetTime / 6f);
        clip.Evaluate(sequentialPose, sequentialPlayback.Time);
        AssertPosesClose(seekPose, sequentialPose);

        sequentialPlayback.Pause();
        var pausedTime = sequentialPlayback.Time;
        sequentialPlayback.Advance(0.25f);
        Assert.Equal(pausedTime, sequentialPlayback.Time);
        Assert.False(sequentialPlayback.IsPlaying);

        var looping = new GltfAnimationPlayback(clip, loop: true, speed: 2f);
        looping.Seek(clip.Duration - 0.05f);
        looping.Play();
        looping.Advance(0.05f);
        Assert.InRange(MathF.Abs(looping.Time - 0.05f), 0f, 0.00001f);
        looping.Pause();
        Assert.False(looping.IsPlaying);

        looping.Speed = -1f;
        looping.Seek(0.1f);
        looping.Play();
        looping.Advance(0.2f);
        Assert.InRange(MathF.Abs(looping.Time - (clip.Duration - 0.1f)), 0f, 0.00001f);

        var once = new GltfAnimationPlayback(clip, loop: false);
        once.Seek(clip.Duration - 0.02f);
        once.Play();
        once.Advance(0.1f);
        Assert.Equal(clip.Duration, once.Time);
        Assert.False(once.IsPlaying);
    }

    [Fact]
    public void TwoCharacterInstancesSharingClipsKeepIndependentPlaybackAndPoses()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var walk = GltfAnimationClipData.Import(model.LogicalAnimations.Single(animation => animation.Name == "Walk"), skin);
        var run = GltfAnimationClipData.Import(model.LogicalAnimations.Single(animation => animation.Name == "Run"), skin);
        var firstPose = skin.CreatePose();
        var secondPose = skin.CreatePose();
        var firstPlayback = new GltfAnimationPlayback(walk, loop: false);
        var secondPlayback = new GltfAnimationPlayback(run, loop: false);

        firstPlayback.Play();
        firstPlayback.Advance(0.2f);
        secondPlayback.Seek(0.8f);
        secondPlayback.Pause();
        walk.Evaluate(firstPose, firstPlayback.Time);
        run.Evaluate(secondPose, secondPlayback.Time);
        var secondHipIndex = skin.JointNodeIndices[2];
        var secondHipBefore = secondPose.GetLocalTransform(secondHipIndex);
        var secondMatricesBefore = secondPose.SkinMatrices.ToArray();

        firstPlayback.Advance(0.15f);
        walk.Evaluate(firstPose, firstPlayback.Time);

        Assert.InRange(MathF.Abs(firstPlayback.Time - 0.35f), 0f, 0.00001f);
        Assert.Equal(0.8f, secondPlayback.Time);
        Assert.False(secondPlayback.IsPlaying);
        Assert.Equal(secondHipBefore, secondPose.GetLocalTransform(secondHipIndex));
        Assert.Equal(secondMatricesBefore, secondPose.SkinMatrices.ToArray());
        Assert.NotEqual(firstPose.GetLocalTransform(secondHipIndex), secondHipBefore);
    }

    [Fact]
    public void CrossfadeMatchesBothClipEndpointsAndInterpolatesTheMiddlePose()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var walk = GltfAnimationClipData.Import(model.LogicalAnimations.Single(animation => animation.Name == "Walk"), skin);
        var run = GltfAnimationClipData.Import(model.LogicalAnimations.Single(animation => animation.Name == "Run"), skin);
        var crossfade = new GltfAnimationCrossfade(walk, run);
        var fromPose = skin.CreatePose();
        var toPose = skin.CreatePose();
        var outputPose = skin.CreatePose();
        var fromTime = walk.Duration * 0.4f;
        var toTime = run.Duration * 0.4f;
        walk.Evaluate(fromPose, fromTime);
        run.Evaluate(toPose, toTime);

        crossfade.Evaluate(outputPose, fromTime, toTime, 0f);
        AssertPosesClose(fromPose, outputPose);
        Assert.Equal(fromPose.SkinMatrices.ToArray(), outputPose.SkinMatrices.ToArray());

        crossfade.Evaluate(outputPose, fromTime, toTime, 1f);
        AssertPosesClose(toPose, outputPose);
        Assert.Equal(toPose.SkinMatrices.ToArray(), outputPose.SkinMatrices.ToArray());

        crossfade.Evaluate(outputPose, fromTime, toTime, 0.5f);
        for (var nodeIndex = 0; nodeIndex < skin.Nodes.Count; nodeIndex++)
        {
            var from = fromPose.GetLocalTransform(nodeIndex);
            var to = toPose.GetLocalTransform(nodeIndex);
            var blended = outputPose.GetLocalTransform(nodeIndex);
            AssertVectorClose(Vector3.Lerp(from.Position, to.Position, 0.5f), blended.Position);
            AssertVectorClose(Vector3.Lerp(from.Scale, to.Scale, 0.5f), blended.Scale);
            var destinationRotation = to.Rotation;
            if (Quaternion.Dot(from.Rotation, destinationRotation) < 0f)
                destinationRotation = new Quaternion(-destinationRotation.X, -destinationRotation.Y,
                    -destinationRotation.Z, -destinationRotation.W);
            var expectedRotation = Quaternion.Normalize(Quaternion.Slerp(from.Rotation, destinationRotation, 0.5f));
            Assert.InRange(MathF.Abs(MathF.Abs(Quaternion.Dot(expectedRotation, blended.Rotation)) - 1f), 0f, 0.0001f);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => crossfade.Evaluate(outputPose, fromTime, toTime, 1.1f));
    }

    [Fact]
    public void NamedHandAttachmentUsesAnimatedBoneWorldAndLocalOffset()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var skin = ImportFoxSkin(model);
        var attachment = new GltfBoneAttachment(skin, "b_RightHand_08", Matrix.CreateTranslation(1f, 2f, 3f));
        var clip = GltfAnimationClipData.Import(model.LogicalAnimations.Single(animation => animation.Name == "Walk"), skin);
        var pose = skin.CreatePose();
        var instanceWorld = Matrix.CreateTranslation(10f, 0f, 0f);
        clip.Evaluate(pose, 0f);
        var bindAttachmentWorld = attachment.GetWorldMatrix(pose, instanceWorld);
        clip.Evaluate(pose, clip.Duration * 0.5f);
        var animatedAttachmentWorld = attachment.GetWorldMatrix(pose, instanceWorld);

        AssertMatrixClose(attachment.LocalOffset * pose.GetNodeWorldMatrix(attachment.BoneNodeIndex) * instanceWorld,
            animatedAttachmentWorld);
        Assert.NotEqual(bindAttachmentWorld, animatedAttachmentWorld);
        Assert.Throws<ArgumentException>(() => new GltfBoneAttachment(skin, "missing-hand", Matrix.Identity));

        var badOffset = Matrix.Identity;
        badOffset.M11 = float.NaN;
        Assert.Throws<ArgumentException>(() => new GltfBoneAttachment(skin, "b_RightHand_08", badOffset));
    }

    private static GltfSkinData ImportFoxSkin(ModelRoot model) =>
        GltfSkinData.Import(model, model.LogicalNodes.Single(node => node.Name == "fox"));

    private static string FoxFixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "Fox.glb");

    private static void AssertPosesClose(GltfSkinPose expected, GltfSkinPose actual)
    {
        Assert.Equal(expected.NodeCount, actual.NodeCount);
        for (var i = 0; i < expected.NodeCount; i++)
        {
            var left = expected.GetLocalTransform(i);
            var right = actual.GetLocalTransform(i);
            AssertVectorClose(left.Position, right.Position);
            AssertVectorClose(left.Scale, right.Scale);
            Assert.InRange(MathF.Abs(MathF.Abs(Quaternion.Dot(left.Rotation, right.Rotation)) - 1f), 0f, 0.0001f);
        }
    }

    private static void AssertVectorClose(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, 0.0001f);
    }

    private static Matrix ToXnaMatrix(System.Numerics.Matrix4x4 value) => new(
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44);

    private static void AssertMatrixClose(Matrix expected, Matrix actual)
    {
        var expectedValues = new[]
        {
            expected.M11, expected.M12, expected.M13, expected.M14,
            expected.M21, expected.M22, expected.M23, expected.M24,
            expected.M31, expected.M32, expected.M33, expected.M34,
            expected.M41, expected.M42, expected.M43, expected.M44
        };
        var actualValues = new[]
        {
            actual.M11, actual.M12, actual.M13, actual.M14,
            actual.M21, actual.M22, actual.M23, actual.M24,
            actual.M31, actual.M32, actual.M33, actual.M34,
            actual.M41, actual.M42, actual.M43, actual.M44
        };
        for (var i = 0; i < expectedValues.Length; i++)
            Assert.InRange(MathF.Abs(expectedValues[i] - actualValues[i]), 0f, 0.0001f);
    }
}

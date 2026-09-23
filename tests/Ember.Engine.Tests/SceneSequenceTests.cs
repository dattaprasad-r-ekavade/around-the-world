using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Assets;
using Ember.Scene;
using Ember.Sequence;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneSequenceTests
{
    [Fact]
    public void CharacterTrackEvaluatesTheSamePoseAfterArbitrarySeekHistory()
    {
        var character = LoadFox();
        var clip = character.Animations.Single(animation => animation.Name == "Walk");
        var scene = new SceneGraph();
        var targetId = Guid.NewGuid();
        scene.Add(new SceneObject(targetId, "Sequence target"));
        var pose = character.CreatePose();
        var poses = new Dictionary<Guid, GltfSkinPose> { [targetId] = pose };
        var sequence = new SceneSequence("Walk proof", 2f,
            [new CharacterClipTrack(targetId, clip, startTime: 0.2f, loop: true)]);

        sequence.Evaluate(0.73f, scene, poses);
        var expected = CapturePose(pose);
        sequence.Evaluate(1.8f, scene, poses);
        sequence.Evaluate(0.21f, scene, poses);
        sequence.Evaluate(0.73f, scene, poses);

        Assert.Equal(expected, CapturePose(pose));
        Assert.Equal(targetId, scene.Objects.Single().Id);
    }

    [Fact]
    public void CameraTracksInterpolateAndCutByStableCameraId()
    {
        var scene = new SceneGraph();
        var targetId = Guid.NewGuid();
        var authoredObject = new SceneObject(targetId, "Actor");
        scene.Add(authoredObject);
        var character = LoadFox();
        var pose = character.CreatePose();
        var poses = new Dictionary<Guid, GltfSkinPose> { [targetId] = pose };
        var wideId = Guid.NewGuid();
        var closeId = Guid.NewGuid();
        var sequence = new SceneSequence("Camera cut proof", 2f,
            [new CharacterClipTrack(targetId, character.Animations.First())],
            [
                new SequenceCameraTrack(wideId, "Wide", [
                    new SequenceCameraKeyframe(0f, new SequenceCameraTransform(Vector3.Zero, Quaternion.Identity, 60f)),
                    new SequenceCameraKeyframe(2f, new SequenceCameraTransform(new Vector3(10f, 4f, 0f), Quaternion.CreateFromAxisAngle(Vector3.Up, MathF.PI), 80f))
                ]),
                new SequenceCameraTrack(closeId, "Close", [
                    new SequenceCameraKeyframe(0f, new SequenceCameraTransform(new Vector3(0f, 0f, 5f), Quaternion.Identity, 35f))
                ])
            ],
            new SequenceCameraCutTrack([
                new SequenceCameraCutKeyframe(0f, wideId),
                new SequenceCameraCutKeyframe(1f, closeId)
            ]));

        var beforeCut = sequence.Evaluate(0.5f, scene, poses);
        var afterCut = sequence.Evaluate(1f, scene, poses);

        Assert.Equal(wideId, beforeCut.CameraId);
        Assert.Equal("Wide", beforeCut.CameraName);
        Assert.Equal(new Vector3(2.5f, 1f, 0f), beforeCut.CameraTransform!.Value.Position);
        Assert.Equal(65f, beforeCut.CameraTransform.Value.FieldOfViewDegrees);
        Assert.Equal(closeId, afterCut.CameraId);
        Assert.Equal(new Vector3(0f, 0f, 5f), afterCut.CameraTransform!.Value.Position);
        Assert.Same(authoredObject, scene.Find(targetId));
    }

    [Fact]
    public void PlaybackScrubbingClampsTimeAndDoesNotDispatchBehaviourInteractions()
    {
        var character = LoadFox();
        var clip = character.Animations.Single(animation => animation.Name == "Walk");
        var scene = new SceneGraph();
        var targetId = Guid.NewGuid();
        scene.Add(new SceneObject(targetId, "Actor"));
        var probe = new InteractionProbe();
        using var behaviours = new SceneBehaviourRuntime(scene);
        behaviours.Add(targetId, probe);
        behaviours.Start();

        var pose = character.CreatePose();
        var sequence = new SceneSequence("Scrub proof", 2f,
            [new CharacterClipTrack(targetId, clip, loop: true)]);
        var player = new SceneSequencePlayer(sequence);
        var poses = new Dictionary<Guid, GltfSkinPose> { [targetId] = pose };
        player.Play();
        player.Advance(0.8f);
        sequence.Evaluate(player.Time, scene, poses);
        player.Pause();
        player.Seek(0.3f);
        sequence.Evaluate(player.Time, scene, poses);
        var expected = CapturePose(pose);
        player.Seek(-4f);
        sequence.Evaluate(player.Time, scene, poses);
        Assert.Equal(0f, player.Time);
        player.Seek(0.3f);
        sequence.Evaluate(player.Time, scene, poses);

        Assert.Equal(expected, CapturePose(pose));
        Assert.Equal(0, probe.InteractionCount);
        player.Seek(10f);
        Assert.Equal(sequence.Duration, player.Time);
        player.Play();
        Assert.Equal(0f, player.Time);
        Assert.True(player.IsPlaying);
    }

    private static GltfSkinnedCharacterData LoadFox() =>
        GltfSkinnedCharacterData.Import(ModelRoot.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Fox.glb")));

    private static GltfLocalTransform[] CapturePose(GltfSkinPose pose) =>
        Enumerable.Range(0, pose.NodeCount).Select(pose.GetLocalTransform).ToArray();

    private sealed class InteractionProbe : SceneBehaviour
    {
        public int InteractionCount { get; private set; }
        protected override void OnInteract(SceneInteraction interaction) => InteractionCount++;
    }
}

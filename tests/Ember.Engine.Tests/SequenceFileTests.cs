using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ember.Assets;
using Ember.Scene;
using Ember.Sequence;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SequenceFileTests
{
    [Fact]
    public void AtomicRoundTripPreservesCharacterCameraAndCutTracksAndEvaluatesTheSameFrame()
    {
        var directory = NewDirectory();
        try
        {
            var fixture = CreateFixture();
            var scenePath = Path.Combine(directory, "scene.json");
            var sequencePath = Path.Combine(directory, "sequence.json");
            SceneFile.SaveAtomic(fixture.Scene, scenePath);
            SequenceFile.SaveAtomic(fixture.Sequence, fixture.Scene, sequencePath);

            var reopenedScene = SceneFile.Load(scenePath);
            var reopened = SequenceFile.Load(sequencePath, reopenedScene, fixture.ClipsByAssetId);
            var characterTrack = Assert.Single(reopened.CharacterTracks);
            Assert.Equal(fixture.TrackId, characterTrack.Id);
            Assert.Equal(fixture.TargetId, characterTrack.TargetObjectId);
            Assert.Equal("Walk", characterTrack.Clip.Name);
            Assert.Equal(0.2f, characterTrack.StartTime);
            Assert.Equal(1.25f, characterTrack.PlaybackSpeed);
            Assert.True(characterTrack.Loop);
            Assert.Equal("Walk shot", reopened.Name);
            Assert.Equal(2f, reopened.Duration);

            Assert.Equal(fixture.WideCameraId, reopened.CameraTracks[0].Id);
            Assert.Equal("Wide", reopened.CameraTracks[0].Name);
            Assert.Equal(new Vector3(2f, 3f, 5f), reopened.CameraTracks[0].Sample(0f).Position);
            Assert.Equal(54f, reopened.CameraTracks[0].Sample(2f).FieldOfViewDegrees);
            Assert.Equal(fixture.CloseCameraId, reopened.CameraTracks[1].Id);
            Assert.Equal("Close", reopened.SampleCamera(1f).CameraName);
            Assert.Equal(fixture.CloseCameraId, reopened.CameraCuts!.Sample(1f, fixture.WideCameraId));

            var originalPose = fixture.Character.CreatePose();
            var reopenedPose = fixture.Character.CreatePose();
            fixture.Sequence.Evaluate(0.73f, fixture.Scene,
                new Dictionary<Guid, GltfSkinPose> { [fixture.TargetId] = originalPose });
            reopened.Evaluate(0.73f, reopenedScene,
                new Dictionary<Guid, GltfSkinPose> { [fixture.TargetId] = reopenedPose });
            Assert.Equal(CapturePose(originalPose), CapturePose(reopenedPose));

            var json = File.ReadAllText(sequencePath);
            Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
            Assert.Contains(fixture.TargetId.ToString(), json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(fixture.AssetId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsMissingTargetAssetAndClipReferences()
    {
        var directory = NewDirectory();
        try
        {
            var fixture = CreateFixture();
            var path = Path.Combine(directory, "sequence.json");
            SequenceFile.SaveAtomic(fixture.Sequence, fixture.Scene, path);

            var missingTarget = new SceneGraph();
            var targetError = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.Load(path, missingTarget, fixture.ClipsByAssetId));
            Assert.Contains("missing scene object", targetError.Message, StringComparison.Ordinal);

            var wrongAssetScene = new SceneGraph();
            wrongAssetScene.Add(new SceneObject(fixture.TargetId, "Actor")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/Fox.glb")
            });
            var assetError = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.Load(path, wrongAssetScene, fixture.ClipsByAssetId));
            Assert.Contains("is not assigned", assetError.Message, StringComparison.Ordinal);

            var missingClipError = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.Load(path, fixture.Scene,
                    new Dictionary<Guid, IReadOnlyList<GltfAnimationClipData>> { [fixture.AssetId] = Array.Empty<GltfAnimationClipData>() }));
            Assert.Contains("missing clip 'Walk'", missingClipError.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsUnsupportedVersionsAndCameraCutsToMissingTracks()
    {
        var directory = NewDirectory();
        try
        {
            var fixture = CreateFixture();
            var path = Path.Combine(directory, "sequence.json");
            SequenceFile.SaveAtomic(fixture.Sequence, fixture.Scene, path);
            var original = File.ReadAllText(path);

            var root = JsonNode.Parse(original)!;
            root["version"] = SequenceFile.CurrentVersion + 1;
            File.WriteAllText(path, root.ToJsonString());
            var versionError = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.Load(path, fixture.Scene, fixture.ClipsByAssetId));
            Assert.Contains("Unsupported sequence version", versionError.Message, StringComparison.Ordinal);

            root = JsonNode.Parse(original)!;
            root["cameraCuts"]![0]!["cameraId"] = Guid.NewGuid();
            File.WriteAllText(path, root.ToJsonString());
            var cutError = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.Load(path, fixture.Scene, fixture.ClipsByAssetId));
            Assert.Contains("reference a camera track ID", cutError.Message, StringComparison.Ordinal);

            File.WriteAllText(path, "{ not-json");
            var jsonError = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.Load(path, fixture.Scene, fixture.ClipsByAssetId));
            Assert.Contains("Sequence JSON is invalid", jsonError.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidSaveDoesNotReplaceThePreviousValidSequence()
    {
        var directory = NewDirectory();
        try
        {
            var fixture = CreateFixture();
            var path = Path.Combine(directory, "sequence.json");
            SequenceFile.SaveAtomic(fixture.Sequence, fixture.Scene, path);
            var previous = File.ReadAllText(path);
            var sceneWithoutTarget = new SceneGraph();

            var exception = Assert.Throws<InvalidDataException>(() =>
                SequenceFile.SaveAtomic(fixture.Sequence, sceneWithoutTarget, path));

            Assert.Contains("missing scene object", exception.Message, StringComparison.Ordinal);
            Assert.Equal(previous, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static Fixture CreateFixture()
    {
        var character = LoadFox();
        var scene = new SceneGraph();
        var targetId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        scene.Add(new SceneObject(targetId, "Actor")
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/Fox.glb")
        });

        var trackId = Guid.NewGuid();
        var wideCameraId = Guid.NewGuid();
        var closeCameraId = Guid.NewGuid();
        var sequence = new SceneSequence("Walk shot", 2f,
            [new CharacterClipTrack(targetId, character.Animations.Single(item => item.Name == "Walk"),
                startTime: 0.2f, playbackSpeed: 1.25f, loop: true, id: trackId)],
            [
                new SequenceCameraTrack(wideCameraId, "Wide", [
                    new SequenceCameraKeyframe(0f, new SequenceCameraTransform(
                        new Vector3(2f, 3f, 5f), Quaternion.Identity, 48f)),
                    new SequenceCameraKeyframe(2f, new SequenceCameraTransform(
                        new Vector3(-4f, 2f, 8f), Quaternion.CreateFromAxisAngle(Vector3.Up, 0.25f), 54f))
                ]),
                new SequenceCameraTrack(closeCameraId, "Close", [
                    new SequenceCameraKeyframe(0f, new SequenceCameraTransform(
                        new Vector3(1f, 2f, 3f), Quaternion.Identity, 36f))
                ])
            ],
            new SequenceCameraCutTrack([
                new SequenceCameraCutKeyframe(0f, wideCameraId),
                new SequenceCameraCutKeyframe(1f, closeCameraId)
            ]));
        IReadOnlyDictionary<Guid, IReadOnlyList<GltfAnimationClipData>> clipsByAssetId =
            new Dictionary<Guid, IReadOnlyList<GltfAnimationClipData>> { [assetId] = character.Animations };

        return new Fixture(scene, sequence, character, clipsByAssetId,
            targetId, assetId, trackId, wideCameraId, closeCameraId);
    }

    private static GltfSkinnedCharacterData LoadFox() => GltfSkinnedCharacterData.Import(
        ModelRoot.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Fox.glb")));

    private static Guid NewDirectoryId() => Guid.NewGuid();

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-sequence-file-" + NewDirectoryId().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static GltfLocalTransform[] CapturePose(GltfSkinPose pose) =>
        Enumerable.Range(0, pose.NodeCount).Select(pose.GetLocalTransform).ToArray();

    private sealed record Fixture(SceneGraph Scene, SceneSequence Sequence, GltfSkinnedCharacterData Character,
        IReadOnlyDictionary<Guid, IReadOnlyList<GltfAnimationClipData>> ClipsByAssetId,
        Guid TargetId, Guid AssetId, Guid TrackId, Guid WideCameraId, Guid CloseCameraId);
}

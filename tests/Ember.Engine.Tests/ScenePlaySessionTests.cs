using System;
using Ember.Audio;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ScenePlaySessionTests
{
    [Fact]
    public void CompiledBehaviourStartsStopsAndReceivesEnabledOwnerInteractionsOnce()
    {
        var scene = new SceneGraph();
        var ownerId = Guid.NewGuid();
        scene.Add(new SceneObject(ownerId, "Switch"));
        var probe = new ProbeBehaviour();
        var session = new ScenePlaySession(scene, (_, runtime) => runtime.Add(ownerId, probe));
        using (session)
        {
            session.Behaviours.Start();
            Assert.Equal(1, probe.StartCount);
            Assert.Equal(1, session.Behaviours.Interact(ownerId, "Activate"));
            Assert.Equal(new[] { "Activate" }, probe.Actions);

            session.RuntimeScene.Find(ownerId)!.Enabled = false;
            Assert.Equal(0, session.Behaviours.Interact(ownerId, "Activate"));
            session.RuntimeScene.Find(ownerId)!.Enabled = true;
        }

        Assert.Equal(1, probe.StopCount);
        Assert.Throws<ObjectDisposedException>(() => session.Behaviours.Interact(ownerId, "Activate"));
    }

    [Fact]
    public void AudioBehaviourRoutesInteractionVolumeAndSceneUnloadToImportedVoice()
    {
        var voice = new FakeAudioVoice();
        var clip = new ImportedAudioClip("interaction.wav", voice);
        var scene = new SceneGraph();
        var ownerId = Guid.NewGuid();
        scene.Add(new SceneObject(ownerId, "Bell"));

        using (var session = new ScenePlaySession(scene, (_, runtime) =>
        {
            runtime.Own(clip);
            runtime.Add(ownerId, new PlayAudioOnInteractionBehaviour(clip));
        }))
        {
            clip.Volume = 0f;
            Assert.Equal(0f, voice.Volume);
            Assert.Equal(1, session.Behaviours.Interact(ownerId, "Interact"));
            Assert.Equal(1, voice.PlayCount);
            Assert.Equal(1, session.Behaviours.Interact(ownerId, "Examine"));
            Assert.Equal(1, voice.PlayCount);
        }

        Assert.Equal(1, voice.StopCount);
        Assert.Equal(1, voice.DisposeCount);
        Assert.True(clip.IsDisposed);
    }

    [Fact]
    public void ReloadingSceneCreatesFreshBehaviourLifecycleWithoutRetainingTheOldOne()
    {
        var scene = new SceneGraph();
        var ownerId = Guid.NewGuid();
        scene.Add(new SceneObject(ownerId, "Door"));
        var first = new ProbeBehaviour();
        var firstSession = new ScenePlaySession(scene, (_, runtime) => runtime.Add(ownerId, first));
        firstSession.Dispose();

        var second = new ProbeBehaviour();
        using var secondSession = new ScenePlaySession(scene, (_, runtime) => runtime.Add(ownerId, second));
        secondSession.Behaviours.Interact(ownerId, "Open");

        Assert.Equal(1, first.StartCount);
        Assert.Equal(1, first.StopCount);
        Assert.Empty(first.Actions);
        Assert.Equal(1, second.StartCount);
        Assert.Equal(new[] { "Open" }, second.Actions);
    }

    [Fact]
    public void PlayCloneCanMoveAndDeleteObjectsWithoutChangingAuthoredScene()
    {
        var authored = new SceneGraph();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        authored.Add(new SceneObject(parentId, "Parent")
        {
            Transform = new Transform { Position = new Vector3(1f, 2f, 3f) }
        });
        authored.Add(new SceneObject(childId, "Child")
        {
            CharacterSettings = new GltfCharacterSettings { ClipName = "Walk", IsPlaying = true }
        });
        authored.SetParent(childId, parentId);

        var session = new ScenePlaySession(authored);
        var runtime = session.RuntimeScene;
        runtime.Find(parentId)!.Transform.Position = new Vector3(20f, 0f, 0f);
        Assert.True(runtime.Remove(childId));
        session.Dispose();
        session.Dispose();

        Assert.Equal(new Vector3(1f, 2f, 3f), authored.Find(parentId)!.Transform.Position);
        Assert.NotNull(authored.Find(childId));
        Assert.Null(runtime.Find(childId));
        Assert.Equal("Walk", authored.Find(childId)!.CharacterSettings!.ClipName);
    }

    [Fact]
    public void PlayCloneKeepsTravelAndRpgIdentityComponents()
    {
        var objectId = Guid.NewGuid();
        var cellId = Guid.NewGuid();
        var spawnId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var authored = new SceneGraph();
        authored.Add(new SceneObject(objectId, "Guard")
        {
            WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Actor, "actors.guard", instanceId),
            Door = new WorldDoorComponent(cellId, spawnId, Quaternion.Identity),
            SpawnPoint = new WorldSpawnComponent(spawnId),
            ResetPolicy = WorldInstanceResetPolicy.ResetOnCellReset
        });

        using var session = new ScenePlaySession(authored);
        var clone = session.RuntimeScene.Find(objectId)!;

        Assert.Equal(instanceId, clone.WorldEntity!.InstanceId);
        Assert.Equal("actors.guard", clone.WorldEntity.DefinitionId);
        Assert.Equal(cellId, clone.Door!.DestinationCellId);
        Assert.Equal(spawnId, clone.SpawnPoint!.Id);
        Assert.Equal(WorldInstanceResetPolicy.ResetOnCellReset, clone.ResetPolicy);
    }

    private sealed class ProbeBehaviour : SceneBehaviour
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public System.Collections.Generic.List<string> Actions { get; } = new();

        protected override void OnStart(SceneBehaviourContext context) => StartCount++;
        protected override void OnInteract(SceneInteraction interaction) => Actions.Add(interaction.Action);
        protected override void OnStop() => StopCount++;
    }

    private sealed class FakeAudioVoice : IAudioClipVoice
    {
        public float Volume { get; set; } = 1f;
        public bool IsPlaying { get; private set; }
        public int PlayCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void Play()
        {
            PlayCount++;
            IsPlaying = true;
        }

        public void Stop()
        {
            StopCount++;
            IsPlaying = false;
        }

        public void Dispose() => DisposeCount++;
    }
}

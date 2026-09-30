using System;
using Ember.Audio;
using Ember.Physics;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ScenePlaySessionTests
{
    [Fact]
    public void PhysicsTriggerTransitionsDispatchToTriggerBehaviourWithPhysicsInstigator()
    {
        var triggerId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(triggerId, "Finish trigger")
        {
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, new Vector3(2f, 2f, 2f), isTrigger: true),
            Transform = new Transform { Position = new Vector3(0f, 1f, 0f) }
        });
        using var physics = new PhysicsWorld(Vector3.Zero);
        using var colliders = new SceneStaticColliderSet(scene, physics);
        using var player = new PhysicsCharacterController(physics, new Vector3(-3.5f, 1f, 0f));
        var probe = new ProbeBehaviour();
        using var session = new ScenePlaySession(scene, (_, runtime) => runtime.Add(triggerId, probe));
        player.SetMoveInput(Vector3.Right);
        var dispatchedCallbacks = 0;

        for (var step = 0; step < 72; step++)
        {
            physics.Step(1f / 60f);
            dispatchedCallbacks += session.DispatchTriggerEvents(colliders.TriggerEvents);
        }

        Assert.True(player.Pose.Position.X > 1f);
        Assert.Equal(2, dispatchedCallbacks);
        Assert.Collection(probe.Interactions,
            entered =>
            {
                Assert.Equal("TriggerEnter", entered.Action);
                Assert.Equal(player.PhysicsBodyId, entered.PhysicsInstigatorId);
                Assert.Null(entered.InstigatorId);
            },
            exited =>
            {
                Assert.Equal("TriggerExit", exited.Action);
                Assert.Equal(player.PhysicsBodyId, exited.PhysicsInstigatorId);
                Assert.Null(exited.InstigatorId);
            });
    }

    [Fact]
    public void SavedTriggerActionRunsOnceAndReportsBoundSceneCharacterIdentity()
    {
        var triggerId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var physicsId = new PhysicsObjectId(42);
        var scene = new SceneGraph();
        scene.Add(new SceneObject(triggerId, "Finish")
        {
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true),
            TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.ReachGoal)
        });
        scene.Add(new SceneObject(characterId, "Player"));

        using var session = new ScenePlaySession(scene);
        session.BindPhysicsCharacter(physicsId, characterId);
        SceneAuthoredActionEvent? executed = null;
        var actionCount = 0;
        session.AuthoredActionExecuted += value =>
        {
            executed = value;
            actionCount++;
        };

        Assert.Equal(0, session.DispatchTriggerEvents(new[]
        {
            new SceneTriggerEvent(triggerId, physicsId, PhysicsTriggerTransition.Entered)
        }));
        Assert.True(session.HasReachedGoal);
        Assert.NotNull(executed);
        Assert.Equal(SceneTriggerActionKind.ReachGoal, executed!.Value.Kind);
        Assert.Equal(triggerId, executed.Value.SceneObjectId);
        Assert.Equal(characterId, executed.Value.InstigatorId);
        Assert.Equal(physicsId, executed.Value.PhysicsInstigatorId);

        session.DispatchTriggerEvents(new[]
        {
            new SceneTriggerEvent(triggerId, physicsId, PhysicsTriggerTransition.Exited),
            new SceneTriggerEvent(triggerId, physicsId, PhysicsTriggerTransition.Entered)
        });
        Assert.Equal(1, actionCount);
        Assert.Equal(characterId, executed.Value.InstigatorId);
    }

    [Fact]
    public void SavedCollectActionDisablesItsTriggerOnlyInThePlayClone()
    {
        var triggerId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(triggerId, "Coin")
        {
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true),
            TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.Collect)
        });
        using var session = new ScenePlaySession(scene);

        session.DispatchTriggerEvents(new[]
        {
            new SceneTriggerEvent(triggerId, new PhysicsObjectId(2), PhysicsTriggerTransition.Entered)
        });

        Assert.False(session.RuntimeScene.Find(triggerId)!.Enabled);
        Assert.True(scene.Find(triggerId)!.Enabled);
    }

    [Fact]
    public void SavedOpenActionRaisesLinkedDoorRequestForTheBoundCharacter()
    {
        var triggerId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var physicsId = new PhysicsObjectId(43);
        var door = new WorldDoorComponent(Guid.NewGuid(), Guid.NewGuid(), Quaternion.Identity);
        var scene = new SceneGraph();
        scene.Add(new SceneObject(triggerId, "House door")
        {
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true),
            Door = door,
            TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.Open)
        });
        scene.Add(new SceneObject(characterId, "Player"));
        using var session = new ScenePlaySession(scene);
        session.BindPhysicsCharacter(physicsId, characterId);
        SceneAuthoredActionEvent? executed = null;
        session.AuthoredActionExecuted += value => executed = value;

        session.DispatchTriggerEvents(new[]
        {
            new SceneTriggerEvent(triggerId, physicsId, PhysicsTriggerTransition.Entered)
        });

        Assert.NotNull(executed);
        Assert.Equal(SceneTriggerActionKind.Open, executed!.Value.Kind);
        Assert.Equal(triggerId, executed.Value.SceneObjectId);
        Assert.Equal(characterId, executed.Value.InstigatorId);
        Assert.Same(door, executed.Value.Door);
    }

    [Fact]
    public void OpenActionWithoutWorldDoorNamesItsOwningObject()
    {
        var item = new SceneObject(Guid.NewGuid(), "Unlinked house door")
        {
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true),
            TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.Open)
        };
        var scene = new SceneGraph();
        scene.Add(item);

        var exception = Assert.Throws<InvalidOperationException>(() => new ScenePlaySession(scene));
        Assert.Contains(item.Id.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(item.Name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidSavedTriggerActionNamesItsOwningObject()
    {
        var item = new SceneObject(Guid.NewGuid(), "Broken goal")
        {
            TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.ReachGoal)
        };
        var scene = new SceneGraph();
        scene.Add(item);

        var exception = Assert.Throws<InvalidOperationException>(() => new ScenePlaySession(scene));
        Assert.Contains(item.Id.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(item.Name, exception.Message, StringComparison.Ordinal);
    }

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
        var authoredCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One);
        authored.Add(new SceneObject(parentId, "Parent")
        {
            Transform = new Transform { Position = new Vector3(1f, 2f, 3f) }
        });
        authored.Add(new SceneObject(childId, "Child")
        {
            CharacterSettings = new GltfCharacterSettings { ClipName = "Walk", IsPlaying = true },
            BoxCollider = authoredCollider
        });
        authored.SetParent(childId, parentId);

        var session = new ScenePlaySession(authored);
        var runtime = session.RuntimeScene;
        Assert.Same(authoredCollider, runtime.Find(childId)!.BoxCollider);
        runtime.Find(childId)!.BoxCollider = new SceneBoxColliderComponent(
            new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f));
        runtime.Find(parentId)!.Transform.Position = new Vector3(20f, 0f, 0f);
        Assert.True(runtime.Remove(childId));
        session.Dispose();
        session.Dispose();

        Assert.Equal(new Vector3(1f, 2f, 3f), authored.Find(parentId)!.Transform.Position);
        Assert.NotNull(authored.Find(childId));
        Assert.Null(runtime.Find(childId));
        Assert.Equal("Walk", authored.Find(childId)!.CharacterSettings!.ClipName);
        Assert.Same(authoredCollider, authored.Find(childId)!.BoxCollider);
    }

    [Fact]
    public void CharacterPlaybackStateInPlayCloneCannotMutateAuthoredSettings()
    {
        var objectId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var authored = new SceneGraph();
        authored.Add(new SceneObject(objectId, "Animated character")
        {
            CharacterSettings = new GltfCharacterSettings
            {
                ClipName = "Walk",
                Time = 0.25f,
                IsPlaying = false
            }
        });
        authored.Find(objectId)!.CharacterSettings!.Attachments.Add(
            new GltfBoneAttachmentReference(attachmentId, "Hand", Matrix.CreateTranslation(1f, 2f, 3f)));

        using var session = new ScenePlaySession(authored);
        var runtimeSettings = session.RuntimeScene.Find(objectId)!.CharacterSettings!;
        runtimeSettings.ClipName = "Run";
        runtimeSettings.Time = 4f;
        runtimeSettings.IsPlaying = true;
        runtimeSettings.Attachments.Clear();

        var authoredSettings = authored.Find(objectId)!.CharacterSettings!;
        Assert.Equal("Walk", authoredSettings.ClipName);
        Assert.Equal(0.25f, authoredSettings.Time);
        Assert.False(authoredSettings.IsPlaying);
        Assert.Equal(attachmentId, Assert.Single(authoredSettings.Attachments).Id);
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
        public System.Collections.Generic.List<SceneInteraction> Interactions { get; } = new();

        protected override void OnStart(SceneBehaviourContext context) => StartCount++;
        protected override void OnInteract(SceneInteraction interaction)
        {
            Actions.Add(interaction.Action);
            Interactions.Add(interaction);
        }
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

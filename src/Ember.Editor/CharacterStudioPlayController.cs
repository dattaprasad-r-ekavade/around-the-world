using System;
using System.Collections.Generic;
using Ember.Audio;
using Ember.Input;
using Ember.Physics;
using Ember.Scene;

namespace Ember.Editor;

internal sealed class CharacterStudioPlayController
{
    public sealed record PathPreviewAgent(
        PhysicsCharacterController Controller, PhysicsCharacterPathFollower Follower);

    public ScenePlaySession? Session { get; private set; }
    public SceneCommandHistory History { get; private set; } = new();
    public InputActionMap InputMap { get; } = new();
    public ImportedAudioClip? InteractionClip { get; set; }
    public float InteractionVolume { get; private set; } = 0.65f;
    public PhysicsCharacterController? CharacterController { get; set; }
    public Guid? CharacterObjectId { get; set; }
    public Dictionary<Guid, PathPreviewAgent> PathFollowers { get; } = new();
    public PhysicsWorld? PhysicsWorld { get; set; }
    public SceneStaticColliderSet? SceneColliders { get; set; }
    public PhysicsFixedStepper? PhysicsStepper { get; set; }

    public void SetSession(ScenePlaySession? session) => Session = session;

    public void SetHistory(SceneCommandHistory history) =>
        History = history ?? throw new ArgumentNullException(nameof(history));

    public void SetInteractionVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume < 0f || volume > 1f) return;
        InteractionVolume = volume;
        if (InteractionClip is { IsDisposed: false } clip) clip.Volume = volume;
    }

    public void TriggerInteraction(Guid? ownerId)
    {
        if (Session is { } session && ownerId is { } id)
            session.Behaviours.Interact(id, "Interact");
    }

    public void DisposePhysics(ScenePlaySession? bindingSession = null)
    {
        bindingSession ??= Session;
        foreach (var agent in PathFollowers.Values)
        {
            bindingSession?.UnbindPhysicsCharacter(agent.Controller.PhysicsBodyId);
            agent.Controller.Dispose();
        }
        PathFollowers.Clear();
        if (CharacterController is { } playerController)
        {
            bindingSession?.UnbindPhysicsCharacter(playerController.PhysicsBodyId);
            playerController.Dispose();
        }
        CharacterController = null;
        CharacterObjectId = null;
        SceneColliders?.Dispose();
        SceneColliders = null;
        PhysicsWorld?.Dispose();
        PhysicsWorld = null;
        PhysicsStepper = null;
    }
}

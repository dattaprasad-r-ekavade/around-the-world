using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ember.Authoring;
using Ember.Assets;
using Ember.Audio;
using Ember;
using Ember.Input;
using Ember.Project;
using Ember.Physics;
using Ember.Scene;
using Ember.Sequence;
using Ember.Render;
using Ember.Rpg;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    private void StartPlaySession()
    {
        if (_playSession is not null || _preview is null) return;
        CancelPendingAssetPreview();
        ScenePlaySession? candidate = null;
        ImportedAudioClip? candidateAudio = null;
        try
        {
            _editorUi?.CompletePendingEdit(_sceneData);
            candidate = new ScenePlaySession(_sceneData, (runtimeScene, behaviours) =>
            {
                if (runtimeScene.Objects.Count == 0)
                    runtimeScene.Add(new SceneObject(Guid.NewGuid(), "Play Test"));
                var interactionClip = behaviours.Own(ImportedAudioClip.Load(
                    Path.Combine(AppContext.BaseDirectory, "Assets", "InteractionChime.wav")));
                candidateAudio = interactionClip;
                interactionClip.Volume = _interactionVolume;
                foreach (var item in runtimeScene.Objects)
                    behaviours.Add(item.Id, new PlayAudioOnInteractionBehaviour(interactionClip));
            });
            candidate.AuthoredActionExecuted += action =>
            {
                var actor = action.InstigatorId is { } actorId
                    ? CurrentScene.Find(actorId)?.Name ?? "A character"
                    : "A character";
                _reimportStatus = action.Kind switch
                {
                    SceneTriggerActionKind.Collect => $"{actor} collected {action.SceneObjectName}.",
                    SceneTriggerActionKind.ReachGoal => $"{actor} reached goal: {action.SceneObjectName}.",
                    _ => $"{actor} triggered {action.SceneObjectName}."
                };
            };
            candidate.RuntimeScene.PlaySettings.ApplyInputBindings(_playInputMap);
            InitializePlayPhysics(candidate);
            var cleanupError = _preview.Reload(() => PreviewResources.Load(
                GraphicsDevice, ResolveSceneAssets(candidate.RuntimeScene), candidate.RuntimeScene));
            _playSession = candidate;
            candidate = null;
            _playAudioClip = candidateAudio;
            _playHistory = new SceneCommandHistory();
            _editorUi?.SetHistory(_playHistory);
            BuildSequencePreview();
            var controls = _playCharacterController is null
                ? "Play clone started. Follow a path to move an actor; P stops and restores."
                : "Play clone started. Configured move keys move; the configured jump key jumps; E interacts; P stops and restores.";
            _reimportStatus = cleanupError is null
                ? controls
                : $"{controls} Previous preview cleanup failed: {cleanupError.Message}";
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
            if (_playCharacterController is { } playerController)
            {
                var settings = _playSession!.RuntimeScene.PlaySettings;
                _camera.Reset(playerController.Pose.Position
                    + new Vector3(0f, settings.CameraTargetOffsetY, 0f),
                    settings.CameraDistance, _camera.Yaw, _camera.Pitch);
            }
        }
        catch (Exception exception)
        {
            if (candidate is not null)
            {
                DisposePathPhysics(candidate);
                candidate.Dispose();
                _reimportStatus = $"Play mode could not start: {exception.Message}";
            }
            else if (_playSession is not null)
            {
                StopPlaySession();
                if (_playSession is null)
                    _reimportStatus = $"Play mode could not start: {exception.Message}";
                else
                    _reimportStatus = $"Play mode startup failed: {exception.Message} Authored preview restoration also failed; stop Play mode to retry.";
            }
            else
            {
                _reimportStatus = $"Play mode could not start: {exception.Message}";
            }
        }
    }

    private void InitializePlayPhysics(ScenePlaySession session)
    {
        var scene = session.RuntimeScene;
        var playerObjectId = FindPlayCharacter(scene);
        CreatePathPhysicsWorld(scene, playerObjectId);
        if (playerObjectId is not { } objectId) return;

        var physicsWorld = _pathPreviewWorld
            ?? throw new InvalidOperationException("Play physics world could not be initialized.");
        var settings = scene.PlaySettings;
        var centerOffset = CharacterCenterOffset(settings);
        var worldPosition = scene.GetWorldMatrix(objectId).Translation;
        var controller = new PhysicsCharacterController(physicsWorld,
            worldPosition + new Vector3(0f, centerOffset, 0f), ToPhysicsCharacterSettings(settings));
        try
        {
            session.BindPhysicsCharacter(controller.PhysicsBodyId, objectId);
            _playCharacterController = controller;
            _playCharacterObjectId = objectId;
        }
        catch
        {
            controller.Dispose();
            throw;
        }
    }

    private Guid? FindPlayCharacter(SceneGraph scene)
    {
        bool IsCharacter(SceneObject item) => item.Enabled && item.TriggerAction is null
            && (item.CharacterSettings is not null
                || item.WorldEntity?.Kind == WorldEntityKind.Actor
                || item.GltfAsset is not null);

        bool IsAuthoredActor(SceneObject item) => item.Enabled && item.TriggerAction is null
            && (item.CharacterSettings is not null || item.WorldEntity?.Kind == WorldEntityKind.Actor);

        if (_editorUi?.SelectedObjectId is { } selectedId
            && scene.Find(selectedId) is { } selected && IsAuthoredActor(selected))
            return selected.Id;

        return scene.Objects.FirstOrDefault(item => item.Enabled
                   && item.TriggerAction is null && item.WorldEntity?.Kind == WorldEntityKind.Actor)?.Id
            ?? scene.Objects.FirstOrDefault(item => item.Enabled
                && item.TriggerAction is null && item.CharacterSettings is not null)?.Id
            ?? (_editorUi?.SelectedObjectId is { } fallbackId
                && scene.Find(fallbackId) is { } fallback && IsCharacter(fallback)
                    ? fallback.Id
                    : scene.Objects.FirstOrDefault(IsCharacter)?.Id);
    }

    private void CreatePathPhysicsWorld(SceneGraph scene, Guid? dynamicCharacterId = null)
    {
        if (_pathPreviewWorld is not null) return;
        _pathPreviewWorld = new PhysicsWorld();
        try
        {
            _pathPreviewWorld.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f));
            var dynamicCharacterIds = scene.Objects
                .Where(item => item.CharacterSettings is not null || item.WorldEntity?.Kind == WorldEntityKind.Actor)
                .Select(item => item.Id).ToHashSet();
            if (dynamicCharacterId is { } actorId) dynamicCharacterIds.Add(actorId);
            _playSceneColliders = new SceneStaticColliderSet(scene, _pathPreviewWorld, dynamicCharacterIds);
            _pathPreviewStepper = new PhysicsFixedStepper();
        }
        catch
        {
            _playSceneColliders?.Dispose();
            _playSceneColliders = null;
            _pathPreviewWorld.Dispose();
            _pathPreviewWorld = null;
            _pathPreviewStepper = null;
            throw;
        }
    }

    private static Vector3 ToOrbitCameraMovement(Vector2 localMovement, float cameraYaw)
    {
        if (localMovement.LengthSquared() > 1f) localMovement.Normalize();
        var rotation = Matrix.CreateRotationY(cameraYaw);
        var right = Vector3.Transform(Vector3.Right, rotation);
        var forward = Vector3.Transform(Vector3.Forward, rotation);
        var movement = right * localMovement.X + forward * localMovement.Y;
        movement.Y = 0f;
        return movement.LengthSquared() > 1f ? Vector3.Normalize(movement) : movement;
    }

    private void StopPlaySession()
    {
        if (_playSession is not { } session || _preview is null) return;
        DisposePathPhysics();
        _editorUi?.CompletePendingEdit(session.RuntimeScene);
        try
        {
            var cleanupError = _preview.Reload(() => PreviewResources.Load(
                GraphicsDevice, ResolveSceneAssets(_sceneData), _sceneData));
            Exception? sessionCleanupError = null;
            try { session.Dispose(); }
            catch (Exception exception) { sessionCleanupError = exception; }
            _playSession = null;
            _playAudioClip = null;
            _sceneData.PlaySettings.ApplyInputBindings(_playInputMap);
            _editorUi?.SetHistory(_editorHistory);
            BuildSequencePreview();
            var errors = new[] { cleanupError, sessionCleanupError }.Where(error => error is not null)
                .Select(error => error!.Message).ToArray();
            _reimportStatus = errors.Length == 0
                ? "Play clone stopped; authored scene restored."
                : $"Authored scene restored with cleanup issue: {string.Join("; ", errors)}";
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Could not restore the authored preview; play mode remains active: {exception.Message}";
        }
    }

    private void TriggerInteraction()
    {
        var ownerId = _editorUi?.SelectedObjectId
            ?? CurrentScene.Objects.FirstOrDefault()?.Id;
        _playController.TriggerInteraction(ownerId);
    }

    private void SetInteractionVolume(float volume) => _playController.SetInteractionVolume(volume);

    private string StartPathFollow(Guid objectId, CellPathGraph graph, CellPathRoute route)
    {
        if (_playSession is null) return "Start play mode before following an authored route.";
        var actor = CurrentScene.Find(objectId);
        if (actor is null) return "The selected actor is not in the play scene.";
        if (_playCharacterObjectId == objectId)
            return "This is the keyboard-controlled player; choose another actor to follow a route.";

        StopPathFollow(objectId);
        try
        {
            if (_pathPreviewWorld is null)
                CreatePathPhysicsWorld(CurrentScene, objectId);

            var physicsWorld = _pathPreviewWorld
                ?? throw new InvalidOperationException("Play physics world could not be initialized.");
            var settings = CurrentScene.PlaySettings;
            var centerOffset = CharacterCenterOffset(settings);
            var worldPosition = CurrentScene.GetWorldMatrix(objectId).Translation;
            var controller = new PhysicsCharacterController(physicsWorld,
                worldPosition + new Vector3(0f, centerOffset, 0f), ToPhysicsCharacterSettings(settings));
            try
            {
                var follower = new PhysicsCharacterPathFollower(controller, graph, route);
                _playSession.BindPhysicsCharacter(controller.PhysicsBodyId, objectId);
                _pathPreviewAgents.Add(objectId,
                    new CharacterStudioPlayController.PathPreviewAgent(controller, follower));
            }
            catch
            {
                _playSession?.UnbindPhysicsCharacter(controller.PhysicsBodyId);
                controller.Dispose();
                throw;
            }
            return $"Following route with collision-aware physics ({route.NodeIds.Count} nodes).";
        }
        catch (Exception exception)
        {
            if (_pathPreviewAgents.Count == 0 && _playCharacterController is null) DisposePathPhysics();
            return $"Could not start route following: {exception.Message}";
        }
    }

    private string? GetPathFollowStatus(Guid objectId) =>
        _pathPreviewAgents.TryGetValue(objectId, out var agent) ? agent.Follower.State.ToString() : null;

    private void StopPathFollow(Guid objectId)
    {
        if (_pathPreviewAgents.Remove(objectId, out var agent))
        {
            _playSession?.UnbindPhysicsCharacter(agent.Controller.PhysicsBodyId);
            agent.Controller.Dispose();
        }
        if (_pathPreviewAgents.Count == 0 && _playCharacterController is null) DisposePathPhysics();
    }

    private void UpdatePathFollowers(float elapsedSeconds)
    {
        if (_pathPreviewWorld is null || _pathPreviewStepper is null
            || (_pathPreviewAgents.Count == 0 && _playCharacterController is null)) return;
        var step = _pathPreviewStepper.Advance(elapsedSeconds, delta =>
        {
            foreach (var agent in _pathPreviewAgents.Values) agent.Follower.Advance(delta);
            _pathPreviewWorld.Step(delta);
            if (_playSession is { } session && _playSceneColliders is { } colliders)
                session.DispatchTriggerEvents(colliders.TriggerEvents);
        });
        foreach (var (objectId, agent) in _pathPreviewAgents.ToArray())
        {
            if (CurrentScene.Find(objectId) is not { } actor)
            {
                StopPathFollow(objectId);
                continue;
            }
            var physicsPose = _pathPreviewWorld.GetInterpolatedPose(agent.Controller.PhysicsBodyId,
                step.InterpolationAlpha);
            SetObjectWorldPosition(CurrentScene, actor,
                physicsPose.Position - new Vector3(0f, CharacterCenterOffset(CurrentScene.PlaySettings), 0f));
        }
        if (_playCharacterController is { } playerController
            && _playCharacterObjectId is { } playerObjectId
            && CurrentScene.Find(playerObjectId) is { } playerObject)
        {
            var position = _pathPreviewWorld.GetInterpolatedPose(playerController.PhysicsBodyId,
                step.InterpolationAlpha).Position;
            SetObjectWorldPosition(CurrentScene, playerObject,
                position - new Vector3(0f, CharacterCenterOffset(CurrentScene.PlaySettings), 0f));
            var cameraSettings = CurrentScene.PlaySettings;
            _camera.Reset(position + new Vector3(0f, cameraSettings.CameraTargetOffsetY, 0f),
                _camera.Distance, _camera.Yaw, _camera.Pitch);
        }
    }

    private static PhysicsCharacterSettings ToPhysicsCharacterSettings(ScenePlaySettings settings) => new()
    {
        Radius = settings.CapsuleRadius,
        CylinderLength = settings.CapsuleCylinderLength,
        MoveSpeed = settings.MoveSpeed,
        JumpSpeed = settings.JumpSpeed
    };

    private static float CharacterCenterOffset(ScenePlaySettings settings) =>
        settings.CapsuleRadius + settings.CapsuleCylinderLength * 0.5f;

    private static void SetObjectWorldPosition(SceneGraph scene, SceneObject item, Vector3 worldPosition)
    {
        if (item.ParentId is { } parentId)
        {
            var parentWorld = scene.GetWorldMatrix(parentId);
            var determinant = parentWorld.Determinant();
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-8f) return;
            worldPosition = Vector3.Transform(worldPosition, Matrix.Invert(parentWorld));
        }
        item.Transform.Position = worldPosition;
    }

    private void DisposePathPhysics(ScenePlaySession? bindingSession = null)
        => _playController.DisposePhysics(bindingSession);

}

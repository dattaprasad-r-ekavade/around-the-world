using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Assets;
using Ember.Audio;
using Ember.Input;
using Ember.Physics;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    private sealed class CharacterStudioPlayController
    {
        public sealed record PathPreviewAgent(
            PhysicsCharacterController Controller, PhysicsCharacterPathFollower Follower);

        private readonly CharacterStudioGame _owner;

        public CharacterStudioPlayController(CharacterStudioGame owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

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
        private readonly Queue<SceneAuthoredActionEvent> _pendingWorldTravelRequests = new();
        private Guid? _playWorldCellId;

        public void StartPlaySession()
        {
            if (Session is not null || _owner._preview is null) return;
            _owner.CancelPendingAssetPreview();
            ScenePlaySession? candidate = null;
            ImportedAudioClip? candidateAudio = null;
            try
            {
                _owner._editorUi?.CompletePendingEdit(_owner._sceneData);
                candidate = CreatePlaySession(_owner._sceneData, out candidateAudio);
                candidate.RuntimeScene.PlaySettings.ApplyInputBindings(InputMap);
                InitializePlayPhysics(candidate, _owner._preview.Current);
                var cleanupError = _owner._preview.Reload(() => PreviewResources.Load(
                    _owner.GraphicsDevice, _owner.ResolveSceneAssets(candidate.RuntimeScene), candidate.RuntimeScene));
                SetSession(candidate);
                _playWorldCellId = _owner._editorUi?.CurrentWorldCellId;
                _owner._editorUi?.SetPlayWorldCellId(_playWorldCellId);
                _pendingWorldTravelRequests.Clear();
                candidate = null;
                InteractionClip = candidateAudio;
                SetHistory(new SceneCommandHistory());
                _owner._editorUi?.SetHistory(History);
                _owner.BuildSequencePreview();
                var controls = CharacterController is null
                    ? "Play clone started. Choose an enabled character in Play setup to control it; P stops and restores."
                    : "Play clone started. Configured move keys move; the configured jump key jumps; E interacts; P stops and restores.";
                _owner._reimportStatus = cleanupError is null
                    ? controls
                    : $"{controls} Previous preview cleanup failed: {cleanupError.Message}";
                if (_owner.GetSceneBounds() is { } bounds) _owner._camera.Frame(bounds);
                if (CharacterController is { } playerController)
                {
                    var settings = Session!.RuntimeScene.PlaySettings;
                    _owner._camera.Reset(playerController.Pose.Position
                        + new Vector3(0f, settings.CameraTargetOffsetY, 0f),
                        settings.CameraDistance, _owner._camera.Yaw, _owner._camera.Pitch);
                }
            }
            catch (Exception exception)
            {
                if (candidate is not null)
                {
                    DisposePhysics(candidate);
                    candidate.Dispose();
                    _owner._reimportStatus = $"Play mode could not start: {exception.Message}";
                }
                else if (Session is not null)
                {
                    StopPlaySession();
                    if (Session is null)
                        _owner._reimportStatus = $"Play mode could not start: {exception.Message}";
                    else
                        _owner._reimportStatus = $"Play mode startup failed: {exception.Message} Authored preview restoration also failed; stop Play mode to retry.";
                }
                else
                {
                    _owner._reimportStatus = $"Play mode could not start: {exception.Message}";
                }
            }
        }

        private ScenePlaySession CreatePlaySession(SceneGraph source,
            out ImportedAudioClip? interactionClip)
        {
            interactionClip = null;
            ImportedAudioClip? createdClip = null;
            var candidate = new ScenePlaySession(source, (runtimeScene, behaviours) =>
            {
                if (runtimeScene.Objects.Count == 0)
                    runtimeScene.Add(new SceneObject(Guid.NewGuid(), "Play Test"));
                var clip = behaviours.Own(ImportedAudioClip.Load(
                    Path.Combine(AppContext.BaseDirectory, "Assets", "InteractionChime.wav")));
                createdClip = clip;
                clip.Volume = InteractionVolume;
                foreach (var item in runtimeScene.Objects)
                    behaviours.Add(item.Id, new PlayAudioOnInteractionBehaviour(clip));
            }, _owner._editorUi?.BehaviourRegistry);
            candidate.AuthoredActionExecuted += HandleAuthoredAction;
            interactionClip = createdClip;
            return candidate;
        }

        private void HandleAuthoredAction(SceneAuthoredActionEvent action)
        {
            if (action.Kind == SceneTriggerActionKind.Open && action.Door is not null)
            {
                _pendingWorldTravelRequests.Enqueue(action);
                return;
            }

            var actor = action.InstigatorId is { } actorId
                ? _owner.CurrentScene.Find(actorId)?.Name ?? "A character"
                : "A character";
            _owner._reimportStatus = action.Kind switch
            {
                SceneTriggerActionKind.Collect => $"{actor} collected {action.SceneObjectName}.",
                SceneTriggerActionKind.ReachGoal => $"{actor} reached goal: {action.SceneObjectName}.",
                _ => $"{actor} triggered {action.SceneObjectName}."
            };
        }

        public void AdvanceWorldTravelRequests()
        {
            if (Session is null || !_pendingWorldTravelRequests.TryDequeue(out var request)) return;
            if (request.Door is not { } door)
            {
                _owner._reimportStatus = $"Open action on '{request.SceneObjectName}' has no linked door.";
                return;
            }

            try { TravelToWorldDoor(request, door); }
            catch (Exception exception)
            {
                _pendingWorldTravelRequests.Clear();
                _owner._reimportStatus = $"Could not travel through '{request.SceneObjectName}': {exception.Message}";
            }
        }

        private void TravelToWorldDoor(SceneAuthoredActionEvent request, WorldDoorComponent door)
        {
            var sourceSession = Session
                ?? throw new InvalidOperationException("Play mode is not active.");
            var editorUi = _owner._editorUi
                ?? throw new InvalidOperationException("The world editor is not ready.");
            var previewOwner = _owner._preview
                ?? throw new InvalidOperationException("Character preview is not ready.");
            var sourcePlayerId = CharacterObjectId
                ?? request.InstigatorId
                ?? throw new InvalidOperationException("Choose an enabled Play character before using this door.");
            var sourcePlayer = sourceSession.RuntimeScene.Find(sourcePlayerId)
                ?? throw new InvalidOperationException($"Play character {sourcePlayerId} is missing.");
            var sourcePlayerWorld = sourceSession.RuntimeScene.GetWorldMatrix(sourcePlayerId);
            if (!sourcePlayerWorld.Decompose(out var sourcePlayerScale, out _, out _))
                throw new InvalidOperationException(
                    $"Play character '{sourcePlayer.Name}' has a transform that cannot be transferred between cells.");
            var destination = editorUi.ResolvePlayDoorDestination(
                sourceSession.RuntimeScene, _playWorldCellId, door);
            var destinationScene = SceneGraphCloner.Clone(destination.Scene);
            if (_playWorldCellId == destination.Cell.Id)
                destinationScene.Remove(sourcePlayerId);
            if (destinationScene.PlaySettings.PlayerObjectId is { } destinationPlayerId)
                destinationScene.Remove(destinationPlayerId);
            var playerCopy = SceneObjectDuplicator.CreateDuplicate(sourceSession.RuntimeScene, sourcePlayerId);
            while (destinationScene.Find(playerCopy.Id) is not null)
                playerCopy = SceneObjectDuplicator.CreateDuplicate(sourceSession.RuntimeScene, sourcePlayerId);
            playerCopy.Door = null;
            playerCopy.SpawnPoint = null;
            playerCopy.WorldEntity = null;
            playerCopy.TriggerAction = null;
            playerCopy.TemplateInstance = null;
            playerCopy.Transform = new Transform
            {
                Position = destination.Spawn.Position,
                Rotation = destination.Spawn.Facing,
                Scale = sourcePlayerScale
            };
            destinationScene.AddUnparented(playerCopy);
            destinationScene.PlaySettings = sourceSession.RuntimeScene.PlaySettings
                with { PlayerObjectId = playerCopy.Id };

            ScenePlaySession? candidateSession = null;
            ImportedAudioClip? candidateAudio = null;
            PreviewResources? candidatePreview = null;
            AttachmentBoxRenderer? attachmentCandidate = null;
            PlayPhysicsResources? candidatePhysics = null;
            try
            {
                candidateSession = CreatePlaySession(destinationScene, out candidateAudio);
                candidateSession.RuntimeScene.PlaySettings.ApplyInputBindings(InputMap);
                candidatePreview = PreviewResources.Load(_owner.GraphicsDevice,
                    _owner.ResolveSceneAssets(candidateSession.RuntimeScene, _owner._sceneAssetRoot),
                    candidateSession.RuntimeScene);
                if (candidatePreview.HasSkinnedCharacters)
                {
                    SkinnedEffectCompatibility.Validate(_owner.GraphicsDevice.GraphicsProfile,
                        candidatePreview.MaximumJointCount);
                    if (candidatePreview.AttachmentsByInstanceId.Count > 0
                        && _owner._attachmentRenderer is null)
                        attachmentCandidate = new AttachmentBoxRenderer(_owner.GraphicsDevice);
                }
                candidatePhysics = CreatePlayPhysicsResources(candidateSession, candidatePreview);

                _owner._editorUi.CompletePendingEdit(sourceSession.RuntimeScene);
                var previousPhysics = new PlayPhysicsResources(sourceSession, PhysicsWorld,
                    SceneColliders, PhysicsStepper, CharacterController, CharacterObjectId);
                var cleanupErrors = new List<string>();
                if (attachmentCandidate is not null)
                {
                    _owner._attachmentRenderer = _owner._sceneResources!.Own(attachmentCandidate);
                    attachmentCandidate = null;
                }
                var previousPreviewCleanup = previewOwner.Reload(() => candidatePreview);
                candidatePreview = null;
                foreach (var follower in PathFollowers.Values)
                {
                    try { sourceSession.UnbindPhysicsCharacter(follower.Controller.PhysicsBodyId); }
                    catch (Exception exception) { cleanupErrors.Add(exception.Message); }
                    try { follower.Controller.Dispose(); }
                    catch (Exception exception) { cleanupErrors.Add(exception.Message); }
                }
                PathFollowers.Clear();
                SetSession(candidateSession);
                _playWorldCellId = destination.Cell.Id;
                _owner._editorUi.SetPlayWorldCellId(_playWorldCellId);
                _owner._editorUi.ResetSceneSelection();
                candidateSession = null;
                InteractionClip = candidateAudio;
                candidateAudio = null;
                SetHistory(new SceneCommandHistory());
                _owner._editorUi.SetHistory(History);
                ApplyPhysicsResources(candidatePhysics);
                candidatePhysics = null;
                _pendingWorldTravelRequests.Clear();
                _owner._farLodByObjectId.Clear();
                try { _owner.BuildSequencePreview(); }
                catch (Exception exception) { cleanupErrors.Add($"sequence preview: {exception.Message}"); }
                if (CharacterController is { } playerController)
                {
                    var settings = Session!.RuntimeScene.PlaySettings;
                    try
                    {
                        _owner._camera.Reset(playerController.Pose.Position
                            + new Vector3(0f, settings.CameraTargetOffsetY, 0f),
                            settings.CameraDistance, _owner._camera.Yaw, _owner._camera.Pitch);
                    }
                    catch (Exception exception) { cleanupErrors.Add($"camera reset: {exception.Message}"); }
                }
                try { previousPhysics.Dispose(); }
                catch (Exception exception) { cleanupErrors.Add(exception.Message); }
                try { sourceSession.Dispose(); }
                catch (Exception exception) { cleanupErrors.Add(exception.Message); }
                var status = $"Entered {WorldCellWorkspace.GetCellName(destination.Cell)} at its linked spawn.";
                if (previousPreviewCleanup is not null)
                    cleanupErrors.Add(previousPreviewCleanup.Message);
                if (cleanupErrors.Count > 0)
                    status += $" Previous cell cleanup reported: {string.Join("; ", cleanupErrors)}";
                _owner._reimportStatus = status;
            }
            catch
            {
                candidatePhysics?.Dispose();
                candidatePreview?.Dispose();
                attachmentCandidate?.Dispose();
                candidateSession?.Dispose();
                throw;
            }
        }

        private PlayPhysicsResources CreatePlayPhysicsResources(ScenePlaySession session,
            PreviewResources preview)
        {
            var scene = session.RuntimeScene;
            var world = new PhysicsWorld();
            SceneStaticColliderSet? colliders = null;
            PhysicsCharacterController? controller = null;
            try
            {
                world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f));
                var playerId = FindPlayCharacter(scene, preview);
                var dynamicCharacterIds = scene.Objects
                    .Where(item => item.CharacterSettings is not null
                        || item.WorldEntity?.Kind == WorldEntityKind.Actor)
                    .Select(item => item.Id).ToHashSet();
                if (playerId is { } selectedPlayerId) dynamicCharacterIds.Add(selectedPlayerId);
                colliders = new SceneStaticColliderSet(scene, world, dynamicCharacterIds);
                var stepper = new PhysicsFixedStepper();
                if (playerId is { } objectId)
                {
                    var settings = scene.PlaySettings;
                    var centerOffset = CharacterCenterOffset(settings);
                    var worldPosition = scene.GetWorldMatrix(objectId).Translation;
                    controller = new PhysicsCharacterController(world,
                        worldPosition + new Vector3(0f, centerOffset, 0f), ToPhysicsCharacterSettings(settings));
                    session.BindPhysicsCharacter(controller.PhysicsBodyId, objectId);
                }
                return new PlayPhysicsResources(session, world, colliders, stepper,
                    controller, playerId);
            }
            catch
            {
                if (controller is not null)
                {
                    session.UnbindPhysicsCharacter(controller.PhysicsBodyId);
                    controller.Dispose();
                }
                colliders?.Dispose();
                world.Dispose();
                throw;
            }
        }

        private void ApplyPhysicsResources(PlayPhysicsResources resources)
        {
            PhysicsWorld = resources.World;
            SceneColliders = resources.Colliders;
            PhysicsStepper = resources.Stepper;
            CharacterController = resources.Controller;
            CharacterObjectId = resources.CharacterObjectId;
        }

        private sealed class PlayPhysicsResources : IDisposable
        {
            public PlayPhysicsResources(ScenePlaySession session, PhysicsWorld? world,
                SceneStaticColliderSet? colliders, PhysicsFixedStepper? stepper,
                PhysicsCharacterController? controller, Guid? characterObjectId)
            {
                Session = session;
                World = world;
                Colliders = colliders;
                Stepper = stepper;
                Controller = controller;
                CharacterObjectId = characterObjectId;
            }

            public ScenePlaySession Session { get; }
            public PhysicsWorld? World { get; }
            public SceneStaticColliderSet? Colliders { get; }
            public PhysicsFixedStepper? Stepper { get; }
            public PhysicsCharacterController? Controller { get; }
            public Guid? CharacterObjectId { get; }

            public void Dispose()
            {
                Exception? failure = null;
                if (Controller is { } controller)
                {
                    try { Session.UnbindPhysicsCharacter(controller.PhysicsBodyId); }
                    catch (Exception exception) { failure = exception; }
                    try { controller.Dispose(); }
                    catch (Exception exception)
                    {
                        failure = failure is null ? exception : new AggregateException(failure, exception);
                    }
                }
                try { Colliders?.Dispose(); }
                catch (Exception exception)
                {
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                }
                try { World?.Dispose(); }
                catch (Exception exception)
                {
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                }
                if (failure is not null) throw failure;
            }
        }

        private void InitializePlayPhysics(ScenePlaySession session, PreviewResources? preview = null)
        {
            var scene = session.RuntimeScene;
            var playerObjectId = FindPlayCharacter(scene, preview);
            CreatePathPhysicsWorld(scene, playerObjectId);
            if (playerObjectId is not { } objectId) return;

            var physicsWorld = PhysicsWorld
                ?? throw new InvalidOperationException("Play physics world could not be initialized.");
            var settings = scene.PlaySettings;
            var centerOffset = CharacterCenterOffset(settings);
            var worldPosition = scene.GetWorldMatrix(objectId).Translation;
            var controller = new PhysicsCharacterController(physicsWorld,
                worldPosition + new Vector3(0f, centerOffset, 0f), ToPhysicsCharacterSettings(settings));
            try
            {
                session.BindPhysicsCharacter(controller.PhysicsBodyId, objectId);
                CharacterController = controller;
                CharacterObjectId = objectId;
            }
            catch
            {
                controller.Dispose();
                throw;
            }
        }

        private Guid? FindPlayCharacter(SceneGraph scene, PreviewResources? preview = null)
        {
            bool IsPlayableCharacter(SceneObject item) => item.Enabled && item.TriggerAction is null
                && (item.CharacterSettings is not null
                    || item.WorldEntity?.Kind == WorldEntityKind.Actor
                    || (preview ?? _owner._preview?.Current)?.IsSkinnedObject(item) == true);

            if (scene.PlaySettings.PlayerObjectId is { } assignedId)
            {
                var assigned = scene.Find(assignedId)
                    ?? throw new InvalidOperationException(
                        $"Saved Play player {assignedId} is missing. Choose a character in Play setup.");
                if (!IsPlayableCharacter(assigned))
                    throw new InvalidOperationException(
                        $"Saved Play player '{assigned.Name}' ({assigned.Id}) is disabled or is not an animated character/actor. Choose another character.");
                return assigned.Id;
            }

            return scene.Objects.Where(IsPlayableCharacter).OrderBy(item => item.Id)
                .Select(item => (Guid?)item.Id).FirstOrDefault();
        }

        private void CreatePathPhysicsWorld(SceneGraph scene, Guid? dynamicCharacterId = null)
        {
            if (PhysicsWorld is not null) return;
            PhysicsWorld = new PhysicsWorld();
            try
            {
                PhysicsWorld.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f));
                var dynamicCharacterIds = scene.Objects
                    .Where(item => item.CharacterSettings is not null || item.WorldEntity?.Kind == WorldEntityKind.Actor)
                    .Select(item => item.Id).ToHashSet();
                if (dynamicCharacterId is { } actorId) dynamicCharacterIds.Add(actorId);
                SceneColliders = new SceneStaticColliderSet(scene, PhysicsWorld, dynamicCharacterIds);
                PhysicsStepper = new PhysicsFixedStepper();
            }
            catch
            {
                SceneColliders?.Dispose();
                SceneColliders = null;
                PhysicsWorld.Dispose();
                PhysicsWorld = null;
                PhysicsStepper = null;
                throw;
            }
        }

        public void StopPlaySession()
        {
            if (Session is not { } session || _owner._preview is null) return;
            DisposePhysics();
            _owner._editorUi?.CompletePendingEdit(session.RuntimeScene);
            try
            {
                var cleanupError = _owner._preview.Reload(() => PreviewResources.Load(
                    _owner.GraphicsDevice, _owner.ResolveSceneAssets(_owner._sceneData), _owner._sceneData));
                Exception? sessionCleanupError = null;
                try { session.Dispose(); }
                catch (Exception exception) { sessionCleanupError = exception; }
                SetSession(null);
                _playWorldCellId = null;
                _owner._editorUi?.SetPlayWorldCellId(null);
                _pendingWorldTravelRequests.Clear();
                InteractionClip = null;
                _owner._sceneData.PlaySettings.ApplyInputBindings(InputMap);
                _owner._editorUi?.SetHistory(_owner._editorHistory);
                _owner.BuildSequencePreview();
                var errors = new[] { cleanupError, sessionCleanupError }.Where(error => error is not null)
                    .Select(error => error!.Message).ToArray();
                _owner._reimportStatus = errors.Length == 0
                    ? "Play clone stopped; authored scene restored."
                    : $"Authored scene restored with cleanup issue: {string.Join("; ", errors)}";
                if (_owner.GetSceneBounds() is { } bounds) _owner._camera.Frame(bounds);
            }
            catch (Exception exception)
            {
                _owner._reimportStatus = $"Could not restore the authored preview; play mode remains active: {exception.Message}";
            }
        }

        public void TriggerSelectedInteraction()
        {
            var ownerId = _owner._editorUi?.SelectedObjectId
                ?? _owner.CurrentScene.Objects.FirstOrDefault()?.Id;
            TriggerInteraction(ownerId);
        }

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

        public string StartPathFollow(Guid objectId, CellPathGraph graph, CellPathRoute route)
        {
            if (Session is null) return "Start play mode before following an authored route.";
            var actor = _owner.CurrentScene.Find(objectId);
            if (actor is null) return "The selected actor is not in the play scene.";
            if (CharacterObjectId == objectId)
                return "This is the keyboard-controlled player; choose another actor to follow a route.";

            StopPathFollow(objectId);
            try
            {
                if (PhysicsWorld is null)
                    CreatePathPhysicsWorld(_owner.CurrentScene, objectId);

                var physicsWorld = PhysicsWorld
                    ?? throw new InvalidOperationException("Play physics world could not be initialized.");
                var settings = _owner.CurrentScene.PlaySettings;
                var centerOffset = CharacterCenterOffset(settings);
                var worldPosition = _owner.CurrentScene.GetWorldMatrix(objectId).Translation;
                var controller = new PhysicsCharacterController(physicsWorld,
                    worldPosition + new Vector3(0f, centerOffset, 0f), ToPhysicsCharacterSettings(settings));
                try
                {
                    var follower = new PhysicsCharacterPathFollower(controller, graph, route);
                    Session!.BindPhysicsCharacter(controller.PhysicsBodyId, objectId);
                    PathFollowers.Add(objectId, new PathPreviewAgent(controller, follower));
                }
                catch
                {
                    Session?.UnbindPhysicsCharacter(controller.PhysicsBodyId);
                    controller.Dispose();
                    throw;
                }
                return $"Following route with collision-aware physics ({route.NodeIds.Count} nodes).";
            }
            catch (Exception exception)
            {
                if (PathFollowers.Count == 0 && CharacterController is null) DisposePhysics();
                return $"Could not start route following: {exception.Message}";
            }
        }

        public string? GetPathFollowStatus(Guid objectId) =>
            PathFollowers.TryGetValue(objectId, out var agent) ? agent.Follower.State.ToString() : null;

        public void StopPathFollow(Guid objectId)
        {
            if (PathFollowers.Remove(objectId, out var agent))
            {
                Session?.UnbindPhysicsCharacter(agent.Controller.PhysicsBodyId);
                agent.Controller.Dispose();
            }
            if (PathFollowers.Count == 0 && CharacterController is null) DisposePhysics();
        }

        public void UpdatePathFollowers(float elapsedSeconds)
        {
            if (PhysicsWorld is null || PhysicsStepper is null
                || (PathFollowers.Count == 0 && CharacterController is null)) return;
            var step = PhysicsStepper.Advance(elapsedSeconds, delta =>
            {
                foreach (var agent in PathFollowers.Values) agent.Follower.Advance(delta);
                PhysicsWorld.Step(delta);
                if (Session is { } session && SceneColliders is { } colliders)
                    session.DispatchTriggerEvents(colliders.TriggerEvents);
            });
            foreach (var (objectId, agent) in PathFollowers.ToArray())
            {
                if (_owner.CurrentScene.Find(objectId) is not { } actor)
                {
                    StopPathFollow(objectId);
                    continue;
                }
                var physicsPose = PhysicsWorld.GetInterpolatedPose(agent.Controller.PhysicsBodyId,
                    step.InterpolationAlpha);
                SetObjectWorldPosition(_owner.CurrentScene, actor,
                    physicsPose.Position - new Vector3(0f, CharacterCenterOffset(_owner.CurrentScene.PlaySettings), 0f));
            }
            if (CharacterController is { } playerController
                && CharacterObjectId is { } playerObjectId
                && _owner.CurrentScene.Find(playerObjectId) is { } playerObject)
            {
                var position = PhysicsWorld.GetInterpolatedPose(playerController.PhysicsBodyId,
                    step.InterpolationAlpha).Position;
                SetObjectWorldPosition(_owner.CurrentScene, playerObject,
                    position - new Vector3(0f, CharacterCenterOffset(_owner.CurrentScene.PlaySettings), 0f));
                var cameraSettings = _owner.CurrentScene.PlaySettings;
                _owner._camera.Reset(position + new Vector3(0f, cameraSettings.CameraTargetOffsetY, 0f),
                    _owner._camera.Distance, _owner._camera.Yaw, _owner._camera.Pitch);
            }
        }

        public static Vector3 ToOrbitCameraMovement(Vector2 localMovement, float cameraYaw)
        {
            if (localMovement.LengthSquared() > 1f) localMovement.Normalize();
            var rotation = Matrix.CreateRotationY(cameraYaw);
            var right = Vector3.Transform(Vector3.Right, rotation);
            var forward = Vector3.Transform(Vector3.Forward, rotation);
            var movement = right * localMovement.X + forward * localMovement.Y;
            movement.Y = 0f;
            return movement.LengthSquared() > 1f ? Vector3.Normalize(movement) : movement;
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

        public void SetSession(ScenePlaySession? session) => Session = session;

        public void SetHistory(SceneCommandHistory history) =>
            History = history ?? throw new ArgumentNullException(nameof(history));

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
}

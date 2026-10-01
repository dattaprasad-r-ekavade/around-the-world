using System;
using System.Collections.Generic;
using System.Linq;
using Ember.Physics;
using Ember.World;

namespace Ember.Scene;

/// <summary>Creates an isolated runtime copy of an authored scene and owns its behaviours.</summary>
public sealed class ScenePlaySession : IDisposable
{
    private bool _disposed;
    private readonly Dictionary<PhysicsObjectId, Guid> _characterIds = new();
    private readonly HashSet<Guid> _reachedGoals = new();

    public ScenePlaySession(SceneGraph authoredScene, Action<SceneGraph, SceneBehaviourRuntime>? configure = null,
        SceneBehaviourRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(authoredScene);
        ValidateTriggerActions(authoredScene);
        RuntimeScene = SceneGraphCloner.Clone(authoredScene);
        Behaviours = new SceneBehaviourRuntime(RuntimeScene);
        try
        {
            configure?.Invoke(RuntimeScene, Behaviours);
            RegisterSavedBehaviours(registry);
            Behaviours.Start();
        }
        catch
        {
            Behaviours.Dispose();
            throw;
        }
    }

    public SceneGraph RuntimeScene { get; }
    public SceneBehaviourRuntime Behaviours { get; }
    public bool IsDisposed => _disposed;
    public bool IsPaused => Behaviours.IsPaused;
    public bool HasReachedGoal => _reachedGoals.Count > 0;

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Behaviours.Pause();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Behaviours.Resume();
    }

    /// <summary>Raised when a saved trigger action runs or requests a world action.</summary>
    public event Action<SceneAuthoredActionEvent>? AuthoredActionExecuted;

    private void RegisterSavedBehaviours(SceneBehaviourRegistry? registry)
    {
        foreach (var owner in RuntimeScene.Objects)
        foreach (var behaviourId in owner.BehaviourAssignments)
        {
            if (registry is null)
                throw new InvalidOperationException(
                    $"Scene object '{owner.Name}' ({owner.Id}) assigns behaviour '{behaviourId}', but no behaviour registry was provided.");
            Behaviours.AddRegistered(owner.Id, behaviourId, registry);
        }
    }

    /// <summary>Associates a runtime physics body with its stable scene character identity.</summary>
    public void BindPhysicsCharacter(PhysicsObjectId physicsObjectId, Guid sceneCharacterId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (physicsObjectId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(physicsObjectId), "Physics object ID must be valid.");
        if (sceneCharacterId == Guid.Empty)
            throw new ArgumentException("Scene character ID cannot be empty.", nameof(sceneCharacterId));
        if (RuntimeScene.Find(sceneCharacterId) is null)
            throw new ArgumentException($"Scene character {sceneCharacterId} is not in the runtime scene.", nameof(sceneCharacterId));
        if (_characterIds.TryGetValue(physicsObjectId, out var existingId) && existingId != sceneCharacterId)
            throw new InvalidOperationException($"Physics object {physicsObjectId.Value} is already bound to scene character {existingId}.");
        _characterIds[physicsObjectId] = sceneCharacterId;
    }

    /// <summary>Removes a physics-to-scene identity association when a character despawns.</summary>
    public bool UnbindPhysicsCharacter(PhysicsObjectId physicsObjectId) => _characterIds.Remove(physicsObjectId);

    /// <summary>Routes physics trigger transitions to behaviours on the trigger scene object.</summary>
    /// <returns>The number of behaviour callbacks invoked for enabled trigger owners.</returns>
    public int DispatchTriggerEvents(IEnumerable<SceneTriggerEvent> triggerEvents)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(triggerEvents);
        if (Behaviours.IsPaused) return 0;

        var callbacksInvoked = 0;
        foreach (var triggerEvent in triggerEvents)
        {
            var action = triggerEvent.Transition switch
            {
                PhysicsTriggerTransition.Entered => "TriggerEnter",
                PhysicsTriggerTransition.Exited => "TriggerExit",
                _ => throw new ArgumentOutOfRangeException(nameof(triggerEvents), triggerEvent.Transition,
                    "Trigger transition is not supported.")
            };
            var instigatorId = _characterIds.TryGetValue(triggerEvent.OtherPhysicsObjectId, out var characterId)
                ? characterId
                : (Guid?)null;
            callbacksInvoked += Behaviours.Interact(triggerEvent.TriggerSceneObjectId, action,
                instigatorId, triggerEvent.OtherPhysicsObjectId);

            if (triggerEvent.Transition == PhysicsTriggerTransition.Entered
                && RuntimeScene.Find(triggerEvent.TriggerSceneObjectId) is { Enabled: true, TriggerAction: { } triggerAction } owner)
                ExecuteTriggerAction(owner, triggerAction, instigatorId, triggerEvent.OtherPhysicsObjectId);
        }

        return callbacksInvoked;
    }

    private void ExecuteTriggerAction(SceneObject owner, SceneTriggerActionComponent triggerAction,
        Guid? instigatorId, PhysicsObjectId physicsInstigatorId)
    {
        switch (triggerAction.Kind)
        {
            case SceneTriggerActionKind.Collect:
                owner.Enabled = false;
                AuthoredActionExecuted?.Invoke(new SceneAuthoredActionEvent(triggerAction.Kind,
                    owner.Id, owner.Name, instigatorId, physicsInstigatorId));
                break;
            case SceneTriggerActionKind.ReachGoal:
                if (_reachedGoals.Add(owner.Id))
                    AuthoredActionExecuted?.Invoke(new SceneAuthoredActionEvent(triggerAction.Kind,
                        owner.Id, owner.Name, instigatorId, physicsInstigatorId));
                break;
            case SceneTriggerActionKind.Open:
                var door = owner.Door ?? throw new InvalidOperationException(
                    $"Scene object {owner.Id} ({owner.Name}) lost its linked world door before its Open action ran.");
                AuthoredActionExecuted?.Invoke(new SceneAuthoredActionEvent(triggerAction.Kind,
                    owner.Id, owner.Name, instigatorId, physicsInstigatorId, door));
                break;
            default:
                throw new InvalidOperationException($"Scene object {owner.Id} has an unsupported trigger action.");
        }
    }

    private static void ValidateTriggerActions(SceneGraph scene)
    {
        foreach (var item in scene.Objects.Where(item => item.TriggerAction is not null))
        {
            if (item.BoxCollider is not { IsTrigger: true })
                throw new InvalidOperationException(
                    $"Scene object {item.Id} ({item.Name}) has a trigger action but no trigger box collider.");
            if (item.TriggerAction?.Kind == SceneTriggerActionKind.Open && item.Door is null)
                throw new InvalidOperationException(
                    $"Scene object {item.Id} ({item.Name}) has an Open action but no linked world door.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Behaviours.Dispose();
    }
}

/// <summary>Copies authored scene state while preserving stable IDs and deeply copying mutable data.</summary>
public static class SceneGraphCloner
{
    public static SceneGraph Clone(SceneGraph source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = new SceneGraph { PlaySettings = source.PlaySettings };
        foreach (var item in source.Objects)
        {
            var copy = new SceneObject(item.Id, item.Name)
            {
                Enabled = item.Enabled,
                Transform = new Transform
                {
                    Position = item.Transform.Position,
                    Rotation = item.Transform.Rotation,
                    Scale = item.Transform.Scale
                },
                GltfAsset = item.GltfAsset,
                StaticMeshLod = item.StaticMeshLod,
                CharacterSettings = item.CharacterSettings?.DeepCopy(),
                BoxCollider = item.BoxCollider,
                TriggerAction = item.TriggerAction,
                Door = item.Door,
                SpawnPoint = item.SpawnPoint,
                WorldEntity = item.WorldEntity,
                TemplateInstance = item.TemplateInstance,
                ResetPolicy = item.ResetPolicy
            };
            copy.SetBehaviourAssignments(item.BehaviourAssignments);
            clone.Add(copy);
        }

        foreach (var item in source.Objects)
            if (item.ParentId is { } parentId) clone.SetParent(item.Id, parentId);
        return clone;
    }

}

/// <summary>Details of a saved scene action reported by a play session.</summary>
/// <param name="Door">The linked destination for an Open action; null for other actions.</param>
public readonly record struct SceneAuthoredActionEvent(SceneTriggerActionKind Kind,
    Guid SceneObjectId, string SceneObjectName, Guid? InstigatorId,
    PhysicsObjectId PhysicsInstigatorId, WorldDoorComponent? Door = null);

/// <summary>A single gameplay interaction sent to a scene object's compiled behaviour.</summary>
public readonly record struct SceneInteraction(string Action, Guid? InstigatorId = null,
    PhysicsObjectId? PhysicsInstigatorId = null);

/// <summary>Context supplied when a compiled behaviour starts.</summary>
public sealed class SceneBehaviourContext
{
    internal SceneBehaviourContext(SceneGraph scene, SceneObject owner, SceneResourceScope resources)
    {
        Scene = scene;
        Owner = owner;
        Resources = resources;
    }

    public SceneGraph Scene { get; }
    public SceneObject Owner { get; }
    public SceneResourceScope Resources { get; }
    public T Own<T>(T resource) where T : class, IDisposable => Resources.Own(resource);
}

/// <summary>Base class for game behaviours compiled into a game or engine assembly.</summary>
public abstract class SceneBehaviour
{
    private bool _started;
    private bool _stopped;

    protected virtual void OnStart(SceneBehaviourContext context) { }
    protected virtual void OnInteract(SceneInteraction interaction) { }
    protected virtual void OnStop() { }

    internal void Start(SceneBehaviourContext context)
    {
        if (_started) throw new InvalidOperationException("A behaviour instance can only be started once.");
        _started = true;
        try { OnStart(context); }
        catch (Exception startError)
        {
            try { Stop(); }
            catch (Exception stopError)
            {
                throw new AggregateException("Behaviour startup and rollback both failed.", startError, stopError);
            }

            throw;
        }
    }

    internal void Interact(SceneInteraction interaction)
    {
        if (_started && !_stopped) OnInteract(interaction);
    }

    internal void Stop()
    {
        if (!_started || _stopped) return;
        _stopped = true;
        OnStop();
    }
}

/// <summary>Runs compiled behaviours for one runtime scene and releases their scene-owned resources.</summary>
public sealed class SceneBehaviourRuntime : IDisposable
{
    private readonly SceneGraph _scene;
    private readonly SceneResourceScope _resources = new();
    private readonly List<Binding> _bindings = new();
    private bool _started;
    private bool _paused;
    private bool _disposed;

    public SceneBehaviourRuntime(SceneGraph scene) =>
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));

    public bool IsStarted => _started;
    public bool IsPaused => _paused;
    public bool IsDisposed => _disposed;

    public T Own<T>(T resource) where T : class, IDisposable => _resources.Own(resource);

    /// <summary>Creates and binds a registered behaviour before this scene starts.</summary>
    public void AddRegistered(Guid ownerId, string behaviourId, SceneBehaviourRegistry registry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            throw new InvalidOperationException("Behaviours must be registered before the scene starts.");
        ArgumentNullException.ThrowIfNull(registry);
        var owner = _scene.Find(ownerId)
            ?? throw new ArgumentException($"Behaviour owner {ownerId} is not in this scene.", nameof(ownerId));
        Add(ownerId, registry.Create(behaviourId, owner));
    }

    public void Add(Guid ownerId, SceneBehaviour behaviour)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) throw new InvalidOperationException("Behaviours must be registered before the scene starts.");
        ArgumentNullException.ThrowIfNull(behaviour);
        if (_scene.Find(ownerId) is null) throw new ArgumentException("Behaviour owner is not in this scene.", nameof(ownerId));
        _bindings.Add(new Binding(ownerId, behaviour));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;
        try
        {
            foreach (var binding in _bindings)
            {
                var owner = _scene.Find(binding.OwnerId)
                    ?? throw new InvalidOperationException($"Behaviour owner {binding.OwnerId} was removed before start.");
                binding.Behaviour.Start(new SceneBehaviourContext(_scene, owner, _resources));
                binding.Started = true;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("Start the scene behaviour runtime before pausing it.");
        _paused = true;
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("Start the scene behaviour runtime before resuming it.");
        _paused = false;
    }

    /// <returns>The number of behavior callbacks invoked for the enabled owner.</returns>
    public int Interact(Guid ownerId, string action, Guid? instigatorId = null,
        PhysicsObjectId? physicsInstigatorId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("Start the scene behaviour runtime before sending interactions.");
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An interaction action is required.", nameof(action));
        if (_paused) return 0;
        if (_scene.Find(ownerId) is not { Enabled: true }) return 0;
        var interaction = new SceneInteraction(action.Trim(), instigatorId, physicsInstigatorId);
        var callbacksInvoked = 0;
        foreach (var binding in _bindings)
        {
            if (binding.OwnerId != ownerId || !binding.Started) continue;
            binding.Behaviour.Interact(interaction);
            callbacksInvoked++;
        }
        return callbacksInvoked;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? errors = null;
        for (var index = _bindings.Count - 1; index >= 0; index--)
        {
            if (!_bindings[index].Started) continue;
            try { _bindings[index].Behaviour.Stop(); }
            catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
        }

        try { _resources.Dispose(); }
        catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
        if (errors is not null) throw new AggregateException("Scene behaviour cleanup failed.", errors);
    }

    private sealed class Binding(Guid ownerId, SceneBehaviour behaviour)
    {
        public Guid OwnerId { get; } = ownerId;
        public SceneBehaviour Behaviour { get; } = behaviour;
        public bool Started { get; set; }
    }
}

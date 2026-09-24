using System;
using System.Collections.Generic;

namespace Ember.Scene;

/// <summary>Creates an isolated runtime copy of an authored scene and owns its behaviours.</summary>
public sealed class ScenePlaySession : IDisposable
{
    private bool _disposed;

    public ScenePlaySession(SceneGraph authoredScene, Action<SceneGraph, SceneBehaviourRuntime>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(authoredScene);
        RuntimeScene = SceneGraphCloner.Clone(authoredScene);
        Behaviours = new SceneBehaviourRuntime(RuntimeScene);
        try
        {
            configure?.Invoke(RuntimeScene, Behaviours);
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
        var clone = new SceneGraph();
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
                CharacterSettings = CopyCharacterSettings(item.CharacterSettings),
                Door = item.Door,
                SpawnPoint = item.SpawnPoint,
                WorldEntity = item.WorldEntity,
                ResetPolicy = item.ResetPolicy
            };
            clone.Add(copy);
        }

        foreach (var item in source.Objects)
            if (item.ParentId is { } parentId) clone.SetParent(item.Id, parentId);
        return clone;
    }

    private static GltfCharacterSettings? CopyCharacterSettings(GltfCharacterSettings? source)
    {
        if (source is null) return null;
        var copy = new GltfCharacterSettings
        {
            ClipName = source.ClipName,
            Time = source.Time,
            Speed = source.Speed,
            Loop = source.Loop,
            IsPlaying = source.IsPlaying,
            CrossfadeClipName = source.CrossfadeClipName,
            BlendAmount = source.BlendAmount
        };
        foreach (var attachment in source.Attachments)
            copy.Attachments.Add(new GltfBoneAttachmentReference(attachment.Id, attachment.BoneName, attachment.LocalOffset));
        return copy;
    }
}

/// <summary>A single gameplay interaction sent to a scene object's compiled behaviour.</summary>
public readonly record struct SceneInteraction(string Action, Guid? InstigatorId = null);

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
    private bool _disposed;

    public SceneBehaviourRuntime(SceneGraph scene) =>
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));

    public bool IsStarted => _started;
    public bool IsDisposed => _disposed;

    public T Own<T>(T resource) where T : class, IDisposable => _resources.Own(resource);

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

    /// <returns>The number of behavior callbacks invoked for the enabled owner.</returns>
    public int Interact(Guid ownerId, string action, Guid? instigatorId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) throw new InvalidOperationException("Start the scene behaviour runtime before sending interactions.");
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An interaction action is required.", nameof(action));
        if (_scene.Find(ownerId) is not { Enabled: true }) return 0;
        var interaction = new SceneInteraction(action.Trim(), instigatorId);
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

using Ember.Scene;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Ember.World;

/// <summary>Owns persistent world stores and applies them as cells enter the live runtime.</summary>
public sealed class WorldPersistenceSession
{
    private readonly WorldManifest _world;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly WorldSaveRequestQueue _saveRequests = new();

    public WorldPersistenceSession(WorldManifest world, WorldSaveSnapshot? initialSave = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        initialSave?.ValidateAgainstWorld(world);
        initialSave?.Restore(Identities, Changes, RuntimeObjects);
        RestoredPlayerLocation = initialSave?.PlayerLocation;
    }

    public WorldInstanceIdentityMap Identities { get; } = new();
    public WorldCellChangeStore Changes { get; } = new();
    public WorldRuntimeObjectStore RuntimeObjects { get; } = new();
    public WorldPlayerLocation? RestoredPlayerLocation { get; }
    public int PendingSaveCount => _saveRequests.PendingCount;

    /// <summary>Restores runtime-created objects, then applies per-instance changes before activation.</summary>
    public IReadOnlyDictionary<Guid, WorldInstanceId> PrepareCell(Guid cellId, SceneGraph scene)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(scene);
        if (_world.FindCell(cellId) is null)
            throw new KeyNotFoundException($"World manifest has no cell with ID {cellId}.");

        Identities.CreateCellIdentities(cellId, scene);
        RuntimeObjects.Restore(cellId, scene, Identities);
        var cellIdentities = Identities.CreateCellIdentities(cellId, scene);
        Changes.Apply(cellId, scene, cellIdentities);
        return cellIdentities;
    }

    /// <summary>Records a transform edit and applies it to the currently loaded scene object.</summary>
    public void SetTransform(Guid cellId, SceneObject sceneObject, Transform transform)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(sceneObject);
        ArgumentNullException.ThrowIfNull(transform);
        EnsureCellExists(cellId);
        var identity = Identities.GetOrCreate(cellId, sceneObject);
        if (RuntimeObjects.SetTransform(cellId, sceneObject.Id, identity, transform))
        {
            sceneObject.Transform = Copy(transform);
            return;
        }
        Changes.SetTransform(cellId, identity, transform);
        sceneObject.Transform = Copy(transform);
    }

    /// <summary>Records an enabled-state edit and applies it to the currently loaded scene object.</summary>
    public void SetEnabled(Guid cellId, SceneObject sceneObject, bool enabled)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(sceneObject);
        EnsureCellExists(cellId);
        var identity = Identities.GetOrCreate(cellId, sceneObject);
        if (RuntimeObjects.SetEnabled(cellId, sceneObject.Id, identity, enabled))
        {
            sceneObject.Enabled = enabled;
            return;
        }
        Changes.SetEnabled(cellId, identity, enabled);
        sceneObject.Enabled = enabled;
    }

    /// <summary>Spawns and records a runtime object in the active scene using stable identities.</summary>
    public WorldRuntimeObjectIdentity Spawn(Guid cellId, SceneGraph activeScene, SceneObject definition)
    {
        EnsureOwnerThread();
        EnsureCellExists(cellId);
        return RuntimeObjects.Spawn(cellId, activeScene, definition, Identities);
    }

    public WorldSaveSnapshot Capture(WorldPlayerLocation playerLocation)
    {
        EnsureOwnerThread();
        return WorldSaveSnapshot.Capture(playerLocation, Identities, Changes, RuntimeObjects);
    }

    /// <summary>Queues capture so a save requested during travel observes one stable location.</summary>
    public Guid RequestSave(string path, Func<WorldPlayerLocation> capturePlayerLocation)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(capturePlayerLocation);
        return _saveRequests.Enqueue(path, () => Capture(capturePlayerLocation()));
    }

    public bool ProcessStableBoundary(bool travelInProgress)
    {
        EnsureOwnerThread();
        return _saveRequests.ProcessStableBoundary(travelInProgress);
    }

    public bool TryDequeueSaveResult(out WorldSaveRequestResult result)
    {
        EnsureOwnerThread();
        return _saveRequests.TryDequeueResult(out result);
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"World persistence must be accessed on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }

    private void EnsureCellExists(Guid cellId)
    {
        if (_world.FindCell(cellId) is null)
            throw new KeyNotFoundException($"World manifest has no cell with ID {cellId}.");
    }

    private static Transform Copy(Transform source) => new()
    {
        Position = source.Position,
        Rotation = Quaternion.Normalize(source.Rotation),
        Scale = source.Scale
    };
}

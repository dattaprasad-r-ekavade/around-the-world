using System;
using System.Collections.Generic;
using Ember.Scene;

namespace Ember.World;

/// <summary>Stable identity for one runtime-created object record in its owning cell.</summary>
public readonly record struct WorldRuntimeObjectIdentity(Guid CellId, Guid SceneObjectId, WorldInstanceId InstanceId);

/// <summary>Retains runtime-created scene objects across cell unload and reload.</summary>
public sealed class WorldRuntimeObjectStore
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<Guid, Dictionary<WorldInstanceId, RuntimeObjectRecord>> _cells = new();

    public int Count
    {
        get
        {
            EnsureOwnerThread();
            var count = 0;
            foreach (var records in _cells.Values) count += records.Count;
            return count;
        }
    }

    /// <summary>Clones a definition into the active scene and records it under a fresh stable identity.</summary>
    public WorldRuntimeObjectIdentity Spawn(Guid cellId, SceneGraph activeScene, SceneObject definition,
        WorldInstanceIdentityMap identities)
    {
        ArgumentNullException.ThrowIfNull(activeScene);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(identities);
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));

        SceneObject instance;
        do
        {
            var sceneObjectId = Guid.NewGuid();
            if (activeScene.Find(sceneObjectId) is not null || identities.TryGet(cellId, sceneObjectId, out _)) continue;
            instance = SceneObjectCopy.Copy(definition, sceneObjectId);
            instance.ParentId = null;
            break;
        } while (true);

        var worldInstanceId = identities.GetOrCreate(cellId, instance);
        var record = new RuntimeObjectRecord(instance.Id, worldInstanceId, SceneObjectCopy.Copy(instance));
        if (!_cells.TryGetValue(cellId, out var records))
            _cells.Add(cellId, records = new Dictionary<WorldInstanceId, RuntimeObjectRecord>());
        records.Add(worldInstanceId, record);
        try
        {
            activeScene.Add(instance);
        }
        catch
        {
            records.Remove(worldInstanceId);
            throw;
        }

        return new WorldRuntimeObjectIdentity(cellId, instance.Id, worldInstanceId);
    }

    /// <summary>Recreates recorded objects in a loaded cell. Reapplying to the same scene is idempotent.</summary>
    public int Restore(Guid cellId, SceneGraph scene, WorldInstanceIdentityMap identities)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(identities);
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));
        if (!_cells.TryGetValue(cellId, out var records)) return 0;

        var restored = 0;
        foreach (var record in records.Values)
        {
            if (scene.Find(record.SceneObjectId) is not null)
            {
                if (!identities.TryGet(cellId, record.SceneObjectId, out var existingId)
                    || existingId != record.InstanceId)
                    throw new InvalidOperationException(
                        $"Runtime object scene ID {record.SceneObjectId} conflicts with a different cell object.");
                continue;
            }

            identities.Register(cellId, record.SceneObjectId, record.InstanceId);
            scene.Add(SceneObjectCopy.Copy(record.Object));
            restored++;
        }

        return restored;
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"Runtime objects must be accessed on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }

    private sealed record RuntimeObjectRecord(Guid SceneObjectId, WorldInstanceId InstanceId, SceneObject Object);
}

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

    /// <summary>Atomically changes a runtime object's cell owner while preserving both stable IDs.</summary>
    public WorldRuntimeObjectIdentity Transfer(Guid sourceCellId, Guid destinationCellId,
        WorldInstanceId instanceId, SceneGraph sourceScene, SceneGraph destinationScene,
        WorldInstanceIdentityMap identities, Transform destinationTransform)
    {
        ArgumentNullException.ThrowIfNull(sourceScene);
        ArgumentNullException.ThrowIfNull(destinationScene);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(destinationTransform);
        EnsureOwnerThread();
        if (sourceCellId == Guid.Empty || destinationCellId == Guid.Empty || sourceCellId == destinationCellId)
            throw new ArgumentException("Object transfer requires two different nonempty cell IDs.");
        if (instanceId.Value == Guid.Empty)
            throw new ArgumentException("World instance ID cannot be empty.", nameof(instanceId));
        if (!_cells.TryGetValue(sourceCellId, out var sourceRecords)
            || !sourceRecords.TryGetValue(instanceId, out var sourceRecord))
            throw new KeyNotFoundException($"World instance {instanceId.Value} is not owned by cell {sourceCellId}.");
        if (sourceScene.Find(sourceRecord.SceneObjectId) is not { } sourceObject)
            throw new InvalidOperationException($"Runtime object {sourceRecord.SceneObjectId} is not loaded in source cell {sourceCellId}.");
        if (destinationScene.Find(sourceRecord.SceneObjectId) is not null)
            throw new InvalidOperationException($"Runtime object scene ID {sourceRecord.SceneObjectId} already exists in destination cell.");
        if (!identities.TryGet(sourceCellId, sourceRecord.SceneObjectId, out var currentId) || currentId != instanceId)
            throw new InvalidOperationException("World instance identity map does not match the source object record.");
        if (identities.TryGet(destinationCellId, sourceRecord.SceneObjectId, out _))
            throw new InvalidOperationException("Destination cell already has an identity mapping for this scene object ID.");

        var destinationObject = SceneObjectCopy.Copy(sourceObject);
        destinationObject.ParentId = null;
        destinationObject.Transform = CopyValidatedTransform(destinationTransform);
        var destinationRecord = new RuntimeObjectRecord(
            sourceRecord.SceneObjectId, instanceId, SceneObjectCopy.Copy(destinationObject));
        if (!_cells.TryGetValue(destinationCellId, out var destinationRecords))
            _cells.Add(destinationCellId, destinationRecords = new Dictionary<WorldInstanceId, RuntimeObjectRecord>());

        destinationScene.Add(destinationObject);
        var identityMoved = false;
        var recordMoved = false;
        var sourceRecordRemoved = false;
        try
        {
            identities.Transfer(sourceCellId, destinationCellId, sourceRecord.SceneObjectId, instanceId);
            identityMoved = true;
            destinationRecords.Add(instanceId, destinationRecord);
            recordMoved = true;
            sourceRecordRemoved = sourceRecords.Remove(instanceId);
            if (!sourceRecordRemoved)
                throw new InvalidOperationException("Source world object record disappeared during transfer.");
            if (!sourceScene.Remove(sourceRecord.SceneObjectId))
                throw new InvalidOperationException("Source scene object disappeared during transfer.");
            if (sourceRecords.Count == 0) _cells.Remove(sourceCellId);
        }
        catch (Exception exception)
        {
            List<Exception>? rollbackFailures = null;
            if (sourceRecordRemoved)
            {
                try { sourceRecords.Add(instanceId, sourceRecord); }
                catch (Exception rollbackException) { (rollbackFailures ??= new()).Add(rollbackException); }
            }
            if (recordMoved)
            {
                try { destinationRecords.Remove(instanceId); }
                catch (Exception rollbackException) { (rollbackFailures ??= new()).Add(rollbackException); }
            }
            if (identityMoved)
            {
                try { identities.Transfer(destinationCellId, sourceCellId, sourceRecord.SceneObjectId, instanceId); }
                catch (Exception rollbackException) { (rollbackFailures ??= new()).Add(rollbackException); }
            }
            try { destinationScene.Remove(destinationObject.Id); }
            catch (Exception rollbackException) { (rollbackFailures ??= new()).Add(rollbackException); }

            if (rollbackFailures is { Count: > 0 })
            {
                rollbackFailures.Insert(0, exception);
                throw new AggregateException("World object transfer failed and rollback was incomplete.", rollbackFailures);
            }
            throw;
        }

        return new WorldRuntimeObjectIdentity(destinationCellId, sourceRecord.SceneObjectId, instanceId);
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

    private static Transform CopyValidatedTransform(Transform source)
    {
        var rotation = source.Rotation;
        var rotationLengthSquared = rotation.LengthSquared();
        if (!IsFinite(source.Position) || !IsFinite(rotation) || !float.IsFinite(rotationLengthSquared)
            || rotationLengthSquared < 1e-8f || !IsFinite(source.Scale))
            throw new ArgumentException("Runtime object transfer transform must be finite and have a nonzero rotation.", nameof(source));

        return new Transform
        {
            Position = source.Position,
            Rotation = Microsoft.Xna.Framework.Quaternion.Normalize(rotation),
            Scale = source.Scale
        };
    }

    private static bool IsFinite(Microsoft.Xna.Framework.Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Microsoft.Xna.Framework.Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private sealed record RuntimeObjectRecord(Guid SceneObjectId, WorldInstanceId InstanceId, SceneObject Object);
}

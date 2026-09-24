using System;
using System.Collections.Generic;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.World;

/// <summary>Retains per-instance transform and enabled overrides while a world cell is unloaded.</summary>
public sealed class WorldCellChangeStore
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<Guid, Dictionary<WorldInstanceId, InstanceChange>> _cells = new();

    public int CellCount
    {
        get
        {
            EnsureOwnerThread();
            return _cells.Count;
        }
    }

    public void SetTransform(Guid cellId, WorldInstanceId instanceId, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        EnsureValidKey(cellId, instanceId);
        var snapshot = TransformSnapshot.Create(transform);
        var change = GetOrCreate(cellId, instanceId);
        EnsureNotDeleted(cellId, instanceId, change);
        change.Transform = snapshot;
    }

    public void SetEnabled(Guid cellId, WorldInstanceId instanceId, bool enabled)
    {
        EnsureValidKey(cellId, instanceId);
        var change = GetOrCreate(cellId, instanceId);
        EnsureNotDeleted(cellId, instanceId, change);
        change.Enabled = enabled;
    }

    /// <summary>Records a persistent removal that takes precedence over transform and enabled overrides.</summary>
    public void MarkDeleted(Guid cellId, WorldInstanceId instanceId)
    {
        EnsureValidKey(cellId, instanceId);
        GetOrCreate(cellId, instanceId).Deleted = true;
    }

    public bool IsDeleted(Guid cellId, WorldInstanceId instanceId)
    {
        EnsureValidKey(cellId, instanceId);
        return _cells.TryGetValue(cellId, out var changes)
            && changes.TryGetValue(instanceId, out var change)
            && change.Deleted;
    }

    /// <summary>Applies stored overrides to a newly loaded scene using its current identity snapshot.</summary>
    public int Apply(Guid cellId, SceneGraph scene,
        IReadOnlyDictionary<Guid, WorldInstanceId> identities)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(identities);
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));
        if (!_cells.TryGetValue(cellId, out var changes)) return 0;

        var applied = 0;
        List<Guid>? deletedObjects = null;
        foreach (var sceneObject in scene.Objects)
        {
            if (!identities.TryGetValue(sceneObject.Id, out var instanceId)) continue;
            if (!changes.TryGetValue(instanceId, out var change)) continue;

            if (change.Deleted)
            {
                (deletedObjects ??= new List<Guid>()).Add(sceneObject.Id);
                applied++;
                continue;
            }

            if (change.Transform is { } transform)
                sceneObject.Transform = transform.ToTransform();
            if (change.Enabled is { } enabled)
                sceneObject.Enabled = enabled;
            applied++;
        }

        if (deletedObjects is not null)
            foreach (var objectId in deletedObjects)
                scene.Remove(objectId);

        return applied;
    }

    private InstanceChange GetOrCreate(Guid cellId, WorldInstanceId instanceId)
    {
        EnsureOwnerThread();
        if (!_cells.TryGetValue(cellId, out var changes))
            _cells.Add(cellId, changes = new Dictionary<WorldInstanceId, InstanceChange>());
        if (!changes.TryGetValue(instanceId, out var change))
            changes.Add(instanceId, change = new InstanceChange());
        return change;
    }

    private void EnsureValidKey(Guid cellId, WorldInstanceId instanceId)
    {
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));
        if (instanceId.Value == Guid.Empty)
            throw new ArgumentException("World instance ID cannot be empty.", nameof(instanceId));
    }

    private static void EnsureNotDeleted(Guid cellId, WorldInstanceId instanceId, InstanceChange change)
    {
        if (change.Deleted)
            throw new InvalidOperationException(
                $"World instance {instanceId.Value} in cell {cellId} has a deletion tombstone.");
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"World cell changes must be accessed on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }

    private sealed class InstanceChange
    {
        public TransformSnapshot? Transform { get; set; }
        public bool? Enabled { get; set; }
        public bool Deleted { get; set; }
    }

    private readonly record struct TransformSnapshot(Vector3 Position, Quaternion Rotation, Vector3 Scale)
    {
        public static TransformSnapshot Create(Transform transform)
        {
            var rotation = transform.Rotation;
            var rotationLengthSquared = rotation.LengthSquared();
            if (!IsFinite(transform.Position) || !IsFinite(rotation) || !float.IsFinite(rotationLengthSquared)
                || rotationLengthSquared < 1e-8f
                || !IsFinite(transform.Scale))
                throw new ArgumentException("World instance transform must contain finite values and a nonzero rotation.", nameof(transform));
            return new TransformSnapshot(transform.Position, Quaternion.Normalize(rotation), transform.Scale);
        }

        public Transform ToTransform() => new()
        {
            Position = Position,
            Rotation = Rotation,
            Scale = Scale
        };

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        private static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);
    }
}

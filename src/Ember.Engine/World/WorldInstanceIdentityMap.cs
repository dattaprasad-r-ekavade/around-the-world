using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using Ember.Scene;

namespace Ember.World;

public readonly record struct WorldInstanceIdentityEntry(Guid CellId, Guid SceneObjectId, WorldInstanceId InstanceId);

/// <summary>A runtime identity for one placed object; distinct in meaning from cell and asset IDs.</summary>
public readonly record struct WorldInstanceId
{
    public WorldInstanceId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("World instance ID cannot be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }
}

/// <summary>
/// Retains stable runtime identities for authored objects while cells unload and reload.
/// Keep this map for the lifetime of the loaded world; later save data can use these IDs as keys.
/// </summary>
public sealed class WorldInstanceIdentityMap
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<InstanceSource, WorldInstanceId> _bySource = new();
    private readonly HashSet<Guid> _instanceValues = new();

    public int Count
    {
        get
        {
            EnsureOwnerThread();
            return _bySource.Count;
        }
    }

    public bool TryGet(Guid cellId, Guid sceneObjectId, out WorldInstanceId instanceId)
    {
        EnsureOwnerThread();
        return _bySource.TryGetValue(new InstanceSource(cellId, sceneObjectId), out instanceId);
    }

    /// <summary>Restores a saved identity mapping for a runtime-created object.</summary>
    public void Register(Guid cellId, Guid sceneObjectId, WorldInstanceId instanceId)
    {
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));
        if (sceneObjectId == Guid.Empty)
            throw new ArgumentException("Scene object ID cannot be empty.", nameof(sceneObjectId));
        if (instanceId.Value == Guid.Empty)
            throw new ArgumentException("World instance ID cannot be empty.", nameof(instanceId));

        var source = new InstanceSource(cellId, sceneObjectId);
        if (_bySource.TryGetValue(source, out var existing))
        {
            if (existing != instanceId)
                throw new InvalidOperationException($"Scene object {sceneObjectId} in cell {cellId} already has another world instance ID.");
            return;
        }
        if (!_instanceValues.Add(instanceId.Value))
            throw new InvalidOperationException($"World instance ID {instanceId.Value} is already assigned to another object.");
        _bySource.Add(source, instanceId);
    }

    /// <summary>Moves one stable scene-object identity to its new owning cell.</summary>
    public void Transfer(Guid sourceCellId, Guid destinationCellId, Guid sceneObjectId, WorldInstanceId instanceId)
    {
        EnsureOwnerThread();
        if (sourceCellId == Guid.Empty || destinationCellId == Guid.Empty || sourceCellId == destinationCellId)
            throw new ArgumentException("Identity transfer requires two different nonempty cell IDs.");
        if (sceneObjectId == Guid.Empty || instanceId.Value == Guid.Empty)
            throw new ArgumentException("Identity transfer requires nonempty object and instance IDs.");

        var source = new InstanceSource(sourceCellId, sceneObjectId);
        var destination = new InstanceSource(destinationCellId, sceneObjectId);
        if (!_bySource.TryGetValue(source, out var current) || current != instanceId)
            throw new InvalidOperationException($"World instance {instanceId.Value} is not owned by source cell {sourceCellId}.");
        if (_bySource.ContainsKey(destination))
            throw new InvalidOperationException($"Scene object ID {sceneObjectId} is already assigned in destination cell {destinationCellId}.");

        _bySource.Remove(source);
        try { _bySource.Add(destination, instanceId); }
        catch
        {
            _bySource.Add(source, instanceId);
            throw;
        }
    }

    public IReadOnlyList<WorldInstanceIdentityEntry> ExportSnapshot()
    {
        EnsureOwnerThread();
        return _bySource
            .Select(pair => new WorldInstanceIdentityEntry(pair.Key.CellId, pair.Key.SceneObjectId, pair.Value))
            .OrderBy(entry => entry.CellId)
            .ThenBy(entry => entry.SceneObjectId)
            .ToArray();
    }

    public void ImportSnapshot(IEnumerable<WorldInstanceIdentityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        EnsureOwnerThread();
        if (_bySource.Count != 0)
            throw new InvalidOperationException("World instance identities can only be restored into an empty map.");
        foreach (var entry in entries)
            Register(entry.CellId, entry.SceneObjectId, entry.InstanceId);
    }

    /// <summary>Returns the same identity for the same cell and authored scene object across reloads.</summary>
    public WorldInstanceId GetOrCreate(Guid cellId, SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));

        var source = new InstanceSource(cellId, sceneObject.Id);
        if (_bySource.TryGetValue(source, out var existing)) return existing;

        Guid value;
        do { value = Guid.NewGuid(); }
        while (value == cellId || value == sceneObject.Id
            || value == sceneObject.GltfAsset?.AssetId || !_instanceValues.Add(value));

        var instanceId = new WorldInstanceId(value);
        _bySource.Add(source, instanceId);
        return instanceId;
    }

    /// <summary>Resolves an identity for every scene object and returns a snapshot keyed by authored object ID.</summary>
    public IReadOnlyDictionary<Guid, WorldInstanceId> CreateCellIdentities(Guid cellId, SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        EnsureOwnerThread();
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));

        var identities = new Dictionary<Guid, WorldInstanceId>();
        foreach (var sceneObject in scene.Objects)
            identities.Add(sceneObject.Id, GetOrCreate(cellId, sceneObject));
        return new ReadOnlyDictionary<Guid, WorldInstanceId>(identities);
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"World instance identities must be accessed on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }

    private readonly record struct InstanceSource(Guid CellId, Guid SceneObjectId);
}

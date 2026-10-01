using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>
/// A small stable-ID scene registry. It owns hierarchy relationships but leaves rendering and
/// gameplay behavior to systems that consume the scene.
/// </summary>
public sealed class SceneGraph
{
    private readonly Dictionary<Guid, SceneObject> _objects = new();
    private IReadOnlyList<SceneAudioAssetReference> _audioAssets = Array.Empty<SceneAudioAssetReference>();
    private ScenePlaySettings _playSettings = new();

    public IReadOnlyCollection<SceneObject> Objects => _objects.Values;

    /// <summary>Stable project asset references used by authored scene audio.</summary>
    public IReadOnlyList<SceneAudioAssetReference> AudioAssets => _audioAssets;

    /// <summary>Replaces the scene audio asset catalog after checking stable IDs and paths.</summary>
    public void SetAudioAssets(IEnumerable<SceneAudioAssetReference> audioAssets)
    {
        ArgumentNullException.ThrowIfNull(audioAssets);
        var values = audioAssets.ToArray();
        if (values.Any(value => value is null))
            throw new ArgumentException("Scene audio assets cannot contain null references.", nameof(audioAssets));
        if (values.Select(value => value.AssetId).Distinct().Count() != values.Length)
            throw new ArgumentException("Scene audio asset IDs must be unique.", nameof(audioAssets));
        if (values.Select(value => value.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length)
            throw new ArgumentException("Scene audio asset paths must be unique.", nameof(audioAssets));
        _audioAssets = Array.AsReadOnly(values);
    }

    /// <summary>Saved gameplay defaults used by the scene's Play session.</summary>
    public ScenePlaySettings PlaySettings
    {
        get => _playSettings;
        set => _playSettings = (value ?? throw new ArgumentNullException(nameof(value))).ValidatedCopy();
    }

    public void Add(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        if (!_objects.TryAdd(sceneObject.Id, sceneObject))
            throw new ArgumentException($"Scene object ID already exists: {sceneObject.Id}", nameof(sceneObject));

        if (sceneObject.ParentId is not null)
        {
            var parent = sceneObject.ParentId.Value;
            sceneObject.ParentId = null;
            SetParent(sceneObject.Id, parent);
        }
    }

    /// <summary>Adds an object without applying its saved parent link, for staged hierarchy restoration.</summary>
    public void AddUnparented(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        if (_objects.ContainsKey(sceneObject.Id))
            throw new ArgumentException($"Scene object ID already exists: {sceneObject.Id}", nameof(sceneObject));
        var originalParentId = sceneObject.ParentId;
        sceneObject.ParentId = null;
        try
        {
            Add(sceneObject);
        }
        catch
        {
            if (!_objects.ContainsKey(sceneObject.Id)) sceneObject.ParentId = originalParentId;
            throw;
        }
    }

    public SceneObject? Find(Guid id) => _objects.TryGetValue(id, out var value) ? value : null;

    public bool Remove(Guid id)
    {
        if (!_objects.Remove(id, out _)) return false;
        if (_playSettings.PlayerObjectId == id)
            _playSettings = _playSettings with { PlayerObjectId = null };

        foreach (var child in _objects.Values)
            if (child.ParentId == id) child.ParentId = null;

        return true;
    }

    public void SetParent(Guid childId, Guid? parentId)
    {
        var child = Require(childId);
        if (parentId is null)
        {
            child.ParentId = null;
            return;
        }

        Require(parentId.Value);
        if (childId == parentId.Value)
            throw new InvalidOperationException("A scene object cannot parent itself.");

        var cursor = parentId;
        while (cursor is not null)
        {
            if (cursor == childId)
                throw new InvalidOperationException("Parenting would create a hierarchy cycle.");

            cursor = Require(cursor.Value).ParentId;
        }

        child.ParentId = parentId;
    }

    public Matrix GetWorldMatrix(Guid id)
    {
        var visited = new HashSet<Guid>();
        return GetWorldMatrix(Require(id), visited);
    }

    private Matrix GetWorldMatrix(SceneObject sceneObject, HashSet<Guid> visited)
    {
        if (!visited.Add(sceneObject.Id))
            throw new InvalidOperationException("Scene hierarchy contains a cycle.");

        var local = sceneObject.Transform.LocalMatrix;
        if (sceneObject.ParentId is null) return local;

        var parent = Require(sceneObject.ParentId.Value);
        return local * GetWorldMatrix(parent, visited);
    }

    private SceneObject Require(Guid id) =>
        Find(id) ?? throw new KeyNotFoundException($"Scene object was not found: {id}");
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Authoring;

/// <summary>Undoable placement of one expanded scene-template hierarchy.</summary>
public sealed class PlaceSceneTemplateCommand : ISceneCommand
{
    private readonly SceneTemplateSnapshot _template;
    private readonly Vector3 _position;
    private readonly Guid? _targetWorldCellId;
    private SceneObject[]? _placedObjects;

    public PlaceSceneTemplateCommand(SceneTemplateSnapshot template, Vector3 position,
        Guid? targetWorldCellId = null)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _position = position;
        _targetWorldCellId = targetWorldCellId;
    }

    public Guid? InstanceObjectId => _placedObjects?.FirstOrDefault(item => item.TemplateInstance is not null)?.Id;

    public void Apply(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_placedObjects is null)
        {
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, _template, _position, _targetWorldCellId);
            var ids = wrapper.TemplateInstance!.ObjectMappings
                .Select(mapping => mapping.InstanceObjectId).Append(wrapper.Id).ToHashSet();
            _placedObjects = scene.Objects.Where(item => ids.Contains(item.Id)).ToArray();
            if (_placedObjects.Length != ids.Count)
                throw new InvalidOperationException("Template placement did not create the complete mapped hierarchy.");
            return;
        }

        Restore(scene);
    }

    public void Revert(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_placedObjects is null)
            throw new InvalidOperationException("Cannot undo a template placement that was not applied.");

        if (_placedObjects.Any(item => scene.Find(item.Id) is null))
            throw new InvalidOperationException("Cannot undo template placement because part of its hierarchy is missing.");

        foreach (var item in OrderByDepth(_placedObjects, descending: true))
            if (!scene.Remove(item.Id))
                throw new InvalidOperationException($"Could not remove template instance object {item.Id}.");
    }

    private void Restore(SceneGraph scene)
    {
        var placedObjects = _placedObjects
            ?? throw new InvalidOperationException("Cannot redo template placement before it has been applied.");
        var placedIds = placedObjects.Select(item => item.Id).ToHashSet();
        if (placedObjects.Any(item => scene.Find(item.Id) is not null))
            throw new InvalidOperationException("Cannot redo template placement because an instance ID is already in use.");
        if (placedObjects.Any(item => item.ParentId is { } parentId
            && !placedIds.Contains(parentId) && scene.Find(parentId) is null))
            throw new InvalidOperationException("Cannot redo template placement because a required parent is missing.");

        var added = new List<Guid>(placedObjects.Length);
        try
        {
            foreach (var item in OrderByDepth(placedObjects, descending: false))
            {
                scene.Add(item);
                added.Add(item.Id);
            }
        }
        catch
        {
            for (var index = added.Count - 1; index >= 0; index--)
                scene.Remove(added[index]);
            throw;
        }
    }

    private static IEnumerable<SceneObject> OrderByDepth(IEnumerable<SceneObject> objects, bool descending)
    {
        var snapshot = objects.ToArray();
        var byId = snapshot.ToDictionary(item => item.Id);
        int Depth(SceneObject item)
        {
            var depth = 0;
            var parentId = item.ParentId;
            var visited = new HashSet<Guid>();
            while (parentId is { } id && byId.TryGetValue(id, out var parent))
            {
                if (!visited.Add(id)) throw new InvalidOperationException("Template instance hierarchy contains a cycle.");
                depth++;
                parentId = parent.ParentId;
            }
            return depth;
        }

        return descending
            ? snapshot.OrderByDescending(Depth).ToArray()
            : snapshot.OrderBy(Depth).ToArray();
    }
}

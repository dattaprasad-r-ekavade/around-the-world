using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>A reversible edit to scene data.</summary>
public interface ISceneCommand
{
    void Apply(SceneGraph scene);
    void Revert(SceneGraph scene);
}

/// <summary>Bounded undo/redo history for authoring commands.</summary>
public sealed class SceneCommandHistory
{
    private const int MaximumCommands = 128;
    private readonly Stack<ISceneCommand> _undo = new();
    private readonly Stack<ISceneCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(SceneGraph scene, ISceneCommand command)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(command);
        command.Apply(scene);
        _undo.Push(command);
        _redo.Clear();
        if (_undo.Count > MaximumCommands)
        {
            var newestFirst = _undo.ToArray();
            _undo.Clear();
            foreach (var item in newestFirst.Take(MaximumCommands).Reverse()) _undo.Push(item);
        }
    }

    public bool Undo(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_undo.TryPeek(out var command)) return false;
        command.Revert(scene);
        _undo.Pop();
        _redo.Push(command);
        return true;
    }

    public bool Redo(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_redo.TryPeek(out var command)) return false;
        command.Apply(scene);
        _redo.Pop();
        _undo.Push(command);
        return true;
    }
}

/// <summary>One completed change to a scene object's local transform.</summary>
public sealed class TransformEditCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private readonly Transform _before;
    private readonly Transform _after;

    public TransformEditCommand(Guid objectId, Transform before, Transform after)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        _objectId = objectId;
        _before = SceneObjectCopy.CopyTransform(before);
        _after = SceneObjectCopy.CopyTransform(after);
    }

    public void Apply(SceneGraph scene) => Require(scene, _objectId).Transform = SceneObjectCopy.CopyTransform(_after);
    public void Revert(SceneGraph scene) => Require(scene, _objectId).Transform = SceneObjectCopy.CopyTransform(_before);

    private static SceneObject Require(SceneGraph scene, Guid id) =>
        scene.Find(id) ?? throw new InvalidOperationException($"Cannot edit missing scene object {id}.");
}

/// <summary>Adds an object, preserving its ID and authored references across redo.</summary>
public sealed class CreateSceneObjectCommand : ISceneCommand
{
    private readonly SceneObject _snapshot;

    public CreateSceneObjectCommand(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);
        _snapshot = SceneObjectCopy.Copy(sceneObject);
    }

    public Guid ObjectId => _snapshot.Id;

    public void Apply(SceneGraph scene) => scene.Add(SceneObjectCopy.Copy(_snapshot));

    public void Revert(SceneGraph scene)
    {
        if (!scene.Remove(_snapshot.Id))
            throw new InvalidOperationException($"Cannot undo creation; scene object {_snapshot.Id} is missing.");
    }
}

/// <summary>Duplicates one object as a new instance, with fresh object and attachment IDs.</summary>
public static class SceneObjectDuplicator
{
    public static SceneObject CreateDuplicate(SceneGraph scene, Guid sourceId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var source = scene.Find(sourceId)
            ?? throw new InvalidOperationException($"Cannot duplicate missing scene object {sourceId}.");
        var duplicateId = Guid.NewGuid();
        while (scene.Find(duplicateId) is not null) duplicateId = Guid.NewGuid();

        var copy = SceneObjectCopy.Copy(source, duplicateId,
            SceneObjectCopy.UniqueName(source.Name, scene.Objects.Select(item => item.Name)));
        if (copy.CharacterSettings is { } settings)
        {
            var usedAttachmentIds = scene.Objects
                .Where(item => item.CharacterSettings is not null)
                .SelectMany(item => item.CharacterSettings!.Attachments)
                .Select(item => item.Id)
                .ToHashSet();
            for (var index = 0; index < settings.Attachments.Count; index++)
            {
                var attachment = settings.Attachments[index];
                var attachmentId = Guid.NewGuid();
                while (usedAttachmentIds.Contains(attachmentId)) attachmentId = Guid.NewGuid();
                usedAttachmentIds.Add(attachmentId);
                settings.Attachments[index] = new GltfBoneAttachmentReference(
                    attachmentId, attachment.BoneName, attachment.LocalOffset);
            }
        }

        return copy;
    }
}

/// <summary>Creates a new scene object that instances an already imported GLB asset.</summary>
public static class SceneObjectFactory
{
    public static SceneObject CreateAssetInstance(SceneGraph scene, GltfAssetReference asset, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(asset);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("Asset instance position must be finite.", nameof(position));

        var id = Guid.NewGuid();
        while (scene.Find(id) is not null) id = Guid.NewGuid();
        var basis = Path.GetFileNameWithoutExtension(asset.SourcePath);
        var name = SceneObjectCopy.UniqueName(basis, scene.Objects.Select(item => item.Name));
        return new SceneObject(id, name)
        {
            GltfAsset = asset,
            Transform = new Transform { Position = position }
        };
    }
}

/// <summary>Removes an object; undo restores its ID, references, parent, and direct-child links.</summary>
public sealed class DeleteSceneObjectCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private SceneObject? _snapshot;
    private Guid[]? _childIds;

    public DeleteSceneObjectCommand(Guid objectId)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        _objectId = objectId;
    }

    public void Apply(SceneGraph scene)
    {
        var item = scene.Find(_objectId)
            ?? throw new InvalidOperationException($"Cannot delete missing scene object {_objectId}.");
        _snapshot ??= SceneObjectCopy.Copy(item);
        _childIds ??= scene.Objects.Where(child => child.ParentId == _objectId).Select(child => child.Id).ToArray();
        if (!scene.Remove(_objectId))
            throw new InvalidOperationException($"Could not delete scene object {_objectId}.");
    }

    public void Revert(SceneGraph scene)
    {
        if (_snapshot is null || _childIds is null)
            throw new InvalidOperationException("Cannot undo a delete command that was not applied.");
        scene.Add(SceneObjectCopy.Copy(_snapshot));
        foreach (var childId in _childIds)
            if (scene.Find(childId) is not null) scene.SetParent(childId, _objectId);
    }
}

internal static class SceneObjectCopy
{
    public static SceneObject Copy(SceneObject source, Guid? id = null, string? name = null)
    {
        var copy = new SceneObject(id ?? source.Id, name ?? source.Name)
        {
            Enabled = source.Enabled,
            ParentId = source.ParentId,
            Transform = CopyTransform(source.Transform),
            GltfAsset = source.GltfAsset,
            CharacterSettings = CopySettings(source.CharacterSettings),
            Door = source.Door,
            SpawnPoint = source.SpawnPoint is null
                ? null
                : id is null ? source.SpawnPoint : new WorldSpawnComponent(Guid.NewGuid())
        };
        return copy;
    }

    public static Transform CopyTransform(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    private static GltfCharacterSettings? CopySettings(GltfCharacterSettings? source)
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
            copy.Attachments.Add(new GltfBoneAttachmentReference(
                attachment.Id, attachment.BoneName, attachment.LocalOffset));
        return copy;
    }

    public static string UniqueName(string sourceName, IEnumerable<string> names)
    {
        var occupied = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{sourceName} Copy";
        for (var suffix = 2; occupied.Contains(candidate); suffix++)
            candidate = $"{sourceName} Copy {suffix}";
        return candidate;
    }
}

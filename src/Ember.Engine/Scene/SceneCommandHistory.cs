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
    private sealed record HistoryEntry(ISceneCommand Command, object BeforeState, object AfterState);

    private const int MaximumCommands = 128;
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private object _currentState = new();
    private object _savedState;

    public SceneCommandHistory() => _savedState = _currentState;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => !ReferenceEquals(_currentState, _savedState);

    /// <summary>Marks the current authored state as successfully written to its scene file.</summary>
    public void MarkSaved() => _savedState = _currentState;

    public void Execute(SceneGraph scene, ISceneCommand command)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(command);
        var beforeState = _currentState;
        command.Apply(scene);
        var afterState = new object();
        _undo.Push(new HistoryEntry(command, beforeState, afterState));
        _currentState = afterState;
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
        if (!_undo.TryPeek(out var entry)) return false;
        entry.Command.Revert(scene);
        _undo.Pop();
        _redo.Push(entry);
        _currentState = entry.BeforeState;
        return true;
    }

    public bool Redo(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_redo.TryPeek(out var entry)) return false;
        entry.Command.Apply(scene);
        _redo.Pop();
        _undo.Push(entry);
        _currentState = entry.AfterState;
        return true;
    }
}

/// <summary>Changes the saved movement, camera, and input defaults for a scene.</summary>
public sealed class EditScenePlaySettingsCommand : ISceneCommand
{
    private readonly ScenePlaySettings _before;
    private readonly ScenePlaySettings _after;

    public EditScenePlaySettingsCommand(ScenePlaySettings before, ScenePlaySettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        _before = before.ValidatedCopy();
        _after = after.ValidatedCopy();
    }

    public void Apply(SceneGraph scene) => scene.PlaySettings = _after;
    public void Revert(SceneGraph scene) => scene.PlaySettings = _before;
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

/// <summary>Changes persisted playback settings for one animated scene object.</summary>
public sealed class EditCharacterSettingsCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private readonly GltfCharacterSettings? _before;
    private readonly GltfCharacterSettings? _after;

    public EditCharacterSettingsCommand(Guid objectId, GltfCharacterSettings? before,
        GltfCharacterSettings? after)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        _objectId = objectId;
        _before = SceneObjectCopy.CopyCharacterSettings(before);
        _after = SceneObjectCopy.CopyCharacterSettings(after);
    }

    public void Apply(SceneGraph scene) =>
        Require(scene, _objectId).CharacterSettings = SceneObjectCopy.CopyCharacterSettings(_after);

    public void Revert(SceneGraph scene) =>
        Require(scene, _objectId).CharacterSettings = SceneObjectCopy.CopyCharacterSettings(_before);

    private static SceneObject Require(SceneGraph scene, Guid id) =>
        scene.Find(id) ?? throw new InvalidOperationException($"Cannot edit missing scene object {id}.");
}

/// <summary>Changes or removes the persisted box collider on one scene object.</summary>
public sealed class EditBoxColliderCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private readonly SceneBoxColliderComponent? _before;
    private readonly SceneBoxColliderComponent? _after;

    public EditBoxColliderCommand(Guid objectId, SceneBoxColliderComponent? before,
        SceneBoxColliderComponent? after)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        _objectId = objectId;
        _before = before;
        _after = after;
    }

    public void Apply(SceneGraph scene) => Require(scene, _objectId).BoxCollider = _after;

    public void Revert(SceneGraph scene) => Require(scene, _objectId).BoxCollider = _before;

    private static SceneObject Require(SceneGraph scene, Guid id) =>
        scene.Find(id) ?? throw new InvalidOperationException($"Cannot edit missing scene object {id}.");
}

/// <summary>Changes or removes the saved trigger action on one scene object.</summary>
public sealed class EditSceneTriggerActionCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private readonly SceneTriggerActionComponent? _before;
    private readonly SceneTriggerActionComponent? _after;

    public EditSceneTriggerActionCommand(Guid objectId, SceneTriggerActionComponent? before,
        SceneTriggerActionComponent? after)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        _objectId = objectId;
        _before = before;
        _after = after;
    }

    public void Apply(SceneGraph scene) => Require(scene, _objectId).TriggerAction = _after;

    public void Revert(SceneGraph scene) => Require(scene, _objectId).TriggerAction = _before;

    private static SceneObject Require(SceneGraph scene, Guid id) =>
        scene.Find(id) ?? throw new InvalidOperationException($"Cannot edit missing scene object {id}.");
}

/// <summary>Changes an object's parent while preserving its world transform when it is representable as TRS.</summary>
public sealed class ReparentSceneObjectCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private readonly Guid? _newParentId;
    private Guid? _previousParentId;
    private Transform? _previousTransform;
    private Transform? _replacementTransform;
    private bool _captured;

    public ReparentSceneObjectCommand(Guid objectId, Guid? newParentId)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        if (newParentId == Guid.Empty) throw new ArgumentException("Parent ID cannot be empty.", nameof(newParentId));
        _objectId = objectId;
        _newParentId = newParentId;
    }

    public void Apply(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var item = scene.Find(_objectId)
            ?? throw new InvalidOperationException($"Cannot reparent missing scene object {_objectId}.");

        if (!_captured)
        {
            ValidateParentChange(scene);
            var world = scene.GetWorldMatrix(_objectId);
            var parentWorld = _newParentId is { } parentId
                ? scene.GetWorldMatrix(parentId)
                : Matrix.Identity;
            var determinant = parentWorld.Determinant();
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-8f)
                throw new InvalidOperationException("Cannot preserve world placement under a parent with a singular transform.");

            var replacementMatrix = world * Matrix.Invert(parentWorld);
            if (!IsFinite(replacementMatrix)
                || !replacementMatrix.Decompose(out var scale, out var rotation, out var position)
                || !IsFinite(scale) || !IsFinite(rotation) || !IsFinite(position))
                throw new InvalidOperationException("Cannot preserve world placement with the requested parent transform.");

            var replacementTransform = new Transform
            {
                Position = position,
                Rotation = rotation,
                Scale = scale
            };
            var recomposed = replacementTransform.LocalMatrix;
            if (!NearlyEqual(replacementMatrix, recomposed))
                throw new InvalidOperationException(
                    "Cannot preserve world placement because this parent change would introduce shear.");

            var previousParentId = item.ParentId;
            var previousTransform = SceneObjectCopy.CopyTransform(item.Transform);
            scene.SetParent(_objectId, _newParentId);
            item.Transform = SceneObjectCopy.CopyTransform(replacementTransform);
            _previousParentId = previousParentId;
            _previousTransform = previousTransform;
            _replacementTransform = replacementTransform;
            _captured = true;
            return;
        }

        scene.SetParent(_objectId, _newParentId);
        item.Transform = SceneObjectCopy.CopyTransform(_replacementTransform!);
    }

    public void Revert(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_captured || _previousTransform is null)
            throw new InvalidOperationException("Cannot undo a reparent operation that was not applied.");
        var item = scene.Find(_objectId)
            ?? throw new InvalidOperationException($"Cannot undo reparenting; scene object {_objectId} is missing.");
        scene.SetParent(_objectId, _previousParentId);
        item.Transform = SceneObjectCopy.CopyTransform(_previousTransform);
    }

    private void ValidateParentChange(SceneGraph scene)
    {
        var cursor = _newParentId;
        while (cursor is { } currentId)
        {
            if (currentId == _objectId)
                throw new InvalidOperationException("Parenting would create a hierarchy cycle.");
            var parent = scene.Find(currentId)
                ?? throw new InvalidOperationException($"Cannot use missing parent scene object {currentId}.");
            cursor = parent.ParentId;
        }
    }

    private static bool NearlyEqual(Matrix first, Matrix second)
    {
        var magnitude = MathF.Max(1f, MathF.Max(MaxAbs(first), MaxAbs(second)));
        var tolerance = magnitude * 1e-5f;
        return Close(first.M11, second.M11, tolerance) && Close(first.M12, second.M12, tolerance)
            && Close(first.M13, second.M13, tolerance) && Close(first.M14, second.M14, tolerance)
            && Close(first.M21, second.M21, tolerance) && Close(first.M22, second.M22, tolerance)
            && Close(first.M23, second.M23, tolerance) && Close(first.M24, second.M24, tolerance)
            && Close(first.M31, second.M31, tolerance) && Close(first.M32, second.M32, tolerance)
            && Close(first.M33, second.M33, tolerance) && Close(first.M34, second.M34, tolerance)
            && Close(first.M41, second.M41, tolerance) && Close(first.M42, second.M42, tolerance)
            && Close(first.M43, second.M43, tolerance) && Close(first.M44, second.M44, tolerance);
    }

    private static bool Close(float first, float second, float tolerance) =>
        float.IsFinite(first) && float.IsFinite(second) && MathF.Abs(first - second) <= tolerance;

    private static float MaxAbs(Matrix value)
    {
        var firstRow = MathF.Max(MathF.Max(MathF.Abs(value.M11), MathF.Abs(value.M12)),
            MathF.Max(MathF.Abs(value.M13), MathF.Abs(value.M14)));
        var secondRow = MathF.Max(MathF.Max(MathF.Abs(value.M21), MathF.Abs(value.M22)),
            MathF.Max(MathF.Abs(value.M23), MathF.Abs(value.M24)));
        var thirdRow = MathF.Max(MathF.Max(MathF.Abs(value.M31), MathF.Abs(value.M32)),
            MathF.Max(MathF.Abs(value.M33), MathF.Abs(value.M34)));
        var fourthRow = MathF.Max(MathF.Max(MathF.Abs(value.M41), MathF.Abs(value.M42)),
            MathF.Max(MathF.Abs(value.M43), MathF.Abs(value.M44)));
        return MathF.Max(MathF.Max(firstRow, secondRow), MathF.Max(thirdRow, fourthRow));
    }

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
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
        if (source.TemplateInstance is not null)
            throw new InvalidOperationException("Duplicate a scene template instance as a hierarchy, not as one object.");
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
    public static SceneObject CreateSpawnMarker(SceneGraph scene, string displayName, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Spawn marker name is required.", nameof(displayName));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("Spawn marker position must be finite.", nameof(position));

        var sceneObjectId = Guid.NewGuid();
        while (scene.Find(sceneObjectId) is not null) sceneObjectId = Guid.NewGuid();
        var spawnIds = scene.Objects.Where(item => item.SpawnPoint is not null)
            .Select(item => item.SpawnPoint!.Id).ToHashSet();
        var spawnId = Guid.NewGuid();
        while (spawnIds.Contains(spawnId)) spawnId = Guid.NewGuid();
        return new SceneObject(sceneObjectId,
            SceneObjectCopy.UniqueName(displayName, scene.Objects.Select(item => item.Name)))
        {
            SpawnPoint = new WorldSpawnComponent(spawnId),
            Transform = new Transform { Position = position }
        };
    }

    public static SceneObject CreateWorldEntityPlacement(SceneGraph scene, WorldEntityKind kind,
        string definitionId, string displayName, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("World entity display name is required.", nameof(displayName));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("World entity position must be finite.", nameof(position));

        var sceneObjectId = Guid.NewGuid();
        while (scene.Find(sceneObjectId) is not null) sceneObjectId = Guid.NewGuid();
        var usedInstanceIds = scene.Objects
            .Where(item => item.WorldEntity is not null)
            .Select(item => item.WorldEntity!.InstanceId)
            .ToHashSet();
        var instanceId = Guid.NewGuid();
        while (usedInstanceIds.Contains(instanceId)) instanceId = Guid.NewGuid();

        return new SceneObject(sceneObjectId,
            SceneObjectCopy.UniqueName(displayName, scene.Objects.Select(item => item.Name)))
        {
            WorldEntity = new WorldEntityPlacementComponent(kind, definitionId, instanceId),
            Transform = new Transform { Position = position }
        };
    }

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

/// <summary>A reversible edit to one scene object's inter-cell door destination.</summary>
public sealed class WorldDoorEditCommand : ISceneCommand
{
    private readonly Guid _objectId;
    private readonly WorldDoorComponent _replacement;
    private WorldDoorComponent? _previous;
    private bool _captured;

    public WorldDoorEditCommand(Guid objectId, WorldDoorComponent replacement)
    {
        if (objectId == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(objectId));
        _objectId = objectId;
        _replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    public void Apply(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var item = scene.Find(_objectId)
            ?? throw new InvalidOperationException($"Cannot edit missing door object {_objectId}.");
        if (!_captured)
        {
            _previous = item.Door;
            _captured = true;
        }
        item.Door = _replacement;
    }

    public void Revert(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!_captured)
            throw new InvalidOperationException("Cannot undo a door edit that was not applied.");
        var item = scene.Find(_objectId)
            ?? throw new InvalidOperationException($"Cannot undo missing door object {_objectId}.");
        item.Door = _previous;
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
            StaticMeshLod = source.StaticMeshLod,
            CharacterSettings = CopyCharacterSettings(source.CharacterSettings),
            BoxCollider = source.BoxCollider,
            TriggerAction = source.TriggerAction,
            Door = source.Door,
            ResetPolicy = source.ResetPolicy,
            WorldEntity = source.WorldEntity is null
                ? null
                : id is null
                    ? source.WorldEntity
                    : new WorldEntityPlacementComponent(source.WorldEntity.Kind,
                        source.WorldEntity.DefinitionId, Guid.NewGuid(), source.WorldEntity.TemplateId,
                        source.WorldEntity.TemplateOverrides),
            TemplateInstance = source.TemplateInstance,
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

    public static GltfCharacterSettings? CopyCharacterSettings(GltfCharacterSettings? source) =>
        source?.DeepCopy();

    public static string UniqueName(string sourceName, IEnumerable<string> names)
    {
        var occupied = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{sourceName} Copy";
        for (var suffix = 2; occupied.Contains(candidate); suffix++)
            candidate = $"{sourceName} Copy {suffix}";
        return candidate;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Authoring;

/// <summary>Places an expanded, independently identified scene-template hierarchy.</summary>
public static class SceneTemplateInstanceSystem
{
    public static SceneObject Instantiate(SceneGraph destination, SceneTemplateSnapshot template,
        Vector3 position, Guid? targetWorldCellId = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(template);
        if (!IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position), "Placement position must be finite.");
        if (targetWorldCellId == Guid.Empty)
            throw new ArgumentException("Target world-cell ID cannot be empty.", nameof(targetWorldCellId));

        var source = SceneFile.FromJson(SceneFile.ToJson(template.Scene));
        var sourceRoot = source.Find(template.RootObjectId)
            ?? throw new InvalidOperationException($"Template root object {template.RootObjectId} is missing.");
        if (sourceRoot.ParentId is not null)
            throw new InvalidOperationException("Template root cannot have a parent outside its hierarchy.");
        if (GetSubtreeIds(source, sourceRoot.Id).Count != source.Objects.Count)
            throw new InvalidOperationException("Template contains objects outside its root hierarchy.");
        if (source.Objects.Any(item => item.TemplateInstance is not null))
            throw new InvalidOperationException("Nested scene-template instances are not supported yet.");

        var objectIds = destination.Objects.Select(item => item.Id).ToHashSet();
        var wrapperId = CreateUniqueId(objectIds);
        var objectMap = source.Objects.ToDictionary(item => item.Id, _ => CreateUniqueId(objectIds));

        var spawnIds = destination.Objects.Where(item => item.SpawnPoint is not null)
            .Select(item => item.SpawnPoint!.Id).ToHashSet();
        var spawnMap = source.Objects.Where(item => item.SpawnPoint is not null)
            .ToDictionary(item => item.SpawnPoint!.Id, _ => CreateUniqueId(spawnIds));

        var entityInstanceIds = destination.Objects.Where(item => item.WorldEntity is not null)
            .Select(item => item.WorldEntity!.InstanceId).ToHashSet();
        var entityInstanceMap = source.Objects.Where(item => item.WorldEntity is not null)
            .ToDictionary(item => item.WorldEntity!.InstanceId, _ => CreateUniqueId(entityInstanceIds));

        var attachmentIds = destination.Objects.Where(item => item.CharacterSettings is not null)
            .SelectMany(item => item.CharacterSettings!.Attachments).Select(item => item.Id).ToHashSet();
        var clones = source.Objects.Select(item => CreateClone(item, objectMap[item.Id], spawnMap,
            entityInstanceMap, attachmentIds, targetWorldCellId)).ToArray();
        var mappings = objectMap.OrderBy(pair => pair.Key)
            .Select(pair => new SceneTemplateObjectMapping(pair.Key, pair.Value)).ToArray();
        var instanceRoot = new SceneObject(wrapperId,
            CreateUniqueName(template.Name, destination.Objects.Select(item => item.Name)))
        {
            Transform = new Transform { Position = position },
            TemplateInstance = new SceneTemplateInstanceComponent(template.Id, template.Revision,
                template.RootObjectId, objectMap[template.RootObjectId], mappings,
                source.Objects.Select(item => new SceneTemplateObjectBaseline(
                    item.Id, item.Name, item.Transform)))
        };

        var addedIds = new List<Guid>(clones.Length + 1);
        try
        {
            destination.Add(instanceRoot);
            addedIds.Add(instanceRoot.Id);
            foreach (var clone in clones)
            {
                destination.Add(clone);
                addedIds.Add(clone.Id);
            }

            foreach (var item in source.Objects)
            {
                var parentId = item.ParentId is { } sourceParent
                    ? objectMap[sourceParent]
                    : instanceRoot.Id;
                destination.SetParent(objectMap[item.Id], parentId);
            }

            return instanceRoot;
        }
        catch
        {
            for (var index = addedIds.Count - 1; index >= 0; index--)
                destination.Remove(addedIds[index]);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing instance to a newer revision when its source-object set is unchanged.
    /// Local name and transform edits are kept; unchanged fields take the new source defaults.
    /// </summary>
    public static void Update(SceneGraph destination, Guid instanceWrapperId, SceneTemplateSnapshot template)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (instanceWrapperId == Guid.Empty)
            throw new ArgumentException("Template instance wrapper ID cannot be empty.", nameof(instanceWrapperId));
        ArgumentNullException.ThrowIfNull(template);

        var wrapper = destination.Find(instanceWrapperId)
            ?? throw new InvalidOperationException($"Template instance wrapper {instanceWrapperId} was not found.");
        var instance = wrapper.TemplateInstance
            ?? throw new InvalidOperationException($"Scene object {instanceWrapperId} is not a template instance wrapper.");
        if (instance.TemplateId != template.Id)
            throw new InvalidOperationException("The template source ID does not match this instance.");
        if (template.Revision <= instance.AppliedRevision)
            throw new InvalidOperationException("The template revision must be newer than the instance revision.");
        if (template.RootObjectId != instance.SourceRootObjectId)
            throw new InvalidOperationException("The template root source ID changed; this instance cannot be updated safely.");
        if (instance.ObjectBaselines.Count == 0)
            throw new InvalidOperationException(
                "This instance has no saved source baseline. Place it again from the current template before updating it.");

        var source = SceneFile.FromJson(SceneFile.ToJson(template.Scene));
        var sourceRoot = source.Find(template.RootObjectId)
            ?? throw new InvalidOperationException($"Template root object {template.RootObjectId} is missing.");
        if (sourceRoot.ParentId is not null)
            throw new InvalidOperationException("Template root cannot have a parent outside its hierarchy.");
        if (GetSubtreeIds(source, sourceRoot.Id).Count != source.Objects.Count)
            throw new InvalidOperationException("Template contains objects outside its root hierarchy.");
        if (source.Objects.Any(item => item.TemplateInstance is not null))
            throw new InvalidOperationException("Nested scene-template instances are not supported yet.");

        var sourceById = source.Objects.ToDictionary(item => item.Id);
        var mappings = instance.ObjectMappings.ToDictionary(mapping => mapping.SourceObjectId,
            mapping => mapping.InstanceObjectId);
        if (!sourceById.Keys.ToHashSet().SetEquals(mappings.Keys))
            throw new InvalidOperationException(
                "This update changes the template's source-object set. Adding and removing template objects are not supported yet.");
        var baselines = instance.ObjectBaselines.ToDictionary(baseline => baseline.SourceObjectId);
        var updates = new List<(SceneObject Target, string Name, Transform Transform, Guid? ParentId)>();
        foreach (var (sourceId, sourceObject) in sourceById)
        {
            if (!baselines.TryGetValue(sourceId, out var baseline))
                throw new InvalidOperationException($"The saved source baseline for object {sourceId} is missing.");
            var instanceObjectId = mappings[sourceId];
            var target = destination.Find(instanceObjectId)
                ?? throw new InvalidOperationException($"Mapped instance object {instanceObjectId} is missing.");
            if (!IsInInstanceHierarchy(destination, target, wrapper.Id))
                throw new InvalidOperationException(
                    $"Mapped instance object {instanceObjectId} has been moved outside its template wrapper.");

            var name = string.Equals(target.Name, baseline.Name, StringComparison.Ordinal)
                ? sourceObject.Name
                : target.Name;
            var transform = baseline.MatchesTransform(target.Transform)
                ? CopyTransform(sourceObject.Transform)
                : CopyTransform(target.Transform);
            var parentId = sourceObject.ParentId is { } sourceParentId
                ? mappings[sourceParentId]
                : wrapper.Id;
            updates.Add((target, name, transform, parentId));
        }

        var before = CaptureState(destination, wrapper.Id);
        try
        {
            foreach (var update in updates) destination.SetParent(update.Target.Id, null);
            foreach (var update in updates)
            {
                update.Target.Name = update.Name;
                update.Target.Transform = update.Transform;
            }
            foreach (var update in updates) destination.SetParent(update.Target.Id, update.ParentId);

            wrapper.TemplateInstance = new SceneTemplateInstanceComponent(template.Id, template.Revision,
                template.RootObjectId, instance.InstanceRootObjectId, instance.ObjectMappings,
                source.Objects.Select(item => new SceneTemplateObjectBaseline(
                    item.Id, item.Name, item.Transform)));
        }
        catch
        {
            RestoreState(destination, before);
            throw;
        }
    }

    internal static SceneTemplateInstanceState CaptureState(SceneGraph scene, Guid wrapperId)
    {
        var wrapper = scene.Find(wrapperId)
            ?? throw new InvalidOperationException($"Template instance wrapper {wrapperId} was not found.");
        var instance = wrapper.TemplateInstance
            ?? throw new InvalidOperationException($"Scene object {wrapperId} is not a template instance wrapper.");
        var ids = instance.ObjectMappings.Select(mapping => mapping.InstanceObjectId)
            .Append(wrapperId).Distinct().ToArray();
        var objects = ids.Select(id =>
        {
            var item = scene.Find(id)
                ?? throw new InvalidOperationException($"Template instance object {id} is missing.");
            return new SceneTemplateObjectState(item.Id, item.Name, CopyTransform(item.Transform),
                item.ParentId, item.TemplateInstance);
        }).ToArray();
        return new SceneTemplateInstanceState(wrapperId, objects);
    }

    internal static void RestoreState(SceneGraph scene, SceneTemplateInstanceState state)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(state);
        foreach (var item in state.Objects)
            if (scene.Find(item.ObjectId) is null)
                throw new InvalidOperationException($"Cannot restore template instance because object {item.ObjectId} is missing.");

        foreach (var item in state.Objects.Where(item => item.ObjectId != state.WrapperId))
            scene.SetParent(item.ObjectId, null);
        foreach (var item in state.Objects)
        {
            var current = scene.Find(item.ObjectId)!;
            current.Name = item.Name;
            current.Transform = CopyTransform(item.Transform);
            current.TemplateInstance = item.TemplateInstance;
        }
        foreach (var item in state.Objects.Where(item => item.ObjectId != state.WrapperId))
            scene.SetParent(item.ObjectId, item.ParentId);
    }

    private static SceneObject CreateClone(SceneObject source, Guid id,
        IReadOnlyDictionary<Guid, Guid> spawnMap,
        IReadOnlyDictionary<Guid, Guid> entityInstanceMap,
        HashSet<Guid> usedAttachmentIds,
        Guid? targetWorldCellId)
    {
        var character = CopyCharacterSettings(source.CharacterSettings, usedAttachmentIds);
        WorldDoorComponent? door = null;
        if (source.Door is { } sourceDoor)
        {
            var cellId = sourceDoor.DestinationCellId;
            var spawnId = sourceDoor.DestinationSpawnId;
            if (spawnMap.TryGetValue(spawnId, out var replacementSpawnId))
            {
                if (targetWorldCellId is null)
                    throw new InvalidOperationException(
                        "This template links a door to a spawn point inside itself; place it in a world cell to remap that link.");
                cellId = targetWorldCellId.Value;
                spawnId = replacementSpawnId;
            }
            door = new WorldDoorComponent(cellId, spawnId, sourceDoor.Facing);
        }

        return new SceneObject(id, source.Name)
        {
            Enabled = source.Enabled,
            Transform = new Transform
            {
                Position = source.Transform.Position,
                Rotation = source.Transform.Rotation,
                Scale = source.Transform.Scale
            },
            GltfAsset = source.GltfAsset,
            StaticMeshLod = source.StaticMeshLod,
            CharacterSettings = character,
            Door = door,
            SpawnPoint = source.SpawnPoint is { } spawn
                ? new WorldSpawnComponent(spawnMap[spawn.Id])
                : null,
            WorldEntity = source.WorldEntity is { } entity
                ? new WorldEntityPlacementComponent(entity.Kind, entity.DefinitionId,
                    entityInstanceMap[entity.InstanceId], entity.TemplateId, entity.TemplateOverrides)
                : null,
            ResetPolicy = source.ResetPolicy
        };
    }

    private static GltfCharacterSettings? CopyCharacterSettings(GltfCharacterSettings? source,
        HashSet<Guid> usedAttachmentIds)
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
                CreateUniqueId(usedAttachmentIds), attachment.BoneName, attachment.LocalOffset));
        return copy;
    }

    private static HashSet<Guid> GetSubtreeIds(SceneGraph scene, Guid rootId)
    {
        var children = scene.Objects.Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
        var result = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(rootId);
        while (pending.TryPop(out var current))
        {
            if (!result.Add(current))
                throw new InvalidOperationException("Template hierarchy contains a cycle.");
            if (children.TryGetValue(current, out var descendants))
                foreach (var child in descendants) pending.Push(child);
        }
        return result;
    }

    private static Guid CreateUniqueId(HashSet<Guid> usedIds)
    {
        Guid id;
        do { id = Guid.NewGuid(); }
        while (!usedIds.Add(id));
        return id;
    }

    private static string CreateUniqueName(string baseName, IEnumerable<string> existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Transform CopyTransform(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    private static bool IsInInstanceHierarchy(SceneGraph scene, SceneObject item, Guid wrapperId)
    {
        var visited = new HashSet<Guid>();
        var parentId = item.ParentId;
        while (parentId is { } currentId)
        {
            if (currentId == wrapperId) return true;
            if (!visited.Add(currentId)) return false;
            parentId = scene.Find(currentId)?.ParentId;
        }
        return false;
    }
}

internal sealed record SceneTemplateInstanceState(Guid WrapperId, IReadOnlyList<SceneTemplateObjectState> Objects);

internal sealed record SceneTemplateObjectState(Guid ObjectId, string Name, Transform Transform,
    Guid? ParentId, SceneTemplateInstanceComponent? TemplateInstance);

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
                source.Objects.Select((item, index) => new SceneTemplateObjectBaseline(
                    item.Id, item.Name, item.Transform, item.Enabled, item.ResetPolicy,
                    item.GltfAsset, hasGltfAssetBaseline: true,
                    staticMeshLod: item.StaticMeshLod, hasStaticMeshLodBaseline: true,
                    characterSettingsBaseline: CaptureCharacterSettingsBaseline(
                        item.CharacterSettings, clones[index].CharacterSettings),
                    hasCharacterSettingsBaseline: true)),
                targetWorldCellId: targetWorldCellId)
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
    /// Updates an existing instance to a newer revision while preserving stable IDs and local
    /// name/transform edits. Removed source objects with local content are retained as orphans.
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
        var oldMappings = instance.ObjectMappings.ToDictionary(mapping => mapping.SourceObjectId,
            mapping => mapping.InstanceObjectId);
        var baselines = instance.ObjectBaselines.ToDictionary(baseline => baseline.SourceObjectId);
        var objectIds = destination.Objects.Select(item => item.Id).ToHashSet();
        var mappings = new Dictionary<Guid, Guid>();
        foreach (var sourceId in sourceById.Keys.OrderBy(id => id))
            mappings[sourceId] = oldMappings.TryGetValue(sourceId, out var existingId)
                ? existingId
                : CreateUniqueId(objectIds);

        var removedSourceIds = oldMappings.Keys.Where(sourceId => !sourceById.ContainsKey(sourceId)).ToHashSet();
        var removedInstanceIds = removedSourceIds.Select(sourceId => oldMappings[sourceId]).ToHashSet();
        var retainedRemovedIds = new HashSet<Guid>();
        foreach (var sourceId in removedSourceIds)
        {
            var target = destination.Find(oldMappings[sourceId])
                ?? throw new InvalidOperationException($"Mapped instance object {oldMappings[sourceId]} is missing.");
            if (!baselines.TryGetValue(sourceId, out var baseline))
                throw new InvalidOperationException($"The saved source baseline for object {sourceId} is missing.");
            var baselineIsIncomplete = baseline.Enabled is null || baseline.ResetPolicy is null
                || !baseline.HasGltfAssetBaseline || !baseline.HasStaticMeshLodBaseline
                || !baseline.HasCharacterSettingsBaseline
                || baseline.CharacterSettingsBaseline is { HasAttachmentMappings: false };
            var hasKnownOverride = !string.Equals(target.Name, baseline.Name, StringComparison.Ordinal)
                || !baseline.MatchesTransform(target.Transform)
                || baseline.Enabled is { } baselineEnabled && target.Enabled != baselineEnabled
                || baseline.ResetPolicy is { } baselineResetPolicy && target.ResetPolicy != baselineResetPolicy
                || baseline.HasGltfAssetBaseline && !MatchesAssetReference(target.GltfAsset, baseline.GltfAsset)
                || baseline.HasStaticMeshLodBaseline && !MatchesStaticMeshLod(target.StaticMeshLod, baseline.StaticMeshLod)
                || baseline.HasCharacterSettingsBaseline
                    && !MatchesCharacterSettings(target.CharacterSettings, baseline.CharacterSettingsBaseline);
            if (baselineIsIncomplete || hasKnownOverride)
                retainedRemovedIds.Add(target.Id);
        }
        var referencedSpawnIds = destination.Objects.Where(item => item.Door is not null)
            .Select(item => item.Door!.DestinationSpawnId).ToHashSet();
        foreach (var sourceId in removedSourceIds)
        {
            var target = destination.Find(oldMappings[sourceId])!;
            if (target.SpawnPoint is { } spawn && referencedSpawnIds.Contains(spawn.Id))
                retainedRemovedIds.Add(target.Id);
        }

        var oldMappedIds = oldMappings.Values.ToHashSet();
        var userContent = destination.Objects.Where(item => item.Id != wrapper.Id
                && !oldMappedIds.Contains(item.Id)
                && IsInInstanceHierarchy(destination, item, wrapper.Id))
            .ToArray();
        foreach (var item in userContent)
        {
            var cursor = item.ParentId;
            while (cursor is { } parentId && parentId != wrapper.Id)
            {
                if (removedInstanceIds.Contains(parentId)) retainedRemovedIds.Add(parentId);
                cursor = destination.Find(parentId)?.ParentId;
            }
        }
        var sourceByInstanceId = oldMappings.ToDictionary(pair => pair.Value, pair => pair.Key);
        var pendingKeep = new Stack<Guid>(retainedRemovedIds);
        while (pendingKeep.TryPop(out var retainedId))
        {
            var retainedObject = destination.Find(retainedId)!;
            if (retainedObject.ParentId is { } parentId
                && sourceByInstanceId.TryGetValue(parentId, out var parentSourceId)
                && removedSourceIds.Contains(parentSourceId)
                && retainedRemovedIds.Add(parentId))
                pendingKeep.Push(parentId);
        }

        var spawnIds = destination.Objects.Where(item => item.SpawnPoint is not null)
            .Select(item => item.SpawnPoint!.Id).ToHashSet();
        var spawnMap = source.Objects.Where(item => item.SpawnPoint is not null)
            .ToDictionary(item => item.SpawnPoint!.Id, item =>
            {
                if (oldMappings.TryGetValue(item.Id, out var oldId)
                    && destination.Find(oldId)?.SpawnPoint is { } oldSpawn)
                    return oldSpawn.Id;
                return CreateUniqueId(spawnIds);
            });
        var entityIds = destination.Objects.Where(item => item.WorldEntity is not null)
            .Select(item => item.WorldEntity!.InstanceId).ToHashSet();
        var entityMap = source.Objects.Where(item => item.WorldEntity is not null)
            .ToDictionary(item => item.WorldEntity!.InstanceId, item =>
            {
                if (oldMappings.TryGetValue(item.Id, out var oldId)
                    && destination.Find(oldId)?.WorldEntity is { } oldEntity)
                    return oldEntity.InstanceId;
                return CreateUniqueId(entityIds);
            });
        var attachmentIds = destination.Objects.Where(item => item.CharacterSettings is not null)
            .SelectMany(item => item.CharacterSettings!.Attachments).Select(item => item.Id).ToHashSet();
        foreach (var attachmentBaseline in baselines.Values
            .SelectMany(baseline => baseline.CharacterSettingsBaseline?.AttachmentBaselines
                ?? Array.Empty<SceneTemplateAttachmentBaseline>()))
            attachmentIds.Add(attachmentBaseline.InstanceAttachmentId);
        var newObjects = source.Objects.Where(item => !oldMappings.ContainsKey(item.Id))
            .Select(item => CreateClone(item, mappings[item.Id], spawnMap, entityMap,
                attachmentIds, instance.TargetWorldCellId)).ToArray();
        var newObjectsById = newObjects.ToDictionary(item => item.Id);
        var characterBaselinesBySourceId = new Dictionary<Guid, SceneTemplateCharacterSettingsBaseline?>();

        var updates = new List<(SceneObject Target, string Name, Transform Transform,
            bool Enabled, WorldInstanceResetPolicy ResetPolicy, GltfAssetReference? GltfAsset,
            GltfStaticMeshLod? StaticMeshLod, GltfCharacterSettings? CharacterSettings,
            SceneTemplateCharacterSettingsBaseline? CharacterBaseline)>();
        foreach (var (sourceId, sourceObject) in sourceById)
        {
            if (!oldMappings.TryGetValue(sourceId, out var instanceObjectId)) continue;
            if (!baselines.TryGetValue(sourceId, out var baseline))
                throw new InvalidOperationException($"The saved source baseline for object {sourceId} is missing.");
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
            var enabled = baseline.Enabled is { } baselineEnabled && target.Enabled == baselineEnabled
                ? sourceObject.Enabled
                : target.Enabled;
            var resetPolicy = baseline.ResetPolicy is { } baselineResetPolicy
                && target.ResetPolicy == baselineResetPolicy
                ? sourceObject.ResetPolicy
                : target.ResetPolicy;
            var gltfAsset = baseline.HasGltfAssetBaseline
                && MatchesAssetReference(target.GltfAsset, baseline.GltfAsset)
                ? sourceObject.GltfAsset
                : target.GltfAsset;
            var staticMeshLod = baseline.HasStaticMeshLodBaseline
                && MatchesStaticMeshLod(target.StaticMeshLod, baseline.StaticMeshLod)
                ? sourceObject.StaticMeshLod
                : target.StaticMeshLod;
            var characterMerge = MergeCharacterSettings(sourceObject.CharacterSettings,
                target.CharacterSettings, baseline, attachmentIds);
            characterBaselinesBySourceId[sourceId] = characterMerge.Baseline;
            updates.Add((target, name, transform, enabled, resetPolicy, gltfAsset,
                staticMeshLod, characterMerge.Settings, characterMerge.Baseline));
        }
        foreach (var sourceObject in source.Objects.Where(item => !oldMappings.ContainsKey(item.Id)))
            characterBaselinesBySourceId[sourceObject.Id] = CaptureCharacterSettingsBaseline(
                sourceObject.CharacterSettings, newObjectsById[mappings[sourceObject.Id]].CharacterSettings);

        var nextOrphanIds = instance.OrphanedObjectIds
            .Where(id => destination.Find(id) is not null)
            .Concat(retainedRemovedIds)
            .Concat(userContent.Where(item => IsUnderAny(destination, item, retainedRemovedIds)).Select(item => item.Id))
            .Distinct().ToArray();
        var before = CaptureState(destination, wrapper.Id);
        try
        {
            foreach (var item in newObjects) destination.AddUnparented(item);
            foreach (var update in updates) destination.SetParent(update.Target.Id, null);
            foreach (var item in newObjects) destination.SetParent(item.Id, null);
            foreach (var update in updates)
            {
                update.Target.Name = update.Name;
                update.Target.Transform = update.Transform;
                update.Target.Enabled = update.Enabled;
                update.Target.ResetPolicy = update.ResetPolicy;
                update.Target.GltfAsset = update.GltfAsset;
                update.Target.StaticMeshLod = update.StaticMeshLod;
                update.Target.CharacterSettings = update.CharacterSettings;
            }
            foreach (var sourceObject in source.Objects)
            {
                var parentId = sourceObject.ParentId is { } sourceParentId
                    ? mappings[sourceParentId]
                    : wrapper.Id;
                destination.SetParent(mappings[sourceObject.Id], parentId);
            }
            foreach (var removedId in OrderBySceneDepth(destination,
                removedInstanceIds.Except(retainedRemovedIds), descending: true))
                destination.Remove(removedId);

            var objectMappings = mappings.OrderBy(pair => pair.Key)
                .Select(pair => new SceneTemplateObjectMapping(pair.Key, pair.Value)).ToArray();
            wrapper.TemplateInstance = new SceneTemplateInstanceComponent(template.Id, template.Revision,
                template.RootObjectId, mappings[template.RootObjectId], objectMappings,
                source.Objects.Select(item => new SceneTemplateObjectBaseline(
                    item.Id, item.Name, item.Transform, item.Enabled, item.ResetPolicy,
                    item.GltfAsset, hasGltfAssetBaseline: true,
                    staticMeshLod: item.StaticMeshLod, hasStaticMeshLodBaseline: true,
                    characterSettingsBaseline: characterBaselinesBySourceId[item.Id],
                    hasCharacterSettingsBaseline: true)),
                nextOrphanIds, instance.TargetWorldCellId);
        }
        catch
        {
            foreach (var item in OrderBySceneDepth(destination,
                newObjects.Select(item => item.Id).Where(id => destination.Find(id) is not null), descending: true))
                destination.Remove(item);
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
            .Concat(instance.OrphanedObjectIds)
            .Concat(scene.Objects.Where(item => IsInInstanceHierarchy(scene, item, wrapperId)).Select(item => item.Id))
            .Append(wrapperId).Distinct().ToArray();
        var objects = ids.Select(id =>
        {
            var item = scene.Find(id)
                ?? throw new InvalidOperationException($"Template instance object {id} is missing.");
            return new SceneTemplateObjectState(item, item.Name, item.Enabled, CopyTransform(item.Transform),
                item.ResetPolicy, item.GltfAsset, item.StaticMeshLod, item.CharacterSettings,
                item.ParentId, item.TemplateInstance);
        }).ToArray();
        return new SceneTemplateInstanceState(wrapperId, objects);
    }

    internal static void RestoreState(SceneGraph scene, SceneTemplateInstanceState state)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(state);
        var desiredIds = state.Objects.Select(item => item.Object.Id).ToHashSet();
        var wrapper = scene.Find(state.WrapperId)
            ?? throw new InvalidOperationException($"Cannot restore template instance because wrapper {state.WrapperId} is missing.");
        var currentIds = wrapper.TemplateInstance is { } currentInstance
            ? currentInstance.ObjectMappings.Select(mapping => mapping.InstanceObjectId)
                .Concat(currentInstance.OrphanedObjectIds)
                .Concat(scene.Objects.Where(item => IsInInstanceHierarchy(scene, item, state.WrapperId)).Select(item => item.Id))
                .Append(state.WrapperId).Distinct().ToHashSet()
            : scene.Objects.Where(item => IsInInstanceHierarchy(scene, item, state.WrapperId))
                .Select(item => item.Id).Append(state.WrapperId).ToHashSet();
        foreach (var extraId in OrderBySceneDepth(scene, currentIds.Except(desiredIds), descending: true))
            scene.Remove(extraId);
        foreach (var item in OrderStateByDepth(state.Objects))
            if (scene.Find(item.Object.Id) is null)
                scene.AddUnparented(item.Object);
        foreach (var item in state.Objects)
            scene.SetParent(item.Object.Id, null);
        foreach (var item in state.Objects)
        {
            var current = scene.Find(item.Object.Id)!;
            current.Name = item.Name;
            current.Enabled = item.Enabled;
            current.Transform = CopyTransform(item.Transform);
            current.ResetPolicy = item.ResetPolicy;
            current.GltfAsset = item.GltfAsset;
            current.StaticMeshLod = item.StaticMeshLod;
            current.CharacterSettings = item.CharacterSettings;
            current.TemplateInstance = item.TemplateInstance;
        }
        foreach (var item in state.Objects)
            scene.SetParent(item.Object.Id, item.ParentId);
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

    private static bool MatchesAssetReference(GltfAssetReference? left, GltfAssetReference? right) =>
        left is null ? right is null
        : right is not null && left.AssetId == right.AssetId
            && string.Equals(left.SourcePath, right.SourcePath, StringComparison.Ordinal);

    private static bool MatchesStaticMeshLod(GltfStaticMeshLod? left, GltfStaticMeshLod? right) =>
        left is null ? right is null
        : right is not null
            && MatchesAssetReference(left.NearAsset, right.NearAsset)
            && MatchesAssetReference(left.FarAsset, right.FarAsset)
            && left.EnterFarDistance.Equals(right.EnterFarDistance)
            && left.ExitFarDistance.Equals(right.ExitFarDistance);

    private static bool MatchesCharacterSettings(GltfCharacterSettings? target,
        SceneTemplateCharacterSettingsBaseline? baseline)
    {
        if (target is null) return baseline is null;
        if (baseline is null || target.Attachments.Count != baseline.AttachmentCount) return false;
        if (!string.Equals(target.ClipName, baseline.ClipName, StringComparison.Ordinal)
            || !target.Time.Equals(baseline.Time)
            || !target.Speed.Equals(baseline.Speed)
            || target.Loop != baseline.Loop
            || target.IsPlaying != baseline.IsPlaying
            || !string.Equals(target.CrossfadeClipName, baseline.CrossfadeClipName, StringComparison.Ordinal)
            || !target.BlendAmount.Equals(baseline.BlendAmount)) return false;
        if (!baseline.HasAttachmentMappings) return true;
        if (target.Attachments.Count != baseline.AttachmentBaselines.Count) return false;
        var targetById = target.Attachments.ToDictionary(attachment => attachment.Id);
        return baseline.AttachmentBaselines.All(attachment =>
            targetById.TryGetValue(attachment.InstanceAttachmentId, out var current)
            && string.Equals(current.BoneName, attachment.BoneName, StringComparison.Ordinal)
            && current.LocalOffset.Equals(attachment.LocalOffset));
    }

    private static (GltfCharacterSettings? Settings, SceneTemplateCharacterSettingsBaseline? Baseline)
        MergeCharacterSettings(GltfCharacterSettings? source,
        GltfCharacterSettings? target, SceneTemplateObjectBaseline baseline, HashSet<Guid> usedAttachmentIds)
    {
        var previous = baseline.HasCharacterSettingsBaseline ? baseline.CharacterSettingsBaseline : null;
        if (source is null)
        {
            var retained = target is not null && (previous is null || !MatchesCharacterSettings(target, previous));
            return (retained ? target : null, null);
        }
        if (target is null)
        {
            if (baseline.HasCharacterSettingsBaseline && previous is null)
            {
                var copy = CopyCharacterSettings(source, usedAttachmentIds)!;
                return (copy, CaptureCharacterSettingsBaseline(source, copy));
            }
            return (null, CaptureCharacterSettingsWithoutInstance(source));
        }

        var merged = new GltfCharacterSettings
        {
            ClipName = previous is not null
                && string.Equals(target.ClipName, previous.ClipName, StringComparison.Ordinal)
                ? source.ClipName : target.ClipName,
            Time = previous is not null && target.Time.Equals(previous.Time) ? source.Time : target.Time,
            Speed = previous is not null && target.Speed.Equals(previous.Speed) ? source.Speed : target.Speed,
            Loop = previous is not null && target.Loop == previous.Loop ? source.Loop : target.Loop,
            IsPlaying = previous is not null && target.IsPlaying == previous.IsPlaying ? source.IsPlaying : target.IsPlaying,
            CrossfadeClipName = previous is not null && string.Equals(target.CrossfadeClipName, previous.CrossfadeClipName,
                StringComparison.Ordinal) ? source.CrossfadeClipName : target.CrossfadeClipName,
            BlendAmount = previous is not null && target.BlendAmount.Equals(previous.BlendAmount)
                ? source.BlendAmount : target.BlendAmount
        };
        var attachmentBaselines = MergeAttachments(source, target, previous, merged, usedAttachmentIds);
        var mergedBaseline = CreateCharacterSettingsBaseline(source, attachmentBaselines);
        return (merged, mergedBaseline);
    }

    private static SceneTemplateCharacterSettingsBaseline? CaptureCharacterSettingsBaseline(
        GltfCharacterSettings? source, GltfCharacterSettings? instance)
    {
        if (source is null) return null;
        if (instance is null || source.Attachments.Count != instance.Attachments.Count)
            return CaptureCharacterSettingsWithoutInstance(source);
        var mappings = source.Attachments.Select((attachment, index) =>
            new SceneTemplateAttachmentBaseline(attachment.Id, instance.Attachments[index].Id,
                attachment.BoneName, attachment.LocalOffset)).ToArray();
        return CreateCharacterSettingsBaseline(source, mappings);
    }

    private static SceneTemplateCharacterSettingsBaseline CaptureCharacterSettingsWithoutInstance(
        GltfCharacterSettings source) => new(
        source.ClipName, source.Time, source.Speed, source.Loop, source.IsPlaying,
        source.CrossfadeClipName, source.BlendAmount, source.Attachments.Count,
        hasAttachmentMappings: false);

    private static SceneTemplateCharacterSettingsBaseline CreateCharacterSettingsBaseline(
        GltfCharacterSettings source, IEnumerable<SceneTemplateAttachmentBaseline> attachments) => new(
        source.ClipName, source.Time, source.Speed, source.Loop, source.IsPlaying,
        source.CrossfadeClipName, source.BlendAmount, source.Attachments.Count,
        attachments, hasAttachmentMappings: true);

    private static IReadOnlyList<SceneTemplateAttachmentBaseline> MergeAttachments(
        GltfCharacterSettings source, GltfCharacterSettings target,
        SceneTemplateCharacterSettingsBaseline? previous, GltfCharacterSettings merged,
        HashSet<Guid> usedAttachmentIds)
    {
        var sourceAttachments = source.Attachments;
        var targetAttachments = target.Attachments;
        var oldMappings = previous?.AttachmentBaselines.ToDictionary(
            attachment => attachment.SourceAttachmentId) ?? new Dictionary<Guid, SceneTemplateAttachmentBaseline>();
        var targetById = targetAttachments.ToDictionary(attachment => attachment.Id);
        var mappings = new List<SceneTemplateAttachmentBaseline>(sourceAttachments.Count);
        var mappedInstanceIds = new HashSet<Guid>();
        foreach (var oldMapping in oldMappings.Values)
            mappedInstanceIds.Add(oldMapping.InstanceAttachmentId);
        var emittedIds = new HashSet<Guid>();

        for (var index = 0; index < sourceAttachments.Count; index++)
        {
            var sourceAttachment = sourceAttachments[index];
            SceneTemplateAttachmentBaseline? oldMapping = null;
            if (oldMappings.TryGetValue(sourceAttachment.Id, out var knownMapping))
                oldMapping = knownMapping;
            else if (previous is { HasAttachmentMappings: false }
                && targetAttachments.Count == previous.AttachmentCount
                && index < previous.AttachmentCount)
            {
                // Legacy baselines saved attachment count and order, but not source IDs.
                oldMapping = new SceneTemplateAttachmentBaseline(sourceAttachment.Id,
                    targetAttachments[index].Id, sourceAttachment.BoneName, sourceAttachment.LocalOffset);
            }

            if (oldMapping is not null)
            {
                mappedInstanceIds.Add(oldMapping.InstanceAttachmentId);
                if (targetById.TryGetValue(oldMapping.InstanceAttachmentId, out var current))
                {
                    var unchanged = string.Equals(current.BoneName, oldMapping.BoneName, StringComparison.Ordinal)
                        && current.LocalOffset.Equals(oldMapping.LocalOffset);
                    var result = unchanged
                        ? new GltfBoneAttachmentReference(current.Id, sourceAttachment.BoneName, sourceAttachment.LocalOffset)
                        : current;
                    if (emittedIds.Add(result.Id)) merged.Attachments.Add(result);
                }
                mappings.Add(new SceneTemplateAttachmentBaseline(sourceAttachment.Id,
                    oldMapping.InstanceAttachmentId, sourceAttachment.BoneName, sourceAttachment.LocalOffset));
                continue;
            }

            if (previous is { HasAttachmentMappings: false }
                && index < previous.AttachmentCount)
            {
                // The instance has no provable match for this legacy source attachment. Keep the
                // mapping absent so updates do not silently recreate a locally removed attachment.
                var placeholderId = CreateUniqueId(usedAttachmentIds);
                mappings.Add(new SceneTemplateAttachmentBaseline(sourceAttachment.Id,
                    placeholderId, sourceAttachment.BoneName, sourceAttachment.LocalOffset));
                continue;
            }

            var newId = CreateUniqueId(usedAttachmentIds);
            merged.Attachments.Add(new GltfBoneAttachmentReference(
                newId, sourceAttachment.BoneName, sourceAttachment.LocalOffset));
            emittedIds.Add(newId);
            mappings.Add(new SceneTemplateAttachmentBaseline(sourceAttachment.Id,
                newId, sourceAttachment.BoneName, sourceAttachment.LocalOffset));
        }

        foreach (var oldMapping in oldMappings.Values)
        {
            if (sourceAttachments.Any(attachment => attachment.Id == oldMapping.SourceAttachmentId)) continue;
            if (targetById.TryGetValue(oldMapping.InstanceAttachmentId, out var current)
                && (!string.Equals(current.BoneName, oldMapping.BoneName, StringComparison.Ordinal)
                    || !current.LocalOffset.Equals(oldMapping.LocalOffset))
                && emittedIds.Add(current.Id))
                merged.Attachments.Add(current);
        }

        foreach (var attachment in targetAttachments)
        {
            if (mappedInstanceIds.Contains(attachment.Id)) continue;
            if (emittedIds.Add(attachment.Id)) merged.Attachments.Add(attachment);
        }
        return mappings;
    }

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

    private static bool IsUnderAny(SceneGraph scene, SceneObject item, IEnumerable<Guid> ancestorIds)
    {
        var ancestors = ancestorIds.ToHashSet();
        var visited = new HashSet<Guid>();
        var parentId = item.ParentId;
        while (parentId is { } currentId)
        {
            if (ancestors.Contains(currentId)) return true;
            if (!visited.Add(currentId)) return false;
            parentId = scene.Find(currentId)?.ParentId;
        }
        return false;
    }

    private static IReadOnlyList<Guid> OrderBySceneDepth(SceneGraph scene, IEnumerable<Guid> ids, bool descending)
    {
        var snapshot = ids.Distinct().ToArray();
        int Depth(Guid id)
        {
            var depth = 0;
            var visited = new HashSet<Guid>();
            var parentId = scene.Find(id)?.ParentId;
            while (parentId is { } currentId)
            {
                if (!visited.Add(currentId)) throw new InvalidOperationException("Scene hierarchy contains a cycle.");
                depth++;
                parentId = scene.Find(currentId)?.ParentId;
            }
            return depth;
        }

        return descending
            ? snapshot.OrderByDescending(Depth).ToArray()
            : snapshot.OrderBy(Depth).ToArray();
    }

    private static IReadOnlyList<SceneTemplateObjectState> OrderStateByDepth(
        IReadOnlyList<SceneTemplateObjectState> objects)
    {
        var byId = objects.ToDictionary(item => item.Object.Id);
        int Depth(SceneTemplateObjectState item)
        {
            var depth = 0;
            var parentId = item.ParentId;
            var visited = new HashSet<Guid>();
            while (parentId is { } currentId && byId.TryGetValue(currentId, out var parent))
            {
                if (!visited.Add(currentId)) throw new InvalidOperationException("Template instance snapshot contains a cycle.");
                depth++;
                parentId = parent.ParentId;
            }
            return depth;
        }

        return objects.OrderBy(Depth).ToArray();
    }
}

internal sealed record SceneTemplateInstanceState(Guid WrapperId, IReadOnlyList<SceneTemplateObjectState> Objects);

internal sealed record SceneTemplateObjectState(SceneObject Object, string Name, bool Enabled,
    Transform Transform, WorldInstanceResetPolicy ResetPolicy, GltfAssetReference? GltfAsset,
    GltfStaticMeshLod? StaticMeshLod, GltfCharacterSettings? CharacterSettings, Guid? ParentId,
    SceneTemplateInstanceComponent? TemplateInstance);

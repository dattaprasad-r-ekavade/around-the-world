using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ember.IO;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>Versioned JSON persistence for scene identity, hierarchy, and transforms.</summary>
public static class SceneFile
{
    public const int CurrentVersion = 14;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static SceneGraph Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A scene path is required.", nameof(path));
        return LoadFromJson(File.ReadAllText(path));
    }

    internal static SceneGraph LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Scene JSON is required.", nameof(json));
        SceneDocument document;
        try
        {
            document = JsonSerializer.Deserialize<SceneDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Scene JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Scene JSON is invalid: {exception.Message}", exception);
        }

        return FromDocument(document);
    }

    public static string ToJson(SceneGraph scene) => SerializeToJson(scene);

    /// <summary>Load and validate a scene from JSON text.</summary>
    public static SceneGraph FromJson(string json) => LoadFromJson(json);

    internal static string SerializeToJson(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return JsonSerializer.Serialize(ToDocument(scene), JsonOptions);
    }

    public static void Save(SceneGraph scene, string path) => SaveAtomic(scene, path);

    /// <summary>Write beside the destination, then replace it only after serialization succeeds.</summary>
    public static void SaveAtomic(SceneGraph scene, string path)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A scene path is required.", nameof(path));

        // Build and validate before touching the destination, so an invalid scene cannot
        // destroy the last valid save.
        var bytes = Encoding.UTF8.GetBytes(SerializeToJson(scene));
        AtomicFile.Write(path, stream => stream.Write(bytes));
    }

    private static SceneGraph FromDocument(SceneDocument document)
    {
        if (document.Version < 1 || document.Version > CurrentVersion)
            throw new InvalidDataException($"Unsupported scene version {document.Version}; expected 1 through {CurrentVersion}.");
        if (document.Objects is null)
            throw new InvalidDataException("Scene object list is missing.");
        foreach (var data in document.Objects) ValidateData(data, document.Version);
        ValidateAssetReferences(document.Objects);
        ValidateAttachmentIds(document.Objects);
        ValidateWorldEntityInstanceIds(document.Objects);

        var scene = new SceneGraph();
        var parents = new Dictionary<Guid, Guid?>();
        foreach (var data in document.Objects)
        {
            if (parents.ContainsKey(data.Id))
                throw new InvalidDataException($"Duplicate scene object ID: {data.Id}.");
            var item = new SceneObject(data.Id, data.Name!)
            {
                Enabled = data.Enabled,
                Transform = ToTransform(data),
                GltfAsset = ToGltfAsset(data),
                StaticMeshLod = ToStaticMeshLod(data.StaticMeshLod),
                CharacterSettings = ToCharacterSettings(data.Character),
                Door = ToDoorComponent(data.Door),
                SpawnPoint = data.SpawnPoint is null ? null : new WorldSpawnComponent(data.SpawnPoint.Id),
                WorldEntity = ToWorldEntityComponent(data.WorldEntity),
                TemplateInstance = ToTemplateInstanceComponent(data.TemplateInstance),
                ResetPolicy = data.ResetPolicy
            };
            scene.Add(item);
            parents.Add(data.Id, data.ParentId);
        }

        foreach (var (id, parentId) in parents)
        {
            if (parentId is not null && scene.Find(parentId.Value) is null)
                throw new InvalidDataException($"Object {id} refers to missing parent {parentId}.");
            try
            {
                scene.SetParent(id, parentId);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException($"Invalid hierarchy at object {id}: {exception.Message}", exception);
            }
        }

        ValidateTemplateInstances(scene);

        return scene;
    }

    private static SceneDocument ToDocument(SceneGraph scene)
    {
        var objects = scene.Objects
            .OrderBy(value => value.Id)
            .Select(value =>
            {
                var transform = value.Transform;
                var rotation = transform.Rotation;
                return new SceneObjectData
                {
                    Id = value.Id,
                    Name = value.Name,
                    Enabled = value.Enabled,
                    ParentId = value.ParentId,
                    GltfAssetId = value.GltfAsset?.AssetId,
                    GltfAssetPath = value.GltfAsset?.SourcePath,
                    StaticMeshLod = ToStaticMeshLodData(value.StaticMeshLod),
                    Character = ToCharacterData(value.CharacterSettings),
                    Door = ToDoorData(value.Door),
                    SpawnPoint = value.SpawnPoint is null ? null : new SceneSpawnData { Id = value.SpawnPoint.Id },
                    WorldEntity = ToWorldEntityData(value.WorldEntity),
                    TemplateInstance = ToTemplateInstanceData(value.TemplateInstance),
                    ResetPolicy = value.ResetPolicy,
                    Position = [transform.Position.X, transform.Position.Y, transform.Position.Z],
                    Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
                    Scale = [transform.Scale.X, transform.Scale.Y, transform.Scale.Z]
                };
            })
            .ToList();

        foreach (var data in objects) ValidateData(data, CurrentVersion);
        foreach (var data in objects)
            if (data.ParentId is not null && objects.All(item => item.Id != data.ParentId.Value))
                throw new InvalidDataException($"Object {data.Id} refers to missing parent {data.ParentId}.");

        ValidateAssetReferences(objects);
        ValidateAttachmentIds(objects);
        ValidateWorldEntityInstanceIds(objects);
        ValidateAcyclic(objects);
        ValidateTemplateInstances(scene);
        return new SceneDocument { Version = CurrentVersion, Objects = objects };
    }

    private static GltfCharacterSettings? ToCharacterSettings(SceneCharacterData? data)
    {
        if (data is null) return null;
        var settings = new GltfCharacterSettings
        {
            ClipName = data.ClipName,
            Time = data.Time,
            Speed = data.Speed,
            Loop = data.Loop,
            IsPlaying = data.IsPlaying,
            CrossfadeClipName = data.CrossfadeClipName,
            BlendAmount = data.BlendAmount
        };
        foreach (var attachment in data.Attachments!)
            settings.Attachments.Add(new GltfBoneAttachmentReference(
                attachment.Id, attachment.BoneName!, ToMatrix(attachment.LocalOffset!)));
        return settings;
    }

    private static SceneCharacterData? ToCharacterData(GltfCharacterSettings? settings)
    {
        if (settings is null) return null;
        return new SceneCharacterData
        {
            ClipName = settings.ClipName,
            Time = settings.Time,
            Speed = settings.Speed,
            Loop = settings.Loop,
            IsPlaying = settings.IsPlaying,
            CrossfadeClipName = settings.CrossfadeClipName,
            BlendAmount = settings.BlendAmount,
            Attachments = settings.Attachments.Select(attachment => new SceneAttachmentData
            {
                Id = attachment.Id,
                BoneName = attachment.BoneName,
                LocalOffset = ToArray(attachment.LocalOffset)
            }).ToList()
        };
    }

    private static WorldDoorComponent? ToDoorComponent(SceneDoorData? data) =>
        data is null
            ? null
            : new WorldDoorComponent(data.DestinationCellId, data.DestinationSpawnId,
                new Quaternion(data.Facing![0], data.Facing[1], data.Facing[2], data.Facing[3]));

    private static WorldEntityPlacementComponent? ToWorldEntityComponent(SceneWorldEntityData? data) =>
        data is null
            ? null
            : new WorldEntityPlacementComponent(data.Kind, data.DefinitionId!, data.InstanceId,
                data.TemplateId, data.TemplateOverrides);

    private static SceneWorldEntityData? ToWorldEntityData(WorldEntityPlacementComponent? component) =>
        component is null
            ? null
            : new SceneWorldEntityData
            {
                Kind = component.Kind,
                DefinitionId = component.DefinitionId,
                InstanceId = component.InstanceId,
                TemplateId = component.TemplateId,
                TemplateOverrides = component.TemplateOverrides
            };

    private static SceneTemplateInstanceComponent? ToTemplateInstanceComponent(SceneTemplateInstanceData? data)
    {
        if (data is null) return null;
        if (data.ObjectMappings is null)
            throw new InvalidDataException("Scene template instance object mapping is missing.");
        try
        {
            return new SceneTemplateInstanceComponent(data.TemplateId, data.AppliedRevision,
                data.SourceRootObjectId, data.InstanceRootObjectId,
                data.ObjectMappings.Select(mapping => new SceneTemplateObjectMapping(
                    mapping.SourceObjectId, mapping.InstanceObjectId)),
                data.ObjectBaselines?.Select(ToTemplateObjectBaseline),
                data.OrphanedObjectIds, data.TargetWorldCellId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Scene template instance metadata is invalid: {exception.Message}", exception);
        }
    }

    private static SceneTemplateInstanceData? ToTemplateInstanceData(SceneTemplateInstanceComponent? component) =>
        component is null
            ? null
            : new SceneTemplateInstanceData
            {
                TemplateId = component.TemplateId,
                AppliedRevision = component.AppliedRevision,
                SourceRootObjectId = component.SourceRootObjectId,
                InstanceRootObjectId = component.InstanceRootObjectId,
                ObjectMappings = component.ObjectMappings.Select(mapping => new SceneTemplateMappingData
                {
                    SourceObjectId = mapping.SourceObjectId,
                    InstanceObjectId = mapping.InstanceObjectId
                }).ToList(),
                ObjectBaselines = component.ObjectBaselines.Select(baseline => new SceneTemplateObjectBaselineData
                {
                    SourceObjectId = baseline.SourceObjectId,
                    Name = baseline.Name,
                    Position = ToArray(baseline.Position),
                    Rotation = ToArray(baseline.Rotation),
                    Scale = ToArray(baseline.Scale),
                    Enabled = baseline.Enabled,
                    ResetPolicy = baseline.ResetPolicy,
                    HasGltfAssetBaseline = baseline.HasGltfAssetBaseline,
                    GltfAssetId = baseline.GltfAsset?.AssetId,
                    GltfAssetPath = baseline.GltfAsset?.SourcePath,
                    HasStaticMeshLodBaseline = baseline.HasStaticMeshLodBaseline,
                    StaticMeshLod = ToStaticMeshLodData(baseline.StaticMeshLod),
                    HasCharacterSettingsBaseline = baseline.HasCharacterSettingsBaseline,
                    CharacterSettingsBaseline = ToTemplateCharacterBaselineData(
                        baseline.CharacterSettingsBaseline),
                    HasDoorBaseline = baseline.HasDoorBaseline,
                    Door = ToDoorData(baseline.Door),
                    HasSpawnPointBaseline = baseline.HasSpawnPointBaseline,
                    SpawnPoint = baseline.SpawnPoint is null
                        ? null : new SceneSpawnData { Id = baseline.SpawnPoint.Id },
                    SourceSpawnPointId = baseline.SourceSpawnPointId,
                    HasWorldEntityBaseline = baseline.HasWorldEntityBaseline,
                    WorldEntity = ToWorldEntityData(baseline.WorldEntity)
                }).ToList(),
                OrphanedObjectIds = component.OrphanedObjectIds.ToList(),
                TargetWorldCellId = component.TargetWorldCellId
            };

    private static SceneTemplateObjectBaseline ToTemplateObjectBaseline(SceneTemplateObjectBaselineData data)
    {
        if (data is null) throw new InvalidDataException("Scene template instance contains a null object baseline.");
        if (data.Position is null || data.Position.Length != 3
            || data.Rotation is null || data.Rotation.Length != 4
            || data.Scale is null || data.Scale.Length != 3)
            throw new InvalidDataException("Scene template instance object baseline has an invalid transform.");
        try
        {
            if (data.ResetPolicy is { } resetPolicy && !Enum.IsDefined(resetPolicy))
                throw new InvalidDataException("Scene template instance baseline has an unknown reset policy.");
            var hasGltfAssetBaseline = data.HasGltfAssetBaseline == true;
            if (!hasGltfAssetBaseline && (data.GltfAssetId is not null || data.GltfAssetPath is not null))
                throw new InvalidDataException("Scene template instance baseline has a GLB reference without a baseline marker.");
            if ((data.GltfAssetId is null) != (data.GltfAssetPath is null))
                throw new InvalidDataException("Scene template instance baseline GLB reference must provide both asset ID and path.");
            var gltfAsset = data.GltfAssetId is { } assetId
                ? new GltfAssetReference(assetId, data.GltfAssetPath!)
                : null;
            var hasStaticMeshLodBaseline = data.HasStaticMeshLodBaseline == true;
            if (!hasStaticMeshLodBaseline && data.StaticMeshLod is not null)
                throw new InvalidDataException("Scene template instance baseline has LOD settings without a baseline marker.");
            var staticMeshLod = ToStaticMeshLod(data.StaticMeshLod);
            var hasCharacterSettingsBaseline = data.HasCharacterSettingsBaseline == true;
            if (!hasCharacterSettingsBaseline && data.CharacterSettingsBaseline is not null)
                throw new InvalidDataException("Scene template instance character settings have no baseline marker.");
            var characterSettingsBaseline = ToTemplateCharacterSettingsBaseline(
                data.CharacterSettingsBaseline);
            var hasDoorBaseline = data.HasDoorBaseline == true;
            if (!hasDoorBaseline && data.Door is not null)
                throw new InvalidDataException("Scene template instance baseline has a door without a baseline marker.");
            var door = ToTemplateDoorBaseline(data.Door);
            var hasSpawnPointBaseline = data.HasSpawnPointBaseline == true;
            if (!hasSpawnPointBaseline && data.SpawnPoint is not null)
                throw new InvalidDataException("Scene template instance baseline has a spawn point without a baseline marker.");
            if (data.SpawnPoint is { Id: var spawnId } && spawnId == Guid.Empty)
                throw new InvalidDataException("Scene template instance baseline has an empty spawn ID.");
            var spawnPoint = data.SpawnPoint is null ? null : new WorldSpawnComponent(data.SpawnPoint.Id);
            var hasWorldEntityBaseline = data.HasWorldEntityBaseline == true;
            if (!hasWorldEntityBaseline && data.WorldEntity is not null)
                throw new InvalidDataException("Scene template instance baseline has a world entity without a baseline marker.");
            var worldEntity = ToWorldEntityComponent(data.WorldEntity);
            return new SceneTemplateObjectBaseline(data.SourceObjectId, data.Name!, new Transform
            {
                Position = new Vector3(data.Position[0], data.Position[1], data.Position[2]),
                Rotation = new Quaternion(data.Rotation[0], data.Rotation[1], data.Rotation[2], data.Rotation[3]),
                Scale = new Vector3(data.Scale[0], data.Scale[1], data.Scale[2])
            }, data.Enabled, data.ResetPolicy, gltfAsset, hasGltfAssetBaseline,
                staticMeshLod, hasStaticMeshLodBaseline,
                characterSettingsBaseline, hasCharacterSettingsBaseline,
                door, hasDoorBaseline, spawnPoint, hasSpawnPointBaseline,
                worldEntity, hasWorldEntityBaseline, data.SourceSpawnPointId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Scene template instance object baseline is invalid: {exception.Message}", exception);
        }
    }

    private static WorldDoorComponent? ToTemplateDoorBaseline(SceneDoorData? data)
    {
        if (data is null) return null;
        if (data.DestinationCellId == Guid.Empty || data.DestinationSpawnId == Guid.Empty
            || data.Facing is null || data.Facing.Length != 4
            || data.Facing.Any(value => !float.IsFinite(value))
            || data.Facing.Sum(value => value * value) < 1e-12f)
            throw new InvalidDataException("Scene template instance door baseline is invalid.");
        return ToDoorComponent(data);
    }

    private static SceneTemplateCharacterSettingsBaselineData? ToTemplateCharacterBaselineData(
        SceneTemplateCharacterSettingsBaseline? baseline) => baseline is null
            ? null
            : new SceneTemplateCharacterSettingsBaselineData
            {
                ClipName = baseline.ClipName,
                Time = baseline.Time,
                Speed = baseline.Speed,
                Loop = baseline.Loop,
                IsPlaying = baseline.IsPlaying,
                CrossfadeClipName = baseline.CrossfadeClipName,
                BlendAmount = baseline.BlendAmount,
                AttachmentCount = baseline.AttachmentCount,
                HasAttachmentMappings = baseline.HasAttachmentMappings,
                AttachmentBaselines = baseline.AttachmentBaselines.Select(attachment =>
                    new SceneTemplateAttachmentBaselineData
                    {
                        SourceAttachmentId = attachment.SourceAttachmentId,
                        InstanceAttachmentId = attachment.InstanceAttachmentId,
                        BoneName = attachment.BoneName,
                        LocalOffset = ToArray(attachment.LocalOffset)
                    }).ToList()
            };

    private static SceneTemplateCharacterSettingsBaseline? ToTemplateCharacterSettingsBaseline(
        SceneTemplateCharacterSettingsBaselineData? data)
    {
        if (data is null) return null;
        if (data.Time is null || data.Speed is null || data.Loop is null
            || data.IsPlaying is null || data.BlendAmount is null || data.AttachmentCount is null
            || data.AttachmentCount < 0)
            throw new InvalidDataException("Scene template character baseline is incomplete.");
        var hasAttachmentMappings = data.HasAttachmentMappings == true;
        var attachmentData = data.AttachmentBaselines ?? new List<SceneTemplateAttachmentBaselineData>();
        if (data.HasAttachmentMappings is null && attachmentData.Count > 0)
            throw new InvalidDataException("Scene template character attachment mappings have no availability marker.");
        if (!hasAttachmentMappings && attachmentData.Count > 0)
            throw new InvalidDataException("Scene template character attachment mappings are present without an availability marker.");
        if (hasAttachmentMappings && (data.AttachmentBaselines is null
            || attachmentData.Count != data.AttachmentCount.Value))
            throw new InvalidDataException("Scene template character attachment mappings are incomplete.");
        var attachmentBaselines = attachmentData.Select(attachment =>
        {
            if (attachment.LocalOffset is null || attachment.LocalOffset.Length != 16)
                throw new InvalidDataException("Scene template character attachment baseline has an invalid transform.");
            return new SceneTemplateAttachmentBaseline(attachment.SourceAttachmentId,
                attachment.InstanceAttachmentId, attachment.BoneName!, ToMatrix(attachment.LocalOffset));
        }).ToArray();
        try
        {
            return new SceneTemplateCharacterSettingsBaseline(data.ClipName, data.Time.Value,
                data.Speed.Value, data.Loop.Value, data.IsPlaying.Value, data.CrossfadeClipName,
                data.BlendAmount.Value, data.AttachmentCount.Value, attachmentBaselines,
                hasAttachmentMappings);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"Scene template character baseline is invalid: {exception.Message}", exception);
        }
    }

    private static void ValidateTemplateInstances(SceneGraph scene)
    {
        foreach (var instanceOwner in scene.Objects)
        {
            if (instanceOwner.TemplateInstance is not { } instance) continue;
            if (instanceOwner.Id == instance.InstanceRootObjectId)
                throw new InvalidDataException("A scene template instance wrapper cannot also be its expanded root object.");
            if (instance.ObjectMappings.Any(mapping => mapping.InstanceObjectId == instanceOwner.Id))
                throw new InvalidDataException("A scene template instance cannot map source content onto its wrapper object.");
            if (instance.OrphanedObjectIds.Contains(instanceOwner.Id))
                throw new InvalidDataException("A scene template instance wrapper cannot be marked as orphaned content.");
            foreach (var mapping in instance.ObjectMappings)
            {
                var mappedObject = scene.Find(mapping.InstanceObjectId)
                    ?? throw new InvalidDataException($"Scene template mapping refers to missing object {mapping.InstanceObjectId}.");
                if (!IsDescendantOf(scene, mappedObject, instanceOwner.Id))
                    throw new InvalidDataException($"Mapped template object {mapping.InstanceObjectId} is outside its instance wrapper.");
            }
            foreach (var orphanId in instance.OrphanedObjectIds)
            {
                var orphan = scene.Find(orphanId)
                    ?? throw new InvalidDataException($"Scene template orphan refers to missing object {orphanId}.");
                if (!IsDescendantOf(scene, orphan, instanceOwner.Id))
                    throw new InvalidDataException($"Orphaned template object {orphanId} is outside its instance wrapper.");
            }
        }
    }

    private static bool IsDescendantOf(SceneGraph scene, SceneObject item, Guid ancestorId)
    {
        var visited = new HashSet<Guid>();
        var parentId = item.ParentId;
        while (parentId is { } currentId)
        {
            if (currentId == ancestorId) return true;
            if (!visited.Add(currentId)) return false;
            parentId = scene.Find(currentId)?.ParentId;
        }
        return false;
    }

    private static SceneDoorData? ToDoorData(WorldDoorComponent? door) =>
        door is null
            ? null
            : new SceneDoorData
            {
                DestinationCellId = door.DestinationCellId,
                DestinationSpawnId = door.DestinationSpawnId,
                Facing = [door.Facing.X, door.Facing.Y, door.Facing.Z, door.Facing.W]
            };

    private static GltfAssetReference? ToGltfAsset(SceneObjectData data) =>
        data.GltfAssetId is { } assetId
            ? new GltfAssetReference(assetId, data.GltfAssetPath!)
            : null;

    private static GltfStaticMeshLod? ToStaticMeshLod(SceneMeshLodData? data) =>
        data is null
            ? null
            : new GltfStaticMeshLod(
                new GltfAssetReference(data.NearAssetId!.Value, data.NearAssetPath!),
                new GltfAssetReference(data.FarAssetId!.Value, data.FarAssetPath!),
                data.EnterFarDistance, data.ExitFarDistance);

    private static SceneMeshLodData? ToStaticMeshLodData(GltfStaticMeshLod? lod) =>
        lod is null
            ? null
            : new SceneMeshLodData
            {
                NearAssetId = lod.NearAsset.AssetId,
                NearAssetPath = lod.NearAsset.SourcePath,
                FarAssetId = lod.FarAsset.AssetId,
                FarAssetPath = lod.FarAsset.SourcePath,
                EnterFarDistance = lod.EnterFarDistance,
                ExitFarDistance = lod.ExitFarDistance
            };

    private static void ValidateAssetReferences(IEnumerable<SceneObjectData> objects)
    {
        var pathsById = new Dictionary<Guid, string>();
        foreach (var data in objects)
        {
            if (data.GltfAssetId is { } assetId)
                AddReference(new GltfAssetReference(assetId, data.GltfAssetPath!));
            if (data.StaticMeshLod is { } lod)
            {
                AddReference(new GltfAssetReference(lod.NearAssetId!.Value, lod.NearAssetPath!));
                AddReference(new GltfAssetReference(lod.FarAssetId!.Value, lod.FarAssetPath!));
            }

            void AddReference(GltfAssetReference reference)
            {
                if (pathsById.TryGetValue(reference.AssetId, out var existingPath)
                    && !string.Equals(existingPath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"GLB asset ID {reference.AssetId} refers to more than one source path.");
                pathsById[reference.AssetId] = reference.SourcePath;
            }
        }
    }

    private static Transform ToTransform(SceneObjectData data) => new()
    {
        Position = new Vector3(data.Position![0], data.Position[1], data.Position[2]),
        Rotation = Quaternion.Normalize(new Quaternion(
            data.Rotation![0], data.Rotation[1], data.Rotation[2], data.Rotation[3])),
        Scale = new Vector3(data.Scale![0], data.Scale[1], data.Scale[2])
    };

    private static void ValidateData(SceneObjectData data, int documentVersion)
    {
        if (data is null) throw new InvalidDataException("Scene contains a null object.");
        if (data.Id == Guid.Empty) throw new InvalidDataException("Scene object ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(data.Name)) throw new InvalidDataException($"Object {data.Id} has no name.");
        if (data.Position is null || data.Position.Length != 3)
            throw new InvalidDataException($"Object {data.Id} position must contain three values.");
        if (data.Rotation is null || data.Rotation.Length != 4)
            throw new InvalidDataException($"Object {data.Id} rotation must contain four values.");
        if (data.Scale is null || data.Scale.Length != 3)
            throw new InvalidDataException($"Object {data.Id} scale must contain three values.");
        if (data.GltfAssetId.HasValue != (data.GltfAssetPath is not null))
            throw new InvalidDataException($"Object {data.Id} must provide both GLB asset ID and source path.");
        if (data.GltfAssetId == Guid.Empty)
            throw new InvalidDataException($"Object {data.Id} has an empty GLB asset ID.");
        if (data.GltfAssetId.HasValue)
        {
            try { _ = new GltfAssetReference(data.GltfAssetId.Value, data.GltfAssetPath!); }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Object {data.Id} has an invalid GLB asset reference: {exception.Message}", exception);
            }
        }

        if (documentVersion < 5 && data.StaticMeshLod is not null)
            throw new InvalidDataException($"Object {data.Id} static mesh LOD requires scene version 5.");
        if (data.StaticMeshLod is { } lod)
        {
            if (data.GltfAssetId.HasValue || data.Character is not null)
                throw new InvalidDataException($"Object {data.Id} static mesh LOD cannot be combined with a direct GLB or character settings.");
            if (lod.NearAssetId is null || lod.NearAssetPath is null
                || lod.FarAssetId is null || lod.FarAssetPath is null)
                throw new InvalidDataException($"Object {data.Id} static mesh LOD must provide both near and far GLB asset IDs and paths.");
            try
            {
                _ = new GltfStaticMeshLod(
                    new GltfAssetReference(lod.NearAssetId.Value, lod.NearAssetPath),
                    new GltfAssetReference(lod.FarAssetId.Value, lod.FarAssetPath),
                    lod.EnterFarDistance, lod.ExitFarDistance);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Object {data.Id} has invalid static mesh LOD settings: {exception.Message}", exception);
            }
        }

        if (documentVersion < 3 && (data.Door is not null || data.SpawnPoint is not null))
            throw new InvalidDataException($"Object {data.Id} world travel components require scene version 3.");
        if (documentVersion < 6 && data.WorldEntity is not null)
            throw new InvalidDataException($"Object {data.Id} world entity placement requires scene version 6.");
        if (documentVersion < 8 && data.TemplateInstance is not null)
            throw new InvalidDataException($"Object {data.Id} scene template instance data requires scene version 8.");
        if (documentVersion < 9 && data.TemplateInstance is { } instanceData
            && (instanceData.ObjectBaselines is { Count: > 0 }
                || instanceData.OrphanedObjectIds is { Count: > 0 }
                || instanceData.TargetWorldCellId is not null))
            throw new InvalidDataException($"Object {data.Id} scene template update metadata requires scene version 9.");
        if (documentVersion < 10 && data.TemplateInstance?.ObjectBaselines?.Any(baseline => baseline is not null
                && (baseline.Enabled is not null || baseline.ResetPolicy is not null)) == true)
            throw new InvalidDataException($"Object {data.Id} extended template field baselines require scene version 10.");
        if (documentVersion < 11 && data.TemplateInstance?.ObjectBaselines?.Any(baseline => baseline is not null
                && (baseline.HasGltfAssetBaseline is not null || baseline.GltfAssetId is not null
                    || baseline.GltfAssetPath is not null || baseline.HasStaticMeshLodBaseline is not null
                    || baseline.StaticMeshLod is not null)) == true)
            throw new InvalidDataException($"Object {data.Id} asset and LOD template baselines require scene version 11.");
        if (documentVersion < 12 && data.TemplateInstance?.ObjectBaselines?.Any(baseline => baseline is not null
                && (baseline.HasCharacterSettingsBaseline is not null
                    || baseline.CharacterSettingsBaseline is not null)) == true)
            throw new InvalidDataException($"Object {data.Id} character template baselines require scene version 12.");
        if (documentVersion < 13 && data.TemplateInstance?.ObjectBaselines?.Any(baseline => baseline?.CharacterSettingsBaseline is { } character
                && (character.HasAttachmentMappings is not null || character.AttachmentBaselines is not null)) == true)
            throw new InvalidDataException($"Object {data.Id} character attachment mappings require scene version 13.");
        if (documentVersion < 14 && data.TemplateInstance?.ObjectBaselines?.Any(baseline => baseline is not null
                && (baseline.HasDoorBaseline is not null || baseline.Door is not null
                    || baseline.HasSpawnPointBaseline is not null || baseline.SpawnPoint is not null
                    || baseline.SourceSpawnPointId is not null
                    || baseline.HasWorldEntityBaseline is not null || baseline.WorldEntity is not null)) == true)
            throw new InvalidDataException($"Object {data.Id} world component template baselines require scene version 14.");
        if (!Enum.IsDefined(data.ResetPolicy))
            throw new InvalidDataException($"Object {data.Id} has unknown reset policy value {(int)data.ResetPolicy}.");
        if (documentVersion < 4 && data.ResetPolicy != WorldInstanceResetPolicy.Preserve)
            throw new InvalidDataException($"Object {data.Id} reset policy requires scene version 4.");
        if (data.SpawnPoint is { Id: var spawnId } && spawnId == Guid.Empty)
            throw new InvalidDataException($"Object {data.Id} has an empty spawn ID.");
        if (data.WorldEntity is { } worldEntity)
        {
            if (documentVersion < 7
                && (worldEntity.TemplateId is not null
                    || worldEntity.TemplateOverrides != PlacementTemplateOverrideFlags.None))
                throw new InvalidDataException($"Object {data.Id} placement template data requires scene version 7.");
            try
            {
                _ = new WorldEntityPlacementComponent(worldEntity.Kind, worldEntity.DefinitionId!,
                    worldEntity.InstanceId, worldEntity.TemplateId, worldEntity.TemplateOverrides);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Object {data.Id} has an invalid world entity placement: {exception.Message}", exception);
            }
        }
        if (data.Door is { } door)
        {
            if (door.DestinationCellId == Guid.Empty || door.DestinationSpawnId == Guid.Empty)
                throw new InvalidDataException($"Object {data.Id} has an empty door destination cell or spawn ID.");
            if (door.Facing is null || door.Facing.Length != 4
                || door.Facing.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException($"Object {data.Id} door facing must contain four finite values.");
            var facingLength = MathF.Sqrt(door.Facing.Sum(value => value * value));
            if (facingLength < 0.000001f)
                throw new InvalidDataException($"Object {data.Id} has a zero-length door facing.");
        }

        if (data.Position.Any(value => !float.IsFinite(value))
            || data.Rotation.Any(value => !float.IsFinite(value))
            || data.Scale.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"Object {data.Id} contains a nonfinite transform value.");

        var rotationLength = MathF.Sqrt(data.Rotation.Sum(value => value * value));
        if (rotationLength < 0.000001f)
            throw new InvalidDataException($"Object {data.Id} has a zero-length rotation.");

        if (data.Character is { } character)
        {
            if (documentVersion < 2)
                throw new InvalidDataException($"Object {data.Id} character settings require scene version 2.");
            if (!data.GltfAssetId.HasValue)
                throw new InvalidDataException($"Object {data.Id} has character settings but no GLB asset reference.");
            if (character.ClipName is not null && string.IsNullOrWhiteSpace(character.ClipName))
                throw new InvalidDataException($"Object {data.Id} has an empty animation clip name.");
            if (character.CrossfadeClipName is not null
                && (string.IsNullOrWhiteSpace(character.CrossfadeClipName) || character.ClipName is null))
                throw new InvalidDataException($"Object {data.Id} has invalid crossfade clip settings.");
            if (!float.IsFinite(character.Time) || character.Time < 0f
                || !float.IsFinite(character.Speed)
                || !float.IsFinite(character.BlendAmount) || character.BlendAmount < 0f || character.BlendAmount > 1f)
                throw new InvalidDataException($"Object {data.Id} has invalid character playback values.");
            if (character.IsPlaying && character.ClipName is null)
                throw new InvalidDataException($"Object {data.Id} cannot play without an animation clip.");
            if (character.Attachments is null)
                throw new InvalidDataException($"Object {data.Id} has no attachment list.");
            foreach (var attachment in character.Attachments)
            {
                if (attachment is null || attachment.Id == Guid.Empty || string.IsNullOrWhiteSpace(attachment.BoneName))
                    throw new InvalidDataException($"Object {data.Id} contains an invalid attachment reference.");
                if (attachment.LocalOffset is null || attachment.LocalOffset.Length != 16
                    || attachment.LocalOffset.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException($"Object {data.Id} attachment {attachment.Id} has an invalid local offset matrix.");
            }
        }
    }

    private static void ValidateAttachmentIds(IEnumerable<SceneObjectData> objects)
    {
        var ids = new HashSet<Guid>();
        foreach (var data in objects)
        foreach (var attachment in data.Character?.Attachments ?? [])
        {
            if (!ids.Add(attachment.Id))
                throw new InvalidDataException($"Duplicate character attachment ID: {attachment.Id}.");
        }
    }

    private static void ValidateWorldEntityInstanceIds(IEnumerable<SceneObjectData> objects)
    {
        var ids = new HashSet<Guid>();
        foreach (var data in objects)
        {
            if (data.WorldEntity is not { } placement) continue;
            if (!ids.Add(placement.InstanceId))
                throw new InvalidDataException($"Duplicate world entity instance ID: {placement.InstanceId}.");
        }
    }

    private static float[] ToArray(Matrix value) =>
    [
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44
    ];

    private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];

    private static float[] ToArray(Quaternion value) => [value.X, value.Y, value.Z, value.W];

    private static Matrix ToMatrix(float[] values) => new(
        values[0], values[1], values[2], values[3],
        values[4], values[5], values[6], values[7],
        values[8], values[9], values[10], values[11],
        values[12], values[13], values[14], values[15]);

    private static void ValidateAcyclic(IReadOnlyList<SceneObjectData> objects)
    {
        var parents = objects.ToDictionary(value => value.Id, value => value.ParentId);
        foreach (var item in objects)
        {
            var visited = new HashSet<Guid>();
            var cursor = item.ParentId;
            while (cursor is not null)
            {
                if (!visited.Add(cursor.Value))
                    throw new InvalidDataException($"Object {item.Id} is part of a hierarchy cycle.");
                if (!parents.TryGetValue(cursor.Value, out cursor))
                    throw new InvalidDataException($"Object {item.Id} refers to missing parent.");
            }
        }
    }

    private sealed class SceneDocument
    {
        public int Version { get; set; }
        public List<SceneObjectData>? Objects { get; set; }
    }

    private sealed class SceneObjectData
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public bool Enabled { get; set; } = true;
        public Guid? ParentId { get; set; }
        public Guid? GltfAssetId { get; set; }
        public string? GltfAssetPath { get; set; }
        public SceneMeshLodData? StaticMeshLod { get; set; }
        public SceneCharacterData? Character { get; set; }
        public SceneDoorData? Door { get; set; }
        public SceneSpawnData? SpawnPoint { get; set; }
        public SceneWorldEntityData? WorldEntity { get; set; }
        public SceneTemplateInstanceData? TemplateInstance { get; set; }
        public WorldInstanceResetPolicy ResetPolicy { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
    }

    private sealed class SceneMeshLodData
    {
        public Guid? NearAssetId { get; set; }
        public string? NearAssetPath { get; set; }
        public Guid? FarAssetId { get; set; }
        public string? FarAssetPath { get; set; }
        public float EnterFarDistance { get; set; }
        public float ExitFarDistance { get; set; }
    }

    private sealed class SceneCharacterData
    {
        public string? ClipName { get; set; }
        public float Time { get; set; }
        public float Speed { get; set; } = 1f;
        public bool Loop { get; set; } = true;
        public bool IsPlaying { get; set; }
        public string? CrossfadeClipName { get; set; }
        public float BlendAmount { get; set; } = 0.5f;
        public List<SceneAttachmentData>? Attachments { get; set; } = new();
    }

    private sealed class SceneAttachmentData
    {
        public Guid Id { get; set; }
        public string? BoneName { get; set; }
        public float[]? LocalOffset { get; set; }
    }

    private sealed class SceneDoorData
    {
        public Guid DestinationCellId { get; set; }
        public Guid DestinationSpawnId { get; set; }
        public float[]? Facing { get; set; }
    }

    private sealed class SceneSpawnData
    {
        public Guid Id { get; set; }
    }

    private sealed class SceneWorldEntityData
    {
        public WorldEntityKind Kind { get; set; }
        public string? DefinitionId { get; set; }
        public Guid InstanceId { get; set; }
        public Guid? TemplateId { get; set; }
        public PlacementTemplateOverrideFlags TemplateOverrides { get; set; }
    }

    private sealed class SceneTemplateInstanceData
    {
        public Guid TemplateId { get; set; }
        public int AppliedRevision { get; set; }
        public Guid SourceRootObjectId { get; set; }
        public Guid InstanceRootObjectId { get; set; }
        public List<SceneTemplateMappingData>? ObjectMappings { get; set; }
        public List<SceneTemplateObjectBaselineData>? ObjectBaselines { get; set; }
        public List<Guid>? OrphanedObjectIds { get; set; }
        public Guid? TargetWorldCellId { get; set; }
    }

    private sealed class SceneTemplateMappingData
    {
        public Guid SourceObjectId { get; set; }
        public Guid InstanceObjectId { get; set; }
    }

    private sealed class SceneTemplateObjectBaselineData
    {
        public Guid SourceObjectId { get; set; }
        public string? Name { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
        public bool? Enabled { get; set; }
        public WorldInstanceResetPolicy? ResetPolicy { get; set; }
        public bool? HasGltfAssetBaseline { get; set; }
        public Guid? GltfAssetId { get; set; }
        public string? GltfAssetPath { get; set; }
        public bool? HasStaticMeshLodBaseline { get; set; }
        public SceneMeshLodData? StaticMeshLod { get; set; }
        public bool? HasCharacterSettingsBaseline { get; set; }
        public SceneTemplateCharacterSettingsBaselineData? CharacterSettingsBaseline { get; set; }
        public bool? HasDoorBaseline { get; set; }
        public SceneDoorData? Door { get; set; }
        public bool? HasSpawnPointBaseline { get; set; }
        public SceneSpawnData? SpawnPoint { get; set; }
        public Guid? SourceSpawnPointId { get; set; }
        public bool? HasWorldEntityBaseline { get; set; }
        public SceneWorldEntityData? WorldEntity { get; set; }
    }

    private sealed class SceneTemplateCharacterSettingsBaselineData
    {
        public string? ClipName { get; set; }
        public float? Time { get; set; }
        public float? Speed { get; set; }
        public bool? Loop { get; set; }
        public bool? IsPlaying { get; set; }
        public string? CrossfadeClipName { get; set; }
        public float? BlendAmount { get; set; }
        public int? AttachmentCount { get; set; }
        public bool? HasAttachmentMappings { get; set; }
        public List<SceneTemplateAttachmentBaselineData>? AttachmentBaselines { get; set; }
    }

    private sealed class SceneTemplateAttachmentBaselineData
    {
        public Guid SourceAttachmentId { get; set; }
        public Guid InstanceAttachmentId { get; set; }
        public string? BoneName { get; set; }
        public float[]? LocalOffset { get; set; }
    }
}

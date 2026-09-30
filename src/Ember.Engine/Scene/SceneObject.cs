using System;
using System.Collections.Generic;
using System.Linq;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>A stable scene reference to one GLB file, stored as project-relative metadata.</summary>
public sealed class GltfAssetReference
{
    public GltfAssetReference(Guid assetId, string sourcePath)
    {
        if (assetId == Guid.Empty) throw new ArgumentException("Asset ID cannot be empty.", nameof(assetId));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("GLB source path is required.", nameof(sourcePath));

        var normalizedPath = sourcePath.Trim().Replace('\\', '/');
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal)
            || (normalizedPath.Length >= 2 && char.IsLetter(normalizedPath[0]) && normalizedPath[1] == ':'))
            throw new ArgumentException("GLB source path must be relative to the project root.", nameof(sourcePath));

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == "..")
                throw new ArgumentException("GLB source path cannot move above the project root.", nameof(sourcePath));
            if (segment != ".") safeSegments.Add(segment);
        }

        if (safeSegments.Count == 0)
            throw new ArgumentException("GLB source path is required.", nameof(sourcePath));

        AssetId = assetId;
        SourcePath = string.Join('/', safeSegments);
        if (!SourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .glb asset paths are supported by this reference.", nameof(sourcePath));
    }

    public Guid AssetId { get; }
    public string SourcePath { get; }
}

/// <summary>Two authored static GLB representations selected by camera distance.</summary>
public sealed class GltfStaticMeshLod
{
    public GltfStaticMeshLod(GltfAssetReference nearAsset, GltfAssetReference farAsset,
        float enterFarDistance, float exitFarDistance)
    {
        ArgumentNullException.ThrowIfNull(nearAsset);
        ArgumentNullException.ThrowIfNull(farAsset);
        if (nearAsset.AssetId == farAsset.AssetId)
            throw new ArgumentException("Near and far LODs must reference different GLB assets.", nameof(farAsset));
        if (!float.IsFinite(enterFarDistance) || enterFarDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(enterFarDistance), "Far LOD entry distance must be finite and positive.");
        if (!float.IsFinite(exitFarDistance) || exitFarDistance < 0f || exitFarDistance >= enterFarDistance)
            throw new ArgumentOutOfRangeException(nameof(exitFarDistance), "Far LOD exit distance must be finite, nonnegative, and less than its entry distance.");

        NearAsset = nearAsset;
        FarAsset = farAsset;
        EnterFarDistance = enterFarDistance;
        ExitFarDistance = exitFarDistance;
    }

    public GltfAssetReference NearAsset { get; }
    public GltfAssetReference FarAsset { get; }
    public float EnterFarDistance { get; }
    public float ExitFarDistance { get; }
    public float HysteresisDistance => EnterFarDistance - ExitFarDistance;

    /// <summary>Maintains the current representation inside the configured hysteresis band.</summary>
    public bool SelectFar(bool currentlyFar, float cameraDistance)
    {
        if (!float.IsFinite(cameraDistance) || cameraDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(cameraDistance), "Camera distance must be finite and nonnegative.");
        return currentlyFar
            ? cameraDistance > ExitFarDistance
            : cameraDistance >= EnterFarDistance;
    }
}

/// <summary>A local-space box collider authored on a scene object.</summary>
public sealed class SceneBoxColliderComponent
{
    public SceneBoxColliderComponent(Vector3 center, Vector3 size, bool isTrigger = false)
    {
        if (!IsFinite(center))
            throw new ArgumentOutOfRangeException(nameof(center), "Collider center must be finite.");
        if (!IsFinite(size) || size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
            throw new ArgumentOutOfRangeException(nameof(size), "Collider size must be finite and positive on every axis.");

        Center = center;
        Size = size;
        IsTrigger = isTrigger;
    }

    public Vector3 Center { get; }
    public Vector3 Size { get; }
    public bool IsTrigger { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

/// <summary>Simple built-in actions that a scene trigger can perform when entered.</summary>
public enum SceneTriggerActionKind
{
    Collect = 0,
    ReachGoal = 1,
    Open = 2
}

/// <summary>A validated, persisted action run when this object's trigger is entered.</summary>
public sealed class SceneTriggerActionComponent
{
    public SceneTriggerActionComponent(SceneTriggerActionKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Trigger action is not supported.");
        Kind = kind;
    }

    public SceneTriggerActionKind Kind { get; }
}

/// <summary>A stable scene object identity and its local authored state.</summary>
public sealed class SceneObject
{
    public SceneObject(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scene object name is required.", nameof(name));

        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public Transform Transform { get; set; } = new();
    public Guid? ParentId { get; internal set; }
    public GltfAssetReference? GltfAsset { get; set; }
    public GltfStaticMeshLod? StaticMeshLod { get; set; }
    public GltfCharacterSettings? CharacterSettings { get; set; }
    public SceneBoxColliderComponent? BoxCollider { get; set; }
    public SceneTriggerActionComponent? TriggerAction { get; set; }
    public WorldDoorComponent? Door { get; set; }
    public WorldSpawnComponent? SpawnPoint { get; set; }
    public WorldEntityPlacementComponent? WorldEntity { get; set; }
    public SceneTemplateInstanceComponent? TemplateInstance { get; set; }
    public WorldInstanceResetPolicy ResetPolicy { get; set; } = WorldInstanceResetPolicy.Preserve;
}

/// <summary>Maps a stable template source object ID to its placed scene-object ID.</summary>
public readonly record struct SceneTemplateObjectMapping(Guid SourceObjectId, Guid InstanceObjectId);

/// <summary>Source defaults used to detect authored field overrides on an instance.</summary>
public sealed class SceneTemplateObjectBaseline
{
    public SceneTemplateObjectBaseline(Guid sourceObjectId, string name, Transform transform,
        bool? enabled = null, WorldInstanceResetPolicy? resetPolicy = null,
        GltfAssetReference? gltfAsset = null, bool hasGltfAssetBaseline = false,
        GltfStaticMeshLod? staticMeshLod = null, bool hasStaticMeshLodBaseline = false,
        SceneTemplateCharacterSettingsBaseline? characterSettingsBaseline = null,
        bool hasCharacterSettingsBaseline = false,
        WorldDoorComponent? door = null, bool hasDoorBaseline = false,
        WorldSpawnComponent? spawnPoint = null, bool hasSpawnPointBaseline = false,
        WorldEntityPlacementComponent? worldEntity = null, bool hasWorldEntityBaseline = false,
        Guid? sourceSpawnPointId = null,
        SceneBoxColliderComponent? boxCollider = null, bool hasBoxColliderBaseline = false,
        SceneTriggerActionComponent? triggerAction = null, bool hasTriggerActionBaseline = false)
    {
        if (sourceObjectId == Guid.Empty) throw new ArgumentException("Template source object ID cannot be empty.", nameof(sourceObjectId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Template source object name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(transform);
        if (!IsFinite(transform.Position) || !IsFinite(transform.Rotation) || !IsFinite(transform.Scale))
            throw new ArgumentException("Template source object transform must be finite.", nameof(transform));
        if (transform.Rotation.LengthSquared() < 1e-12f)
            throw new ArgumentException("Template source object rotation must be nonzero.", nameof(transform));
        if (resetPolicy is { } policy && !Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(resetPolicy), "Template source reset policy is unknown.");
        if (!hasGltfAssetBaseline && gltfAsset is not null)
            throw new ArgumentException("A GLB reference requires an available source baseline.", nameof(gltfAsset));
        if (!hasStaticMeshLodBaseline && staticMeshLod is not null)
            throw new ArgumentException("Static-mesh LOD requires an available source baseline.", nameof(staticMeshLod));
        if (!hasCharacterSettingsBaseline && characterSettingsBaseline is not null)
            throw new ArgumentException("Character settings require an available source baseline.", nameof(characterSettingsBaseline));
        if (!hasDoorBaseline && door is not null)
            throw new ArgumentException("A door component requires an available source baseline.", nameof(door));
        if (!hasSpawnPointBaseline && spawnPoint is not null)
            throw new ArgumentException("A spawn component requires an available source baseline.", nameof(spawnPoint));
        if (sourceSpawnPointId == Guid.Empty)
            throw new ArgumentException("Template source spawn ID cannot be empty.", nameof(sourceSpawnPointId));
        if (hasSpawnPointBaseline && ((spawnPoint is null) != (sourceSpawnPointId is null)))
            throw new ArgumentException("Template source and instance spawn baselines must be saved together.", nameof(sourceSpawnPointId));
        if (!hasSpawnPointBaseline && sourceSpawnPointId is not null)
            throw new ArgumentException("A source spawn ID requires an available spawn baseline.", nameof(sourceSpawnPointId));
        if (!hasWorldEntityBaseline && worldEntity is not null)
            throw new ArgumentException("A world entity requires an available source baseline.", nameof(worldEntity));
        if (!hasBoxColliderBaseline && boxCollider is not null)
            throw new ArgumentException("A box collider requires an available source baseline.", nameof(boxCollider));
        if (!hasTriggerActionBaseline && triggerAction is not null)
            throw new ArgumentException("A trigger action requires an available source baseline.", nameof(triggerAction));

        SourceObjectId = sourceObjectId;
        Name = name;
        Position = transform.Position;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
        Enabled = enabled;
        ResetPolicy = resetPolicy;
        GltfAsset = gltfAsset;
        HasGltfAssetBaseline = hasGltfAssetBaseline;
        StaticMeshLod = staticMeshLod;
        HasStaticMeshLodBaseline = hasStaticMeshLodBaseline;
        CharacterSettingsBaseline = characterSettingsBaseline;
        HasCharacterSettingsBaseline = hasCharacterSettingsBaseline;
        Door = door;
        HasDoorBaseline = hasDoorBaseline;
        SpawnPoint = spawnPoint;
        HasSpawnPointBaseline = hasSpawnPointBaseline;
        SourceSpawnPointId = sourceSpawnPointId;
        WorldEntity = worldEntity;
        HasWorldEntityBaseline = hasWorldEntityBaseline;
        BoxCollider = boxCollider;
        HasBoxColliderBaseline = hasBoxColliderBaseline;
        TriggerAction = triggerAction;
        HasTriggerActionBaseline = hasTriggerActionBaseline;
    }

    public Guid SourceObjectId { get; }
    public string Name { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public Vector3 Scale { get; }
    public bool? Enabled { get; }
    public WorldInstanceResetPolicy? ResetPolicy { get; }
    public GltfAssetReference? GltfAsset { get; }
    public bool HasGltfAssetBaseline { get; }
    public GltfStaticMeshLod? StaticMeshLod { get; }
    public bool HasStaticMeshLodBaseline { get; }
    public SceneTemplateCharacterSettingsBaseline? CharacterSettingsBaseline { get; }
    public bool HasCharacterSettingsBaseline { get; }
    public WorldDoorComponent? Door { get; }
    public bool HasDoorBaseline { get; }
    public WorldSpawnComponent? SpawnPoint { get; }
    public bool HasSpawnPointBaseline { get; }
    public Guid? SourceSpawnPointId { get; }
    public WorldEntityPlacementComponent? WorldEntity { get; }
    public bool HasWorldEntityBaseline { get; }
    public SceneBoxColliderComponent? BoxCollider { get; }
    public bool HasBoxColliderBaseline { get; }
    public SceneTriggerActionComponent? TriggerAction { get; }
    public bool HasTriggerActionBaseline { get; }

    public Transform ToTransform() => new()
    {
        Position = Position,
        Rotation = Rotation,
        Scale = Scale
    };

    public bool MatchesTransform(Transform value) =>
        Position == value.Position && Rotation == value.Rotation && Scale == value.Scale;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>A source attachment and its stable remapped ID in an expanded template instance.</summary>
public sealed class SceneTemplateAttachmentBaseline
{
    public SceneTemplateAttachmentBaseline(Guid sourceAttachmentId, Guid instanceAttachmentId,
        string boneName, Matrix localOffset)
    {
        if (sourceAttachmentId == Guid.Empty)
            throw new ArgumentException("Template source attachment ID cannot be empty.", nameof(sourceAttachmentId));
        if (instanceAttachmentId == Guid.Empty)
            throw new ArgumentException("Template instance attachment ID cannot be empty.", nameof(instanceAttachmentId));
        _ = new GltfBoneAttachmentReference(instanceAttachmentId, boneName, localOffset);
        SourceAttachmentId = sourceAttachmentId;
        InstanceAttachmentId = instanceAttachmentId;
        BoneName = boneName;
        LocalOffset = localOffset;
    }

    public Guid SourceAttachmentId { get; }
    public Guid InstanceAttachmentId { get; }
    public string BoneName { get; }
    public Matrix LocalOffset { get; }
}

/// <summary>Playback and attachment defaults used to detect character overrides on a template instance.</summary>
public sealed class SceneTemplateCharacterSettingsBaseline
{
    public SceneTemplateCharacterSettingsBaseline(string? clipName, float time, float speed,
        bool loop, bool isPlaying, string? crossfadeClipName, float blendAmount, int attachmentCount,
        IEnumerable<SceneTemplateAttachmentBaseline>? attachmentBaselines = null,
        bool hasAttachmentMappings = false)
    {
        if (!float.IsFinite(time) || !float.IsFinite(speed) || !float.IsFinite(blendAmount))
            throw new ArgumentException("Character playback baselines must be finite.");
        if (attachmentCount < 0) throw new ArgumentOutOfRangeException(nameof(attachmentCount));
        var attachments = (attachmentBaselines ?? Array.Empty<SceneTemplateAttachmentBaseline>()).ToArray();
        if (attachments.Any(attachment => attachment is null))
            throw new ArgumentException("Character attachment baselines cannot contain null entries.", nameof(attachmentBaselines));
        if (hasAttachmentMappings && attachments.Length != attachmentCount)
            throw new ArgumentException("Character attachment mappings must match the source attachment count.", nameof(attachmentBaselines));
        if (!hasAttachmentMappings && attachments.Length != 0)
            throw new ArgumentException("Character attachment mappings require a known mapping marker.", nameof(attachmentBaselines));
        if (attachments.Select(attachment => attachment.SourceAttachmentId).Distinct().Count() != attachments.Length)
            throw new ArgumentException("Character source attachment IDs must be unique.", nameof(attachmentBaselines));
        if (attachments.Select(attachment => attachment.InstanceAttachmentId).Distinct().Count() != attachments.Length)
            throw new ArgumentException("Character instance attachment IDs must be unique.", nameof(attachmentBaselines));
        ClipName = clipName;
        Time = time;
        Speed = speed;
        Loop = loop;
        IsPlaying = isPlaying;
        CrossfadeClipName = crossfadeClipName;
        BlendAmount = blendAmount;
        AttachmentCount = attachmentCount;
        AttachmentBaselines = Array.AsReadOnly(attachments);
        HasAttachmentMappings = hasAttachmentMappings;
    }

    public string? ClipName { get; }
    public float Time { get; }
    public float Speed { get; }
    public bool Loop { get; }
    public bool IsPlaying { get; }
    public string? CrossfadeClipName { get; }
    public float BlendAmount { get; }
    public int AttachmentCount { get; }
    public IReadOnlyList<SceneTemplateAttachmentBaseline> AttachmentBaselines { get; }
    public bool HasAttachmentMappings { get; }
}

/// <summary>Persistent source identity for an expanded scene-template instance hierarchy.</summary>
public sealed class SceneTemplateInstanceComponent
{
    public SceneTemplateInstanceComponent(Guid templateId, int appliedRevision,
        Guid sourceRootObjectId, Guid instanceRootObjectId,
        IEnumerable<SceneTemplateObjectMapping> objectMappings,
        IEnumerable<SceneTemplateObjectBaseline>? objectBaselines = null,
        IEnumerable<Guid>? orphanedObjectIds = null,
        Guid? targetWorldCellId = null)
    {
        if (templateId == Guid.Empty) throw new ArgumentException("Template ID cannot be empty.", nameof(templateId));
        if (appliedRevision < 1) throw new ArgumentOutOfRangeException(nameof(appliedRevision));
        if (sourceRootObjectId == Guid.Empty)
            throw new ArgumentException("Template root source ID cannot be empty.", nameof(sourceRootObjectId));
        if (instanceRootObjectId == Guid.Empty)
            throw new ArgumentException("Template root instance ID cannot be empty.", nameof(instanceRootObjectId));
        if (targetWorldCellId == Guid.Empty)
            throw new ArgumentException("Target world-cell ID cannot be empty.", nameof(targetWorldCellId));
        ArgumentNullException.ThrowIfNull(objectMappings);

        var mappings = objectMappings.ToArray();
        if (mappings.Length == 0)
            throw new ArgumentException("A template instance must retain its source-object mapping.", nameof(objectMappings));
        if (mappings.Any(mapping => mapping.SourceObjectId == Guid.Empty || mapping.InstanceObjectId == Guid.Empty))
            throw new ArgumentException("Template object mappings cannot contain empty IDs.", nameof(objectMappings));
        if (mappings.Select(mapping => mapping.SourceObjectId).Distinct().Count() != mappings.Length)
            throw new ArgumentException("Template source object IDs must be unique.", nameof(objectMappings));
        if (mappings.Select(mapping => mapping.InstanceObjectId).Distinct().Count() != mappings.Length)
            throw new ArgumentException("Template instance object IDs must be unique.", nameof(objectMappings));
        if (!mappings.Contains(new SceneTemplateObjectMapping(sourceRootObjectId, instanceRootObjectId)))
            throw new ArgumentException("Template root mapping is missing.", nameof(objectMappings));

        var baselines = (objectBaselines ?? Array.Empty<SceneTemplateObjectBaseline>()).ToArray();
        if (baselines.Any(baseline => baseline is null))
            throw new ArgumentException("Template object baselines cannot contain null entries.", nameof(objectBaselines));
        if (baselines.Select(baseline => baseline.SourceObjectId).Distinct().Count() != baselines.Length)
            throw new ArgumentException("Template object baseline source IDs must be unique.", nameof(objectBaselines));
        if (baselines.Length > 0
            && !baselines.Select(baseline => baseline.SourceObjectId).ToHashSet()
                .SetEquals(mappings.Select(mapping => mapping.SourceObjectId)))
            throw new ArgumentException("Template object baselines must match the source-object mapping.", nameof(objectBaselines));
        var orphans = (orphanedObjectIds ?? Array.Empty<Guid>()).ToArray();
        if (orphans.Any(id => id == Guid.Empty))
            throw new ArgumentException("Orphaned template instance object IDs cannot be empty.", nameof(orphanedObjectIds));
        if (orphans.Distinct().Count() != orphans.Length)
            throw new ArgumentException("Orphaned template instance object IDs must be unique.", nameof(orphanedObjectIds));
        if (orphans.Contains(instanceRootObjectId)
            || orphans.Intersect(mappings.Select(mapping => mapping.InstanceObjectId)).Any())
            throw new ArgumentException("Orphaned template objects cannot overlap mapped objects.", nameof(orphanedObjectIds));
        TemplateId = templateId;
        AppliedRevision = appliedRevision;
        SourceRootObjectId = sourceRootObjectId;
        InstanceRootObjectId = instanceRootObjectId;
        ObjectMappings = Array.AsReadOnly(mappings);
        ObjectBaselines = Array.AsReadOnly(baselines);
        OrphanedObjectIds = Array.AsReadOnly(orphans);
        TargetWorldCellId = targetWorldCellId;
    }

    public Guid TemplateId { get; }
    public int AppliedRevision { get; }
    public Guid SourceRootObjectId { get; }
    public Guid InstanceRootObjectId { get; }
    public IReadOnlyList<SceneTemplateObjectMapping> ObjectMappings { get; }
    public IReadOnlyList<SceneTemplateObjectBaseline> ObjectBaselines { get; }
    public IReadOnlyList<Guid> OrphanedObjectIds { get; }
    public Guid? TargetWorldCellId { get; }
}

/// <summary>Per-instance skeletal clip, playback, and attachment settings saved with a scene object.</summary>
public sealed class GltfCharacterSettings
{
    public string? ClipName { get; set; }
    public float Time { get; set; }
    public float Speed { get; set; } = 1f;
    public bool Loop { get; set; } = true;
    public bool IsPlaying { get; set; }
    public string? CrossfadeClipName { get; set; }
    public float BlendAmount { get; set; } = 0.5f;
    public List<GltfBoneAttachmentReference> Attachments { get; } = new();

    /// <summary>Creates an independent copy, including the mutable attachment list.</summary>
    public GltfCharacterSettings DeepCopy()
    {
        var copy = new GltfCharacterSettings
        {
            ClipName = ClipName,
            Time = Time,
            Speed = Speed,
            Loop = Loop,
            IsPlaying = IsPlaying,
            CrossfadeClipName = CrossfadeClipName,
            BlendAmount = BlendAmount
        };
        foreach (var attachment in Attachments)
            copy.Attachments.Add(new GltfBoneAttachmentReference(
                attachment.Id, attachment.BoneName, attachment.LocalOffset));
        return copy;
    }
}

/// <summary>Stable scene reference to a character joint and a prop's joint-local transform.</summary>
public sealed class GltfBoneAttachmentReference
{
    public GltfBoneAttachmentReference(Guid id, string boneName, Matrix localOffset)
    {
        if (id == Guid.Empty) throw new ArgumentException("Attachment ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(boneName)) throw new ArgumentException("Bone name is required.", nameof(boneName));
        if (!IsFinite(localOffset)) throw new ArgumentException("Attachment offset must be finite.", nameof(localOffset));

        Id = id;
        BoneName = boneName;
        LocalOffset = localOffset;
    }

    public Guid Id { get; }
    public string BoneName { get; }
    public Matrix LocalOffset { get; }

    private static bool IsFinite(Matrix matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}

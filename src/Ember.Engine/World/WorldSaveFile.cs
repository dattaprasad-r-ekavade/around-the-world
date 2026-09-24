using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.World;

public readonly record struct WorldPlayerLocation
{
    public WorldPlayerLocation(Guid cellId, Vector3 position, Quaternion facing)
    {
        var facingLengthSquared = facing.LengthSquared();
        if (cellId == Guid.Empty)
            throw new ArgumentException("Player cell ID cannot be empty.", nameof(cellId));
        if (!IsFinite(position))
            throw new ArgumentException("Player position must be finite.", nameof(position));
        if (!IsFinite(facing) || !float.IsFinite(facingLengthSquared) || facingLengthSquared < 1e-8f)
            throw new ArgumentException("Player facing must be finite and nonzero.", nameof(facing));

        CellId = cellId;
        Position = position;
        Facing = Quaternion.Normalize(facing);
    }

    public Guid CellId { get; }
    public Vector3 Position { get; }
    public Quaternion Facing { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>A validated in-memory snapshot of persistent player, identity, change, and runtime-object state.</summary>
public sealed class WorldSaveSnapshot
{
    public WorldSaveSnapshot(WorldPlayerLocation playerLocation,
        IEnumerable<WorldInstanceIdentityEntry> identities,
        IEnumerable<WorldCellChangeEntry> changes,
        IEnumerable<WorldRuntimeObjectEntry> runtimeObjects)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(runtimeObjects);
        PlayerLocation = new WorldPlayerLocation(playerLocation.CellId, playerLocation.Position, playerLocation.Facing);
        Identities = identities.ToArray();
        Changes = changes.ToArray();
        RuntimeObjects = runtimeObjects
            .Select(entry =>
            {
                ArgumentNullException.ThrowIfNull(entry);
                ArgumentNullException.ThrowIfNull(entry.SceneObject);
                return entry with { SceneObject = SceneObjectCopy.Copy(entry.SceneObject) };
            })
            .ToArray();
        Validate();
    }

    public WorldPlayerLocation PlayerLocation { get; }
    public IReadOnlyList<WorldInstanceIdentityEntry> Identities { get; }
    public IReadOnlyList<WorldCellChangeEntry> Changes { get; }
    public IReadOnlyList<WorldRuntimeObjectEntry> RuntimeObjects { get; }

    public static WorldSaveSnapshot Capture(WorldPlayerLocation playerLocation,
        WorldInstanceIdentityMap identities, WorldCellChangeStore changes, WorldRuntimeObjectStore runtimeObjects)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(runtimeObjects);
        return new WorldSaveSnapshot(playerLocation, identities.ExportSnapshot(),
            changes.ExportSnapshot(), runtimeObjects.ExportSnapshot());
    }

    public void Restore(WorldInstanceIdentityMap identities, WorldCellChangeStore changes,
        WorldRuntimeObjectStore runtimeObjects)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(runtimeObjects);
        if (identities.Count != 0 || changes.CellCount != 0 || runtimeObjects.Count != 0)
            throw new InvalidOperationException("A world save can only be restored into empty world-state stores.");

        identities.ImportSnapshot(Identities);
        changes.ImportSnapshot(Changes);
        runtimeObjects.ImportSnapshot(RuntimeObjects);
    }

    private void Validate()
    {
        var sources = new HashSet<(Guid CellId, Guid SceneObjectId)>();
        var instanceIds = new HashSet<Guid>();
        var cellByInstanceId = new Dictionary<Guid, Guid>();
        foreach (var entry in Identities)
        {
            if (entry.CellId == Guid.Empty || entry.SceneObjectId == Guid.Empty || entry.InstanceId.Value == Guid.Empty)
                throw new InvalidDataException("World save contains an empty instance identity field.");
            if (!sources.Add((entry.CellId, entry.SceneObjectId)))
                throw new InvalidDataException($"World save contains duplicate scene identity {entry.SceneObjectId} in cell {entry.CellId}.");
            if (!instanceIds.Add(entry.InstanceId.Value))
                throw new InvalidDataException($"World save reuses world instance ID {entry.InstanceId.Value}.");
            cellByInstanceId.Add(entry.InstanceId.Value, entry.CellId);
        }

        var identityBySource = Identities.ToDictionary(
            entry => (entry.CellId, entry.SceneObjectId), entry => entry.InstanceId);
        var changeKeys = new HashSet<(Guid CellId, WorldInstanceId InstanceId)>();
        foreach (var entry in Changes)
        {
            if (entry.CellId == Guid.Empty || entry.InstanceId.Value == Guid.Empty)
                throw new InvalidDataException("World save contains an empty cell-change identity.");
            if (!changeKeys.Add((entry.CellId, entry.InstanceId)))
                throw new InvalidDataException($"World save contains duplicate changes for instance {entry.InstanceId.Value}.");
            if (entry.Transform is { } transform)
            {
                try { transform.Validate(); }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"World save transform for instance {entry.InstanceId.Value} is invalid.", exception);
                }
            }
            if (entry.Transform is null && entry.Enabled is null && !entry.Deleted)
                throw new InvalidDataException($"World save change for instance {entry.InstanceId.Value} is empty.");
            if (!cellByInstanceId.TryGetValue(entry.InstanceId.Value, out var owningCell)
                || owningCell != entry.CellId)
                throw new InvalidDataException($"World save change refers to unknown instance {entry.InstanceId.Value}.");
        }

        var runtimeKeys = new HashSet<(Guid CellId, WorldInstanceId InstanceId)>();
        foreach (var entry in RuntimeObjects)
        {
            if (entry.CellId == Guid.Empty || entry.SceneObjectId == Guid.Empty || entry.InstanceId.Value == Guid.Empty)
                throw new InvalidDataException("World save contains an empty runtime-object identity field.");
            if (entry.SceneObject is null || entry.SceneObject.Id != entry.SceneObjectId)
                throw new InvalidDataException($"World save runtime object {entry.SceneObjectId} has inconsistent scene data.");
            if (!runtimeKeys.Add((entry.CellId, entry.InstanceId)))
                throw new InvalidDataException($"World save contains duplicate runtime object {entry.InstanceId.Value} in cell {entry.CellId}.");
            if (!identityBySource.TryGetValue((entry.CellId, entry.SceneObjectId), out var identity)
                || identity != entry.InstanceId)
                throw new InvalidDataException($"World save runtime object {entry.SceneObjectId} has no matching world-instance mapping.");
            try
            {
                var validationScene = new SceneGraph();
                validationScene.Add(SceneObjectCopy.Copy(entry.SceneObject));
                _ = SceneFile.SerializeToJson(validationScene);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                throw new InvalidDataException($"World save runtime object {entry.SceneObjectId} is invalid: {exception.Message}", exception);
            }
        }
    }
}

/// <summary>Versioned, atomically replaced persistence for world state.</summary>
public static class WorldSaveFile
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void SaveAtomic(string path, WorldSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A world-save path is required.", nameof(path));

        var document = ToDocument(snapshot);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("World-save path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static WorldSaveSnapshot Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A world-save path is required.", nameof(path));
        WorldSaveDocument document;
        try
        {
            document = JsonSerializer.Deserialize<WorldSaveDocument>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("World-save JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"World-save JSON is invalid: {exception.Message}", exception);
        }

        if (document.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported world-save version {document.Version}; expected {CurrentVersion}.");
        if (document.PlayerLocation is null)
            throw new InvalidDataException("World save has no player location.");
        if (document.InstanceIdentities is null || document.CellChanges is null || document.RuntimeObjects is null)
            throw new InvalidDataException("World save is missing an identity, change, or runtime-object list.");

        try
        {
            var player = new WorldPlayerLocation(document.PlayerLocation.CellId,
                ToVector3(document.PlayerLocation.Position, "player position"),
                ToQuaternion(document.PlayerLocation.Facing, "player facing"));
            var identities = document.InstanceIdentities.Select(entry => entry is null
                ? throw new InvalidDataException("World save contains a null identity entry.")
                : new WorldInstanceIdentityEntry(entry.CellId, entry.SceneObjectId, new WorldInstanceId(entry.InstanceId))).ToArray();
            var changes = document.CellChanges.Select(entry =>
            {
                if (entry is null) throw new InvalidDataException("World save contains a null cell-change entry.");
                var hasAnyTransform = entry.Position is not null || entry.Rotation is not null || entry.Scale is not null;
                WorldTransformState? transform = null;
                if (hasAnyTransform)
                {
                    if (entry.Position is null || entry.Rotation is null || entry.Scale is null)
                        throw new InvalidDataException($"World save change {entry.InstanceId} has an incomplete transform.");
                    transform = new WorldTransformState(ToVector3(entry.Position, "change position"),
                        ToQuaternion(entry.Rotation, "change rotation"), ToVector3(entry.Scale, "change scale"));
                }
                return new WorldCellChangeEntry(entry.CellId, new WorldInstanceId(entry.InstanceId),
                    transform, entry.Enabled, entry.Deleted);
            }).ToArray();
            var runtimeObjects = document.RuntimeObjects.Select(entry =>
            {
                if (entry is null) throw new InvalidDataException("World save contains a null runtime-object entry.");
                var scene = SceneFile.LoadFromJson(entry.SceneJson
                    ?? throw new InvalidDataException($"Runtime object {entry.SceneObjectId} has no scene data."));
                if (scene.Objects.Count != 1)
                    throw new InvalidDataException($"Runtime object {entry.SceneObjectId} must contain exactly one scene object.");
                var sceneObject = scene.Find(entry.SceneObjectId)
                    ?? throw new InvalidDataException($"Runtime object scene does not contain object {entry.SceneObjectId}.");
                return new WorldRuntimeObjectEntry(entry.CellId, entry.SceneObjectId,
                    new WorldInstanceId(entry.InstanceId), sceneObject);
            }).ToArray();

            return new WorldSaveSnapshot(player, identities, changes, runtimeObjects);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidDataException($"World-save data is invalid: {exception.Message}", exception);
        }
    }

    private static WorldSaveDocument ToDocument(WorldSaveSnapshot snapshot) => new()
    {
        Version = CurrentVersion,
        PlayerLocation = new PlayerLocationDocument
        {
            CellId = snapshot.PlayerLocation.CellId,
            Position = [snapshot.PlayerLocation.Position.X, snapshot.PlayerLocation.Position.Y, snapshot.PlayerLocation.Position.Z],
            Facing = [snapshot.PlayerLocation.Facing.X, snapshot.PlayerLocation.Facing.Y,
                snapshot.PlayerLocation.Facing.Z, snapshot.PlayerLocation.Facing.W]
        },
        InstanceIdentities = snapshot.Identities.Select(entry => new IdentityDocument
        {
            CellId = entry.CellId,
            SceneObjectId = entry.SceneObjectId,
            InstanceId = entry.InstanceId.Value
        }).ToList(),
        CellChanges = snapshot.Changes.Select(entry => new CellChangeDocument
        {
            CellId = entry.CellId,
            InstanceId = entry.InstanceId.Value,
            Position = entry.Transform is { } transform ? [transform.Position.X, transform.Position.Y, transform.Position.Z] : null,
            Rotation = entry.Transform is { } transformRotation ? [transformRotation.Rotation.X, transformRotation.Rotation.Y,
                transformRotation.Rotation.Z, transformRotation.Rotation.W] : null,
            Scale = entry.Transform is { } transformScale ? [transformScale.Scale.X, transformScale.Scale.Y, transformScale.Scale.Z] : null,
            Enabled = entry.Enabled,
            Deleted = entry.Deleted
        }).ToList(),
        RuntimeObjects = snapshot.RuntimeObjects.Select(entry =>
        {
            var scene = new SceneGraph();
            scene.Add(SceneObjectCopy.Copy(entry.SceneObject));
            return new RuntimeObjectDocument
            {
                CellId = entry.CellId,
                SceneObjectId = entry.SceneObjectId,
                InstanceId = entry.InstanceId.Value,
                SceneJson = SceneFile.SerializeToJson(scene)
            };
        }).ToList()
    };

    private static Vector3 ToVector3(float[]? values, string label)
    {
        if (values is null || values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"World save {label} must contain three finite values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ToQuaternion(float[]? values, string label)
    {
        if (values is null || values.Length != 4 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException($"World save {label} must contain four finite values.");
        var rotation = new Quaternion(values[0], values[1], values[2], values[3]);
        if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < 1e-8f)
            throw new InvalidDataException($"World save {label} must be nonzero.");
        return Quaternion.Normalize(rotation);
    }

    private sealed class WorldSaveDocument
    {
        public int Version { get; set; }
        public PlayerLocationDocument? PlayerLocation { get; set; }
        public List<IdentityDocument>? InstanceIdentities { get; set; }
        public List<CellChangeDocument>? CellChanges { get; set; }
        public List<RuntimeObjectDocument>? RuntimeObjects { get; set; }
    }

    private sealed class PlayerLocationDocument
    {
        public Guid CellId { get; set; }
        public float[]? Position { get; set; }
        public float[]? Facing { get; set; }
    }

    private sealed class IdentityDocument
    {
        public Guid CellId { get; set; }
        public Guid SceneObjectId { get; set; }
        public Guid InstanceId { get; set; }
    }

    private sealed class CellChangeDocument
    {
        public Guid CellId { get; set; }
        public Guid InstanceId { get; set; }
        public float[]? Position { get; set; }
        public float[]? Rotation { get; set; }
        public float[]? Scale { get; set; }
        public bool? Enabled { get; set; }
        public bool Deleted { get; set; }
    }

    private sealed class RuntimeObjectDocument
    {
        public Guid CellId { get; set; }
        public Guid SceneObjectId { get; set; }
        public Guid InstanceId { get; set; }
        public string? SceneJson { get; set; }
    }
}

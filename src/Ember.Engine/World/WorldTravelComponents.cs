using Ember.Scene;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ember.World;

/// <summary>A door's stable destination cell, spawn point, and facing after travel.</summary>
public sealed class WorldDoorComponent
{
    public WorldDoorComponent(Guid destinationCellId, Guid destinationSpawnId, Quaternion facing)
    {
        if (destinationCellId == Guid.Empty)
            throw new ArgumentException("Destination cell ID cannot be empty.", nameof(destinationCellId));
        if (destinationSpawnId == Guid.Empty)
            throw new ArgumentException("Destination spawn ID cannot be empty.", nameof(destinationSpawnId));
        if (!IsFinite(facing) || facing.LengthSquared() < 1e-8f)
            throw new ArgumentException("Door facing must be a finite, nonzero quaternion.", nameof(facing));

        DestinationCellId = destinationCellId;
        DestinationSpawnId = destinationSpawnId;
        Facing = Quaternion.Normalize(facing);
    }

    public Guid DestinationCellId { get; }
    public Guid DestinationSpawnId { get; }
    public Quaternion Facing { get; }

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>Stable spawn identifier attached to a scene object; its transform supplies the position.</summary>
public sealed class WorldSpawnComponent(Guid id)
{
    public Guid Id { get; } = id != Guid.Empty
        ? id
        : throw new ArgumentException("Spawn ID cannot be empty.", nameof(id));
}

/// <summary>A resolved travel location derived from an authored spawn marker.</summary>
public readonly record struct WorldSpawnLocation(
    Guid CellId,
    Guid SpawnId,
    Vector3 Position,
    Quaternion Facing);

/// <summary>Validates doors against authored cells and spawn markers in the loaded scene set.</summary>
public static class WorldTravelValidator
{
    public static WorldSpawnLocation ResolveDestination(WorldManifest world,
        IReadOnlyDictionary<Guid, SceneGraph> scenes, WorldDoorComponent door)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(door);
        if (world.FindCell(door.DestinationCellId) is null)
            throw new InvalidDataException($"Door targets unknown cell {door.DestinationCellId}.");
        if (!scenes.TryGetValue(door.DestinationCellId, out var targetScene))
            throw new InvalidDataException($"Door destination cell {door.DestinationCellId} has no loaded scene.");

        foreach (var sceneObject in targetScene.Objects)
        {
            if (sceneObject.SpawnPoint?.Id != door.DestinationSpawnId) continue;
            var worldMatrix = targetScene.GetWorldMatrix(sceneObject.Id);
            return new WorldSpawnLocation(
                door.DestinationCellId,
                door.DestinationSpawnId,
                new Vector3(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43),
                door.Facing);
        }

        throw new InvalidDataException(
            $"Door targets missing spawn {door.DestinationSpawnId} in cell {door.DestinationCellId}.");
    }

    public static void Validate(WorldManifest world, IReadOnlyDictionary<Guid, SceneGraph> scenes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(scenes);

        foreach (var (cellId, scene) in scenes)
        {
            if (scene is null)
                throw new InvalidDataException($"Loaded scene for world cell {cellId} is null.");
            if (world.FindCell(cellId) is null)
                throw new InvalidDataException($"Loaded scenes include unknown world cell {cellId}.");

            var spawnIds = new HashSet<Guid>();
            foreach (var sceneObject in scene.Objects)
            {
                if (sceneObject.SpawnPoint is { } spawn && !spawnIds.Add(spawn.Id))
                    throw new InvalidDataException($"World cell {cellId} has duplicate spawn ID {spawn.Id}.");
            }
        }

        foreach (var (sourceCellId, scene) in scenes)
        foreach (var sceneObject in scene.Objects)
        {
            if (sceneObject.Door is not { } door) continue;
            try { _ = ResolveDestination(world, scenes, door); }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Door object '{sceneObject.Name}' in cell {sourceCellId} is invalid: {exception.Message}", exception);
            }
        }
    }
}

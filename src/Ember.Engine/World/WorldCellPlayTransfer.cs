using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.World;

/// <summary>A prepared Play-scene copy after carrying the player through a linked world door.</summary>
public sealed record WorldCellPlayTransfer(
    Guid DestinationCellId,
    Guid PlayerObjectId,
    WorldSpawnLocation Spawn,
    SceneGraph DestinationScene);

/// <summary>Builds a destination scene for synchronous, single-player world-cell travel.</summary>
public static class WorldCellPlayTransferFactory
{
    /// <summary>
    /// Resolves the door spawn, copies the destination runtime scene, removes its previous player,
    /// and inserts a fresh copy of the current player at the linked position and facing.
    /// </summary>
    public static WorldCellPlayTransfer Prepare(WorldManifest world,
        IDictionary<Guid, SceneGraph> cachedCellScenes, Guid? currentCellId,
        SceneGraph sourceRuntimeScene, Guid sourcePlayerId, WorldDoorComponent door)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cachedCellScenes);
        ArgumentNullException.ThrowIfNull(sourceRuntimeScene);
        ArgumentNullException.ThrowIfNull(door);
        if (sourcePlayerId == Guid.Empty)
            throw new ArgumentException("Player object ID cannot be empty.", nameof(sourcePlayerId));
        if (currentCellId == Guid.Empty)
            throw new ArgumentException("Current world cell ID cannot be empty.", nameof(currentCellId));

        var sourcePlayer = sourceRuntimeScene.Find(sourcePlayerId)
            ?? throw new InvalidDataException($"Play character {sourcePlayerId} is missing from the current scene.");
        var sourcePlayerWorld = sourceRuntimeScene.GetWorldMatrix(sourcePlayerId);
        if (!sourcePlayerWorld.Decompose(out var sourcePlayerScale, out _, out _))
            throw new InvalidDataException(
                $"Play character '{sourcePlayer.Name}' has a transform that cannot be transferred between cells.");

        if (currentCellId is { } activeCellId)
            cachedCellScenes[activeCellId] = sourceRuntimeScene;

        if (!cachedCellScenes.TryGetValue(door.DestinationCellId, out var authoredDestinationScene))
        {
            authoredDestinationScene = SceneFile.Load(world.ResolveScenePath(door.DestinationCellId));
            cachedCellScenes.Add(door.DestinationCellId, authoredDestinationScene);
        }

        var spawn = WorldTravelValidator.ResolveDestination(world,
            new Dictionary<Guid, SceneGraph> { [door.DestinationCellId] = authoredDestinationScene }, door);
        var destinationScene = SceneGraphCloner.Clone(authoredDestinationScene);
        if (currentCellId == spawn.CellId && destinationScene.Find(sourcePlayerId) is not null)
            RemoveHierarchy(destinationScene, sourcePlayerId);
        if (destinationScene.PlaySettings.PlayerObjectId is { } previousPlayerId
            && destinationScene.Find(previousPlayerId) is not null)
            RemoveHierarchy(destinationScene, previousPlayerId);

        var playerHierarchy = CopyPlayerHierarchy(sourceRuntimeScene, destinationScene, sourcePlayer);
        var playerCopy = playerHierarchy[0];
        playerCopy.Door = null;
        playerCopy.SpawnPoint = null;
        playerCopy.WorldEntity = null;
        playerCopy.TriggerAction = null;
        playerCopy.TemplateInstance = null;
        playerCopy.Transform = new Transform
        {
            Position = spawn.Position,
            Rotation = spawn.Facing,
            Scale = sourcePlayerScale
        };
        destinationScene.AddUnparented(playerCopy);
        foreach (var child in playerHierarchy.Skip(1))
            destinationScene.Add(child);
        destinationScene.PlaySettings = sourceRuntimeScene.PlaySettings
            with { PlayerObjectId = playerCopy.Id };

        return new WorldCellPlayTransfer(spawn.CellId, playerCopy.Id, spawn, destinationScene);
    }

    private static List<SceneObject> CopyPlayerHierarchy(SceneGraph sourceScene,
        SceneGraph destinationScene, SceneObject sourcePlayer)
    {
        var childrenByParent = sourceScene.Objects.Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var sources = new List<SceneObject> { sourcePlayer };
        var visited = new HashSet<Guid> { sourcePlayer.Id };
        for (var index = 0; index < sources.Count; index++)
        {
            if (!childrenByParent.TryGetValue(sources[index].Id, out var children)) continue;
            foreach (var child in children)
                if (visited.Add(child.Id)) sources.Add(child);
        }

        var objectIds = new Dictionary<Guid, Guid>(sources.Count);
        var usedObjectIds = destinationScene.Objects.Select(item => item.Id).ToHashSet();
        foreach (var source in sources)
        {
            var id = Guid.NewGuid();
            while (!usedObjectIds.Add(id)) id = Guid.NewGuid();
            objectIds.Add(source.Id, id);
        }

        var usedNames = destinationScene.Objects.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedAttachmentIds = destinationScene.Objects
            .Where(item => item.CharacterSettings is not null)
            .SelectMany(item => item.CharacterSettings!.Attachments)
            .Select(item => item.Id).ToHashSet();
        var copies = new List<SceneObject>(sources.Count);
        foreach (var source in sources)
        {
            var copy = SceneObjectCopy.Copy(source, objectIds[source.Id],
                SceneObjectCopy.UniqueName(source.Name, usedNames));
            usedNames.Add(copy.Name);
            copy.TemplateInstance = null;
            copy.ParentId = source.Id == sourcePlayer.Id
                ? null
                : objectIds[source.ParentId!.Value];
            if (copy.CharacterSettings is { } settings)
            {
                for (var index = 0; index < settings.Attachments.Count; index++)
                {
                    var attachment = settings.Attachments[index];
                    var attachmentId = Guid.NewGuid();
                    while (!usedAttachmentIds.Add(attachmentId)) attachmentId = Guid.NewGuid();
                    settings.Attachments[index] = new GltfBoneAttachmentReference(
                        attachmentId, attachment.BoneName, attachment.LocalOffset);
                }
            }
            copies.Add(copy);
        }
        return copies;
    }

    private static void RemoveHierarchy(SceneGraph scene, Guid rootId)
    {
        var hierarchy = new List<Guid> { rootId };
        var visited = new HashSet<Guid> { rootId };
        for (var index = 0; index < hierarchy.Count; index++)
        {
            foreach (var child in scene.Objects.Where(item => item.ParentId == hierarchy[index]))
                if (visited.Add(child.Id)) hierarchy.Add(child.Id);
        }

        for (var index = hierarchy.Count - 1; index >= 0; index--)
            scene.Remove(hierarchy[index]);
    }
}

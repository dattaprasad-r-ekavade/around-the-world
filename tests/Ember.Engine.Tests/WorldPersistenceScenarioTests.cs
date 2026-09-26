using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldPersistenceScenarioTests
{
    [Fact]
    public void RepeatablePersistenceScenario_ExecutesAndRestoresAllStatesExactlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-persistence-scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellAId = Guid.NewGuid();
            var cellBId = Guid.NewGuid();
            var interiorId = Guid.NewGuid();

            var sceneAPath = Path.Combine(directory, "cell_a.json");
            var sceneBPath = Path.Combine(directory, "cell_b.json");
            var interiorPath = Path.Combine(directory, "interior.json");
            var manifestPath = Path.Combine(directory, "world.json");
            var worldSavePath = Path.Combine(directory, "world-save.json");
            var rpgSavePath = Path.Combine(directory, "rpg-save.json");

            var interiorSpawnId = Guid.NewGuid();

            // Setup authored scene A (exterior 0,0)
            var chestObjectId = Guid.NewGuid();
            var enemyObjectId = Guid.NewGuid();
            var doorObjectId = Guid.NewGuid();

            var authoredSceneA = new SceneGraph();
            authoredSceneA.Add(new SceneObject(chestObjectId, "Treasure Chest")
            {
                Transform = new Transform { Position = new Vector3(5f, 0.5f, 5f) }
            });
            authoredSceneA.Add(new SceneObject(enemyObjectId, "Bandit Raider")
            {
                Transform = new Transform { Position = new Vector3(20f, 1f, 20f) }
            });
            authoredSceneA.Add(new SceneObject(doorObjectId, "Door to House A")
            {
                Transform = new Transform { Position = new Vector3(8f, 1f, 8f) },
                Door = new WorldDoorComponent(interiorId, interiorSpawnId, Quaternion.Identity)
            });
            SceneFile.SaveAtomic(authoredSceneA, sceneAPath);

            // Setup authored scene B (exterior 1,0)
            var markerBId = Guid.NewGuid();
            var authoredSceneB = new SceneGraph();
            authoredSceneB.Add(new SceneObject(markerBId, "Road Marker B")
            {
                Transform = new Transform { Position = new Vector3(10f, 0.5f, 10f) }
            });
            SceneFile.SaveAtomic(authoredSceneB, sceneBPath);

            // Setup authored interior scene
            var authoredInterior = new SceneGraph();
            authoredInterior.Add(new SceneObject(Guid.NewGuid(), "Interior Spawn")
            {
                Transform = new Transform { Position = new Vector3(4f, 1f, 6f) },
                SpawnPoint = new WorldSpawnComponent(interiorSpawnId)
            });
            SceneFile.SaveAtomic(authoredInterior, interiorPath);

            // World manifest
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = cellAId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "cell_a.json"
                },
                new WorldCellDefinition
                {
                    Id = cellBId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(1, 0),
                    ScenePath = "cell_b.json"
                },
                new WorldCellDefinition
                {
                    Id = interiorId,
                    Kind = WorldCellKind.Interior,
                    ScenePath = "interior.json"
                }
            ]);

            var world = WorldManifest.Load(manifestPath);

            // RPG item definitions
            var appleId = new ContentId<ItemContentKind>("item.rpgslice.apple");
            var potionId = new ContentId<ItemContentKind>("item.rpgslice.potion");
            var enemyActorId = new ContentId<ActorContentKind>("actor.rpgslice.raider");
            var appleDef = new ItemDef(appleId, "Apple", null, stackable: true);
            var potionDef = new ItemDef(potionId, "Health Potion", null, stackable: true);
            var itemCatalogue = new ItemCatalogue();
            itemCatalogue.Add(appleDef);
            itemCatalogue.Add(potionDef);

            // Starting RPG state
            var playerBag = new Bag();
            playerBag.Add(appleDef, 3);
            var containers = new ContainerInventoryStore();
            var worldItems = new WorldItemStore();
            var actorStates = new ActorRuntimeStore();

            // Start live world persistence session
            var session = new WorldPersistenceSession(world);

            var sceneA = SceneFile.Load(sceneAPath);
            var identitiesA = session.PrepareCell(cellAId, sceneA);
            var chestInstanceId = identitiesA[chestObjectId];
            var enemyInstanceId = identitiesA[enemyObjectId];

            var sceneB = SceneFile.Load(sceneBPath);
            session.PrepareCell(cellBId, sceneB);

            // Container authored initial contents: 2 potions
            var chestBag = new Bag();
            chestBag.Add(potionDef, 2);
            containers = containers.SetContents(chestInstanceId.Value, chestBag);

            // Enemy initial state: 50 HP, alive
            actorStates = actorStates.Set(new ActorRuntimeState(enemyInstanceId.Value, enemyActorId, 50));

            // Spawn follower in Cell A
            var followerObject = new SceneObject(Guid.NewGuid(), "Follower Companion")
            {
                Transform = new Transform { Position = new Vector3(2f, 1f, 3f) }
            };
            var follower = session.Spawn(cellAId, sceneA, followerObject);
            Assert.NotNull(sceneA.Find(follower.SceneObjectId));

            // -------------------------------------------------------------
            // Action 1: Drop item
            // -------------------------------------------------------------
            var dropObject = new SceneObject(Guid.NewGuid(), "Dropped Apple")
            {
                Transform = new Transform { Position = new Vector3(3f, 0.5f, 4f) }
            };
            var droppedItemIdentity = session.Spawn(cellAId, sceneA, dropObject);
            Assert.True(InventoryTransfer.TryDrop(droppedItemIdentity.InstanceId.Value, playerBag, worldItems,
                itemCatalogue, appleId, 1, out playerBag, out worldItems));
            Assert.Equal(2, playerBag.Count(appleId));
            Assert.True(worldItems.TryGet(droppedItemIdentity.InstanceId.Value, out var droppedStack));
            Assert.Equal(1, droppedStack.Count);

            // -------------------------------------------------------------
            // Action 2: Loot container
            // -------------------------------------------------------------
            var chestContents = containers.GetContents(chestInstanceId.Value)!;
            Assert.Equal(2, chestContents.Count(potionId));
            Assert.True(InventoryTransfer.TryMove(chestContents, playerBag, itemCatalogue,
                potionId, 2, out var updatedChestBag, out playerBag));
            containers = containers.SetContents(chestInstanceId.Value, updatedChestBag);
            Assert.Equal(0, containers.GetContents(chestInstanceId.Value)!.Count(potionId));
            Assert.Equal(2, playerBag.Count(potionId));

            // -------------------------------------------------------------
            // Action 3: Kill enemy
            // -------------------------------------------------------------
            actorStates = actorStates.Set(new ActorRuntimeState(enemyInstanceId.Value, enemyActorId, 0));
            session.SetEnabled(cellAId, sceneA.Find(enemyObjectId)!, false);

            // -------------------------------------------------------------
            // Action 4: Move follower across cells (Cell A -> Cell B)
            // -------------------------------------------------------------
            var followerDestinationTransform = new Transform { Position = new Vector3(15f, 1f, 12f) };
            var movedFollower = session.Transfer(cellAId, cellBId, follower.InstanceId,
                sceneA, sceneB, followerDestinationTransform);
            Assert.Equal(follower.InstanceId, movedFollower.InstanceId);
            Assert.Null(sceneA.Find(follower.SceneObjectId));
            Assert.NotNull(sceneB.Find(follower.SceneObjectId));

            // -------------------------------------------------------------
            // Action 5: Enter interior
            // -------------------------------------------------------------
            var interiorPlayerLocation = new WorldPlayerLocation(interiorId,
                new Vector3(4f, 1f, 6f), Quaternion.Identity);

            // -------------------------------------------------------------
            // Action 6: Save and Restart
            // -------------------------------------------------------------
            // Save World
            session.RequestSave(worldSavePath, () => interiorPlayerLocation);
            Assert.True(session.ProcessStableBoundary(travelInProgress: false));
            Assert.True(session.TryDequeueSaveResult(out var saveResult));
            Assert.Null(saveResult.Failure);

            // Save RPG
            var rpgSave = new SaveState
            {
                Player = new PlayerRecord { Bag = playerBag },
                ContainerInventories = containers,
                WorldItems = worldItems,
                ActorStates = actorStates,
                ItemDefs = itemCatalogue
            };
            File.WriteAllText(rpgSavePath, rpgSave.ToJson());

            // -------------------------------------------------------------
            // Verify: Reload / Restart session
            // -------------------------------------------------------------
            var restoredWorldSave = WorldSaveFile.Load(worldSavePath, world);
            Assert.Equal(interiorPlayerLocation, restoredWorldSave.PlayerLocation);

            var restartedSession = new WorldPersistenceSession(world, restoredWorldSave);
            var restoredRpgSave = SaveState.FromJson(File.ReadAllText(rpgSavePath));

            // Reload all cell scenes
            var reloadedSceneA = SceneFile.Load(sceneAPath);
            var reloadedIdentitiesA = restartedSession.PrepareCell(cellAId, reloadedSceneA);

            var reloadedSceneB = SceneFile.Load(sceneBPath);
            var reloadedIdentitiesB = restartedSession.PrepareCell(cellBId, reloadedSceneB);

            var reloadedInterior = SceneFile.Load(interiorPath);
            var reloadedIdentitiesC = restartedSession.PrepareCell(interiorId, reloadedInterior);

            // Verify Action 1: Dropped item
            Assert.NotNull(reloadedSceneA.Find(droppedItemIdentity.SceneObjectId));
            Assert.Null(reloadedSceneB.Find(droppedItemIdentity.SceneObjectId));
            Assert.Null(reloadedInterior.Find(droppedItemIdentity.SceneObjectId));
            Assert.True(restoredRpgSave.WorldItems.TryGet(droppedItemIdentity.InstanceId.Value, out var restoredDrop));
            Assert.Equal(1, restoredDrop.Count);
            Assert.Equal(appleId, restoredDrop.ItemId);
            Assert.Equal(2, restoredRpgSave.Player.Bag.Count(appleId));

            // Verify Action 2: Looted container
            var restoredChest = reloadedSceneA.Find(chestObjectId);
            Assert.NotNull(restoredChest);
            var restoredChestContents = restoredRpgSave.ContainerInventories.GetContents(chestInstanceId.Value);
            Assert.NotNull(restoredChestContents);
            Assert.Equal(0, restoredChestContents.Count(potionId));
            Assert.Equal(2, restoredRpgSave.Player.Bag.Count(potionId));

            // Verify Action 3: Killed enemy
            var restoredEnemy = reloadedSceneA.Find(enemyObjectId);
            Assert.NotNull(restoredEnemy);
            Assert.False(restoredEnemy.Enabled);
            Assert.True(restoredRpgSave.ActorStates.TryGet(enemyInstanceId.Value, out var restoredEnemyState));
            Assert.True(restoredEnemyState.IsDead);
            Assert.Equal(0, restoredEnemyState.CurrentHealth);

            // Verify Action 4: Moved follower across cells
            Assert.Null(reloadedSceneA.Find(follower.SceneObjectId));
            var restoredFollower = reloadedSceneB.Find(follower.SceneObjectId);
            Assert.NotNull(restoredFollower);
            Assert.Equal(followerDestinationTransform.Position, restoredFollower.Transform.Position);
            Assert.Null(reloadedInterior.Find(follower.SceneObjectId));

            // Verify Action 5: Enter interior
            Assert.Equal(interiorId, restartedSession.RestoredPlayerLocation?.CellId);
            Assert.Equal(new Vector3(4f, 1f, 6f), restartedSession.RestoredPlayerLocation?.Position);

            // -------------------------------------------------------------
            // Verify: Exactly-once instance assertion across all cells
            // -------------------------------------------------------------
            var allInstances = new List<WorldInstanceId>();

            // Collect all instance IDs from cell A
            foreach (var obj in reloadedSceneA.Objects)
            {
                if (reloadedIdentitiesA.TryGetValue(obj.Id, out var instanceId))
                    allInstances.Add(instanceId);
            }
            // Collect all instance IDs from cell B
            foreach (var obj in reloadedSceneB.Objects)
            {
                if (reloadedIdentitiesB.TryGetValue(obj.Id, out var instanceId))
                    allInstances.Add(instanceId);
            }
            // Collect all instance IDs from interior
            foreach (var obj in reloadedInterior.Objects)
            {
                if (reloadedIdentitiesC.TryGetValue(obj.Id, out var instanceId))
                    allInstances.Add(instanceId);
            }

            // Assert every instance ID in the world is unique (no duplicates)
            var uniqueInstances = allInstances.ToHashSet();
            Assert.Equal(allInstances.Count, uniqueInstances.Count);

            // Follower exists exactly once
            Assert.Contains(follower.InstanceId, uniqueInstances);
            Assert.Equal(1, allInstances.Count(id => id == follower.InstanceId));
            Assert.True(reloadedIdentitiesB.ContainsKey(follower.SceneObjectId));
            Assert.False(reloadedIdentitiesA.ContainsKey(follower.SceneObjectId));

            // Dropped item exists exactly once
            Assert.Contains(droppedItemIdentity.InstanceId, uniqueInstances);
            Assert.Equal(1, allInstances.Count(id => id == droppedItemIdentity.InstanceId));
            Assert.True(reloadedIdentitiesA.ContainsKey(droppedItemIdentity.SceneObjectId));
            Assert.False(reloadedIdentitiesB.ContainsKey(droppedItemIdentity.SceneObjectId));

            // Runtime store in restarted session contains exactly 2 runtime objects (follower and dropped item)
            var exportedRuntime = restartedSession.RuntimeObjects.ExportSnapshot();
            Assert.Equal(2, exportedRuntime.Count);
            Assert.Single(exportedRuntime, r => r.CellId == cellBId && r.InstanceId == follower.InstanceId);
            Assert.Single(exportedRuntime, r => r.CellId == cellAId && r.InstanceId == droppedItemIdentity.InstanceId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

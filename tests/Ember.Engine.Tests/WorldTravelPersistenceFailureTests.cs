using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldTravelPersistenceFailureTests
{
    private sealed class PreparedTestCell(Guid cellId, SceneGraph scene) : IDisposable, ICellActivationCost
    {
        public Guid CellId { get; } = cellId;
        public SceneGraph Scene { get; } = scene;
        public bool IsDisposed { get; private set; }
        public long EstimatedActivationCost => 1;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class ActiveTestCell(Guid cellId, SceneGraph scene) : IDisposable
    {
        public Guid CellId { get; } = cellId;
        public SceneGraph Scene { get; } = scene;
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    [Fact]
    public void RepeatedTravelSaveLoad_WithDelayedAndFailedReads_RecoversWithoutDuplicateActorsOrLostItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-travel-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellAId = Guid.NewGuid();
            var cellBId = Guid.NewGuid();
            var spawnAId = Guid.NewGuid();
            var spawnBId = Guid.NewGuid();

            var sceneAPath = Path.Combine(directory, "scene_a.json");
            var sceneBPath = Path.Combine(directory, "scene_b.json");
            var manifestPath = Path.Combine(directory, "world.json");
            var worldSavePath = Path.Combine(directory, "world-save.json");
            var rpgSavePath = Path.Combine(directory, "rpg-save.json");

            var followerObjectId = Guid.NewGuid();
            var enemyObjectId = Guid.NewGuid();
            var chestObjectId = Guid.NewGuid();

            // Scene A setup
            var authoredA = new SceneGraph();
            authoredA.Add(new SceneObject(followerObjectId, "Follower")
            {
                Transform = new Transform { Position = new Vector3(2f, 1f, 2f) }
            });
            authoredA.Add(new SceneObject(enemyObjectId, "Enemy")
            {
                Transform = new Transform { Position = new Vector3(8f, 1f, 8f) }
            });
            authoredA.Add(new SceneObject(chestObjectId, "Chest")
            {
                Transform = new Transform { Position = new Vector3(4f, 1f, 4f) }
            });
            authoredA.Add(new SceneObject(Guid.NewGuid(), "Door to B")
            {
                Transform = new Transform { Position = new Vector3(10f, 1f, 10f) },
                Door = new WorldDoorComponent(cellBId, spawnBId, Quaternion.Identity)
            });
            authoredA.Add(new SceneObject(Guid.NewGuid(), "Spawn A")
            {
                Transform = new Transform { Position = new Vector3(1f, 1f, 1f) },
                SpawnPoint = new WorldSpawnComponent(spawnAId)
            });
            SceneFile.SaveAtomic(authoredA, sceneAPath);

            // Scene B setup
            var authoredB = new SceneGraph();
            authoredB.Add(new SceneObject(Guid.NewGuid(), "Door to A")
            {
                Transform = new Transform { Position = new Vector3(15f, 1f, 15f) },
                Door = new WorldDoorComponent(cellAId, spawnAId, Quaternion.Identity)
            });
            authoredB.Add(new SceneObject(Guid.NewGuid(), "Spawn B")
            {
                Transform = new Transform { Position = new Vector3(5f, 1f, 5f) },
                SpawnPoint = new WorldSpawnComponent(spawnBId)
            });
            SceneFile.SaveAtomic(authoredB, sceneBPath);

            // World manifest
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition { Id = cellAId, Kind = WorldCellKind.Exterior, ExteriorCoordinate = new ExteriorCellCoordinate(0, 0), ScenePath = "scene_a.json" },
                new WorldCellDefinition { Id = cellBId, Kind = WorldCellKind.Interior, ScenePath = "scene_b.json" }
            ]);

            var world = WorldManifest.Load(manifestPath);

            // Setup RPG catalogue & items
            var potionId = new ContentId<ItemContentKind>("item.rpgslice.potion");
            var swordId = new ContentId<ItemContentKind>("item.rpgslice.sword");
            var enemyActorId = new ContentId<ActorContentKind>("actor.rpgslice.raider");

            var potionDef = new ItemDef(potionId, "Potion", null, stackable: true);
            var swordDef = new ItemDef(swordId, "Sword", null, stackable: false);
            var itemCatalogue = new ItemCatalogue();
            itemCatalogue.Add(potionDef);
            itemCatalogue.Add(swordDef);

            var playerBag = new Bag();
            playerBag.Add(swordDef, 1);
            playerBag.Add(potionDef, 5);

            var chestBag = new Bag();
            chestBag.Add(potionDef, 3);

            var currentCellId = cellAId;
            var currentScene = SceneFile.Load(sceneAPath);
            var session = new WorldPersistenceSession(world);
            var identitiesA = session.PrepareCell(cellAId, currentScene);
            var chestInstanceId = identitiesA[chestObjectId];
            var enemyInstanceId = identitiesA[enemyObjectId];
            var followerInstanceId = identitiesA[followerObjectId];

            var containers = new ContainerInventoryStore().SetContents(chestInstanceId.Value, chestBag);
            var actorStates = new ActorRuntimeStore().Set(new ActorRuntimeState(enemyInstanceId.Value, enemyActorId, 50));
            var worldItems = new WorldItemStore();

            // Run 5 repeated cycles of delayed travel, failed travel recovery, save, and reload
            for (var cycle = 0; cycle < 5; cycle++)
            {
                var destinationCellId = currentCellId == cellAId ? cellBId : cellAId;
                var destinationSpawnId = destinationCellId == cellAId ? spawnAId : spawnBId;
                var destinationScenePath = destinationCellId == cellAId ? sceneAPath : sceneBPath;
                var sourceScenePath = currentCellId == cellAId ? sceneAPath : sceneBPath;

                // -------------------------------------------------------------
                // 1. DELAYED ASSET READ DURING TRAVEL
                // -------------------------------------------------------------
                CreateActive(currentCellId, currentScene, out var sourceOp);

                var destinationOp = new WorldCellLoadOperation<PreparedTestCell, ActiveTestCell>();
                var delayGate = new TaskCompletionSource<PreparedTestCell>(TaskCreationOptions.RunContinuationsAsynchronously);

                var targetSpawn = new WorldSpawnLocation(destinationCellId, destinationSpawnId,
                    new Vector3(5f, 1f, 5f), Quaternion.Identity);
                WorldSpawnLocation? placedLocation = null;

                var delayedTravel = new WorldCellTravelTransaction<PreparedTestCell, ActiveTestCell>(
                    currentCellId, sourceOp, destinationOp, targetSpawn,
                    (_, _) => delayGate.Task,
                    prepared => new ActiveTestCell(prepared.CellId, prepared.Scene),
                    loc => placedLocation = loc);

                // Tick while delayed: must stay in PreparingDestination
                Assert.False(delayedTravel.Tick());
                Assert.Equal(WorldCellTravelState.PreparingDestination, delayedTravel.State);
                Assert.Equal(CellLifecycleState.Active, sourceOp.State);
                Assert.Null(placedLocation);

                // Save request during in-progress travel must NOT commit mid-transition
                session.RequestSave(worldSavePath, () => new WorldPlayerLocation(currentCellId, Vector3.Zero, Quaternion.Identity));
                Assert.False(session.ProcessStableBoundary(travelInProgress: true));

                // Complete delayed read
                var destScene = SceneFile.Load(destinationScenePath);
                session.PrepareCell(destinationCellId, destScene);
                var destPrepared = new PreparedTestCell(destinationCellId, destScene);
                delayGate.SetResult(destPrepared);

                while (delayedTravel.State != WorldCellTravelState.Completed)
                {
                    if (delayedTravel.Tick()) break;
                }

                Assert.Equal(WorldCellTravelState.Completed, delayedTravel.State);
                Assert.Equal(CellLifecycleState.Unloaded, sourceOp.State);
                Assert.Equal(CellLifecycleState.Active, destinationOp.State);
                Assert.Equal(targetSpawn, placedLocation);

                currentCellId = destinationCellId;
                currentScene = destScene;

                // Process save at stable boundary now that travel is finished
                Assert.True(session.ProcessStableBoundary(travelInProgress: false));
                Assert.True(session.TryDequeueSaveResult(out var saveResult));
                Assert.Null(saveResult.Failure);

                // -------------------------------------------------------------
                // 2. FAILED ASSET READ DURING TRAVEL (MUST BE RECOVERABLE)
                // -------------------------------------------------------------
                var returnCellId = currentCellId == cellAId ? cellBId : cellAId;
                var returnSpawnId = returnCellId == cellAId ? spawnAId : spawnBId;
                var returnTargetSpawn = new WorldSpawnLocation(returnCellId, returnSpawnId,
                    new Vector3(1f, 1f, 1f), Quaternion.Identity);

                var failSourceOp = destinationOp; // currently active
                var failDestOp = new WorldCellLoadOperation<PreparedTestCell, ActiveTestCell>();
                WorldSpawnLocation? failPlacedLocation = null;

                var failingTravel = new WorldCellTravelTransaction<PreparedTestCell, ActiveTestCell>(
                    currentCellId, failSourceOp, failDestOp, returnTargetSpawn,
                    (_, _) => Task.FromException<PreparedTestCell>(new IOException("Simulated corrupted asset read")),
                    prepared => new ActiveTestCell(prepared.CellId, prepared.Scene),
                    loc => failPlacedLocation = loc);

                WaitForPreparation(failingTravel.PreparationTask);
                // Advance: must fail cleanly
                failingTravel.Tick();
                Assert.Equal(WorldCellTravelState.Failed, failingTravel.State);
                Assert.Contains("Simulated corrupted asset read", failingTravel.Failure!.Message, StringComparison.Ordinal);
                // Source cell must remain Active and uncorrupted!
                Assert.Equal(CellLifecycleState.Active, failSourceOp.State);
                Assert.Null(failPlacedLocation);

                // RECOVER: Discard failed destination operation, retry with valid read
                failDestOp.Discard();
                var retryDestOp = new WorldCellLoadOperation<PreparedTestCell, ActiveTestCell>();
                var retryScene = SceneFile.Load(returnCellId == cellAId ? sceneAPath : sceneBPath);
                session.PrepareCell(returnCellId, retryScene);
                var retryPrepared = new PreparedTestCell(returnCellId, retryScene);

                WorldSpawnLocation? retryPlacedLocation = null;
                var retryTravel = new WorldCellTravelTransaction<PreparedTestCell, ActiveTestCell>(
                    currentCellId, failSourceOp, retryDestOp, returnTargetSpawn,
                    (_, _) => Task.FromResult(retryPrepared),
                    prepared => new ActiveTestCell(prepared.CellId, prepared.Scene),
                    loc => retryPlacedLocation = loc);

                while (retryTravel.State != WorldCellTravelState.Completed)
                {
                    if (retryTravel.Tick()) break;
                }

                Assert.Equal(WorldCellTravelState.Completed, retryTravel.State);
                Assert.Equal(CellLifecycleState.Active, retryDestOp.State);
                Assert.Equal(returnTargetSpawn, retryPlacedLocation);

                currentCellId = returnCellId;
                currentScene = retryScene;

                // -------------------------------------------------------------
                // 3. FAILED SAVE WRITE (ATOMIC PRESERVATION)
                // -------------------------------------------------------------
                // Attempt to write save to invalid directory (blocked by file): previous save must remain untouched
                var blockedParent = Path.Combine(directory, $"blocked-{cycle}");
                File.WriteAllText(blockedParent, "blocking file");
                var invalidSavePath = Path.Combine(blockedParent, "save.json");
                session.RequestSave(invalidSavePath, () => new WorldPlayerLocation(currentCellId, Vector3.Zero, Quaternion.Identity));
                session.ProcessStableBoundary(travelInProgress: false);
                Assert.True(session.TryDequeueSaveResult(out var failedWriteResult));
                Assert.NotNull(failedWriteResult.Failure);
                Assert.True(File.Exists(worldSavePath), "Valid previous save must be preserved!");

                // Valid save
                var validPlayerLocation = new WorldPlayerLocation(currentCellId, new Vector3(2f, 1f, 2f), Quaternion.Identity);
                session.RequestSave(worldSavePath, () => validPlayerLocation);
                session.ProcessStableBoundary(travelInProgress: false);
                Assert.True(session.TryDequeueSaveResult(out var validSaveResult));
                Assert.Null(validSaveResult.Failure);

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
                // 4. CORRUPTED SAVE LOAD (RECOVERABLE, PREVENTS PARTIAL RESTORATION)
                // -------------------------------------------------------------
                var corruptSavePath = Path.Combine(directory, "corrupt-save.json");
                File.WriteAllText(corruptSavePath, "{ \"invalid_json\": true, truncated ");
                Assert.Throws<InvalidDataException>(() => WorldSaveFile.Load(corruptSavePath, world));
                Assert.Throws<JsonException>(() => SaveState.FromJson("{ corrupt json"));

                // Load valid saves
                var restoredWorld = WorldSaveFile.Load(worldSavePath, world);
                var restoredRpg = SaveState.FromJson(File.ReadAllText(rpgSavePath));
                var restartedSession = new WorldPersistenceSession(world, restoredWorld);

                var reloadedA = SceneFile.Load(sceneAPath);
                var reloadedIdentitiesA = restartedSession.PrepareCell(cellAId, reloadedA);

                var reloadedB = SceneFile.Load(sceneBPath);
                var reloadedIdentitiesB = restartedSession.PrepareCell(cellBId, reloadedB);

                // -------------------------------------------------------------
                // 5. VERIFY: NO DUPLICATE ACTORS, NO LOST ITEMS, NO PARTIAL SCENES
                // -------------------------------------------------------------
                // No duplicate actors: Follower and Enemy exist exactly once
                var allFollowers = reloadedA.Objects.Count(o => o.Name == "Follower")
                                 + reloadedB.Objects.Count(o => o.Name == "Follower");
                Assert.Equal(1, allFollowers);

                var allEnemies = reloadedA.Objects.Count(o => o.Name == "Enemy")
                               + reloadedB.Objects.Count(o => o.Name == "Enemy");
                Assert.Equal(1, allEnemies);

                Assert.True(restoredRpg.ActorStates.TryGet(enemyInstanceId.Value, out var enemyState));
                Assert.Equal(50, enemyState.CurrentHealth);

                // No lost items
                Assert.Equal(1, restoredRpg.Player.Bag.Count(swordId));
                Assert.Equal(5, restoredRpg.Player.Bag.Count(potionId));
                var restoredChest = restoredRpg.ContainerInventories.GetContents(chestInstanceId.Value);
                Assert.NotNull(restoredChest);
                Assert.Equal(3, restoredChest.Count(potionId));

                // No duplicate identities across both cells
                var totalIdentities = reloadedIdentitiesA.Values.Concat(reloadedIdentitiesB.Values).ToList();
                Assert.Equal(totalIdentities.Count, totalIdentities.ToHashSet().Count);

                // Reset session for next iteration
                session = restartedSession;
                currentScene = currentCellId == cellAId ? reloadedA : reloadedB;
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RepeatedStreaming_WithDelayedAndFailedReads_RecoversWithoutOrphanedObjectsOrDuplicateIdentities()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-stream-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellId = Guid.NewGuid();
            var manifestPath = Path.Combine(directory, "world.json");
            var scenePath = Path.Combine(directory, "origin.json");

            var scene = new SceneGraph();
            var obj1Id = Guid.NewGuid();
            var obj2Id = Guid.NewGuid();
            scene.Add(new SceneObject(obj1Id, "Prop 1"));
            scene.Add(new SceneObject(obj2Id, "Prop 2"));
            SceneFile.SaveAtomic(scene, scenePath);

            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = cellId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "origin.json"
                }
            ]);
            var world = WorldManifest.Load(manifestPath);
            var session = new WorldPersistenceSession(world);

            var attempts = 0;
            var retries = 0;

            using var streamer = new WorldCellStreamer<PreparedTestCell, ActiveTestCell>(
                world,
                prepare: async (_, id, token) =>
                {
                    var count = Interlocked.Increment(ref attempts);
                    if (count == 1)
                    {
                        await Task.Delay(20, token).ConfigureAwait(false);
                        throw new IOException("Simulated transient disk error");
                    }
                    var loadedScene = SceneFile.Load(scenePath);
                    return new PreparedTestCell(id, loadedScene);
                },
                createStepper: _ => new TestStepper(prepared =>
                {
                    session.PrepareCell(prepared.CellId, prepared.Scene);
                    return new ActiveTestCell(prepared.CellId, prepared.Scene);
                }),
                options: new WorldCellStreamingOptions
                {
                    RetryBaseDelay = TimeSpan.FromMilliseconds(5),
                    RetryMaximumDelay = TimeSpan.FromMilliseconds(20),
                    MaximumStartupAttempts = 3
                });

            streamer.RetryScheduled += (_, _, _) => retries++;
            streamer.Start(Vector3.Zero);

            Assert.True(attempts >= 2);
            Assert.True(retries >= 1);
            Assert.Equal(1, streamer.ActiveCellCount);

            var active = Assert.Single(streamer.ActiveCells);
            Assert.NotNull(active.Scene.Find(obj1Id));
            Assert.NotNull(active.Scene.Find(obj2Id));
            Assert.True(session.Identities.TryGet(cellId, obj1Id, out var id1));
            Assert.True(session.Identities.TryGet(cellId, obj2Id, out var id2));
            Assert.NotEqual(id1, id2);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestStepper(Func<PreparedTestCell, ActiveTestCell> createActive)
        : ICellActivationStepper<PreparedTestCell, ActiveTestCell>
    {
        private long _remainingCost;

        public CellActivationStepResult<ActiveTestCell> Step(PreparedTestCell prepared, long maximumCost)
        {
            if (_remainingCost == 0) _remainingCost = prepared.EstimatedActivationCost;
            var consumed = Math.Min(1, Math.Min(_remainingCost, maximumCost));
            _remainingCost -= consumed;
            var complete = _remainingCost == 0;
            return new CellActivationStepResult<ActiveTestCell>(consumed, complete,
                complete ? createActive(prepared) : null);
        }

        public void Dispose() { }
    }

    private static ActiveTestCell CreateActive(Guid cellId, SceneGraph scene,
        out WorldCellLoadOperation<PreparedTestCell, ActiveTestCell> operation)
    {
        operation = new WorldCellLoadOperation<PreparedTestCell, ActiveTestCell>();
        var preparation = operation.PrepareAsync(_ => Task.FromResult(new PreparedTestCell(cellId, scene)));
        WaitForPreparation(preparation);
        operation.PumpCompletions();
        return operation.Activate(prepared => new ActiveTestCell(prepared.CellId, prepared.Scene));
    }

    private static void WaitForPreparation(Task preparation)
    {
        if (!SpinWait.SpinUntil(() => preparation.IsCompleted, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Cell preparation did not finish within five seconds.");
        if (preparation.IsFaulted)
            throw preparation.Exception!.GetBaseException();
        if (preparation.IsCanceled)
            throw new TaskCanceledException(preparation);
    }
}

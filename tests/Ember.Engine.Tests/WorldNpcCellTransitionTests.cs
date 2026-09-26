using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldNpcCellTransitionTests
{
    [Fact]
    public void RouteLoadsEachCellTransfersOneNpcAndKeepsItsOwnerAfterSaveReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ember-npc-travel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var westId = Guid.NewGuid();
            var eastId = Guid.NewGuid();
            var interiorId = Guid.NewGuid();
            var start = Node(0, 1, 0);
            var westExit = Node(2, 1, 0);
            var eastEntry = Node(10, 1, 0);
            var eastDoor = Node(12, 1, 0);
            var interiorDoor = Node(0, 1, 0);
            var destination = Node(2, 1, 0);
            var graphs = new[]
            {
                Graph(westId, WorldCellKind.Exterior, start, westExit, new CellPathEdge(start.Id, westExit.Id, 0.5f)),
                Graph(eastId, WorldCellKind.Exterior, eastEntry, eastDoor, new CellPathEdge(eastEntry.Id, eastDoor.Id, 0.5f)),
                Graph(interiorId, WorldCellKind.Interior, interiorDoor, destination,
                    new CellPathEdge(interiorDoor.Id, destination.Id, 0.5f))
            };
            var network = new WorldPathNetwork
            {
                Cells = graphs,
                Connections = new[]
                {
                    new WorldPathConnection
                    {
                        Id = Guid.NewGuid(),
                        From = new WorldPathNodeRef(westId, westExit.Id),
                        To = new WorldPathNodeRef(eastId, eastEntry.Id),
                        Kind = WorldPathConnectionKind.ExteriorBoundary,
                        ClearanceRadius = 0.5f
                    },
                    new WorldPathConnection
                    {
                        Id = Guid.NewGuid(),
                        From = new WorldPathNodeRef(eastId, eastDoor.Id),
                        To = new WorldPathNodeRef(interiorId, interiorDoor.Id),
                        Kind = WorldPathConnectionKind.Door,
                        DoorInstanceId = Guid.NewGuid(),
                        ClearanceRadius = 0.5f
                    }
                }
            };
            var route = WorldRouteSearch.FindShortestRoute(network,
                new WorldPathNodeRef(westId, start.Id), new WorldPathNodeRef(interiorId, destination.Id));
            Assert.NotNull(route);
            Assert.Equal(3, route.Legs.Count);

            var scenePaths = new Dictionary<Guid, string>();
            var identities = new WorldInstanceIdentityMap();
            var scenes = new Dictionary<Guid, SceneGraph>();
            foreach (var cellId in new[] { westId, eastId, interiorId })
            {
                var scenePath = Path.Combine(directory, $"{cellId:N}.json");
                SceneFile.SaveAtomic(new SceneGraph(), scenePath);
                scenePaths.Add(cellId, scenePath);
                var scene = SceneFile.Load(scenePath);
                scenes.Add(cellId, scene);
                identities.CreateCellIdentities(cellId, scene);
            }

            var runtimeObjects = new WorldRuntimeObjectStore();
            var npc = runtimeObjects.Spawn(westId, scenes[westId],
                new SceneObject(Guid.NewGuid(), "Follower") { Transform = new Transform { Position = new Vector3(0, 1, 0) } },
                identities);

            for (var legIndex = 0; legIndex < route!.Legs.Count - 1; legIndex++)
            {
                var leg = route.Legs[legIndex];
                var transition = Assert.IsType<WorldPathTransition>(leg.TransitionToNext);
                var targetRef = transition.To;
                var targetNode = graphs.Single(graph => graph.CellId == targetRef.CellId)
                    .Nodes.Single(node => node.Id == targetRef.NodeId);
                var destinationLoad = new WorldCellLoadOperation<PreparedCell, ActiveCell>();
                var travel = new WorldNpcCellTransition<PreparedCell, ActiveCell>(transition, npc.InstanceId,
                    runtimeObjects, identities, scenes[leg.CellId], destinationLoad,
                    (cellId, _) => Task.FromResult(new PreparedCell(cellId)),
                    prepared => new ActiveCell(prepared.CellId, SceneFile.Load(scenePaths[prepared.CellId])),
                    active => active.Scene,
                    new Transform { Position = new Vector3(targetNode.Position.X, targetNode.Position.Y, targetNode.Position.Z) });

                WaitForPreparation(travel.PreparationTask);
                Assert.False(travel.Tick());
                Assert.Equal(WorldNpcCellTransitionState.DestinationReady, travel.State);
                Assert.True(travel.Tick());
                Assert.Equal(WorldNpcCellTransitionState.Completed, travel.State);
                Assert.Equal(CellLifecycleState.Active, destinationLoad.State);
                Assert.Null(scenes[leg.CellId].Find(npc.SceneObjectId));
                scenes[transition.To.CellId] = destinationLoad.ActiveResources!.Scene;
                Assert.NotNull(scenes[transition.To.CellId].Find(npc.SceneObjectId));
                Assert.True(identities.TryGet(transition.To.CellId, npc.SceneObjectId, out var owner));
                Assert.Equal(npc.InstanceId, owner);
            }

            var movedRecord = Assert.Single(runtimeObjects.ExportSnapshot());
            Assert.Equal(interiorId, movedRecord.CellId);
            var savePath = Path.Combine(directory, "world-save.json");
            WorldSaveFile.SaveAtomic(savePath, WorldSaveSnapshot.Capture(
                new WorldPlayerLocation(westId, Vector3.Zero, Quaternion.Identity), identities,
                new WorldCellChangeStore(), runtimeObjects));

            var restored = WorldSaveFile.Load(savePath);
            var restoredIdentities = new WorldInstanceIdentityMap();
            var restoredChanges = new WorldCellChangeStore();
            var restoredRuntimeObjects = new WorldRuntimeObjectStore();
            restored.Restore(restoredIdentities, restoredChanges, restoredRuntimeObjects);
            var reloadedScenes = new[] { westId, eastId, interiorId }
                .ToDictionary(cellId => cellId, cellId => SceneFile.Load(scenePaths[cellId]));
            foreach (var cellId in reloadedScenes.Keys)
                Assert.Equal(cellId == interiorId ? 1 : 0,
                    restoredRuntimeObjects.Restore(cellId, reloadedScenes[cellId], restoredIdentities));

            var copies = reloadedScenes.Values.Count(scene => scene.Find(npc.SceneObjectId) is not null);
            Assert.Equal(1, copies);
            Assert.Equal(interiorId, Assert.Single(restoredRuntimeObjects.ExportSnapshot()).CellId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DestinationCommitFailureKeepsNpcAndIdentityInSourceCell()
    {
        var sourceCellId = Guid.NewGuid();
        var destinationCellId = Guid.NewGuid();
        var sourceScene = new SceneGraph();
        var destinationScene = new SceneGraph();
        var identities = new WorldInstanceIdentityMap();
        identities.CreateCellIdentities(sourceCellId, sourceScene);
        var runtimeObjects = new WorldRuntimeObjectStore();
        var npc = runtimeObjects.Spawn(sourceCellId, sourceScene,
            new SceneObject(Guid.NewGuid(), "Follower"), identities);
        destinationScene.Add(new SceneObject(npc.SceneObjectId, "Conflicting object"));
        var destination = new WorldCellLoadOperation<PreparedCell, ActiveCell>();
        var transition = new WorldPathTransition(Guid.NewGuid(), WorldPathConnectionKind.ExteriorBoundary,
            new WorldPathNodeRef(sourceCellId, Guid.NewGuid()),
            new WorldPathNodeRef(destinationCellId, Guid.NewGuid()), null);
        var travel = new WorldNpcCellTransition<PreparedCell, ActiveCell>(transition, npc.InstanceId,
            runtimeObjects, identities, sourceScene, destination,
            (cellId, _) => Task.FromResult(new PreparedCell(cellId)),
            prepared => new ActiveCell(prepared.CellId, destinationScene),
            active => active.Scene, new Transform());

        WaitForPreparation(travel.PreparationTask);
        Assert.False(travel.Tick());
        Assert.False(travel.Tick());

        Assert.Equal(WorldNpcCellTransitionState.Failed, travel.State);
        Assert.Contains("already exists in destination", travel.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal(CellLifecycleState.Unloaded, destination.State);
        Assert.NotNull(sourceScene.Find(npc.SceneObjectId));
        Assert.Equal("Conflicting object", destinationScene.Find(npc.SceneObjectId)!.Name);
        Assert.True(identities.TryGet(sourceCellId, npc.SceneObjectId, out var sourceOwner));
        Assert.Equal(npc.InstanceId, sourceOwner);
        Assert.False(identities.TryGet(destinationCellId, npc.SceneObjectId, out _));
        Assert.Equal(sourceCellId, Assert.Single(runtimeObjects.ExportSnapshot()).CellId);
    }

    [Fact]
    public void ActiveDestinationIsReusedAndRemainsActiveAfterNpcTransfer()
    {
        var sourceCellId = Guid.NewGuid();
        var destinationCellId = Guid.NewGuid();
        var sourceScene = new SceneGraph();
        var destinationScene = new SceneGraph();
        var identities = new WorldInstanceIdentityMap();
        identities.CreateCellIdentities(sourceCellId, sourceScene);
        identities.CreateCellIdentities(destinationCellId, destinationScene);
        var runtimeObjects = new WorldRuntimeObjectStore();
        var npc = runtimeObjects.Spawn(sourceCellId, sourceScene,
            new SceneObject(Guid.NewGuid(), "Active Destination Follower"), identities);
        var destination = new WorldCellLoadOperation<PreparedCell, ActiveCell>();
        var preparation = destination.PrepareAsync(_ => Task.FromResult(new PreparedCell(destinationCellId)));
        WaitForPreparation(preparation);
        destination.PumpCompletions();
        destination.Activate(prepared => new ActiveCell(prepared.CellId, destinationScene));
        var transition = new WorldPathTransition(Guid.NewGuid(), WorldPathConnectionKind.ExteriorBoundary,
            new WorldPathNodeRef(sourceCellId, Guid.NewGuid()),
            new WorldPathNodeRef(destinationCellId, Guid.NewGuid()), null);

        var travel = WorldNpcCellTransition<PreparedCell, ActiveCell>.ReuseActiveDestination(
            transition, npc.InstanceId, runtimeObjects, identities, sourceScene, destination,
            active => active.Scene, new Transform { Position = new Vector3(12f, 1f, 4f) });

        Assert.True(travel.Tick());
        Assert.Equal(WorldNpcCellTransitionState.Completed, travel.State);
        Assert.Equal(CellLifecycleState.Active, destination.State);
        Assert.Null(sourceScene.Find(npc.SceneObjectId));
        Assert.Equal(new Vector3(12f, 1f, 4f), destinationScene.Find(npc.SceneObjectId)!.Transform.Position);
        Assert.Equal(destinationCellId, Assert.Single(runtimeObjects.ExportSnapshot()).CellId);
    }

    [Fact]
    public void FailedTransferToActiveDestinationDoesNotUnloadPlayerCell()
    {
        var sourceCellId = Guid.NewGuid();
        var destinationCellId = Guid.NewGuid();
        var sourceScene = new SceneGraph();
        var destinationScene = new SceneGraph();
        var identities = new WorldInstanceIdentityMap();
        identities.CreateCellIdentities(sourceCellId, sourceScene);
        var runtimeObjects = new WorldRuntimeObjectStore();
        var npc = runtimeObjects.Spawn(sourceCellId, sourceScene,
            new SceneObject(Guid.NewGuid(), "Conflicted Follower"), identities);
        var destination = new WorldCellLoadOperation<PreparedCell, ActiveCell>();
        var preparation = destination.PrepareAsync(_ => Task.FromResult(new PreparedCell(destinationCellId)));
        WaitForPreparation(preparation);
        destination.PumpCompletions();
        destination.Activate(prepared => new ActiveCell(prepared.CellId, destinationScene));
        destinationScene.Add(new SceneObject(npc.SceneObjectId, "Player-owned object"));
        var transition = new WorldPathTransition(Guid.NewGuid(), WorldPathConnectionKind.ExteriorBoundary,
            new WorldPathNodeRef(sourceCellId, Guid.NewGuid()),
            new WorldPathNodeRef(destinationCellId, Guid.NewGuid()), null);

        var travel = WorldNpcCellTransition<PreparedCell, ActiveCell>.ReuseActiveDestination(
            transition, npc.InstanceId, runtimeObjects, identities, sourceScene, destination,
            active => active.Scene, new Transform());

        Assert.False(travel.Tick());
        Assert.Equal(WorldNpcCellTransitionState.Failed, travel.State);
        Assert.Equal(CellLifecycleState.Active, destination.State);
        Assert.NotNull(sourceScene.Find(npc.SceneObjectId));
        Assert.Equal("Player-owned object", destinationScene.Find(npc.SceneObjectId)!.Name);
        Assert.Equal(sourceCellId, Assert.Single(runtimeObjects.ExportSnapshot()).CellId);
    }

    private static CellPathNode Node(float x, float y, float z) => new(Guid.NewGuid(), new NavigationPoint(x, y, z));

    private static CellPathGraph Graph(Guid cellId, WorldCellKind kind, CellPathNode first,
        CellPathNode second, CellPathEdge edge) => new()
    {
        CellId = cellId,
        Kind = kind,
        Nodes = new[] { first, second },
        Edges = new[] { edge }
    };

    private static void WaitForPreparation(Task preparation)
    {
        if (!SpinWait.SpinUntil(() => preparation.IsCompleted, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("NPC destination preparation did not finish.");
        if (preparation.IsFaulted) throw preparation.Exception!.GetBaseException();
        if (preparation.IsCanceled) throw new TaskCanceledException(preparation);
    }

    private sealed class PreparedCell(Guid cellId) : IDisposable
    {
        public Guid CellId { get; } = cellId;
        public void Dispose() { }
    }

    private sealed class ActiveCell(Guid cellId, SceneGraph scene) : IDisposable
    {
        public Guid CellId { get; } = cellId;
        public SceneGraph Scene { get; } = scene;
        public void Dispose() { }
    }
}

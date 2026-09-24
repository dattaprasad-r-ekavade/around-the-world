using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellTravelTransactionTests
{
    [Fact]
    public void TravelKeepsSourceActiveUntilDestinationIsReadyThenUsesAuthoredSpawn()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var spawnId = Guid.NewGuid();
        var preparedSource = CreateActive(sourceId, out var source);
        var destination = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var preparation = new TaskCompletionSource<PreparedProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authoredSpawn = new WorldSpawnLocation(destinationId, spawnId,
            new Vector3(3f, 1.25f, -6f), Quaternion.CreateFromYawPitchRoll(0.7f, 0f, 0f));
        WorldSpawnLocation? playerLocation = null;
        var travel = new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            authoredSpawn, (_, _) => preparation.Task, prepared => new ActiveProbe(prepared.CellId),
            location => playerLocation = location);

        Assert.False(travel.Tick());
        Assert.Equal(WorldCellTravelState.PreparingDestination, travel.State);
        Assert.Equal(CellLifecycleState.Active, source.State);
        Assert.Same(preparedSource, source.ActiveResources);

        var preparedDestination = new PreparedProbe(destinationId);
        preparation.SetResult(preparedDestination);
        WaitForPreparation(travel.PreparationTask);

        Assert.False(travel.Tick());
        Assert.Equal(WorldCellTravelState.DestinationReady, travel.State);
        Assert.Equal(CellLifecycleState.Active, source.State);
        Assert.Equal(CellLifecycleState.Ready, destination.State);
        Assert.Null(playerLocation);

        Assert.True(travel.Tick());
        Assert.Equal(WorldCellTravelState.Completed, travel.State);
        Assert.Equal(CellLifecycleState.Unloaded, source.State);
        Assert.Equal(CellLifecycleState.Active, destination.State);
        Assert.Equal(authoredSpawn, playerLocation);
        Assert.True(preparedSource.IsDisposed);
        Assert.True(preparedDestination.IsDisposed);
    }

    [Fact]
    public void TravelingIntoTwoInteriorsReturnsToTheAuthoredExteriorSpawn()
    {
        var worldFixture = CreateDoorFixture();
        try
        {
            var world = WorldManifest.Load(worldFixture.ManifestPath);
            var exteriorId = worldFixture.ExteriorId;
            var houseAId = worldFixture.HouseAId;
            var houseBId = worldFixture.HouseBId;
            var scenes = worldFixture.Scenes;
            var exteriorDoorA = scenes[exteriorId].Find(worldFixture.ExteriorDoorAId)!.Door!;
            var exteriorDoorB = scenes[exteriorId].Find(worldFixture.ExteriorDoorBId)!.Door!;
            var houseAReturn = scenes[houseAId].Find(worldFixture.HouseAReturnDoorId)!.Door!;
            var houseBReturn = scenes[houseBId].Find(worldFixture.HouseBReturnDoorId)!.Door!;
            var exteriorSpawnA = WorldTravelValidator.ResolveDestination(world, scenes, houseAReturn);
            var exteriorSpawnB = WorldTravelValidator.ResolveDestination(world, scenes, houseBReturn);
            var source = CreateActive(exteriorId, out var exterior);
            WorldSpawnLocation? playerLocation = null;

            var houseA = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
            var enterA = BeginTravel(exteriorId, exterior, houseA, world, scenes, exteriorDoorA, player => playerLocation = player);
            CompleteTravel(enterA);
            Assert.Equal(houseAId, playerLocation!.Value.CellId);
            Assert.Equal(CellLifecycleState.Unloaded, exterior.State);

            var returnToExterior = BeginTravel(houseAId, houseA, exterior, world, scenes,
                houseAReturn, player => playerLocation = player);
            CompleteTravel(returnToExterior);
            Assert.Equal(exteriorSpawnA, playerLocation);
            Assert.Equal(exteriorId, playerLocation!.Value.CellId);

            var houseB = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
            var enterB = BeginTravel(exteriorId, exterior, houseB, world, scenes, exteriorDoorB, player => playerLocation = player);
            CompleteTravel(enterB);
            Assert.Equal(houseBId, playerLocation!.Value.CellId);

            var returnFromB = BeginTravel(houseBId, houseB, exterior, world, scenes,
                houseBReturn, player => playerLocation = player);
            CompleteTravel(returnFromB);
            Assert.Equal(exteriorSpawnB, playerLocation);
            Assert.Equal(exteriorId, playerLocation!.Value.CellId);
            Assert.Equal(CellLifecycleState.Active, exterior.State);
            Assert.Equal(CellLifecycleState.Unloaded, houseA.State);
            Assert.Equal(CellLifecycleState.Unloaded, houseB.State);
        }
        finally
        {
            Directory.Delete(worldFixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void FailedPreparationOrActivationLeavesSourcePlayable()
    {
        var sourceId = Guid.NewGuid();
        var sourceResource = CreateActive(sourceId, out var source);
        var destinationId = Guid.NewGuid();
        var destination = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var failedLoad = new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            new WorldSpawnLocation(destinationId, Guid.NewGuid(), Vector3.Zero, Quaternion.Identity),
            (_, _) => Task.FromException<PreparedProbe>(new IOException("asset is corrupt")),
            prepared => new ActiveProbe(prepared.CellId), _ => throw new InvalidOperationException("must not move"));

        WaitForPreparation(failedLoad.PreparationTask);
        Assert.False(failedLoad.Tick());
        Assert.Equal(WorldCellTravelState.Failed, failedLoad.State);
        Assert.Contains("asset is corrupt", failedLoad.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal(CellLifecycleState.Active, source.State);
        Assert.Same(sourceResource, source.ActiveResources);

        destination.Discard();
        var retryPrepared = new PreparedProbe(destinationId);
        var failedActivation = new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            new WorldSpawnLocation(destinationId, Guid.NewGuid(), Vector3.Zero, Quaternion.Identity),
            (_, _) => Task.FromResult(retryPrepared),
            _ => throw new InvalidOperationException("GPU activation failed"), _ => throw new InvalidOperationException("must not move"));
        WaitForPreparation(failedActivation.PreparationTask);
        Assert.False(failedActivation.Tick());
        Assert.Equal(WorldCellTravelState.DestinationReady, failedActivation.State);
        Assert.False(failedActivation.Tick());

        Assert.Equal(WorldCellTravelState.Failed, failedActivation.State);
        Assert.Contains("GPU activation failed", failedActivation.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal(CellLifecycleState.Failed, destination.State);
        Assert.Equal(CellLifecycleState.Active, source.State);
        Assert.Same(sourceResource, source.ActiveResources);
        Assert.True(retryPrepared.IsDisposed);
    }

    [Fact]
    public void CancelingPreparationKeepsSourceAndDisposesLatePreparedData()
    {
        var sourceId = Guid.NewGuid();
        var sourceResource = CreateActive(sourceId, out var source);
        var destination = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var preparation = new TaskCompletionSource<PreparedProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
        var travel = new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            new WorldSpawnLocation(Guid.NewGuid(), Guid.NewGuid(), Vector3.Zero, Quaternion.Identity),
            (_, _) => preparation.Task, prepared => new ActiveProbe(prepared.CellId), _ => { });

        travel.Cancel();
        var lateResult = new PreparedProbe(Guid.NewGuid());
        preparation.SetResult(lateResult);
        WaitForPreparation(travel.PreparationTask);
        Assert.False(travel.Tick());

        Assert.Equal(WorldCellTravelState.Cancelled, travel.State);
        Assert.Equal(CellLifecycleState.Active, source.State);
        Assert.Same(sourceResource, source.ActiveResources);
        Assert.Equal(CellLifecycleState.Unloaded, destination.State);
        Assert.True(lateResult.IsDisposed);
    }

    [Fact]
    public void FailedPlacementUnloadsDestinationAndKeepsSourceActive()
    {
        var sourceId = Guid.NewGuid();
        var sourceResource = CreateActive(sourceId, out var source);
        var destinationId = Guid.NewGuid();
        var destination = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        ActiveProbe? activated = null;
        var travel = new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            new WorldSpawnLocation(destinationId, Guid.NewGuid(), Vector3.One, Quaternion.Identity),
            (_, _) => Task.FromResult(new PreparedProbe(destinationId)), prepared => activated = new ActiveProbe(prepared.CellId),
            _ => throw new InvalidOperationException("player placement rejected"));

        WaitForPreparation(travel.PreparationTask);
        Assert.False(travel.Tick());
        Assert.False(travel.Tick());

        Assert.Equal(WorldCellTravelState.Failed, travel.State);
        Assert.Contains("player placement rejected", travel.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal(CellLifecycleState.Unloaded, destination.State);
        Assert.True(activated!.IsDisposed);
        Assert.Equal(CellLifecycleState.Active, source.State);
        Assert.Same(sourceResource, source.ActiveResources);
    }

    [Fact]
    public void SourceCleanupFailureKeepsTravelCommittedToActiveDestination()
    {
        var sourceId = Guid.NewGuid();
        var source = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var sourcePreparation = source.PrepareAsync(_ => Task.FromResult(new PreparedProbe(sourceId)));
        WaitForPreparation(sourcePreparation);
        source.PumpCompletions();
        source.Activate(prepared => new ActiveProbe(prepared.CellId, throwOnDispose: true));

        var destinationId = Guid.NewGuid();
        var destination = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var spawn = new WorldSpawnLocation(destinationId, Guid.NewGuid(), Vector3.One, Quaternion.Identity);
        WorldSpawnLocation? playerLocation = null;
        var travel = new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            spawn, (_, _) => Task.FromResult(new PreparedProbe(destinationId)),
            prepared => new ActiveProbe(prepared.CellId), location => playerLocation = location);

        WaitForPreparation(travel.PreparationTask);
        Assert.False(travel.Tick());
        Assert.True(travel.Tick());

        Assert.Equal(WorldCellTravelState.Completed, travel.State);
        Assert.Contains("cleanup failed", travel.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal(CellLifecycleState.Failed, source.State);
        Assert.Equal(CellLifecycleState.Active, destination.State);
        Assert.Equal(spawn, playerLocation);
    }

    private static WorldCellTravelTransaction<PreparedProbe, ActiveProbe> BeginTravel(Guid sourceId,
        WorldCellLoadOperation<PreparedProbe, ActiveProbe> source,
        WorldCellLoadOperation<PreparedProbe, ActiveProbe> destination,
        WorldManifest world, IReadOnlyDictionary<Guid, SceneGraph> scenes, WorldDoorComponent door,
        Action<WorldSpawnLocation> placePlayer)
    {
        var spawn = WorldTravelValidator.ResolveDestination(world, scenes, door);
        return new WorldCellTravelTransaction<PreparedProbe, ActiveProbe>(sourceId, source, destination,
            spawn, (cellId, _) => Task.FromResult(new PreparedProbe(cellId)),
            prepared => new ActiveProbe(prepared.CellId), placePlayer);
    }

    private static void CompleteTravel(WorldCellTravelTransaction<PreparedProbe, ActiveProbe> travel)
    {
        WaitForPreparation(travel.PreparationTask);
        Assert.False(travel.Tick());
        Assert.Equal(WorldCellTravelState.DestinationReady, travel.State);
        Assert.True(travel.Tick());
        Assert.Equal(WorldCellTravelState.Completed, travel.State);
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

    private static ActiveProbe CreateActive(Guid cellId, out WorldCellLoadOperation<PreparedProbe, ActiveProbe> operation)
    {
        operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var preparation = operation.PrepareAsync(_ => Task.FromResult(new PreparedProbe(cellId)));
        WaitForPreparation(preparation);
        operation.PumpCompletions();
        return operation.Activate(prepared => new ActiveProbe(prepared.CellId));
    }

    private static DoorFixture CreateDoorFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-travel-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var exteriorId = Guid.NewGuid();
        var houseAId = Guid.NewGuid();
        var houseBId = Guid.NewGuid();
        var exteriorSpawnId = Guid.NewGuid();
        var houseASpawnId = Guid.NewGuid();
        var houseBSpawnId = Guid.NewGuid();
        var doorAId = Guid.NewGuid();
        var doorBId = Guid.NewGuid();
        var returnAId = Guid.NewGuid();
        var returnBId = Guid.NewGuid();
        var exterior = new SceneGraph();
        exterior.Add(new SceneObject(doorAId, "Door A")
        {
            Door = new WorldDoorComponent(houseAId, houseASpawnId, Quaternion.Identity)
        });
        exterior.Add(new SceneObject(doorBId, "Door B")
        {
            Door = new WorldDoorComponent(houseBId, houseBSpawnId, Quaternion.Identity)
        });
        exterior.Add(new SceneObject(Guid.NewGuid(), "Exterior Spawn")
        {
            SpawnPoint = new WorldSpawnComponent(exteriorSpawnId),
            Transform = new Transform { Position = new Vector3(15f, 1f, 20f) }
        });
        var houseA = CreateInterior(returnAId, exteriorId, exteriorSpawnId, houseASpawnId, new Vector3(2f, 1f, 3f));
        var houseB = CreateInterior(returnBId, exteriorId, exteriorSpawnId, houseBSpawnId, new Vector3(-4f, 1f, 7f));
        var exteriorPath = Path.Combine(directory, "exterior.json");
        var houseAPath = Path.Combine(directory, "house-a.json");
        var houseBPath = Path.Combine(directory, "house-b.json");
        SceneFile.SaveAtomic(exterior, exteriorPath);
        SceneFile.SaveAtomic(houseA, houseAPath);
        SceneFile.SaveAtomic(houseB, houseBPath);
        var manifestPath = Path.Combine(directory, "world.json");
        WorldManifest.SaveAtomic(manifestPath, 32f,
        [
            new WorldCellDefinition { Id = exteriorId, Kind = WorldCellKind.Exterior,
                ExteriorCoordinate = new ExteriorCellCoordinate(0, 0), ScenePath = "exterior.json" },
            new WorldCellDefinition { Id = houseAId, Kind = WorldCellKind.Interior, ScenePath = "house-a.json" },
            new WorldCellDefinition { Id = houseBId, Kind = WorldCellKind.Interior, ScenePath = "house-b.json" }
        ]);
        return new DoorFixture(directory, manifestPath, exteriorId, houseAId, houseBId,
            doorAId, doorBId, returnAId, returnBId,
            new Dictionary<Guid, SceneGraph> { [exteriorId] = exterior, [houseAId] = houseA, [houseBId] = houseB });
    }

    private static SceneGraph CreateInterior(Guid returnDoorId, Guid exteriorId, Guid exteriorSpawnId,
        Guid interiorSpawnId, Vector3 spawnPosition)
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(returnDoorId, "Return Door")
        {
            Door = new WorldDoorComponent(exteriorId, exteriorSpawnId, Quaternion.Identity)
        });
        scene.Add(new SceneObject(Guid.NewGuid(), "Entry Spawn")
        {
            SpawnPoint = new WorldSpawnComponent(interiorSpawnId),
            Transform = new Transform { Position = spawnPosition }
        });
        return scene;
    }

    private sealed record DoorFixture(string Directory, string ManifestPath, Guid ExteriorId, Guid HouseAId, Guid HouseBId,
        Guid ExteriorDoorAId, Guid ExteriorDoorBId, Guid HouseAReturnDoorId, Guid HouseBReturnDoorId,
        IReadOnlyDictionary<Guid, SceneGraph> Scenes);

    private sealed class PreparedProbe(Guid cellId) : IDisposable
    {
        public Guid CellId { get; } = cellId;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class ActiveProbe(Guid cellId, bool throwOnDispose = false) : IDisposable
    {
        public Guid CellId { get; } = cellId;
        public bool IsDisposed { get; private set; }
        public void Dispose()
        {
            IsDisposed = true;
            if (throwOnDispose) throw new IOException("source cleanup failed");
        }
    }
}

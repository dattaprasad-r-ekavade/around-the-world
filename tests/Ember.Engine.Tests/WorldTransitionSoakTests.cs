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

public sealed class WorldTransitionSoakTests
{
    private sealed class SoakPreparedCell(Guid cellId) : IDisposable, ICellActivationCost
    {
        public Guid CellId { get; } = cellId;
        public bool IsDisposed { get; private set; }
        public long EstimatedActivationCost => 1;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class SoakActiveCell(Guid cellId) : IDisposable
    {
        public Guid CellId { get; } = cellId;
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    [Fact]
    public void RepeatedTransitions_50Times_MaintainsResourceStabilityWithoutErrorsOrLeaks()
    {
        var exteriorId = Guid.NewGuid();
        var interiorId = Guid.NewGuid();
        var exteriorSpawnId = Guid.NewGuid();
        var interiorSpawnId = Guid.NewGuid();

        var exteriorSpawn = new WorldSpawnLocation(exteriorId, exteriorSpawnId,
            new Vector3(18f, 1.1f, 23f), Quaternion.Identity);
        var interiorSpawn = new WorldSpawnLocation(interiorId, interiorSpawnId,
            new Vector3(2.1f, 1f, 2.8f), Quaternion.Identity);

        // Initial exterior active cell
        var currentCellId = exteriorId;
        var currentOp = new WorldCellLoadOperation<SoakPreparedCell, SoakActiveCell>();
        var initPrep = currentOp.PrepareAsync(_ => Task.FromResult(new SoakPreparedCell(exteriorId)));
        WaitForTask(initPrep);
        currentOp.PumpCompletions();
        currentOp.Activate(p => new SoakActiveCell(p.CellId));

        var currentSpawn = exteriorSpawn;
        WorldSpawnLocation? playerLocation = exteriorSpawn;

        const int targetTransitions = 50;
        var completedTransitions = 0;
        var activeCellHistory = new List<int>();

        for (var i = 0; i < targetTransitions; i++)
        {
            var isEntering = currentCellId == exteriorId;
            var destinationId = isEntering ? interiorId : exteriorId;
            var destinationSpawn = isEntering ? interiorSpawn : exteriorSpawn;

            var destinationOp = new WorldCellLoadOperation<SoakPreparedCell, SoakActiveCell>();
            var travel = new WorldCellTravelTransaction<SoakPreparedCell, SoakActiveCell>(
                currentCellId,
                currentOp,
                destinationOp,
                destinationSpawn,
                (cellId, _) => Task.FromResult(new SoakPreparedCell(cellId)),
                prepared => new SoakActiveCell(prepared.CellId),
                loc => playerLocation = loc);

            WaitForTask(travel.PreparationTask);
            Assert.False(travel.Tick()); // DestinationReady
            Assert.Equal(WorldCellTravelState.DestinationReady, travel.State);
            Assert.True(travel.Tick());  // Completed
            Assert.Equal(WorldCellTravelState.Completed, travel.State);

            Assert.Equal(CellLifecycleState.Unloaded, currentOp.State);
            Assert.Equal(CellLifecycleState.Active, destinationOp.State);
            Assert.Equal(destinationSpawn, playerLocation);

            currentCellId = destinationId;
            currentOp = destinationOp;
            currentSpawn = destinationSpawn;
            completedTransitions++;

            // Exactly 1 cell is active at any time
            activeCellHistory.Add(currentOp.State == CellLifecycleState.Active ? 1 : 0);
        }

        Assert.Equal(targetTransitions, completedTransitions);
        Assert.All(activeCellHistory, count => Assert.Equal(1, count));
    }

    private static void WaitForTask(Task task)
    {
        if (!SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Task did not complete within five seconds.");
        if (task.IsFaulted)
            throw task.Exception!.GetBaseException();
        if (task.IsCanceled)
            throw new TaskCanceledException(task);
    }
}

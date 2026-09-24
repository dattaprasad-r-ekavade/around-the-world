using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class CellActivationQueueTests
{
    [Fact]
    public void NineCellsRespectPerFrameCostAndCellLimitsIncludingSlowPreparation()
    {
        const long frameCostLimit = 100;
        const int frameCellLimit = 4;
        var queue = new CellActivationQueue<PreparedProbe, ActiveProbe>(
            frameCostLimit, frameCellLimit, TimeSpan.FromMilliseconds(20));
        var operations = new List<(WorldCellLoadOperation<PreparedProbe, ActiveProbe> Operation, Task Preparation)>();
        var steppers = new List<ActivationStepper>();
        var slowCell = new ExteriorCellCoordinate(1, 1);
        using var allPreparationsCompleted = new ManualResetEventSlim();

        foreach (var z in Enumerable.Range(-1, 3))
        foreach (var x in Enumerable.Range(-1, 3))
        {
            var coordinate = new ExteriorCellCoordinate(x, z);
            var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
            var preparation = operation.PrepareAsync(async token =>
            {
                if (coordinate == slowCell) await Task.Delay(35, token);
                return new PreparedProbe(coordinate, estimatedActivationCost: 250);
            });
            operations.Add((operation, preparation));
        }
        Task.WhenAll(operations.Select(item => item.Preparation)).ContinueWith(
            _ => allPreparationsCompleted.Set(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try
        {
            var safetyFrames = 0;
            while (operations.Any(item => item.Operation.State != CellLifecycleState.Active))
            {
                Assert.True(++safetyFrames < 200, "The bounded activation queue did not finish the 3x3 grid.");
                foreach (var (operation, _) in operations)
                {
                    operation.PumpCompletions();
                    if (operation.State == CellLifecycleState.Ready
                        && !steppers.Any(stepper => ReferenceEquals(stepper.Operation, operation)))
                    {
                        var stepper = new ActivationStepper(operation);
                        steppers.Add(stepper);
                        queue.Enqueue(operation, stepper);
                    }
                }

                var metrics = queue.ProcessFrame();
                Assert.InRange(metrics.CostConsumed, 0, frameCostLimit);
                Assert.InRange(metrics.CellsProcessed, 0, frameCellLimit);
                Assert.True(metrics.ElapsedMilliseconds >= 0);
                if (metrics.CellsProcessed == 0) Thread.Sleep(2);
            }

            Assert.Equal(9, steppers.Count);
            Assert.All(operations, item => Assert.Equal(CellLifecycleState.Active, item.Operation.State));
            Assert.All(steppers, stepper =>
            {
                Assert.Equal(3, stepper.StepCount);
                Assert.Equal(1, stepper.DisposeCount);
            });
            Assert.Equal(0, queue.PendingCount);
            Assert.Equal(0, queue.QueuedEstimatedCost);
        }
        finally
        {
            queue.Dispose();
            foreach (var (operation, preparation) in operations)
            {
                Assert.True(allPreparationsCompleted.Wait(TimeSpan.FromSeconds(5)));
                operation.PumpCompletions();
                if (operation.State == CellLifecycleState.Active) operation.Unload();
                else if (operation.State == CellLifecycleState.Ready) operation.Discard();
                else if (operation.State == CellLifecycleState.Preparing) operation.Cancel();
            }
        }
    }

    [Fact]
    public void QueueStopsAtMaximumCellsEvenWhenCostBudgetRemains()
    {
        var queue = new CellActivationQueue<PreparedProbe, ActiveProbe>(
            maximumCostPerFrame: 1_000, maximumCellsPerFrame: 2,
            maximumElapsedPerFrame: TimeSpan.FromSeconds(1));
        var operations = new List<WorldCellLoadOperation<PreparedProbe, ActiveProbe>>();
        try
        {
            for (var index = 0; index < 5; index++)
            {
                var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
                var preparation = operation.PrepareAsync(_ => Task.FromResult(
                    new PreparedProbe(new ExteriorCellCoordinate(index, 0), estimatedActivationCost: 1)));
                using var completed = new ManualResetEventSlim();
                preparation.ContinueWith(_ => completed.Set(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
                operation.PumpCompletions();
                operations.Add(operation);
                queue.Enqueue(operation, new ActivationStepper(operation));
            }

            var metrics = queue.ProcessFrame();

            Assert.Equal(2, metrics.CellsProcessed);
            Assert.Equal(2, metrics.CellsCompleted);
            Assert.Equal(2, metrics.CostConsumed);
            Assert.Equal(3, metrics.PendingCells);
        }
        finally
        {
            queue.Dispose();
            foreach (var operation in operations)
            {
                if (operation.State == CellLifecycleState.Active) operation.Unload();
                else if (operation.State == CellLifecycleState.Ready) operation.Discard();
            }
        }
    }

    private sealed class PreparedProbe(ExteriorCellCoordinate coordinate, long estimatedActivationCost)
        : IDisposable, ICellActivationCost
    {
        public ExteriorCellCoordinate Coordinate { get; } = coordinate;
        public long EstimatedActivationCost { get; } = estimatedActivationCost;
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ActiveProbe : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class ActivationStepper(
        WorldCellLoadOperation<PreparedProbe, ActiveProbe> operation)
        : ICellActivationStepper<PreparedProbe, ActiveProbe>
    {
        private long _remainingCost;

        public WorldCellLoadOperation<PreparedProbe, ActiveProbe> Operation { get; } = operation;
        public int StepCount { get; private set; }
        public int DisposeCount { get; private set; }

        public CellActivationStepResult<ActiveProbe> Step(PreparedProbe prepared, long maximumCost)
        {
            if (_remainingCost == 0) _remainingCost = prepared.EstimatedActivationCost;
            Assert.True(_remainingCost > 0);
            StepCount++;
            var cost = Math.Min(_remainingCost, maximumCost);
            _remainingCost -= cost;
            return new CellActivationStepResult<ActiveProbe>(cost, _remainingCost == 0,
                _remainingCost == 0 ? new ActiveProbe() : null);
        }

        public void Dispose() => DisposeCount++;
    }
}

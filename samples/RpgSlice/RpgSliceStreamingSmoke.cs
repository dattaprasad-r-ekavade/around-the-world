using Ember.World;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RpgSlice;

/// <summary>Runs a 3x3 world-cell prepare/upload pass with one deliberately slow cell.</summary>
internal sealed class RpgSliceStreamingSmoke : IDisposable
{
    private const long UploadBudgetPerFrame = 300;
    private const int CellStepLimitPerFrame = 3;
    private static readonly ExteriorCellCoordinate SlowCell = new(1, 1);

    private readonly ExteriorCellLoadingRing _ring;
    private readonly CellActivationQueue<SmokePreparedCell, SmokeActiveCell> _queue =
        new(UploadBudgetPerFrame, CellStepLimitPerFrame, TimeSpan.FromMilliseconds(6));
    private readonly Dictionary<ExteriorCellCoordinate, WorldCellLoadOperation<SmokePreparedCell, SmokeActiveCell>> _operations = new();
    private readonly HashSet<WorldCellLoadOperation<SmokePreparedCell, SmokeActiveCell>> _queued = new();
    private readonly List<Task> _preparations = new();
    private int _frame;
    private long _totalUploadCost;
    private bool _complete;
    private bool _disposed;

    public RpgSliceStreamingSmoke(WorldManifest world)
    {
        _ring = world.CreateLoadingRing(radiusInCells: 1, retentionRadiusInCells: 2);
        var update = _ring.UpdatePlayerPosition(Vector3.Zero);
        if (update.Entered.Count != 9)
            throw new InvalidOperationException($"The streaming smoke expected 9 grid requests, got {update.Entered.Count}.");

        foreach (var coordinate in update.Entered)
        {
            if (!world.TryGetExterior(coordinate, out var definition) || definition is null)
                throw new InvalidOperationException($"The RpgSlice manifest is missing test-grid cell ({coordinate.X}, {coordinate.Z}).");

            var operation = new WorldCellLoadOperation<SmokePreparedCell, SmokeActiveCell>();
            _operations.Add(coordinate, operation);
            _preparations.Add(operation.PrepareAsync(coordinate == SlowCell
                ? async token =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
                    return new SmokePreparedCell(coordinate, estimatedUploadCost: 250);
                }
                : _ => Task.FromResult(new SmokePreparedCell(coordinate, estimatedUploadCost: 250))));
        }

        Console.WriteLine("RpgSlice: streaming smoke started for the manifest's 3x3 exterior grid; cell (1, 1) is delayed by 150 ms.");
    }

    public bool Tick()
    {
        if (_complete) return true;
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var operation in _operations.Values)
        {
            operation.PumpCompletions();
            if (operation.State == CellLifecycleState.Failed)
                throw new InvalidOperationException("RpgSlice streaming smoke failed to prepare a cell.", operation.Failure);
            if (operation.State == CellLifecycleState.Ready && _queued.Add(operation))
                _queue.Enqueue(operation, new SmokeUploadStepper());
        }

        var metrics = _queue.ProcessFrame();
        _frame++;
        _totalUploadCost += metrics.CostConsumed;
        if (metrics.CostConsumed > UploadBudgetPerFrame || metrics.CellsProcessed > CellStepLimitPerFrame)
            throw new InvalidOperationException("The cell activation queue exceeded a configured per-frame budget.");
        Console.WriteLine(
            $"RpgSlice stream frame {_frame}: cells={metrics.CellsProcessed}, completed={metrics.CellsCompleted}, " +
            $"upload-cost={metrics.CostConsumed}/{UploadBudgetPerFrame}, pending={metrics.PendingCells}, " +
            $"queued-cost={metrics.QueuedEstimatedCost}, elapsed={metrics.ElapsedMilliseconds:0.000} ms");

        if (_operations.Values.All(operation => operation.State == CellLifecycleState.Active))
        {
            if (_frame < 2 || _operations[SlowCell].PreparationThreadId is null)
                throw new InvalidOperationException("The delayed 3x3 cell did not pass through asynchronous preparation.");
            _complete = true;
            Console.WriteLine(
                $"RpgSlice: 3x3 streaming smoke passed in {_frame} frames; total upload cost={_totalUploadCost}, " +
                $"slow cell prepared on worker thread {_operations[SlowCell].PreparationThreadId}.");
        }

        return _complete;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Dispose();
        foreach (var operation in _operations.Values)
        {
            switch (operation.State)
            {
                case CellLifecycleState.Preparing:
                    operation.Cancel();
                    break;
                case CellLifecycleState.Ready:
                    operation.Discard();
                    break;
                case CellLifecycleState.Active:
                    operation.Unload();
                    break;
            }
        }

        if (_preparations.Count > 0)
        {
            Task.WaitAll(_preparations.ToArray(), TimeSpan.FromSeconds(3));
            foreach (var operation in _operations.Values) operation.PumpCompletions();
        }
    }

    private sealed class SmokePreparedCell(
        ExteriorCellCoordinate coordinate, long estimatedUploadCost) : IDisposable, ICellActivationCost
    {
        public ExteriorCellCoordinate Coordinate { get; } = coordinate;
        public long EstimatedActivationCost { get; } = estimatedUploadCost;
        public void Dispose() { }
    }

    private sealed class SmokeActiveCell(ExteriorCellCoordinate coordinate) : IDisposable
    {
        public ExteriorCellCoordinate Coordinate { get; } = coordinate;
        public void Dispose() { }
    }

    private sealed class SmokeUploadStepper : ICellActivationStepper<SmokePreparedCell, SmokeActiveCell>
    {
        private long _remainingCost;

        public CellActivationStepResult<SmokeActiveCell> Step(SmokePreparedCell prepared, long maximumCost)
        {
            if (_remainingCost == 0) _remainingCost = prepared.EstimatedActivationCost;
            var consumed = Math.Min(_remainingCost, Math.Min(maximumCost, 100));
            _remainingCost -= consumed;
            return new CellActivationStepResult<SmokeActiveCell>(consumed, _remainingCost == 0,
                _remainingCost == 0 ? new SmokeActiveCell(prepared.Coordinate) : null);
        }

        public void Dispose() { }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Ember.World;

/// <summary>Estimated GPU/upload work declared by prepared cell data, measured in bytes or work units.</summary>
public interface ICellActivationCost
{
    long EstimatedActivationCost { get; }
}

/// <summary>Incrementally creates owner-thread resources from one cell's prepared CPU data.</summary>
public interface ICellActivationStepper<TPrepared, TActive> : IDisposable
    where TPrepared : IDisposable
    where TActive : IDisposable
{
    CellActivationStepResult<TActive> Step(TPrepared prepared, long maximumCost);
}

/// <summary>The result of one bounded activation step.</summary>
public readonly record struct CellActivationStepResult<TActive>(
    long CostConsumed,
    bool IsComplete,
    TActive? ActiveResources)
    where TActive : IDisposable;

/// <summary>Diagnostics captured after one activation-queue frame.</summary>
public readonly record struct CellActivationQueueFrameMetrics(
    int CellsProcessed,
    int CellsCompleted,
    int PendingCells,
    long CostConsumed,
    long QueuedEstimatedCost,
    double ElapsedMilliseconds);

/// <summary>
/// Fairly advances ready cells under per-frame cost, cell-count, and elapsed-time limits.
/// Each activation step runs on the owning thread of its cell operation.
/// </summary>
public sealed class CellActivationQueue<TPrepared, TActive> : IDisposable
    where TPrepared : IDisposable
    where TActive : IDisposable
{
    private readonly Queue<PendingActivation> _pending = new();
    private readonly HashSet<WorldCellLoadOperation<TPrepared, TActive>> _operations = new();
    private readonly long _maximumCostPerFrame;
    private readonly int _maximumCellsPerFrame;
    private readonly TimeSpan _maximumElapsedPerFrame;
    private long _queuedEstimatedCost;
    private bool _disposed;

    public CellActivationQueue(long maximumCostPerFrame, int maximumCellsPerFrame,
        TimeSpan maximumElapsedPerFrame)
    {
        if (maximumCostPerFrame <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCostPerFrame), "Per-frame activation cost must be positive.");
        if (maximumCellsPerFrame <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCellsPerFrame), "Per-frame cell limit must be positive.");
        if (maximumElapsedPerFrame <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumElapsedPerFrame), "Per-frame time limit must be positive.");

        _maximumCostPerFrame = maximumCostPerFrame;
        _maximumCellsPerFrame = maximumCellsPerFrame;
        _maximumElapsedPerFrame = maximumElapsedPerFrame;
    }

    public int PendingCount => _pending.Count;
    public long QueuedEstimatedCost => _queuedEstimatedCost;
    public CellActivationQueueFrameMetrics LastFrameMetrics { get; private set; }

    /// <summary>Adds one ready operation whose prepared value implements <see cref="ICellActivationCost"/>.</summary>
    public void Enqueue(WorldCellLoadOperation<TPrepared, TActive> operation,
        ICellActivationStepper<TPrepared, TActive> stepper)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(stepper);
        if (operation.State != CellLifecycleState.Ready)
            throw new InvalidOperationException($"Only a ready cell can enter the activation queue; current state is {operation.State}.");
        if (!_operations.Add(operation))
            throw new InvalidOperationException("Cell operation is already queued for activation.");

        try
        {
            var estimate = operation.PreparedActivationCost;
            _queuedEstimatedCost = checked(_queuedEstimatedCost + estimate);
            _pending.Enqueue(new PendingActivation(operation, stepper, estimate));
        }
        catch
        {
            _operations.Remove(operation);
            throw;
        }
    }

    /// <summary>Advances as many activation steps as fit inside all three configured frame budgets.</summary>
    public CellActivationQueueFrameMetrics ProcessFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var timer = Stopwatch.StartNew();
        var cellsProcessed = 0;
        var cellsCompleted = 0;
        long costConsumed = 0;
        var maximumActivationsThisFrame = Math.Min(_maximumCellsPerFrame, _pending.Count);

        while (_pending.Count > 0
            && cellsProcessed < maximumActivationsThisFrame
            && costConsumed < _maximumCostPerFrame
            && timer.Elapsed < _maximumElapsedPerFrame)
        {
            var activation = _pending.Dequeue();
            var remainingBudget = _maximumCostPerFrame - costConsumed;
            CellActivationStepResult<TActive> result;
            try
            {
                result = activation.Operation.ActivateStep(activation.Stepper, remainingBudget);
            }
            catch
            {
                _queuedEstimatedCost -= activation.RemainingEstimate;
                _operations.Remove(activation.Operation);
                throw;
            }

            cellsProcessed++;
            costConsumed += result.CostConsumed;
            var estimateConsumed = Math.Min(activation.RemainingEstimate, result.CostConsumed);
            activation.RemainingEstimate -= estimateConsumed;
            _queuedEstimatedCost -= estimateConsumed;

            if (result.IsComplete)
            {
                _queuedEstimatedCost -= activation.RemainingEstimate;
                activation.RemainingEstimate = 0;
                _operations.Remove(activation.Operation);
                cellsCompleted++;
            }
            else
            {
                _pending.Enqueue(activation);
            }
        }

        LastFrameMetrics = new CellActivationQueueFrameMetrics(
            cellsProcessed,
            cellsCompleted,
            _pending.Count,
            costConsumed,
            _queuedEstimatedCost,
            timer.Elapsed.TotalMilliseconds);
        return LastFrameMetrics;
    }

    /// <summary>Removes a queued cell, disposing its partial activation and prepared CPU data.</summary>
    public bool Cancel(WorldCellLoadOperation<TPrepared, TActive> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!_operations.Remove(operation)) return false;

        var count = _pending.Count;
        PendingActivation? canceled = null;
        for (var index = 0; index < count; index++)
        {
            var activation = _pending.Dequeue();
            if (canceled is null && ReferenceEquals(activation.Operation, operation)) canceled = activation;
            else _pending.Enqueue(activation);
        }

        if (canceled is null) return false;
        _queuedEstimatedCost -= canceled.RemainingEstimate;
        operation.CancelIncrementalActivation(canceled.Stepper);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        while (_pending.TryDequeue(out var activation))
        {
            _operations.Remove(activation.Operation);
            _queuedEstimatedCost -= activation.RemainingEstimate;
            try { activation.Operation.CancelIncrementalActivation(activation.Stepper); }
            catch (Exception exception) { (failures ??= new()).Add(exception); }
        }
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private sealed class PendingActivation(
        WorldCellLoadOperation<TPrepared, TActive> operation,
        ICellActivationStepper<TPrepared, TActive> stepper,
        long remainingEstimate)
    {
        public WorldCellLoadOperation<TPrepared, TActive> Operation { get; } = operation;
        public ICellActivationStepper<TPrepared, TActive> Stepper { get; } = stepper;
        public long RemainingEstimate { get; set; } = remainingEstimate;
    }
}

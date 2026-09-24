using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ember.World;

/// <summary>Configuration for exterior-cell request, activation, and retry budgets.</summary>
public sealed record WorldCellStreamingOptions
{
    public int LoadingRadiusInCells { get; init; } = 1;
    public int RetentionRadiusInCells { get; init; } = 2;
    public long MaximumActivationCostPerFrame { get; init; } = 8;
    public int MaximumActivationStepsPerFrame { get; init; } = 4;
    public TimeSpan MaximumActivationTimePerFrame { get; init; } = TimeSpan.FromMilliseconds(4);
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RetryMaximumDelay { get; init; } = TimeSpan.FromSeconds(8);
    public int MaximumStartupAttempts { get; init; } = 3;
}

/// <summary>
/// Coordinates exterior-cell preparation, bounded owner-thread activation, retry, and retirement.
/// Cell-specific resource creation stays in the supplied delegates and activation stepper.
/// </summary>
public sealed class WorldCellStreamer<TPrepared, TActive> : IDisposable
    where TPrepared : class, IDisposable, ICellActivationCost
    where TActive : class, IDisposable
{
    private readonly WorldManifest _world;
    private readonly Func<ExteriorCellCoordinate, Guid, CancellationToken, Task<TPrepared>> _prepare;
    private readonly Func<ExteriorCellCoordinate, ICellActivationStepper<TPrepared, TActive>> _createStepper;
    private readonly Action<ExteriorCellCoordinate, bool>? _collisionAvailabilityChanged;
    private readonly WorldCellStreamingOptions _options;
    private readonly ExteriorCellLoadingRing _loadingRing;
    private readonly CellActivationQueue<TPrepared, TActive> _activationQueue;
    private readonly Dictionary<ExteriorCellCoordinate, CellOperation> _cells = new();
    private readonly List<RetiredOperation> _retired = new();
    private ExteriorCellCoordinate? _statusCell;
    private bool _disposed;

    public WorldCellStreamer(WorldManifest world,
        Func<ExteriorCellCoordinate, Guid, CancellationToken, Task<TPrepared>> prepare,
        Func<ExteriorCellCoordinate, ICellActivationStepper<TPrepared, TActive>> createStepper,
        Action<ExteriorCellCoordinate, bool>? collisionAvailabilityChanged = null,
        WorldCellStreamingOptions? options = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        _createStepper = createStepper ?? throw new ArgumentNullException(nameof(createStepper));
        _collisionAvailabilityChanged = collisionAvailabilityChanged;
        _options = options ?? new WorldCellStreamingOptions();
        if (_options.RetryBaseDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "RetryBaseDelay must be positive.");
        if (_options.RetryMaximumDelay < _options.RetryBaseDelay)
            throw new ArgumentOutOfRangeException(nameof(options), "RetryMaximumDelay cannot be smaller than RetryBaseDelay.");
        if (_options.MaximumStartupAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumStartupAttempts must be positive.");

        _loadingRing = world.CreateLoadingRing(_options.LoadingRadiusInCells,
            _options.RetentionRadiusInCells);
        _activationQueue = new CellActivationQueue<TPrepared, TActive>(
            _options.MaximumActivationCostPerFrame,
            _options.MaximumActivationStepsPerFrame,
            _options.MaximumActivationTimePerFrame);
    }

    public IEnumerable<TActive> ActiveCells
    {
        get
        {
            foreach (var entry in _cells.Values)
                if (entry.Operation.State == CellLifecycleState.Active
                    && entry.Operation.ActiveResources is { } active)
                    yield return active;
        }
    }

    public int ActiveCellCount
    {
        get
        {
            var count = 0;
            foreach (var entry in _cells.Values)
                if (entry.Operation.State == CellLifecycleState.Active) count++;
            return count;
        }
    }

    public int ActivationAttemptCount { get; private set; }
    public double LongestActivationMilliseconds { get; private set; }
    public string? LoadingStatus { get; private set; }

    /// <summary>Raised after a failed attempt when the retry delay has been selected.</summary>
    public event Action<ExteriorCellCoordinate, Exception, TimeSpan>? RetryScheduled;

    /// <summary>Returns the active operation for a streamed exterior cell, for a travel transaction.</summary>
    public bool TryGetActiveOperation(Guid cellId,
        out WorldCellLoadOperation<TPrepared, TActive>? operation)
    {
        ThrowIfDisposed();
        operation = null;
        var definition = _world.FindCell(cellId);
        if (definition?.Kind != WorldCellKind.Exterior || definition.ExteriorCoordinate is not { } coordinate
            || !_cells.TryGetValue(coordinate, out var entry)
            || entry.Operation.State != CellLifecycleState.Active)
            return false;
        operation = entry.Operation;
        return true;
    }

    /// <summary>Removes an operation already unloaded by a travel transaction.</summary>
    public bool ForgetUnloadedCell(Guid cellId)
    {
        ThrowIfDisposed();
        var definition = _world.FindCell(cellId);
        if (definition?.Kind != WorldCellKind.Exterior || definition.ExteriorCoordinate is not { } coordinate
            || !_cells.TryGetValue(coordinate, out var entry))
            return false;

        if (entry.Operation.State == CellLifecycleState.Failed)
            entry.Operation.Discard();
        else if (entry.Operation.State != CellLifecycleState.Unloaded)
            throw new InvalidOperationException(
                $"Cell {cellId} must be unloaded before it can be forgotten; current state is {entry.Operation.State}.");

        _cells.Remove(coordinate);
        _loadingRing.Forget(coordinate);
        NotifyCollisionAvailability(coordinate, false);
        if (_statusCell == coordinate)
        {
            _statusCell = null;
            LoadingStatus = null;
        }
        return true;
    }

    /// <summary>Stops and removes a tracked exterior cell before travel prepares a fresh destination operation.</summary>
    public bool UnloadAndForgetCell(Guid cellId)
    {
        ThrowIfDisposed();
        var definition = _world.FindCell(cellId);
        if (definition?.Kind != WorldCellKind.Exterior || definition.ExteriorCoordinate is not { } coordinate
            || !_cells.Remove(coordinate, out var entry))
            return false;

        NotifyCollisionAvailability(coordinate, false);
        _loadingRing.Forget(coordinate);
        if (_statusCell == coordinate)
        {
            _statusCell = null;
            LoadingStatus = null;
        }

        if (entry.Operation.State == CellLifecycleState.Preparing)
        {
            entry.Operation.Cancel();
            _retired.Add(new RetiredOperation(entry.Operation, entry.Preparation));
        }
        else
        {
            StopOperation(entry);
        }
        return true;
    }

    /// <summary>Adopts a destination operation committed by a travel transaction into exterior streaming.</summary>
    public void AdoptActiveCell(Guid cellId, WorldCellLoadOperation<TPrepared, TActive> operation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);
        var definition = _world.FindCell(cellId)
            ?? throw new InvalidOperationException($"World manifest has no cell with ID {cellId}.");
        if (definition.Kind != WorldCellKind.Exterior || definition.ExteriorCoordinate is not { } coordinate)
            throw new ArgumentException("Only an authored exterior cell can be adopted by the exterior streamer.", nameof(cellId));
        if (operation.OwningThreadId != Environment.CurrentManagedThreadId
            || operation.State != CellLifecycleState.Active)
            throw new InvalidOperationException("The adopted operation must be active on the current owning thread.");
        if (!_cells.TryAdd(coordinate, new CellOperation(operation)
            {
                CollisionReady = true
            }))
            throw new InvalidOperationException($"Exterior cell ({coordinate.X}, {coordinate.Z}) is already tracked.");
        NotifyCollisionAvailability(coordinate, true);
    }

    /// <summary>Requests nearby cells and waits for the containing cell to become active.</summary>
    public void Start(Vector3 playerPosition)
    {
        ThrowIfDisposed();
        UpdateRequests(playerPosition);
        var coordinate = _world.GetExteriorCoordinate(playerPosition);
        if (!_cells.TryGetValue(coordinate, out var center))
            throw new InvalidOperationException($"No exterior cell exists at ({coordinate.X}, {coordinate.Z}).");

        while (center.Operation.State != CellLifecycleState.Active)
        {
            if (center.Operation.State == CellLifecycleState.Preparing)
                center.Preparation.GetAwaiter().GetResult();

            PumpOperations();
            if (center.Operation.State != CellLifecycleState.Failed) continue;
            if (center.RetryCount >= _options.MaximumStartupAttempts)
                throw new InvalidOperationException(
                    $"The starting cell ({coordinate.X}, {coordinate.Z}) failed after {center.RetryCount} attempts.",
                    center.Operation.Failure);
            var delay = center.RetryAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) Thread.Sleep(delay);
        }
    }

    /// <summary>Updates requests, advances ready activations within their frame budgets, and retires old cells.</summary>
    public void Update(Vector3 playerPosition)
    {
        ThrowIfDisposed();
        UpdateRequests(playerPosition);
        PumpOperations();
        DrainRetired();
    }

    /// <summary>Requests one authored exterior cell, for example from a collision-gate callback.</summary>
    public void Request(ExteriorCellCoordinate coordinate)
    {
        ThrowIfDisposed();
        if (_cells.ContainsKey(coordinate)
            || !_world.TryGetExterior(coordinate, out var definition)
            || definition is null)
            return;

        var entry = new CellOperation(new WorldCellLoadOperation<TPrepared, TActive>());
        _cells.Add(coordinate, entry);
        BeginPreparation(coordinate, definition.Id, entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { _activationQueue.Dispose(); }
        catch (Exception exception) { (failures ??= new()).Add(exception); }

        foreach (var pair in _cells.ToArray())
        {
            NotifyCollisionAvailability(pair.Key, false);
            try
            {
                if (pair.Value.Operation.State == CellLifecycleState.Preparing)
                {
                    pair.Value.Operation.Cancel();
                    _retired.Add(new RetiredOperation(pair.Value.Operation, pair.Value.Preparation));
                }
                else
                {
                    StopOperation(pair.Value);
                }
            }
            catch (Exception exception) { (failures ??= new()).Add(exception); }
        }
        _cells.Clear();

        foreach (var retired in _retired)
        {
            try
            {
                retired.Preparation.GetAwaiter().GetResult();
                retired.Operation.PumpCompletions();
            }
            catch (Exception exception) { (failures ??= new()).Add(exception); }
        }
        _retired.Clear();
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    private void BeginPreparation(ExteriorCellCoordinate coordinate, Guid cellId, CellOperation entry)
    {
        entry.Preparation = entry.Operation.PrepareAsync(token => _prepare(coordinate, cellId, token));
    }

    private void UpdateRequests(Vector3 playerPosition)
    {
        var update = _loadingRing.UpdatePlayerPosition(playerPosition);
        foreach (var coordinate in update.Entered) Request(coordinate);
        foreach (var coordinate in update.Left)
        {
            if (!_cells.Remove(coordinate, out var leaving)) continue;
            NotifyCollisionAvailability(coordinate, false);
            if (leaving.Operation.State == CellLifecycleState.Preparing)
            {
                leaving.Operation.Cancel();
                _retired.Add(new RetiredOperation(leaving.Operation, leaving.Preparation));
            }
            else
            {
                StopOperation(leaving);
            }
        }
    }

    private void PumpOperations()
    {
        foreach (var pair in _cells.ToArray())
        {
            var entry = pair.Value;
            var operation = entry.Operation;
            operation.PumpCompletions();
            if (operation.State != CellLifecycleState.Ready || entry.ActivationQueued) continue;

            var innerStepper = _createStepper(pair.Key)
                ?? throw new InvalidOperationException("Cell stepper factory returned null.");
            var stepper = new TimedStepper(innerStepper, this);
            try
            {
                _activationQueue.Enqueue(operation, stepper);
                entry.ActivationQueued = true;
                entry.Stepper = stepper;
            }
            catch
            {
                stepper.Dispose();
                throw;
            }
        }

        var frameClock = Stopwatch.StartNew();
        try { _activationQueue.ProcessFrame(); }
        catch (Exception)
        {
            // The operation stores the activation exception; the retry pass below reports it once per attempt.
        }
        finally
        {
            frameClock.Stop();
            LongestActivationMilliseconds = Math.Max(LongestActivationMilliseconds,
                frameClock.Elapsed.TotalMilliseconds);
            foreach (var pair in _cells)
            {
                var entry = pair.Value;
                if (entry.Operation.State != CellLifecycleState.Ready)
                    entry.ActivationQueued = false;
                if (entry.Operation.State == CellLifecycleState.Active && !entry.CollisionReady)
                {
                    NotifyCollisionAvailability(pair.Key, true);
                    entry.CollisionReady = true;
                    if (_statusCell == pair.Key)
                    {
                        _statusCell = null;
                        LoadingStatus = null;
                    }
                }
            }
        }

        foreach (var pair in _cells)
        {
            var entry = pair.Value;
            if (entry.Operation.State != CellLifecycleState.Failed) continue;
            ReportFailure(pair.Key, entry, entry.Operation.Failure
                ?? new InvalidOperationException("Cell operation failed without an exception."));
            if (DateTimeOffset.UtcNow < entry.RetryAt) continue;

            entry.Operation.Discard();
            entry.FailureReported = false;
            if (_world.TryGetExterior(pair.Key, out var definition) && definition is not null)
                BeginPreparation(pair.Key, definition.Id, entry);
        }
    }

    private void ReportFailure(ExteriorCellCoordinate coordinate, CellOperation entry, Exception failure)
    {
        if (entry.FailureReported) return;
        entry.RetryCount++;
        var exponent = Math.Min(entry.RetryCount - 1, 30);
        var milliseconds = Math.Min(
            _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, exponent),
            _options.RetryMaximumDelay.TotalMilliseconds);
        var delay = TimeSpan.FromMilliseconds(milliseconds);
        entry.RetryAt = DateTimeOffset.UtcNow + delay;
        _statusCell = coordinate;
        LoadingStatus = $"Cell ({coordinate.X}, {coordinate.Z}) failed; retrying in {delay.TotalSeconds:0.##}s";
        entry.FailureReported = true;
        RetryScheduled?.Invoke(coordinate, failure, delay);
    }

    private void StopOperation(CellOperation entry)
    {
        if (entry.ActivationQueued)
        {
            _activationQueue.Cancel(entry.Operation);
            entry.ActivationQueued = false;
            entry.Stepper = null;
        }

        switch (entry.Operation.State)
        {
            case CellLifecycleState.Active:
                entry.Operation.Unload();
                break;
            case CellLifecycleState.Ready:
            case CellLifecycleState.Failed:
                entry.Operation.Discard();
                break;
            case CellLifecycleState.Preparing:
                entry.Operation.Cancel();
                break;
        }
    }

    private void DrainRetired()
    {
        for (var index = _retired.Count - 1; index >= 0; index--)
        {
            var retired = _retired[index];
            if (!retired.Preparation.IsCompleted) continue;
            retired.Operation.PumpCompletions();
            _retired.RemoveAt(index);
        }
    }

    private void NotifyCollisionAvailability(ExteriorCellCoordinate coordinate, bool available)
    {
        _collisionAvailabilityChanged?.Invoke(coordinate, available);
    }

    private void RecordStep(double elapsedMilliseconds)
    {
        ActivationAttemptCount++;
        LongestActivationMilliseconds = Math.Max(LongestActivationMilliseconds, elapsedMilliseconds);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class CellOperation(WorldCellLoadOperation<TPrepared, TActive> operation)
    {
        public WorldCellLoadOperation<TPrepared, TActive> Operation { get; } = operation;
        public Task Preparation { get; set; } = Task.CompletedTask;
        public bool ActivationQueued { get; set; }
        public bool CollisionReady { get; set; }
        public bool FailureReported { get; set; }
        public int RetryCount { get; set; }
        public DateTimeOffset RetryAt { get; set; }
        public ICellActivationStepper<TPrepared, TActive>? Stepper { get; set; }
    }

    private sealed record RetiredOperation(
        WorldCellLoadOperation<TPrepared, TActive> Operation,
        Task Preparation);

    private sealed class TimedStepper(
        ICellActivationStepper<TPrepared, TActive> inner,
        WorldCellStreamer<TPrepared, TActive> owner) : ICellActivationStepper<TPrepared, TActive>
    {
        private bool _disposed;

        public CellActivationStepResult<TActive> Step(TPrepared prepared, long maximumCost)
        {
            var timer = Stopwatch.StartNew();
            try { return inner.Step(prepared, maximumCost); }
            finally
            {
                timer.Stop();
                owner.RecordStep(timer.Elapsed.TotalMilliseconds);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            inner.Dispose();
        }
    }
}

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ember.World;

/// <summary>
/// Runs CPU preparation on a worker and commits active resources on the owning thread.
/// Prepared and active objects remain private until their lifecycle state is ready or active.
/// </summary>
public sealed class WorldCellLoadOperation<TPrepared, TActive>
    where TPrepared : IDisposable
    where TActive : IDisposable
{
    private readonly int _owningThreadId;
    private readonly CellLifecycle _lifecycle = new();
    private TActive? _activeResources;
    private bool _activationInProgress;

    public WorldCellLoadOperation(int? owningThreadId = null)
    {
        _owningThreadId = owningThreadId ?? Environment.CurrentManagedThreadId;
        if (_owningThreadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(owningThreadId), "Owning thread ID must be positive.");
    }

    public CellLifecycleState State => _lifecycle.State;
    public Exception? Failure => _lifecycle.Failure;
    public int OwningThreadId => _owningThreadId;
    public int? PreparationThreadId { get; private set; }
    public int? ActivationThreadId { get; private set; }
    public TActive? ActiveResources
    {
        get
        {
            EnsureOwningThread();
            return State == CellLifecycleState.Active ? _activeResources : default;
        }
    }

    /// <summary>Invokes the preparation delegate on a worker thread and stores its result as ready data.</summary>
    public async Task PrepareAsync(Func<CancellationToken, Task<TPrepared>> prepare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        EnsureOwningThread();
        _lifecycle.TransitionTo(CellLifecycleState.Preparing);
        TPrepared? preparedValue = default;
        var attachedToLifecycle = false;
        try
        {
            var result = await Task.Run(async () =>
            {
                var threadId = Environment.CurrentManagedThreadId;
                var value = await prepare(cancellationToken).ConfigureAwait(false);
                return (Value: value, ThreadId: threadId);
            }, cancellationToken).ConfigureAwait(false);

            preparedValue = result.Value;
            if (preparedValue is null)
                throw new InvalidOperationException("Cell preparation returned no data.");
            PreparationThreadId = result.ThreadId;
            _lifecycle.SetPreparationResource(preparedValue);
            attachedToLifecycle = true;
            _lifecycle.TransitionTo(CellLifecycleState.Ready);
            preparedValue = default;
        }
        catch (Exception exception)
        {
            if (!attachedToLifecycle) preparedValue?.Dispose();
            if (State is CellLifecycleState.Preparing or CellLifecycleState.Ready)
                _lifecycle.MarkFailed(exception);
            throw;
        }
    }

    /// <summary>
    /// Builds and publishes active resources only after the owner-thread callback succeeds.
    /// The callback must release allocations it made itself if it throws before returning them.
    /// </summary>
    public TActive Activate(Func<TPrepared, TActive> activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        EnsureOwningThread();
        if (State != CellLifecycleState.Ready)
            throw new InvalidOperationException($"Only a ready cell can activate; current state is {State}.");
        if (_activationInProgress)
            throw new InvalidOperationException("Cell activation is already in progress.");

        _activationInProgress = true;
        try
        {
            ActivateOnOwningThread(activate);
        }
        finally
        {
            _activationInProgress = false;
        }
        return _activeResources!;
    }

    /// <summary>Disposes active resources and returns the operation to the unloaded state.</summary>
    public void Unload()
    {
        EnsureOwningThread();
        if (State != CellLifecycleState.Active)
            throw new InvalidOperationException($"Only an active cell can unload; current state is {State}.");

        _lifecycle.TransitionTo(CellLifecycleState.Unloading);
        var active = _activeResources;
        _activeResources = default;
        try
        {
            active?.Dispose();
            _lifecycle.TransitionTo(CellLifecycleState.Unloaded);
        }
        catch (Exception exception)
        {
            _lifecycle.MarkFailed(exception);
            throw;
        }
    }

    private void ActivateOnOwningThread(Func<TPrepared, TActive> activate)
    {
        var prepared = (TPrepared?)_lifecycle.TakePreparationResource()
            ?? throw new InvalidOperationException("Ready cell has no prepared data.");
        TActive? activated = default;
        Exception? failure = null;
        try
        {
            activated = activate(prepared);
            if (activated is null)
                throw new InvalidOperationException("Cell activation returned no active resources.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            prepared.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        if (failure is not null)
        {
            DisposeAfterFailure(activated, ref failure);
            _lifecycle.MarkFailed(failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        try
        {
            _activeResources = activated;
            _lifecycle.TransitionTo(CellLifecycleState.Active);
            ActivationThreadId = Environment.CurrentManagedThreadId;
        }
        catch (Exception exception)
        {
            _activeResources = default;
            DisposeAfterFailure(activated, ref exception);
            _lifecycle.MarkFailed(exception);
            throw;
        }
    }

    private void EnsureOwningThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _owningThreadId)
            throw new InvalidOperationException(
                $"Cell access must run on owning thread {_owningThreadId}; current thread is {currentThreadId}.");
    }

    private static void DisposeAfterFailure(TActive? resource, ref Exception failure)
    {
        if (resource is null) return;
        try
        {
            resource.Dispose();
        }
        catch (Exception disposeException)
        {
            failure = new AggregateException(failure, disposeException);
        }
    }
}

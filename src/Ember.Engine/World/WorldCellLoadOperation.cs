using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ember.World;

/// <summary>
/// Runs CPU preparation on a worker and commits lifecycle changes/resources on the owning thread.
/// Await <see cref="PrepareAsync"/> for worker completion, then call <see cref="PumpCompletions"/>
/// on the owner thread to publish Ready or Failed. A game loop should pump once per frame.
/// </summary>
public sealed class WorldCellLoadOperation<TPrepared, TActive>
    where TPrepared : IDisposable
    where TActive : IDisposable
{
    private readonly int _owningThreadId;
    private readonly CellLifecycle _lifecycle = new();
    private readonly ConcurrentQueue<PreparationCompletion> _completions = new();
    private TActive? _activeResources;
    private CancellationTokenSource? _preparationCancellation;
    private ICellActivationStepper<TPrepared, TActive>? _incrementalActivation;
    private long _generation;
    private bool _activationInProgress;

    public WorldCellLoadOperation(int? owningThreadId = null)
    {
        _owningThreadId = owningThreadId ?? Environment.CurrentManagedThreadId;
        if (_owningThreadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(owningThreadId), "Owning thread ID must be positive.");
    }

    public CellLifecycleState State
    {
        get
        {
            EnsureOwningThread();
            return _lifecycle.State;
        }
    }

    public Exception? Failure
    {
        get
        {
            EnsureOwningThread();
            return _lifecycle.Failure;
        }
    }

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

    internal long PreparedActivationCost
    {
        get
        {
            EnsureOwningThread();
            if (_lifecycle.GetPreparationResource() is not ICellActivationCost cost)
                throw new InvalidOperationException(
                    $"Prepared data must implement {nameof(ICellActivationCost)} before it can enter the activation queue.");
            if (cost.EstimatedActivationCost < 0)
                throw new InvalidOperationException("Prepared activation cost cannot be negative.");
            return cost.EstimatedActivationCost;
        }
    }

    /// <summary>
    /// Starts CPU-only preparation on a worker. This task completes after a result is queued;
    /// it does not change <see cref="State"/>. The owner must call <see cref="PumpCompletions"/>.
    /// Preparation must not create or mutate graphics-device resources.
    /// </summary>
    public Task PrepareAsync(Func<CancellationToken, Task<TPrepared>> prepare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        EnsureOwningThread();

        if (_lifecycle.State == CellLifecycleState.Failed)
            _lifecycle.TransitionTo(CellLifecycleState.Unloaded);
        if (_lifecycle.State != CellLifecycleState.Unloaded)
            throw new InvalidOperationException($"Only an unloaded or failed cell can prepare; current state is {_lifecycle.State}.");

        var generation = checked(++_generation);
        _lifecycle.TransitionTo(CellLifecycleState.Preparing);
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _preparationCancellation = source;
        return Task.Run(async () =>
        {
            var threadId = Environment.CurrentManagedThreadId;
            TPrepared? prepared = default;
            Exception? failure = null;
            try
            {
                prepared = await prepare(source.Token).ConfigureAwait(false);
                if (prepared is null)
                    throw new InvalidOperationException("Cell preparation returned no data.");
                source.Token.ThrowIfCancellationRequested();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            // This is the worker's only operation on the load operation. Lifecycle data and
            // resource ownership are committed later by PumpCompletions on the owner thread.
            _completions.Enqueue(new PreparationCompletion(generation, threadId, prepared, failure, source));
        }, CancellationToken.None);
    }

    /// <summary>Commits queued worker results on the owner thread and returns the number drained.</summary>
    public int PumpCompletions()
    {
        EnsureOwningThread();
        var drained = 0;
        while (_completions.TryDequeue(out var completion))
        {
            drained++;
            if (completion.Generation != _generation || _lifecycle.State != CellLifecycleState.Preparing)
            {
                completion.Prepared?.Dispose();
                completion.Cancellation.Dispose();
                continue;
            }

            PreparationThreadId = completion.ThreadId;
            if (ReferenceEquals(_preparationCancellation, completion.Cancellation))
                _preparationCancellation = null;

            if (completion.Failure is { } failure)
            {
                try
                {
                    completion.Prepared?.Dispose();
                }
                catch (Exception disposeException)
                {
                    failure = new AggregateException(failure, disposeException);
                }

                _lifecycle.MarkFailed(failure);
                completion.Cancellation.Dispose();
                continue;
            }

            var attachedToLifecycle = false;
            try
            {
                var prepared = completion.Prepared
                    ?? throw new InvalidOperationException("Cell preparation returned no data.");
                _lifecycle.SetPreparationResource(prepared);
                attachedToLifecycle = true;
                _lifecycle.TransitionTo(CellLifecycleState.Ready);
            }
            catch (Exception exception)
            {
                if (!attachedToLifecycle)
                {
                    try { completion.Prepared?.Dispose(); }
                    catch (Exception disposeException)
                    {
                        exception = new AggregateException(exception, disposeException);
                    }
                }
                if (_lifecycle.State is CellLifecycleState.Preparing or CellLifecycleState.Ready)
                    _lifecycle.MarkFailed(exception);
                else
                    throw;
            }
            finally
            {
                completion.Cancellation.Dispose();
            }
        }

        return drained;
    }

    /// <summary>Discards ready data or clears a failed attempt; a later PrepareAsync retries the operation.</summary>
    public void Discard()
    {
        EnsureOwningThread();
        if (_lifecycle.State == CellLifecycleState.Ready)
        {
            if (_activationInProgress)
                throw new InvalidOperationException("Cancel incremental activation before discarding its prepared cell.");
            _lifecycle.TransitionTo(CellLifecycleState.Unloading);
            _lifecycle.TransitionTo(CellLifecycleState.Unloaded);
            return;
        }
        if (_lifecycle.State == CellLifecycleState.Failed)
        {
            _lifecycle.TransitionTo(CellLifecycleState.Unloaded);
            return;
        }
        throw new InvalidOperationException($"Only a ready or failed cell can be discarded; current state is {_lifecycle.State}.");
    }

    /// <summary>Performs one bounded owner-thread activation step.</summary>
    internal CellActivationStepResult<TActive> ActivateStep(
        ICellActivationStepper<TPrepared, TActive> stepper, long maximumCost)
    {
        ArgumentNullException.ThrowIfNull(stepper);
        EnsureOwningThread();
        if (maximumCost <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCost), "Activation step budget must be positive.");
        if (_lifecycle.State != CellLifecycleState.Ready)
            throw new InvalidOperationException($"Only a ready cell can activate; current state is {_lifecycle.State}.");
        if (_incrementalActivation is not null && !ReferenceEquals(_incrementalActivation, stepper))
            throw new InvalidOperationException("An incremental cell activation must keep using its original stepper.");

        _incrementalActivation = stepper;
        _activationInProgress = true;
        try
        {
            var prepared = (TPrepared?)_lifecycle.GetPreparationResource()
                ?? throw new InvalidOperationException("Ready cell has no prepared data.");
            var result = stepper.Step(prepared, maximumCost);
            if (result.CostConsumed < 0 || result.CostConsumed > maximumCost)
                throw new InvalidOperationException("Activation step consumed an invalid amount of its cost budget.");
            if (!result.IsComplete && result.CostConsumed == 0)
                throw new InvalidOperationException("An incomplete activation step must consume positive cost.");
            if (!result.IsComplete && result.ActiveResources is not null)
                throw new InvalidOperationException("An incomplete activation step cannot publish active resources.");
            if (!result.IsComplete) return result;
            if (result.ActiveResources is null)
                throw new InvalidOperationException("A completed activation step must return active resources.");

            CompleteIncrementalActivation(prepared, result.ActiveResources, stepper);
            return result;
        }
        catch (Exception exception)
        {
            FailIncrementalActivation(stepper, exception);
            throw;
        }
    }

    /// <summary>Cancels queued or in-progress incremental activation and disposes its partial work.</summary>
    internal void CancelIncrementalActivation()
    {
        EnsureOwningThread();
        if (_lifecycle.State != CellLifecycleState.Ready)
            throw new InvalidOperationException($"Only a ready cell can cancel activation; current state is {_lifecycle.State}.");
        if (!_activationInProgress)
        {
            Discard();
            return;
        }

        var stepper = _incrementalActivation;
        _incrementalActivation = null;
        _activationInProgress = false;
        Exception? failure = null;
        try { stepper?.Dispose(); }
        catch (Exception exception) { failure = exception; }
        try { Discard(); }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    /// Cancels preparation and immediately invalidates its generation. Any late worker result is
    /// disposed by the owner thread when <see cref="PumpCompletions"/> drains it.
    /// </summary>
    public void Cancel()
    {
        EnsureOwningThread();
        if (_lifecycle.State != CellLifecycleState.Preparing)
            throw new InvalidOperationException($"Only a preparing cell can be canceled; current state is {_lifecycle.State}.");

        var cancellation = _preparationCancellation;
        _preparationCancellation = null;
        _generation = checked(_generation + 1);
        try
        {
            cancellation?.Cancel();
        }
        finally
        {
            _lifecycle.TransitionTo(CellLifecycleState.Unloading);
            _lifecycle.TransitionTo(CellLifecycleState.Unloaded);
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

    private void CompleteIncrementalActivation(TPrepared prepared, TActive active,
        ICellActivationStepper<TPrepared, TActive> stepper)
    {
        Exception? failure = null;
        try { stepper.Dispose(); }
        catch (Exception exception) { failure = exception; }
        finally { _incrementalActivation = null; }
        try { prepared.Dispose(); }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        _lifecycle.TakePreparationResource();
        if (failure is not null)
        {
            DisposeAfterFailure(active, ref failure);
            _activationInProgress = false;
            try { _lifecycle.MarkFailed(failure); }
            catch (Exception disposeException) { failure = new AggregateException(failure, disposeException); }
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        try
        {
            _activeResources = active;
            _lifecycle.TransitionTo(CellLifecycleState.Active);
            ActivationThreadId = Environment.CurrentManagedThreadId;
        }
        catch (Exception exception)
        {
            _activeResources = default;
            DisposeAfterFailure(active, ref exception);
            _lifecycle.MarkFailed(exception);
            throw;
        }
        finally
        {
            _incrementalActivation = null;
            _activationInProgress = false;
        }
    }

    private void FailIncrementalActivation(ICellActivationStepper<TPrepared, TActive> stepper,
        Exception failure)
    {
        var disposeStepper = ReferenceEquals(_incrementalActivation, stepper);
        _incrementalActivation = null;
        _activationInProgress = false;
        if (disposeStepper)
        {
            try { stepper.Dispose(); }
            catch (Exception disposeException) { failure = new AggregateException(failure, disposeException); }
        }
        if (_lifecycle.State is CellLifecycleState.Preparing or CellLifecycleState.Ready or CellLifecycleState.Unloading)
        {
            try { _lifecycle.MarkFailed(failure); }
            catch (Exception disposeException) { failure = new AggregateException(failure, disposeException); }
        }
        ExceptionDispatchInfo.Capture(failure).Throw();
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

    private sealed record PreparationCompletion(long Generation, int ThreadId,
        TPrepared? Prepared, Exception? Failure, CancellationTokenSource Cancellation);
}

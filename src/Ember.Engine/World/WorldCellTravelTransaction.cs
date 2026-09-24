using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ember.World;

public enum WorldCellTravelState
{
    PreparingDestination,
    DestinationReady,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Loads and activates a destination while the source remains playable, then moves the player
/// and releases the source only after destination activation succeeds.
/// </summary>
public sealed class WorldCellTravelTransaction<TPrepared, TActive>
    where TPrepared : IDisposable
    where TActive : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly Guid _sourceCellId;
    private readonly WorldCellLoadOperation<TPrepared, TActive> _source;
    private readonly WorldCellLoadOperation<TPrepared, TActive> _destination;
    private readonly WorldSpawnLocation _destinationSpawn;
    private readonly Func<TPrepared, TActive> _activate;
    private readonly Action<WorldSpawnLocation> _placePlayer;
    private readonly Task _preparationTask;

    public WorldCellTravelTransaction(Guid sourceCellId,
        WorldCellLoadOperation<TPrepared, TActive> source,
        WorldCellLoadOperation<TPrepared, TActive> destination,
        WorldSpawnLocation destinationSpawn,
        Func<Guid, CancellationToken, Task<TPrepared>> prepareDestination,
        Func<TPrepared, TActive> activateDestination,
        Action<WorldSpawnLocation> placePlayer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(prepareDestination);
        ArgumentNullException.ThrowIfNull(activateDestination);
        ArgumentNullException.ThrowIfNull(placePlayer);
        if (sourceCellId == Guid.Empty)
            throw new ArgumentException("Source cell ID cannot be empty.", nameof(sourceCellId));
        if (destinationSpawn.CellId == Guid.Empty || destinationSpawn.SpawnId == Guid.Empty)
            throw new ArgumentException("Destination spawn must identify a cell and spawn point.", nameof(destinationSpawn));
        if (sourceCellId == destinationSpawn.CellId)
            throw new ArgumentException("Travel must target a different cell.", nameof(destinationSpawn));
        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Source and destination must use separate cell operations.", nameof(destination));

        _ownerThreadId = Environment.CurrentManagedThreadId;
        if (source.OwningThreadId != _ownerThreadId || destination.OwningThreadId != _ownerThreadId)
            throw new InvalidOperationException("Travel and both cell operations must use the current owning thread.");
        if (source.State != CellLifecycleState.Active)
            throw new InvalidOperationException($"Source cell must be active before travel; current state is {source.State}.");
        if (destination.State != CellLifecycleState.Unloaded)
            throw new InvalidOperationException($"Destination cell must be unloaded before travel; current state is {destination.State}.");

        _sourceCellId = sourceCellId;
        _source = source;
        _destination = destination;
        _destinationSpawn = destinationSpawn;
        _activate = activateDestination;
        _placePlayer = placePlayer;
        State = WorldCellTravelState.PreparingDestination;
        _preparationTask = destination.PrepareAsync(
            token => prepareDestination(destinationSpawn.CellId, token), cancellationToken);
    }

    public WorldCellTravelState State { get; private set; }
    public Exception? Failure { get; private set; }
    public Guid SourceCellId => _sourceCellId;
    public Guid DestinationCellId => _destinationSpawn.CellId;
    public WorldSpawnLocation DestinationSpawn => _destinationSpawn;

    /// <summary>Completes when worker preparation has queued its result; the owner must still call Tick.</summary>
    public Task PreparationTask => _preparationTask;

    /// <summary>
    /// Pumps destination preparation. The first call that observes Ready returns in
    /// DestinationReady, leaving one owner-thread boundary where travel can be canceled.
    /// A later call activates, places, and commits travel.
    /// </summary>
    public bool Tick()
    {
        EnsureOwnerThread();
        _destination.PumpCompletions();

        if (State is WorldCellTravelState.Completed or WorldCellTravelState.Failed
            or WorldCellTravelState.Cancelled)
            return State == WorldCellTravelState.Completed;

        if (State == WorldCellTravelState.PreparingDestination)
        {
            switch (_destination.State)
            {
                case CellLifecycleState.Preparing:
                    return false;
                case CellLifecycleState.Ready:
                    State = WorldCellTravelState.DestinationReady;
                    return false;
                case CellLifecycleState.Failed:
                    Failure = _destination.Failure
                        ?? new InvalidOperationException("Destination preparation failed without an error.");
                    State = WorldCellTravelState.Failed;
                    return false;
                default:
                    Failure = new InvalidOperationException(
                        $"Destination entered unexpected state {_destination.State} during travel preparation.");
                    State = WorldCellTravelState.Failed;
                    return false;
            }
        }

        try
        {
            _destination.Activate(_activate);
        }
        catch (Exception exception)
        {
            Failure = exception;
            State = WorldCellTravelState.Failed;
            return false;
        }

        try
        {
            _placePlayer(_destinationSpawn);
        }
        catch (Exception exception)
        {
            Failure = exception;
            try { _destination.Unload(); }
            catch (Exception cleanupException) { Failure = new AggregateException(exception, cleanupException); }
            State = WorldCellTravelState.Failed;
            return false;
        }

        try
        {
            _source.Unload();
        }
        catch (Exception exception)
        {
            // Destination activation and player placement already committed. Keep travel committed
            // so callers can adopt the destination, while exposing the source cleanup failure.
            Failure = exception;
        }

        State = WorldCellTravelState.Completed;
        return true;
    }

    /// <summary>Cancels preparation or discards a ready destination without touching the source cell.</summary>
    public void Cancel()
    {
        EnsureOwnerThread();
        if (State == WorldCellTravelState.PreparingDestination)
            _destination.Cancel();
        else if (State == WorldCellTravelState.DestinationReady)
            _destination.Discard();
        else
            throw new InvalidOperationException($"Travel cannot be canceled while it is {State}.");

        State = WorldCellTravelState.Cancelled;
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"Travel transaction access must run on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }
}

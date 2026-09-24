using System;
using System.Threading;
using System.Threading.Tasks;
using Ember.Scene;

namespace Ember.World;

public enum WorldNpcCellTransitionState
{
    PreparingDestination,
    DestinationReady,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Loads a route destination before transferring one runtime-owned NPC into it. The source cell
/// remains active because a player may still be using it.
/// </summary>
public sealed class WorldNpcCellTransition<TPrepared, TActive>
    where TPrepared : IDisposable
    where TActive : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly WorldPathTransition _transition;
    private readonly WorldInstanceId _npcInstanceId;
    private readonly WorldRuntimeObjectStore _runtimeObjects;
    private readonly WorldInstanceIdentityMap _identities;
    private readonly SceneGraph _sourceScene;
    private readonly WorldCellLoadOperation<TPrepared, TActive> _destination;
    private readonly Func<TPrepared, TActive> _activate;
    private readonly Func<TActive, SceneGraph> _getScene;
    private readonly Transform _destinationTransform;
    private readonly Task _preparationTask;

    public WorldNpcCellTransition(WorldPathTransition transition, WorldInstanceId npcInstanceId,
        WorldRuntimeObjectStore runtimeObjects, WorldInstanceIdentityMap identities, SceneGraph sourceScene,
        WorldCellLoadOperation<TPrepared, TActive> destination,
        Func<Guid, CancellationToken, Task<TPrepared>> prepareDestination,
        Func<TPrepared, TActive> activateDestination, Func<TActive, SceneGraph> getScene,
        Transform destinationTransform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeObjects);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(sourceScene);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(prepareDestination);
        ArgumentNullException.ThrowIfNull(activateDestination);
        ArgumentNullException.ThrowIfNull(getScene);
        ValidateTransition(transition);
        if (npcInstanceId.Value == Guid.Empty)
            throw new ArgumentException("NPC world instance ID cannot be empty.", nameof(npcInstanceId));

        _ownerThreadId = Environment.CurrentManagedThreadId;
        if (destination.OwningThreadId != _ownerThreadId)
            throw new InvalidOperationException("NPC travel and destination loading must use the current owning thread.");
        if (destination.State != CellLifecycleState.Unloaded)
            throw new InvalidOperationException(
                $"NPC travel destination must be unloaded before preparation; current state is {destination.State}.");

        _transition = transition;
        _npcInstanceId = npcInstanceId;
        _runtimeObjects = runtimeObjects;
        _identities = identities;
        _sourceScene = sourceScene;
        _destination = destination;
        _activate = activateDestination;
        _getScene = getScene;
        _destinationTransform = destinationTransform ?? throw new ArgumentNullException(nameof(destinationTransform));
        State = WorldNpcCellTransitionState.PreparingDestination;
        _preparationTask = destination.PrepareAsync(
            token => prepareDestination(transition.To.CellId, token), cancellationToken);
    }

    public WorldNpcCellTransitionState State { get; private set; }
    public Exception? Failure { get; private set; }
    public WorldPathTransition Transition => _transition;
    public WorldInstanceId NpcInstanceId => _npcInstanceId;
    public Task PreparationTask => _preparationTask;

    /// <summary>Pumps preparation and commits the ownership change after destination activation.</summary>
    public bool Tick()
    {
        EnsureOwnerThread();
        _destination.PumpCompletions();
        if (State is WorldNpcCellTransitionState.Completed or WorldNpcCellTransitionState.Failed
            or WorldNpcCellTransitionState.Cancelled)
            return State == WorldNpcCellTransitionState.Completed;

        if (State == WorldNpcCellTransitionState.PreparingDestination)
        {
            switch (_destination.State)
            {
                case CellLifecycleState.Preparing:
                    return false;
                case CellLifecycleState.Ready:
                    State = WorldNpcCellTransitionState.DestinationReady;
                    return false;
                case CellLifecycleState.Failed:
                    Failure = _destination.Failure
                        ?? new InvalidOperationException("NPC travel destination preparation failed without an error.");
                    State = WorldNpcCellTransitionState.Failed;
                    return false;
                default:
                    Failure = new InvalidOperationException(
                        $"NPC travel destination entered unexpected state {_destination.State}.");
                    State = WorldNpcCellTransitionState.Failed;
                    return false;
            }
        }

        try
        {
            var active = _destination.Activate(_activate);
            var destinationScene = _getScene(active)
                ?? throw new InvalidOperationException("NPC travel destination activation returned no scene.");
            _runtimeObjects.Transfer(_transition.From.CellId, _transition.To.CellId, _npcInstanceId,
                _sourceScene, destinationScene, _identities, _destinationTransform);
            State = WorldNpcCellTransitionState.Completed;
            return true;
        }
        catch (Exception exception)
        {
            Failure = exception;
            if (_destination.State == CellLifecycleState.Active)
            {
                try { _destination.Unload(); }
                catch (Exception cleanupException) { Failure = new AggregateException(exception, cleanupException); }
            }
            State = WorldNpcCellTransitionState.Failed;
            return false;
        }
    }

    public void Cancel()
    {
        EnsureOwnerThread();
        if (State == WorldNpcCellTransitionState.PreparingDestination)
            _destination.Cancel();
        else if (State == WorldNpcCellTransitionState.DestinationReady)
            _destination.Discard();
        else
            throw new InvalidOperationException($"NPC travel cannot be canceled while it is {State}.");
        State = WorldNpcCellTransitionState.Cancelled;
    }

    private static void ValidateTransition(WorldPathTransition transition)
    {
        if (transition is null || transition.ConnectionId == Guid.Empty
            || !Enum.IsDefined(transition.Kind)
            || transition.From.CellId == Guid.Empty || transition.From.NodeId == Guid.Empty
            || transition.To.CellId == Guid.Empty || transition.To.NodeId == Guid.Empty
            || transition.From.CellId == transition.To.CellId)
            throw new ArgumentException("NPC travel requires one valid cross-cell route transition.", nameof(transition));
        if (transition.Kind == WorldPathConnectionKind.Door
                ? !transition.DoorInstanceId.HasValue || transition.DoorInstanceId.Value == Guid.Empty
                : transition.DoorInstanceId.HasValue)
            throw new ArgumentException("NPC travel transition has an invalid door instance reference.", nameof(transition));
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"NPC travel must run on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }
}

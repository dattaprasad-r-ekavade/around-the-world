using System;
using System.Collections.Generic;
using System.Linq;

namespace Ember.World;

public enum CellLifecycleState
{
    Unloaded,
    Preparing,
    Ready,
    Active,
    Unloading,
    Failed
}

/// <summary>Validated state for one cell load and activation attempt.</summary>
public sealed class CellLifecycle
{
    private static readonly IReadOnlyDictionary<CellLifecycleState, CellLifecycleState[]> AllowedTransitions =
        new Dictionary<CellLifecycleState, CellLifecycleState[]>
        {
            [CellLifecycleState.Unloaded] = [CellLifecycleState.Preparing],
            [CellLifecycleState.Preparing] = [CellLifecycleState.Ready, CellLifecycleState.Unloading],
            [CellLifecycleState.Ready] = [CellLifecycleState.Active, CellLifecycleState.Unloading],
            [CellLifecycleState.Active] = [CellLifecycleState.Unloading],
            [CellLifecycleState.Unloading] = [CellLifecycleState.Unloaded],
            [CellLifecycleState.Failed] = [CellLifecycleState.Unloaded]
        };

    private IDisposable? _preparationResource;

    public CellLifecycleState State { get; private set; } = CellLifecycleState.Unloaded;
    public Exception? Failure { get; private set; }

    /// <summary>Owns temporary CPU preparation data until it is transferred or discarded.</summary>
    public void SetPreparationResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (State != CellLifecycleState.Preparing)
            throw new InvalidOperationException("Preparation resources can only be attached while a cell is preparing.");
        if (_preparationResource is not null)
            throw new InvalidOperationException("A cell already owns preparation resources.");
        _preparationResource = resource;
    }

    /// <summary>Transfers prepared data to the activation caller while the cell is ready.</summary>
    public IDisposable? TakePreparationResource()
    {
        if (State != CellLifecycleState.Ready)
            throw new InvalidOperationException("Preparation resources can only be taken from a ready cell.");
        var resource = _preparationResource;
        _preparationResource = null;
        return resource;
    }

    internal IDisposable? GetPreparationResource()
    {
        if (State != CellLifecycleState.Ready)
            throw new InvalidOperationException("Preparation resources can only be inspected while a cell is ready.");
        return _preparationResource;
    }

    public void TransitionTo(CellLifecycleState next)
    {
        if (next == CellLifecycleState.Failed)
            throw new InvalidOperationException("Use MarkFailed to record the loading error.");
        if (!AllowedTransitions[State].Contains(next))
            throw new InvalidOperationException($"Cell lifecycle cannot transition from {State} to {next}.");

        State = next;
        if (next == CellLifecycleState.Unloaded)
        {
            Failure = null;
            DisposePreparationResource();
        }
    }

    public void MarkFailed(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (State is not (CellLifecycleState.Preparing or CellLifecycleState.Ready or CellLifecycleState.Unloading))
            throw new InvalidOperationException($"Cell lifecycle cannot fail from {State}.");

        State = CellLifecycleState.Failed;
        Failure = failure;
        DisposePreparationResource();
    }

    private void DisposePreparationResource()
    {
        var resource = _preparationResource;
        _preparationResource = null;
        resource?.Dispose();
    }
}

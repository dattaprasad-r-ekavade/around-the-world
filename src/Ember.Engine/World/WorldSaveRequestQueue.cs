using System;
using System.Collections.Generic;

namespace Ember.World;

public readonly record struct WorldSaveRequestResult(Guid RequestId, string Path, Exception? Failure);

/// <summary>Defers world-save capture and disk writes until the simulation reports a stable boundary.</summary>
public sealed class WorldSaveRequestQueue
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Queue<PendingRequest> _pending = new();
    private readonly Queue<WorldSaveRequestResult> _completed = new();

    public int PendingCount
    {
        get
        {
            EnsureOwnerThread();
            return _pending.Count;
        }
    }

    public Guid Enqueue(string path, Func<WorldSaveSnapshot> capture)
    {
        EnsureOwnerThread();
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A world-save path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(capture);
        var requestId = Guid.NewGuid();
        _pending.Enqueue(new PendingRequest(requestId, path, capture));
        return requestId;
    }

    /// <summary>
    /// Processes at most one queued save when no travel transaction is preparing or waiting to commit.
    /// Capture runs here, so a request made during travel observes the post-travel or rolled-back state.
    /// </summary>
    public bool ProcessStableBoundary(bool travelInProgress)
    {
        EnsureOwnerThread();
        if (travelInProgress || !_pending.TryDequeue(out var request)) return false;

        try
        {
            var snapshot = request.Capture()
                ?? throw new InvalidOperationException("World save capture returned no snapshot.");
            WorldSaveFile.SaveAtomic(request.Path, snapshot);
            _completed.Enqueue(new WorldSaveRequestResult(request.Id, request.Path, null));
        }
        catch (Exception exception)
        {
            _completed.Enqueue(new WorldSaveRequestResult(request.Id, request.Path, exception));
        }

        return true;
    }

    public bool TryDequeueResult(out WorldSaveRequestResult result)
    {
        EnsureOwnerThread();
        return _completed.TryDequeue(out result);
    }

    private void EnsureOwnerThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"World save requests must be accessed on owning thread {_ownerThreadId}; current thread is {currentThreadId}.");
    }

    private sealed record PendingRequest(Guid Id, string Path, Func<WorldSaveSnapshot> Capture);
}

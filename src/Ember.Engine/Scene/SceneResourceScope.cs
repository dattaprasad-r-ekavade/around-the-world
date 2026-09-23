using System;
using System.Collections.Generic;

namespace Ember.Scene;

/// <summary>Owns disposable resources created for one loaded scene.</summary>
public sealed class SceneResourceScope : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    private readonly HashSet<IDisposable> _owned = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public bool IsDisposed => _disposed;

    /// <summary>Register and return a scene-created resource for convenient initialization.</summary>
    public T Own<T>(T resource) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_owned.Add(resource))
            throw new ArgumentException("The resource is already owned by this scene scope.", nameof(resource));

        _resources.Add(resource);
        return resource;
    }

    /// <summary>Release all resources in reverse registration order. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        List<Exception>? errors = null;
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            try { _resources[index].Dispose(); }
            catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
        }

        _resources.Clear();
        _owned.Clear();

        if (errors is not null)
            throw new AggregateException("One or more scene resources failed to dispose.", errors);
    }
}

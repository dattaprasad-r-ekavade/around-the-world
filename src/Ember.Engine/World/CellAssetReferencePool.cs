using System;
using System.Collections.Generic;

namespace Ember.World;

/// <summary>
/// Shares disposable runtime assets between active cells. Acquire and release on the resource-owning
/// thread; the asset is disposed when its last cell lease is released.
/// </summary>
public sealed class CellAssetReferencePool<TKey, TAsset> : IDisposable
    where TKey : notnull
    where TAsset : class, IDisposable
{
    private readonly int _owningThreadId;
    private readonly Dictionary<TKey, Entry> _entries;
    private long _totalReferences;
    private bool _disposed;

    public CellAssetReferencePool(IEqualityComparer<TKey>? comparer = null, int? owningThreadId = null)
    {
        _owningThreadId = owningThreadId ?? Environment.CurrentManagedThreadId;
        if (_owningThreadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(owningThreadId), "Owning thread ID must be positive.");
        _entries = new Dictionary<TKey, Entry>(comparer);
    }

    public int LiveAssetCount
    {
        get { EnsureOwningThread(); return _entries.Count; }
    }

    public long TotalReferenceCount
    {
        get { EnsureOwningThread(); return _totalReferences; }
    }

    public CellAssetReference<TKey, TAsset> Acquire(TKey key, Func<TAsset> createAsset)
    {
        ArgumentNullException.ThrowIfNull(createAsset);
        EnsureNotDisposed();
        if (_entries.TryGetValue(key, out var existing))
        {
            var nextReferenceCount = checked(existing.ReferenceCount + 1);
            var nextTotalReferences = checked(_totalReferences + 1);
            existing.ReferenceCount = nextReferenceCount;
            _totalReferences = nextTotalReferences;
            return new CellAssetReference<TKey, TAsset>(this, existing);
        }

        var updatedTotalReferences = checked(_totalReferences + 1);
        var asset = createAsset()
            ?? throw new InvalidOperationException($"Asset factory returned null for key '{key}'.");
        var entry = new Entry(key, asset);
        try
        {
            _entries.Add(key, entry);
            _totalReferences = updatedTotalReferences;
            return new CellAssetReference<TKey, TAsset>(this, entry);
        }
        catch
        {
            asset.Dispose();
            throw;
        }
    }

    public int GetReferenceCount(TKey key)
    {
        EnsureOwningThread();
        return _entries.TryGetValue(key, out var entry) ? entry.ReferenceCount : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureOwningThread();
        _disposed = true;
        List<Exception>? failures = null;
        foreach (var entry in _entries.Values)
        {
            try { entry.Asset.Dispose(); }
            catch (Exception exception) { (failures ??= new()).Add(exception); }
        }
        _entries.Clear();
        _totalReferences = 0;
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is { Count: > 1 }) throw new AggregateException(failures);
    }

    internal TAsset GetAsset(Entry entry)
    {
        EnsureNotDisposed();
        if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
            throw new ObjectDisposedException(nameof(CellAssetReference<TKey, TAsset>));
        return entry.Asset;
    }

    internal void Release(Entry entry)
    {
        if (_disposed) return;
        EnsureOwningThread();
        if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
            throw new InvalidOperationException("Cell asset reference does not belong to this live pool entry.");
        if (entry.ReferenceCount <= 0)
            throw new InvalidOperationException("Cell asset reference count is already zero.");

        entry.ReferenceCount--;
        _totalReferences--;
        if (entry.ReferenceCount != 0) return;
        _entries.Remove(entry.Key);
        entry.Asset.Dispose();
    }

    private void EnsureNotDisposed()
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwningThread()
    {
        var currentThreadId = Environment.CurrentManagedThreadId;
        if (currentThreadId != _owningThreadId)
            throw new InvalidOperationException(
                $"Cell assets must be accessed on owning thread {_owningThreadId}; current thread is {currentThreadId}.");
    }

    internal sealed class Entry(TKey key, TAsset asset)
    {
        public TKey Key { get; } = key;
        public TAsset Asset { get; } = asset;
        public int ReferenceCount { get; set; } = 1;
    }
}

/// <summary>A cell-owned lease that releases one reference when disposed.</summary>
public sealed class CellAssetReference<TKey, TAsset> : IDisposable
    where TKey : notnull
    where TAsset : class, IDisposable
{
    private CellAssetReferencePool<TKey, TAsset>? _pool;
    private readonly CellAssetReferencePool<TKey, TAsset>.Entry _entry;

    internal CellAssetReference(CellAssetReferencePool<TKey, TAsset> pool,
        CellAssetReferencePool<TKey, TAsset>.Entry entry)
    {
        _pool = pool;
        _entry = entry;
    }

    public TAsset Asset => (_pool
        ?? throw new ObjectDisposedException(nameof(CellAssetReference<TKey, TAsset>))).GetAsset(_entry);

    public void Dispose()
    {
        var pool = _pool;
        if (pool is null) return;
        try { pool.Release(_entry); }
        finally { _pool = null; }
    }
}

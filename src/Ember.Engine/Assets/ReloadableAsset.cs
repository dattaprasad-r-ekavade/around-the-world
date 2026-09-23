using System;

namespace Ember.Assets;

/// <summary>Owns the current disposable asset and replaces it only after a candidate is built.</summary>
public sealed class ReloadableAsset<T> : IDisposable where T : class, IDisposable
{
    private T? _current;
    private bool _disposed;

    public ReloadableAsset(T initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public T Current
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _current!;
        }
    }

    /// <summary>
    /// Build the entire replacement before changing Current. A factory exception leaves the old
    /// asset untouched. Returns a prior-resource cleanup error after a successful replacement.
    /// </summary>
    public Exception? Reload(Func<T> createReplacement)
    {
        ArgumentNullException.ThrowIfNull(createReplacement);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var replacement = createReplacement()
            ?? throw new InvalidOperationException("The replacement asset factory returned null.");
        if (ReferenceEquals(replacement, _current))
            throw new InvalidOperationException("A reload must produce a new asset instance.");

        var previous = _current!;
        _current = replacement;
        try
        {
            previous.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var current = _current;
        _current = null;
        current?.Dispose();
    }
}

using System;
using System.Collections.Generic;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneResourceScopeTests
{
    [Fact]
    public void DisposesOwnedResourcesInReverseOrderAndOnlyOnce()
    {
        var order = new List<int>();
        var scope = new SceneResourceScope();
        scope.Own(new Probe(1, order));
        scope.Own(new Probe(2, order));

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(new[] { 2, 1 }, order);
        Assert.True(scope.IsDisposed);
    }

    [Fact]
    public void ContinuesDisposingAfterOneResourceFails()
    {
        var order = new List<int>();
        var scope = new SceneResourceScope();
        scope.Own(new Probe(1, order));
        scope.Own(new Probe(2, order, throwOnDispose: true));
        scope.Own(new Probe(3, order));

        var error = Assert.Throws<AggregateException>(() => scope.Dispose());

        Assert.Single(error.InnerExceptions);
        Assert.Equal(new[] { 3, 2, 1 }, order);
    }

    [Fact]
    public void DuplicateOwnershipIsRejected()
    {
        var scope = new SceneResourceScope();
        var resource = new Probe(1, new List<int>());
        scope.Own(resource);

        Assert.Throws<ArgumentException>(() => scope.Own(resource));
        scope.Dispose();
    }

    private sealed class Probe : IDisposable
    {
        private readonly int _id;
        private readonly List<int> _order;
        private readonly bool _throwOnDispose;

        public Probe(int id, List<int> order, bool throwOnDispose = false)
        {
            _id = id;
            _order = order;
            _throwOnDispose = throwOnDispose;
        }

        public void Dispose()
        {
            _order.Add(_id);
            if (_throwOnDispose) throw new InvalidOperationException("simulated disposal error");
        }
    }
}

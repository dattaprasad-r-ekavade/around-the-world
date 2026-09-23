using System;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class CellLifecycleTests
{
    [Fact]
    public void ValidCellLifecycleCanPrepareActivateAndUnload()
    {
        var lifecycle = new CellLifecycle();

        lifecycle.TransitionTo(CellLifecycleState.Preparing);
        lifecycle.TransitionTo(CellLifecycleState.Ready);
        lifecycle.TransitionTo(CellLifecycleState.Active);
        lifecycle.TransitionTo(CellLifecycleState.Unloading);
        lifecycle.TransitionTo(CellLifecycleState.Unloaded);

        Assert.Equal(CellLifecycleState.Unloaded, lifecycle.State);
        Assert.Null(lifecycle.Failure);
    }

    [Theory]
    [InlineData(CellLifecycleState.Unloaded, CellLifecycleState.Ready)]
    [InlineData(CellLifecycleState.Preparing, CellLifecycleState.Active)]
    [InlineData(CellLifecycleState.Ready, CellLifecycleState.Unloaded)]
    [InlineData(CellLifecycleState.Active, CellLifecycleState.Ready)]
    [InlineData(CellLifecycleState.Failed, CellLifecycleState.Active)]
    public void InvalidCellLifecycleTransitionsAreRejected(CellLifecycleState initial, CellLifecycleState next)
    {
        var lifecycle = new CellLifecycle();
        if (initial != CellLifecycleState.Unloaded)
        {
            lifecycle.TransitionTo(CellLifecycleState.Preparing);
            if (initial is CellLifecycleState.Ready or CellLifecycleState.Active)
                lifecycle.TransitionTo(CellLifecycleState.Ready);
            if (initial == CellLifecycleState.Active)
                lifecycle.TransitionTo(CellLifecycleState.Active);
            if (initial == CellLifecycleState.Failed)
                lifecycle.MarkFailed(new InvalidOperationException("fixture"));
        }

        Assert.Throws<InvalidOperationException>(() => lifecycle.TransitionTo(next));
    }

    [Fact]
    public void FailedPreparationReleasesTemporaryResourcesAndCanReset()
    {
        var lifecycle = new CellLifecycle();
        var resource = new DisposableProbe();
        var failure = new InvalidOperationException("scene parse failed");
        lifecycle.TransitionTo(CellLifecycleState.Preparing);
        lifecycle.SetPreparationResource(resource);

        lifecycle.MarkFailed(failure);

        Assert.Equal(CellLifecycleState.Failed, lifecycle.State);
        Assert.Same(failure, lifecycle.Failure);
        Assert.Equal(1, resource.DisposeCount);
        lifecycle.TransitionTo(CellLifecycleState.Unloaded);
        Assert.Equal(1, resource.DisposeCount);
        Assert.Null(lifecycle.Failure);
    }

    [Fact]
    public void ReadyCellCanTransferPreparationResourceToActivator()
    {
        var lifecycle = new CellLifecycle();
        var resource = new DisposableProbe();
        lifecycle.TransitionTo(CellLifecycleState.Preparing);
        lifecycle.SetPreparationResource(resource);
        lifecycle.TransitionTo(CellLifecycleState.Ready);

        Assert.Same(resource, lifecycle.TakePreparationResource());
        lifecycle.MarkFailed(new InvalidOperationException("activation failed"));
        Assert.Equal(0, resource.DisposeCount);
        resource.Dispose();
        Assert.Equal(1, resource.DisposeCount);
    }

    private sealed class DisposableProbe : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}

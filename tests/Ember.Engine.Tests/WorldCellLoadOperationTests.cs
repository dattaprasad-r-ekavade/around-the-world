using System;
using System.Threading;
using System.Threading.Tasks;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellLoadOperationTests
{
    [Fact]
    public void PreparationRunsOffOwnerThreadAndActivationCommitsOnlyAfterCallbackReturns()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>(ownerThreadId);
        var prepared = new PreparedProbe();
        PrepareOnCurrentThread(operation, _ => Task.FromResult(prepared));

        Assert.Equal(CellLifecycleState.Ready, operation.State);
        Assert.NotEqual(ownerThreadId, operation.PreparationThreadId);
        Assert.Null(operation.ActiveResources);

        var active = new ActiveProbe();
        var returned = operation.Activate(value =>
        {
            Assert.Same(prepared, value);
            Assert.Null(operation.ActiveResources);
            Assert.Equal(CellLifecycleState.Ready, operation.State);
            Assert.Equal(ownerThreadId, Environment.CurrentManagedThreadId);
            return active;
        });

        Assert.Same(active, returned);
        Assert.Same(active, operation.ActiveResources);
        Assert.Equal(ownerThreadId, operation.ActivationThreadId);
        Assert.Equal(CellLifecycleState.Active, operation.State);
        Assert.Equal(1, prepared.DisposeCount);
        Assert.Equal(0, active.DisposeCount);

        operation.Unload();
        Assert.Equal(CellLifecycleState.Unloaded, operation.State);
        Assert.Null(operation.ActiveResources);
        Assert.Equal(1, active.DisposeCount);
    }

    [Fact]
    public void ActivationOnWrongThreadLeavesPreparedCellReadyAndUnpublished()
    {
        var ownerThreadId = Environment.CurrentManagedThreadId;
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>(ownerThreadId);
        PrepareOnCurrentThread(operation, _ => Task.FromResult(new PreparedProbe()));

        var error = RunOnWorker(() => Assert.Throws<InvalidOperationException>(() =>
            operation.Activate(_ => new ActiveProbe())));

        Assert.Contains("owning thread", error.Message, StringComparison.Ordinal);
        Assert.Equal(CellLifecycleState.Ready, operation.State);
        Assert.Null(operation.ActiveResources);
        operation.Activate(_ => new ActiveProbe());
    }

    [Fact]
    public void FailedActivationDisposesPreparedDataAndNeverPublishesActiveResources()
    {
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var prepared = new PreparedProbe();
        PrepareOnCurrentThread(operation, _ => Task.FromResult(prepared));

        Assert.Throws<InvalidOperationException>(() => operation.Activate(_ =>
            throw new InvalidOperationException("graphics device rejected resource")));

        Assert.Equal(CellLifecycleState.Failed, operation.State);
        Assert.Null(operation.ActiveResources);
        Assert.Equal(1, prepared.DisposeCount);
    }

    [Fact]
    public void FailedPreparationMarksLifecycleFailed()
    {
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        Assert.Throws<InvalidOperationException>(() => PrepareOnCurrentThread(operation, _ =>
            Task.FromException<PreparedProbe>(new InvalidOperationException("scene decode failed"))));

        Assert.Equal(CellLifecycleState.Failed, operation.State);
        Assert.NotNull(operation.Failure);
        Assert.Null(operation.ActiveResources);
    }

    private sealed class PreparedProbe : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class ActiveProbe : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private static void PrepareOnCurrentThread<TActive>(WorldCellLoadOperation<PreparedProbe, TActive> operation,
        Func<CancellationToken, Task<PreparedProbe>> prepare)
        where TActive : IDisposable => operation.PrepareAsync(prepare).GetAwaiter().GetResult();

    private static TResult RunOnWorker<TResult>(Func<TResult> action)
    {
        TResult result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result;
    }
}

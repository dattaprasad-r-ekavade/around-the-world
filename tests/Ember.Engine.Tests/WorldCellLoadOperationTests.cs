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
        PrepareOnCurrentThread(operation, _ =>
            Task.FromException<PreparedProbe>(new InvalidOperationException("scene decode failed")));

        Assert.Equal(CellLifecycleState.Failed, operation.State);
        Assert.NotNull(operation.Failure);
        Assert.Null(operation.ActiveResources);

        var retry = new PreparedProbe();
        PrepareOnCurrentThread(operation, _ => Task.FromResult(retry));
        Assert.Equal(CellLifecycleState.Ready, operation.State);
        var retryActive = new ActiveProbe();
        Assert.Same(retryActive, operation.Activate(_ => retryActive));
        operation.Unload();
    }

    [Fact]
    public void ReadyCellCanBeDiscardedWithoutActivation()
    {
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>();
        var prepared = new PreparedProbe();
        PrepareOnCurrentThread(operation, _ => Task.FromResult(prepared));

        operation.Discard();

        Assert.Equal(CellLifecycleState.Unloaded, operation.State);
        Assert.Equal(1, prepared.DisposeCount);
        Assert.Null(operation.ActiveResources);
    }

    [Fact]
    public void OwnerCanPollWhilePreparationIsBlockedAndPumpCommitsReadyState()
    {
        var ownerThread = Environment.CurrentManagedThreadId;
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>(ownerThread);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new ManualResetEventSlim();
        var prepared = new PreparedProbe();
        var preparation = operation.PrepareAsync(_ =>
        {
            started.Set();
            release.Wait();
            return Task.FromResult(prepared);
        });
        preparation.ContinueWith(_ => completed.Set(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            for (var index = 0; index < 20; index++)
                Assert.Equal(CellLifecycleState.Preparing, operation.State);

            release.Set();
            Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(preparation.IsCompletedSuccessfully);
            Assert.Equal(CellLifecycleState.Preparing, operation.State);
            Assert.Equal(1, operation.PumpCompletions());
            Assert.Equal(ownerThread, operation.OwningThreadId);
            Assert.NotEqual(ownerThread, operation.PreparationThreadId);
            Assert.Equal(CellLifecycleState.Ready, operation.State);
        }
        finally
        {
            release.Set();
            completed.Wait(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void CancelDiscardsLateGenerationAndLeavesNewAttemptReady()
    {
        var ownerThread = Environment.CurrentManagedThreadId;
        var operation = new WorldCellLoadOperation<PreparedProbe, ActiveProbe>(ownerThread);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var lateCompleted = new ManualResetEventSlim();
        using var currentCompleted = new ManualResetEventSlim();
        var latePrepared = new PreparedProbe();
        var latePreparation = operation.PrepareAsync(_ =>
        {
            started.Set();
            release.Wait(); // Deliberately ignores cancellation.
            return Task.FromResult(latePrepared);
        });
        latePreparation.ContinueWith(_ => lateCompleted.Set(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            operation.Cancel();
            Assert.Equal(CellLifecycleState.Unloaded, operation.State);
            var currentPrepared = new PreparedProbe();
            var currentPreparation = operation.PrepareAsync(_ => Task.FromResult(currentPrepared));
            currentPreparation.ContinueWith(_ => currentCompleted.Set(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Assert.True(currentCompleted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(currentPreparation.IsCompletedSuccessfully);
            Assert.Equal(1, operation.PumpCompletions());
            Assert.Equal(CellLifecycleState.Ready, operation.State);
            var currentActive = new ActiveProbe();
            Assert.Same(currentActive, operation.Activate(_ => currentActive));
            Assert.Equal(1, currentPrepared.DisposeCount);

            release.Set();
            Assert.True(lateCompleted.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(latePreparation.IsCompletedSuccessfully);
            Assert.Equal(1, operation.PumpCompletions());
            Assert.Equal(1, latePrepared.DisposeCount);
            Assert.Equal(CellLifecycleState.Active, operation.State);
            operation.Unload();
        }
        finally
        {
            release.Set();
            lateCompleted.Wait(TimeSpan.FromSeconds(5));
            currentCompleted.Wait(TimeSpan.FromSeconds(5));
        }
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
        where TActive : IDisposable
    {
        operation.PrepareAsync(prepare).GetAwaiter().GetResult();
        operation.PumpCompletions();
    }

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

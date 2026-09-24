using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellStreamerTests
{
    [Fact]
    public void RetriesTransientPreparationFailureThenActivatesAndReportsReady()
    {
        var fixture = CreateWorldFixture();
        try
        {
            var world = WorldManifest.Load(fixture.ManifestPath);
            var attempts = 0;
            var retryCount = 0;
            var active = new List<ActiveProbe>();
            var availability = new List<bool>();
            using var streamer = CreateStreamer(world,
                prepare: (_, _, _) =>
                {
                    if (Interlocked.Increment(ref attempts) == 1)
                        throw new IOException("transient scene read failure");
                    return Task.FromResult(new PreparedProbe(estimatedActivationCost: 2));
                },
                createActive: () =>
                {
                    var resource = new ActiveProbe();
                    active.Add(resource);
                    return resource;
                },
                availabilityChanged: (_, ready) => availability.Add(ready),
                maximumStartupAttempts: 3);
            streamer.RetryScheduled += (_, failure, delay) =>
            {
                Assert.IsType<IOException>(failure);
                Assert.Equal(TimeSpan.FromMilliseconds(2), delay);
                retryCount++;
            };

            streamer.Start(Vector3.Zero);

            Assert.Equal(2, attempts);
            Assert.Equal(1, retryCount);
            Assert.Equal(1, streamer.ActiveCellCount);
            Assert.Equal(2, streamer.ActivationAttemptCount);
            Assert.Null(streamer.LoadingStatus);
            Assert.Equal(new[] { true }, availability);
            Assert.False(active[0].IsDisposed);
            streamer.Dispose();
            Assert.True(active[0].IsDisposed);
            Assert.Equal(new[] { true, false }, availability);
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    [Fact]
    public void LeavingRetentionRingUnloadsActiveCellAndSignalsCollisionUnavailable()
    {
        var fixture = CreateWorldFixture();
        try
        {
            var world = WorldManifest.Load(fixture.ManifestPath);
            ActiveProbe? active = null;
            var availability = new List<bool>();
            using var streamer = CreateStreamer(world,
                prepare: (_, _, _) => Task.FromResult(new PreparedProbe(estimatedActivationCost: 1)),
                createActive: () => active = new ActiveProbe(),
                availabilityChanged: (_, ready) => availability.Add(ready));

            streamer.Start(Vector3.Zero);
            Assert.Equal(1, streamer.ActiveCellCount);
            streamer.Update(new Vector3(world.ExteriorCellWidth * 2f, 0f, 0f));

            Assert.Equal(0, streamer.ActiveCellCount);
            Assert.True(active!.IsDisposed);
            Assert.Equal(new[] { true, false }, availability);
            streamer.Dispose();
        }
        finally
        {
            Directory.Delete(fixture.Directory, recursive: true);
        }
    }

    private static WorldCellStreamer<PreparedProbe, ActiveProbe> CreateStreamer(WorldManifest world,
        Func<ExteriorCellCoordinate, Guid, CancellationToken, Task<PreparedProbe>> prepare,
        Func<ActiveProbe> createActive,
        Action<ExteriorCellCoordinate, bool> availabilityChanged,
        int maximumStartupAttempts = 3)
    {
        return new WorldCellStreamer<PreparedProbe, ActiveProbe>(
            world,
            prepare,
            _ => new ProbeStepper(createActive),
            availabilityChanged,
            new WorldCellStreamingOptions
            {
                LoadingRadiusInCells = 0,
                RetentionRadiusInCells = 0,
                MaximumActivationCostPerFrame = 4,
                MaximumActivationStepsPerFrame = 1,
                MaximumActivationTimePerFrame = TimeSpan.FromMilliseconds(100),
                RetryBaseDelay = TimeSpan.FromMilliseconds(2),
                RetryMaximumDelay = TimeSpan.FromMilliseconds(8),
                MaximumStartupAttempts = maximumStartupAttempts
            });
    }

    private static (string Directory, string ManifestPath) CreateWorldFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ember-world-streamer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "world.json");
        WorldCellWorkspace.CreateWorld(manifestPath, exteriorCellWidth: 10f);
        WorldCellWorkspace.CreateCell(manifestPath, WorldCellKind.Exterior,
            "Origin", new ExteriorCellCoordinate(0, 0));
        return (directory, manifestPath);
    }

    private sealed class PreparedProbe(long estimatedActivationCost) : IDisposable, ICellActivationCost
    {
        public long EstimatedActivationCost { get; } = estimatedActivationCost;
        public void Dispose() { }
    }

    private sealed class ActiveProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class ProbeStepper(Func<ActiveProbe> createActive)
        : ICellActivationStepper<PreparedProbe, ActiveProbe>
    {
        private long _remainingCost;

        public CellActivationStepResult<ActiveProbe> Step(PreparedProbe prepared, long maximumCost)
        {
            if (_remainingCost == 0) _remainingCost = prepared.EstimatedActivationCost;
            var consumed = Math.Min(1, Math.Min(_remainingCost, maximumCost));
            _remainingCost -= consumed;
            var complete = _remainingCost == 0;
            return new CellActivationStepResult<ActiveProbe>(consumed, complete,
                complete ? createActive() : null);
        }

        public void Dispose() { }
    }
}

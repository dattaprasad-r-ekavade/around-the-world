using System;
using System.IO;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldSaveRequestQueueTests
{
    [Fact]
    public void SaveRequestedDuringTravelCapturesOnlyTheCommittedLocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-save-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourceCellId = Guid.NewGuid();
            var destinationCellId = Guid.NewGuid();
            var path = Path.Combine(directory, "save.json");
            var queue = new WorldSaveRequestQueue();
            var currentLocation = new WorldPlayerLocation(sourceCellId, Vector3.Zero, Quaternion.Identity);
            var captureCount = 0;
            var requestId = queue.Enqueue(path, () =>
            {
                captureCount++;
                return new WorldSaveSnapshot(currentLocation, [], [], []);
            });

            Assert.False(queue.ProcessStableBoundary(travelInProgress: true));
            Assert.Equal(0, captureCount);
            Assert.False(File.Exists(path));
            Assert.Equal(1, queue.PendingCount);

            currentLocation = new WorldPlayerLocation(destinationCellId, new Vector3(7f, 2f, 4f),
                Quaternion.CreateFromYawPitchRoll(1f, 0f, 0f));
            Assert.True(queue.ProcessStableBoundary(travelInProgress: false));
            Assert.Equal(1, captureCount);
            Assert.Equal(0, queue.PendingCount);
            Assert.True(queue.TryDequeueResult(out var result));
            Assert.Equal(requestId, result.RequestId);
            Assert.Null(result.Failure);
            Assert.Equal(currentLocation, WorldSaveFile.Load(path).PlayerLocation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedTravelCanSaveTheStillActiveSourceLocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-save-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sourceLocation = new WorldPlayerLocation(Guid.NewGuid(), new Vector3(2f, 1f, 3f), Quaternion.Identity);
            var path = Path.Combine(directory, "save.json");
            var queue = new WorldSaveRequestQueue();
            queue.Enqueue(path, () => new WorldSaveSnapshot(sourceLocation, [], [], []));
            Assert.False(queue.ProcessStableBoundary(travelInProgress: true));
            Assert.True(queue.ProcessStableBoundary(travelInProgress: false));

            Assert.True(queue.TryDequeueResult(out var result));
            Assert.Null(result.Failure);
            Assert.Equal(sourceLocation, WorldSaveFile.Load(path).PlayerLocation);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedQueuedWriteReturnsAnErrorAndDoesNotBlockLaterRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-save-queue-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var queue = new WorldSaveRequestQueue();
            var goodPath = Path.Combine(directory, "good.json");
            var blockedParent = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blockedParent, "file blocks this directory path");
            var badPath = Path.Combine(blockedParent, "bad.json");
            queue.Enqueue(badPath, () => new WorldSaveSnapshot(
                new WorldPlayerLocation(Guid.NewGuid(), Vector3.Zero, Quaternion.Identity), [], [], []));
            queue.Enqueue(goodPath, () => new WorldSaveSnapshot(
                new WorldPlayerLocation(Guid.NewGuid(), Vector3.Zero, Quaternion.Identity), [], [], []));

            Assert.True(queue.ProcessStableBoundary(travelInProgress: false));
            Assert.True(queue.TryDequeueResult(out var failed));
            Assert.NotNull(failed.Failure);
            Assert.True(queue.ProcessStableBoundary(travelInProgress: false));
            Assert.True(queue.TryDequeueResult(out var succeeded));
            Assert.Null(succeeded.Failure);
            Assert.True(File.Exists(goodPath));
            Assert.Equal(0, queue.PendingCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

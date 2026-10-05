using System;
using System.IO;
using Ember.Rpg;
using Ember.World;
using Microsoft.Xna.Framework;
using RpgSlice;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class RpgSliceSaveBundleTests
{
    [Fact]
    public void SaveRequestedDuringTravelCommitsWorldAndRpgStateTogetherAtTheStableBoundary()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "game-save.json");
            var sourceCellId = Guid.NewGuid();
            var destinationCellId = Guid.NewGuid();
            var currentLocation = new WorldPlayerLocation(sourceCellId, Vector3.Zero, Quaternion.Identity);
            var currentRpg = new SaveState { WorldTimeSeconds = 1.5 };
            var queue = new WorldSaveRequestQueue();
            var requestId = queue.Enqueue(path,
                () => new WorldSaveSnapshot(currentLocation, [], [], []),
                snapshot => RpgSliceSaveBundle.SaveAtomic(path, snapshot, currentRpg));

            Assert.False(queue.ProcessStableBoundary(travelInProgress: true));
            Assert.False(File.Exists(path));
            Assert.Equal(1, queue.PendingCount);

            currentLocation = new WorldPlayerLocation(destinationCellId, new Vector3(7f, 2f, 4f),
                Quaternion.CreateFromYawPitchRoll(1f, 0f, 0f));
            currentRpg = new SaveState { WorldTimeSeconds = 9.25 };
            Assert.True(queue.ProcessStableBoundary(travelInProgress: false));

            Assert.True(queue.TryDequeueResult(out var result));
            Assert.Equal(requestId, result.RequestId);
            Assert.Null(result.Failure);
            var bundle = RpgSliceSaveBundle.Load(path);
            Assert.Equal(currentLocation, bundle.World.PlayerLocation);
            Assert.Equal(currentRpg.WorldTimeSeconds, bundle.Rpg.WorldTimeSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedReplacementPreservesThePreviousWorldAndRpgSaveBundle()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "game-save.json");
            var oldCellId = Guid.NewGuid();
            var newCellId = Guid.NewGuid();
            var oldWorld = Snapshot(oldCellId, Vector3.One);
            var newWorld = Snapshot(newCellId, new Vector3(8f, 2f, 4f));
            RpgSliceSaveBundle.SaveAtomic(path, oldWorld, new SaveState { WorldTimeSeconds = 1.5 });
            var originalJson = File.ReadAllText(path);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => RpgSliceSaveBundle.SaveAtomic(path, newWorld,
                    new SaveState { WorldTimeSeconds = 9.25 }));

            Assert.Equal(originalJson, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
            var preserved = RpgSliceSaveBundle.Load(path);
            Assert.Equal(oldCellId, preserved.World.PlayerLocation.CellId);
            Assert.Equal(1.5, preserved.Rpg.WorldTimeSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsUnsupportedBundleVersion()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "game-save.json");
            File.WriteAllText(path,
                "{\"Version\":99,\"WorldSaveJson\":\"{}\",\"RpgSaveJson\":\"{}\"}");

            var error = Assert.Throws<InvalidDataException>(() => RpgSliceSaveBundle.Load(path));

            Assert.Contains("Unsupported game save bundle version 99", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WorldSaveSnapshot Snapshot(Guid cellId, Vector3 position) =>
        new(new WorldPlayerLocation(cellId, position, Quaternion.Identity), [], [], []);

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-rpg-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

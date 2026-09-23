using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Ember.Sequence;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SequenceFrameExportTests
{
    [Fact]
    public void TenSecondsAtThirtyFramesPerSecondContainsExactlyThreeHundredFrames()
    {
        var settings = CreateSettings(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            start: 4f, end: 14f, frameRate: 30);

        Assert.Equal(300, settings.FrameCount);
        Assert.Equal(4f, settings.FrameTime(0));
        Assert.InRange(MathF.Abs(settings.FrameTime(299) - (4f + 299f / 30f)), 0f, 0.00001f);
        Assert.EndsWith("frame_000299.png", settings.FramePath(299), StringComparison.Ordinal);
    }

    [Fact]
    public void FrameCountUsesEndExclusiveBoundariesAndDistinguishesAdjacentFloatTimes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-frame-boundary-" + Guid.NewGuid().ToString("N"));
        var oneFrameBoundary = 1f / 30f;
        var nonzeroStartBoundary = (float)(1d + 1d / 30d);

        Assert.Equal(3, CreateSettings(directory, start: 0f, end: 0.1f, frameRate: 30).FrameCount);
        Assert.Equal(4, CreateSettings(directory, start: 0f, end: MathF.BitIncrement(0.1f), frameRate: 30).FrameCount);
        Assert.Equal(3, CreateSettings(directory, start: 0f, end: MathF.BitDecrement(0.1f), frameRate: 30).FrameCount);
        Assert.Equal(1, CreateSettings(directory, start: 0f, end: MathF.BitDecrement(oneFrameBoundary), frameRate: 30).FrameCount);
        Assert.Equal(1, CreateSettings(directory, start: 0f, end: oneFrameBoundary, frameRate: 30).FrameCount);
        Assert.Equal(2, CreateSettings(directory, start: 0f, end: MathF.BitIncrement(oneFrameBoundary), frameRate: 30).FrameCount);
        Assert.Equal(1, CreateSettings(directory, start: 1f, end: nonzeroStartBoundary, frameRate: 30).FrameCount);
        Assert.Equal(2, CreateSettings(directory, start: 1f, end: MathF.BitIncrement(nonzeroStartBoundary), frameRate: 30).FrameCount);
    }

    [Fact]
    public void NonAlignedEndpointsIncludeOnlyFrameTimesBeforeTheEnd()
    {
        var settings = CreateSettings(Path.Combine(Path.GetTempPath(), "ember-frame-range-" + Guid.NewGuid().ToString("N")),
            start: 0.125f, end: 0.36f, frameRate: 30);

        Assert.Equal(8, settings.FrameCount);
        Assert.Equal(0.125f, settings.FrameTime(0));
        Assert.InRange(settings.FrameTime(7), 0.3583f, 0.3584f);
        Assert.True(settings.FrameTime(7) < settings.EndTime);
    }

    [Fact]
    public void CompletingFramesWritesHashesSettingsAndCompleteManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-sequence-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = CreateSettings(directory, start: 0f, end: 0.1f, frameRate: 30);
            var assetId = Guid.NewGuid();
            var asset = new SequenceExportAssetVersion(assetId, "Assets/actor.glb",
                Convert.ToHexString(SHA256.HashData("fixture"u8)), 7);
            var job = new SequenceFrameExportJob(settings, "Walk proof", [asset]);
            var times = new List<float>();

            while (job.IsRunning)
                job.ProcessNextFrame(frame =>
                {
                    times.Add(frame.Time);
                    File.WriteAllText(frame.OutputPath, $"frame {frame.FrameIndex}");
                });

            Assert.Equal(SequenceFrameExportState.Completed, job.State);
            Assert.Equal(1d, job.Progress);
            Assert.Equal(3, times.Count);
            Assert.Equal(new[] { 0f, 1f / 30f, 2f / 30f }, times);
            Assert.All(Enumerable.Range(0, 3), index => Assert.True(File.Exists(settings.FramePath(index))));

            using var manifest = JsonDocument.Parse(File.ReadAllText(job.ManifestPath));
            var root = manifest.RootElement;
            Assert.Equal("completed", root.GetProperty("status").GetString());
            Assert.True(root.GetProperty("isComplete").GetBoolean());
            Assert.Equal(3, root.GetProperty("completedFrames").GetInt32());
            Assert.Equal(30, root.GetProperty("settings").GetProperty("frameRate").GetInt32());
            Assert.Equal(1280, root.GetProperty("settings").GetProperty("width").GetInt32());
            Assert.Equal(assetId, root.GetProperty("assets")[0].GetProperty("assetId").GetGuid());
            Assert.Equal(asset.Sha256, root.GetProperty("assets")[0].GetProperty("sha256").GetString());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CancellationAndFrameWriteFailureLeaveExplicitIncompleteManifests()
    {
        var canceledDirectory = Path.Combine(Path.GetTempPath(), "ember-sequence-cancel-" + Guid.NewGuid().ToString("N"));
        var failedDirectory = Path.Combine(Path.GetTempPath(), "ember-sequence-fail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var canceledSettings = CreateSettings(canceledDirectory, start: 0f, end: 1f, frameRate: 2);
            var canceled = new SequenceFrameExportJob(canceledSettings, "Cancelable");
            canceled.ProcessNextFrame(frame => File.WriteAllText(frame.OutputPath, "frame 0"));
            canceled.Cancel();
            canceled.ProcessNextFrame(_ => throw new InvalidOperationException("Canceled job must not continue."));

            Assert.Equal(SequenceFrameExportState.Canceled, canceled.State);
            Assert.Equal(1, canceled.CompletedFrames);
            Assert.True(File.Exists(canceledSettings.FramePath(0)));
            using (var manifest = JsonDocument.Parse(File.ReadAllText(canceled.ManifestPath)))
            {
                Assert.Equal("canceled", manifest.RootElement.GetProperty("status").GetString());
                Assert.False(manifest.RootElement.GetProperty("isComplete").GetBoolean());
                Assert.Equal(1, manifest.RootElement.GetProperty("completedFrames").GetInt32());
            }

            var failed = new SequenceFrameExportJob(
                CreateSettings(failedDirectory, start: 0f, end: 1f, frameRate: 2), "Write failure");
            failed.ProcessNextFrame(_ => throw new IOException("disk full"));

            Assert.Equal(SequenceFrameExportState.Failed, failed.State);
            Assert.Equal(0, failed.CompletedFrames);
            Assert.False(File.Exists(failed.Settings.FramePath(0)));
            Assert.Contains("disk full", failed.Error, StringComparison.Ordinal);
            using var failedManifest = JsonDocument.Parse(File.ReadAllText(failed.ManifestPath));
            Assert.Equal("failed", failedManifest.RootElement.GetProperty("status").GetString());
            Assert.False(failedManifest.RootElement.GetProperty("isComplete").GetBoolean());
            Assert.Contains("disk full", failedManifest.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(canceledDirectory)) Directory.Delete(canceledDirectory, recursive: true);
            if (Directory.Exists(failedDirectory)) Directory.Delete(failedDirectory, recursive: true);
        }
    }

    [Fact]
    public void ExistingManifestIsNeverOverwrittenByANewExport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-sequence-existing-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var original = "keep this manifest";
            File.WriteAllText(Path.Combine(directory, SequenceFrameExportJob.ManifestFileName), original);

            var exception = Assert.Throws<IOException>(() => new SequenceFrameExportJob(
                CreateSettings(directory, start: 0f, end: 1f, frameRate: 1), "No overwrite"));

            Assert.Contains("Choose an empty output folder", exception.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllText(Path.Combine(directory, SequenceFrameExportJob.ManifestFileName)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static SequenceFrameExportSettings CreateSettings(string directory, float start, float end, int frameRate) =>
        new(directory, start, end, frameRate, width: 1280, height: 720);
}

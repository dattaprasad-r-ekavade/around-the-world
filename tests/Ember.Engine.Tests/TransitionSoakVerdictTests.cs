using System;
using System.Text.Json;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class TransitionSoakVerdictTests
{
    private const long MiB = 1024L * 1024L;

    [Fact]
    public void CompleteStableRun_PassesEachMeasuredCategoryAndExportsAllSamples()
    {
        var soak = CreateRun(10);

        Assert.True(soak.IsComplete);
        Assert.True(soak.Passed);
        Assert.Equal(0, soak.ExitCode);

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.Contains("Transition count | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Transition latency | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Resource trend | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Working-set memory | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Managed-heap memory | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Accumulated errors | **PASS**", report, StringComparison.Ordinal);

        using var raw = JsonDocument.Parse(soak.BuildRawData("Test adapter", 1280, 720));
        Assert.Equal(10, raw.RootElement.GetProperty("samples").GetArrayLength());
        Assert.Equal(10, raw.RootElement.GetProperty("run").GetProperty("completedTransitions").GetInt32());
        Assert.Equal("Test adapter", raw.RootElement.GetProperty("run").GetProperty("adapter").GetString());
        Assert.Equal(1280, raw.RootElement.GetProperty("run").GetProperty("viewportWidth").GetInt32());
    }

    [Fact]
    public void CommittedTravelCleanupError_IsRecordedAndFailsVerdict()
    {
        var soak = CreateRun(10);
        soak.RecordError("committed-travel cleanup", "source-cell disposal failed");

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.False(soak.Passed);
        Assert.Equal(1, soak.ExitCode);
        Assert.Contains("Accumulated errors | **FAIL**", report, StringComparison.Ordinal);
        Assert.Contains("committed-travel cleanup", report, StringComparison.Ordinal);
        Assert.Contains("source-cell disposal failed", report, StringComparison.Ordinal);

        using var raw = JsonDocument.Parse(soak.BuildRawData("Test adapter", 1280, 720));
        Assert.Equal("committed-travel cleanup", raw.RootElement.GetProperty("errors")[0].GetProperty("Stage").GetString());
    }

    [Fact]
    public void PostWarmupResourceGrowth_FailsResourceCheckIndependently()
    {
        var soak = CreateRun(10, resourceGrowthAtTransition: 10);

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.False(soak.Passed);
        Assert.Equal(1, soak.ExitCode);
        Assert.Contains("Resource trend | **FAIL**", report, StringComparison.Ordinal);
        Assert.Contains("tracked graphics resources grew at Exterior", report, StringComparison.Ordinal);
        Assert.Contains("Working-set memory | **PASS**", report, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkingSetGrowth_FailsMemoryCheckIndependently()
    {
        var soak = CreateRun(10, workingSetGrowthAtTransition: 10, workingSetGrowthMiB: 31);

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.False(soak.Passed);
        Assert.Equal(1, soak.ExitCode);
        Assert.Contains("Working-set memory | **FAIL**", report, StringComparison.Ordinal);
        Assert.Contains("Resource trend | **PASS**", report, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteRun_FailsCountCheckAndKeepsPartialSamples()
    {
        var soak = CreateRun(4, samplesToRecord: 2);

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.False(soak.Passed);
        Assert.Contains("Transition count | **FAIL**", report, StringComparison.Ordinal);
        using var raw = JsonDocument.Parse(soak.BuildRawData("Test adapter", 1280, 720));
        Assert.Equal(2, raw.RootElement.GetProperty("samples").GetArrayLength());
    }

    [Fact]
    public void Timeout_FailsAndIncludesTimeoutReasonInDiagnostics()
    {
        var soak = CreateRun(10, samplesToRecord: 4);
        soak.RecordTimeout("timed out after 300 seconds");

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.False(soak.Passed);
        Assert.Contains("Timeout | **FAIL**", report, StringComparison.Ordinal);
        Assert.Contains("timed out after 300 seconds", report, StringComparison.Ordinal);
    }

    [Fact]
    public void OneTransitionRun_IsReportedAsInconclusiveWithoutWarmupIndexFailure()
    {
        var soak = CreateRun(1);

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.True(soak.IsComplete);
        Assert.False(soak.Passed);
        Assert.Equal(1, soak.ExitCode);
        Assert.Contains("Resource trend | **INCONCLUSIVE**", report, StringComparison.Ordinal);
        Assert.Contains("Working-set memory | **INCONCLUSIVE**", report, StringComparison.Ordinal);
        using var raw = JsonDocument.Parse(soak.BuildRawData("Test adapter", 1280, 720));
        Assert.Equal(1, raw.RootElement.GetProperty("samples").GetArrayLength());
    }

    [Fact]
    public void InvalidLatency_FailsLatencyCheckAndRecordsError()
    {
        var soak = new RpgSlice.RpgSliceTransitionSoak(1);
        soak.RecordTransition("Exterior", "Interior", double.NaN, 1, 0, 10, 100 * MiB);

        var report = soak.BuildReport("Test adapter", 1280, 720);
        Assert.False(soak.Passed);
        Assert.Contains("Transition latency | **FAIL**", report, StringComparison.Ordinal);
        Assert.Contains("Accumulated errors | **FAIL**", report, StringComparison.Ordinal);
    }

    [Fact]
    public void NonPositiveTarget_IsRejectedInsteadOfSilentlyChanged()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpgSlice.RpgSliceTransitionSoak(0));
    }

    private static RpgSlice.RpgSliceTransitionSoak CreateRun(int target,
        int? samplesToRecord = null,
        int? resourceGrowthAtTransition = null,
        int? workingSetGrowthAtTransition = null,
        int workingSetGrowthMiB = 0)
    {
        var soak = new RpgSlice.RpgSliceTransitionSoak(target);
        var count = samplesToRecord ?? target;
        for (var i = 1; i <= count; i++)
        {
            var isExterior = i % 2 == 0;
            var destination = isExterior ? "Exterior" : "Interior";
            var resources = isExterior ? 22 : 10;
            if (resourceGrowthAtTransition == i) resources++;
            var workingSet = 100L * MiB;
            if (workingSetGrowthAtTransition == i) workingSet += workingSetGrowthMiB * MiB;
            soak.RecordTransition(isExterior ? "Interior" : "Exterior", destination,
                0.01d, isExterior ? 9 : 1, isExterior ? 12 : 0, resources, workingSet);
        }
        return soak;
    }
}

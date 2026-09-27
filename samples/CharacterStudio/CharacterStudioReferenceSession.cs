using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CharacterStudio;

internal sealed class CharacterStudioReferenceSession
{
    internal sealed record MemorySample(double ElapsedSeconds, string Mode, int SceneObjects,
        int ImportedAssets, int CharacterInstances, int OwnedPreviewGraphicsResources,
        long WorkingSetBytes, long PrivateMemoryBytes, long ManagedHeapBytes);

    internal sealed record ModeChange(double ElapsedSeconds, string Mode);
    internal sealed record HostPauseInterval(double ActiveElapsedSeconds, double DurationMilliseconds);

    private const double MaximumActiveFrameIntervalMilliseconds = 5000d;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<double> _frameIntervalsMilliseconds = new();
    private readonly List<MemorySample> _memorySamples = new();
    private readonly List<ModeChange> _modeChanges = new();
    private readonly List<HostPauseInterval> _hostPauseIntervals = new();
    private readonly List<string> _errors = new();
    private double _activeElapsedSeconds;

    public CharacterStudioReferenceSession(TimeSpan targetDuration, string referenceScene)
    {
        if (targetDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(targetDuration));
        if (string.IsNullOrWhiteSpace(referenceScene))
            throw new ArgumentException("A reference scene description is required.", nameof(referenceScene));

        TargetDuration = targetDuration;
        ReferenceScene = referenceScene;
        RunId = Guid.NewGuid();
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public Guid RunId { get; }
    public DateTimeOffset StartedUtc { get; }
    public TimeSpan TargetDuration { get; }
    public string ReferenceScene { get; }
    public double ElapsedSeconds => _activeElapsedSeconds;
    public double WallElapsedSeconds => _clock.Elapsed.TotalSeconds;
    public bool TargetReached => _activeElapsedSeconds >= TargetDuration.TotalSeconds;
    public int FrameCount => _frameIntervalsMilliseconds.Count;
    public IReadOnlyList<string> Errors => _errors;

    public void RecordFrameInterval(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0d) return;
        if (milliseconds > MaximumActiveFrameIntervalMilliseconds)
        {
            _hostPauseIntervals.Add(new HostPauseInterval(_activeElapsedSeconds, milliseconds));
            return;
        }

        _frameIntervalsMilliseconds.Add(milliseconds);
        _activeElapsedSeconds += milliseconds / 1000d;
    }

    public void RecordSample(MemorySample sample) => _memorySamples.Add(sample);
    public void RecordModeChange(string mode) => _modeChanges.Add(new ModeChange(ElapsedSeconds, mode));
    public void RecordError(string error) => _errors.Add(error);

    public string BuildReport(string adapter, int width, int height, bool passed)
    {
        var intervals = _frameIntervalsMilliseconds.OrderBy(value => value).ToArray();
        var summary = Summarize(intervals);
        var measuredSamples = _memorySamples.Skip(Math.Min(4, _memorySamples.Count)).ToArray();
        var report = new StringBuilder();
        report.AppendLine("# CharacterStudio Reference Performance Session");
        report.AppendLine();
        report.AppendLine($"Run ID: {RunId:N}");
        report.AppendLine($"Started UTC: {StartedUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
        report.AppendLine($"Machine: {Environment.MachineName}; {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}");
        report.AppendLine($"Graphics: {adapter} ({width}x{height})");
        report.AppendLine($"Reference scene: {ReferenceScene}");
        report.AppendLine($"Target/active duration: {TargetDuration.TotalMinutes:F1}/{ElapsedSeconds / 60d:F1} minutes; wall-clock duration: {WallElapsedSeconds / 60d:F1} minutes");
        report.AppendLine("Modes alternate every 60 seconds of active rendered time. Frame intervals are wall-clock Update-to-Update measurements, including rendering and presentation pacing; they are not GPU-only timings.");
        report.AppendLine();
        report.AppendLine("## Frame timing");
        report.AppendLine();
        report.AppendLine($"Samples: {summary.Count}; average: {summary.AverageMilliseconds:F2} ms; p50: {summary.P50Milliseconds:F2} ms; p95: {summary.P95Milliseconds:F2} ms; p99: {summary.P99Milliseconds:F2} ms; max: {summary.MaximumMilliseconds:F2} ms.");
        report.AppendLine($"Intervals over 16.7/33.3/50/100 ms: {summary.Over16Milliseconds}/{summary.Over33Milliseconds}/{summary.Over50Milliseconds}/{summary.Over100Milliseconds}.");
        var hostPauseTotalSeconds = _hostPauseIntervals.Sum(interval => interval.DurationMilliseconds) / 1000d;
        report.AppendLine($"Host pause gaps over {MaximumActiveFrameIntervalMilliseconds / 1000d:F0}s excluded from active frame statistics: {_hostPauseIntervals.Count}; total {hostPauseTotalSeconds:F1}s; maximum {(_hostPauseIntervals.Count == 0 ? 0d : _hostPauseIntervals.Max(interval => interval.DurationMilliseconds) / 1000d):F1}s.");
        report.AppendLine();
        report.AppendLine("## Resource and memory samples after four warmup samples");
        report.AppendLine();
        report.AppendLine("| Metric | First | Final | Peak | Peak growth |");
        report.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        AppendMetric(report, "Owned preview graphics resources", measuredSamples.Select(sample => (long)sample.OwnedPreviewGraphicsResources), "");
        AppendMetric(report, "Working set", measuredSamples.Select(sample => sample.WorkingSetBytes), " MiB");
        AppendMetric(report, "Private memory", measuredSamples.Select(sample => sample.PrivateMemoryBytes), " MiB");
        AppendMetric(report, "Managed heap", measuredSamples.Select(sample => sample.ManagedHeapBytes), " MiB");
        report.AppendLine();
        report.AppendLine("## Mode changes");
        report.AppendLine();
        foreach (var change in _modeChanges)
            report.AppendLine($"- {change.ElapsedSeconds:F1}s: {change.Mode}");
        report.AppendLine();
        report.AppendLine(passed
            ? "**PASS** - the mixed editor/runtime session reached its target duration without recorded lifecycle errors. This establishes a reference measurement, not a cross-machine performance guarantee."
            : "**FAIL** - the session ended early or recorded a lifecycle error.");
        if (_errors.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Errors");
            foreach (var error in _errors) report.AppendLine($"- {error}");
        }
        return report.ToString();
    }

    public string BuildRawData(string adapter, int width, int height, bool passed)
    {
        var intervals = _frameIntervalsMilliseconds.OrderBy(value => value).ToArray();
        var summary = Summarize(intervals);
        var payload = new
        {
            schemaVersion = 1,
            run = new
            {
                runId = RunId,
                startedUtc = StartedUtc,
                generatedUtc = DateTimeOffset.UtcNow,
                machineName = Environment.MachineName,
                operatingSystem = RuntimeInformation.OSDescription,
                framework = RuntimeInformation.FrameworkDescription,
                adapter,
                viewportWidth = width,
                viewportHeight = height,
                referenceScene = ReferenceScene,
                targetDurationSeconds = TargetDuration.TotalSeconds,
                measuredDurationSeconds = ElapsedSeconds,
                wallElapsedSeconds = WallElapsedSeconds,
                passed
            },
            frameTiming = summary,
            warmupMemorySamplesExcluded = Math.Min(4, _memorySamples.Count),
            modeChanges = _modeChanges,
            excludedHostPauseIntervals = _hostPauseIntervals,
            memorySamples = _memorySamples,
            frameIntervalsMilliseconds = _frameIntervalsMilliseconds,
            errors = _errors
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static FrameSummary Summarize(double[] ordered)
    {
        if (ordered.Length == 0) return new FrameSummary(0, 0d, 0d, 0d, 0d, 0d, 0, 0, 0, 0);
        return new FrameSummary(ordered.Length, ordered.Average(), Percentile(ordered, 0.50d),
            Percentile(ordered, 0.95d), Percentile(ordered, 0.99d), ordered[^1],
            ordered.Count(value => value > 16.7d), ordered.Count(value => value > 33.3d),
            ordered.Count(value => value > 50d), ordered.Count(value => value > 100d));
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static void AppendMetric(StringBuilder report, string name, IEnumerable<long> values, string suffix)
    {
        var samples = values.ToArray();
        if (samples.Length == 0)
        {
            report.AppendLine($"| {name} | inconclusive | inconclusive | inconclusive | inconclusive |");
            return;
        }

        var scale = suffix == " MiB" ? 1024d * 1024d : 1d;
        var format = suffix == " MiB" ? "F1" : "F0";
        var peakGrowth = samples.Max() - samples[0];
        report.AppendLine($"| {name} | {(samples[0] / scale).ToString(format)}{suffix} | {(samples[^1] / scale).ToString(format)}{suffix} | {(samples.Max() / scale).ToString(format)}{suffix} | {(peakGrowth / scale).ToString(format)}{suffix} |");
    }

    private sealed record FrameSummary(int Count, double AverageMilliseconds, double P50Milliseconds,
        double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds,
        int Over16Milliseconds, int Over33Milliseconds, int Over50Milliseconds, int Over100Milliseconds);
}

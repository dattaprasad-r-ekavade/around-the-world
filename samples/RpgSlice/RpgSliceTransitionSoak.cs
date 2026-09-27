using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RpgSlice;

/// <summary>Records evidence from repeated interior/exterior door transitions.</summary>
internal sealed class RpgSliceTransitionSoak
{
    public const int DefaultTargetTransitions = 50;
    public const double MemoryGrowthLimitMiB = 30d;

    private readonly int _targetTransitions;
    private readonly Stopwatch _totalClock = new();
    private readonly List<TransitionRecord> _records = new();
    private readonly List<SoakError> _errors = new();
    private bool _timedOut;
    private string? _timeoutMessage;

    public sealed record TransitionRecord(
        int Index,
        string FromCell,
        string ToCell,
        double TransitionSeconds,
        int ActiveCells,
        int TerrainChunks,
        int TrackedGraphicsResources,
        long WorkingSetBytes,
        long ManagedHeapBytes);

    public sealed record SoakError(string Stage, string Message);

    private sealed record MetricCheck(string Status, string Detail, bool Passed);

    private sealed record Evaluation(
        bool Passed,
        MetricCheck TransitionCount,
        MetricCheck Timeout,
        MetricCheck Latency,
        MetricCheck Resources,
        MetricCheck WorkingSet,
        MetricCheck ManagedHeap,
        MetricCheck Errors,
        int WarmupCount,
        IReadOnlyList<TransitionRecord> PostWarmupRecords);

    public RpgSliceTransitionSoak(int targetTransitions = DefaultTargetTransitions)
    {
        if (targetTransitions <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetTransitions), "Target transitions must be positive.");
        _targetTransitions = targetTransitions;
        RunId = Guid.NewGuid();
        StartedUtc = DateTimeOffset.UtcNow;
        _totalClock.Start();
    }

    public Guid RunId { get; }
    public DateTimeOffset StartedUtc { get; }
    public int TargetTransitions => _targetTransitions;
    public int CompletedTransitions => _records.Count;
    public bool IsComplete => _records.Count >= _targetTransitions;
    public IReadOnlyList<TransitionRecord> Records => _records;
    public IReadOnlyList<SoakError> Errors => _errors;
    public double TotalElapsedSeconds => _totalClock.Elapsed.TotalSeconds;
    public int ExitCode => Passed ? 0 : 1;

    public void RecordTransition(string fromCell, string toCell, double transitionSeconds,
        int activeCells, int terrainChunks, int trackedGraphicsResources, long workingSetBytes)
    {
        fromCell ??= string.Empty;
        toCell ??= string.Empty;
        var record = new TransitionRecord(
            _records.Count + 1,
            fromCell,
            toCell,
            transitionSeconds,
            activeCells,
            terrainChunks,
            trackedGraphicsResources,
            workingSetBytes,
            GC.GetTotalMemory(false));
        _records.Add(record);

        if (string.IsNullOrWhiteSpace(fromCell) || string.IsNullOrWhiteSpace(toCell))
            RecordError("sample validation", $"Transition {record.Index} has a missing source or destination cell.");
        if (!double.IsFinite(transitionSeconds) || transitionSeconds < 0d)
            RecordError("sample validation", $"Transition {record.Index} has invalid latency: {transitionSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        if (activeCells < 0 || terrainChunks < 0 || trackedGraphicsResources < 0)
            RecordError("sample validation", $"Transition {record.Index} contains a negative resource count.");
        if (workingSetBytes <= 0)
            RecordError("sample validation", $"Transition {record.Index} has invalid working-set sample: {workingSetBytes} bytes.");
    }

    public void RecordError(string stage, string message)
    {
        _errors.Add(new SoakError(
            string.IsNullOrWhiteSpace(stage) ? "unspecified" : stage.Trim(),
            string.IsNullOrWhiteSpace(message) ? "Unspecified soak failure." : message.Trim()));
    }

    public void RecordError(string stage, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        RecordError(stage, $"{error.GetType().Name}: {error.Message}");
    }

    public void RecordTimeout(string message)
    {
        if (_timedOut) return;
        _timedOut = true;
        _timeoutMessage = string.IsNullOrWhiteSpace(message) ? "Transition soak timed out." : message.Trim();
        RecordError("timeout", _timeoutMessage);
    }

    public bool Passed => Evaluate().Passed;

    public string BuildReport(string adapter, int viewportWidth, int viewportHeight)
    {
        var evaluation = Evaluate();
        var sb = new StringBuilder();
        sb.AppendLine("# RPG Slice - Transition Soak Report");
        sb.AppendLine();
        AppendMetadata(sb, adapter, viewportWidth, viewportHeight);
        sb.AppendLine($"Target transitions: {_targetTransitions}");
        sb.AppendLine($"Completed transitions: {_records.Count}");
        sb.AppendLine($"Total duration: {TotalElapsedSeconds:F1}s ({TotalElapsedSeconds / 60d:F2} min)");
        sb.AppendLine($"Timed out: {_timedOut}");
        sb.AppendLine();

        sb.AppendLine("## Verification Checks");
        sb.AppendLine();
        sb.AppendLine("| Check | Result | Details |");
        sb.AppendLine("| --- | --- | --- |");
        AppendCheck(sb, "Transition count", evaluation.TransitionCount);
        AppendCheck(sb, "Timeout", evaluation.Timeout);
        AppendCheck(sb, "Transition latency", evaluation.Latency);
        AppendCheck(sb, "Resource trend", evaluation.Resources);
        AppendCheck(sb, "Working-set memory", evaluation.WorkingSet);
        AppendCheck(sb, "Managed-heap memory", evaluation.ManagedHeap);
        AppendCheck(sb, "Accumulated errors", evaluation.Errors);
        sb.AppendLine();

        sb.AppendLine("## Resource Trend Samples");
        sb.AppendLine();
        sb.AppendLine($"Warmup transitions excluded from trend checks: {evaluation.WarmupCount}");
        sb.AppendLine("Resource counts are compared within each destination cell; any post-warmup peak above that cell's first post-warmup sample is reported as growth.");
        sb.AppendLine();
        AppendResourceTrend(sb, evaluation.PostWarmupRecords, "active cells", r => r.ActiveCells);
        AppendResourceTrend(sb, evaluation.PostWarmupRecords, "terrain chunks", r => r.TerrainChunks);
        AppendResourceTrend(sb, evaluation.PostWarmupRecords, "tracked graphics resources", r => r.TrackedGraphicsResources);
        sb.AppendLine();

        sb.AppendLine("## Memory Samples");
        sb.AppendLine();
        AppendMemoryTrend(sb, "Working set", evaluation.PostWarmupRecords, r => r.WorkingSetBytes);
        AppendMemoryTrend(sb, "Managed heap", evaluation.PostWarmupRecords, r => r.ManagedHeapBytes);
        sb.AppendLine($"Memory growth limit: {MemoryGrowthLimitMiB.ToString("F1", CultureInfo.InvariantCulture)} MiB per metric (provisional diagnostic limit; reference-PC budgets remain for M0.3).");
        sb.AppendLine();

        sb.AppendLine("## Errors");
        sb.AppendLine();
        if (_errors.Count == 0)
            sb.AppendLine("None recorded.");
        else
            for (var i = 0; i < _errors.Count; i++)
                sb.AppendLine($"{i + 1}. **{EscapeCell(_errors[i].Stage)}:** {EscapeCell(_errors[i].Message)}");
        sb.AppendLine();

        sb.AppendLine("## Raw Transition Samples");
        sb.AppendLine();
        sb.AppendLine("The complete machine-readable record is exported alongside this report as JSON. The table below is a readable preview of every transition.");
        sb.AppendLine();
        sb.AppendLine("| # | From -> To | Latency (ms) | Active cells | Terrain chunks | Gfx resources | Working set (MiB) | Managed heap (MiB) |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var record in _records)
        {
            sb.AppendLine($"| {record.Index} | {EscapeCell(record.FromCell)} -> {EscapeCell(record.ToCell)} | {FormatMilliseconds(record.TransitionSeconds)} | {record.ActiveCells} | {record.TerrainChunks} | {record.TrackedGraphicsResources} | {ToMiB(record.WorkingSetBytes):F1} | {ToMiB(record.ManagedHeapBytes):F1} |");
        }
        if (_records.Count == 0)
            sb.AppendLine("| — | No transitions recorded | — | — | — | — | — | — |");
        sb.AppendLine();

        sb.AppendLine("## Verification Verdict");
        sb.AppendLine();
        sb.AppendLine(evaluation.Passed
            ? "**PASS** — all recorded checks passed for this run. This does not establish leak freedom for other workloads."
            : "**FAIL** — one or more checks failed or lacked enough samples to support a stability verdict.");
        return sb.ToString();
    }

    public string BuildRawData(string adapter, int viewportWidth, int viewportHeight)
    {
        var evaluation = Evaluate();
        var payload = new
        {
            schemaVersion = 1,
            run = new
            {
                runId = RunId,
                startedUtc = StartedUtc,
                generatedUtc = DateTimeOffset.UtcNow,
                adapter,
                viewportWidth,
                viewportHeight,
                machineName = Environment.MachineName,
                operatingSystem = RuntimeInformation.OSDescription,
                framework = RuntimeInformation.FrameworkDescription,
                targetTransitions = _targetTransitions,
                completedTransitions = _records.Count,
                elapsedSeconds = TotalElapsedSeconds,
                timedOut = _timedOut,
                passed = evaluation.Passed,
                warmupTransitionCount = evaluation.WarmupCount,
                memoryGrowthLimitMiB = MemoryGrowthLimitMiB
            },
            checks = new
            {
                transitionCount = evaluation.TransitionCount,
                timeout = evaluation.Timeout,
                latency = evaluation.Latency,
                resources = evaluation.Resources,
                workingSet = evaluation.WorkingSet,
                managedHeap = evaluation.ManagedHeap,
                errors = evaluation.Errors
            },
            errors = _errors,
            samples = _records
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
    }

    private Evaluation Evaluate()
    {
        var countPassed = !_timedOut && _records.Count == _targetTransitions;
        var count = new MetricCheck(countPassed ? "PASS" : "FAIL",
            $"target={_targetTransitions}, completed={_records.Count}" + (_records.Count > _targetTransitions ? " (unexpected extra samples)" : string.Empty), countPassed);
        var timeout = new MetricCheck(_timedOut ? "FAIL" : "PASS", _timedOut ? _timeoutMessage ?? "run timed out" : "run completed before the timeout", !_timedOut);

        var latencyValid = _records.Count > 0 && _records.All(r => double.IsFinite(r.TransitionSeconds) && r.TransitionSeconds >= 0d);
        var latencyDetail = _records.Count == 0
            ? "no latency samples"
            : latencyValid
                ? $"n={_records.Count}, avg={_records.Average(r => r.TransitionSeconds) * 1000d:F1} ms, p95={Percentile95(_records.Select(r => r.TransitionSeconds * 1000d)):F1} ms, max={_records.Max(r => r.TransitionSeconds) * 1000d:F1} ms; validity checked, performance budget not set"
                : "one or more latency samples are negative or non-finite";
        var latency = new MetricCheck(latencyValid ? "PASS" : "FAIL", latencyDetail, latencyValid);

        var warmupCount = Math.Min(4, _records.Count / 2);
        var postWarmup = _records.Skip(warmupCount).ToArray();
        var resource = EvaluateResources(postWarmup);
        var workingSet = EvaluateMemory(postWarmup, r => r.WorkingSetBytes, "working-set");
        var managedHeap = EvaluateMemory(postWarmup, r => r.ManagedHeapBytes, "managed-heap");
        var errors = new MetricCheck(_errors.Count == 0 ? "PASS" : "FAIL",
            _errors.Count == 0 ? "0 recorded" : $"{_errors.Count} recorded", _errors.Count == 0);

        var passed = count.Passed && timeout.Passed && latency.Passed && resource.Passed
            && workingSet.Passed && managedHeap.Passed && errors.Passed;
        return new Evaluation(passed, count, timeout, latency, resource, workingSet, managedHeap,
            errors, warmupCount, postWarmup);
    }

    private static MetricCheck EvaluateResources(IReadOnlyList<TransitionRecord> samples)
    {
        if (samples.Count < 2)
            return new MetricCheck("INCONCLUSIVE", $"only {samples.Count} post-warmup sample(s); need repeated samples per destination cell", false);

        var groups = samples.GroupBy(r => r.ToCell, StringComparer.OrdinalIgnoreCase).ToArray();
        var insufficient = groups.Where(group => group.Count() < 2).Select(group => group.Key).ToArray();
        if (insufficient.Length > 0)
            return new MetricCheck("INCONCLUSIVE", "need repeated post-warmup samples for: " + string.Join(", ", insufficient), false);

        var growth = new List<string>();
        CheckResourceGrowth(samples, r => r.ActiveCells, "active cells", growth);
        CheckResourceGrowth(samples, r => r.TerrainChunks, "terrain chunks", growth);
        CheckResourceGrowth(samples, r => r.TrackedGraphicsResources, "tracked graphics resources", growth);
        return growth.Count == 0
            ? new MetricCheck("PASS", $"no post-warmup peak growth across {groups.Length} destination cell(s)", true)
            : new MetricCheck("FAIL", string.Join("; ", growth), false);
    }

    private static void CheckResourceGrowth(IReadOnlyList<TransitionRecord> samples,
        Func<TransitionRecord, int> selector, string label, ICollection<string> growth)
    {
        foreach (var group in samples.GroupBy(r => r.ToCell, StringComparer.OrdinalIgnoreCase))
        {
            var values = group.Select(selector).ToArray();
            var first = values[0];
            var peak = values.Max();
            if (peak > first)
                growth.Add($"{label} grew at {group.Key} ({first} -> peak {peak})");
        }
    }

    private static MetricCheck EvaluateMemory(IReadOnlyList<TransitionRecord> samples,
        Func<TransitionRecord, long> selector, string label)
    {
        if (samples.Count < 2)
            return new MetricCheck("INCONCLUSIVE", $"only {samples.Count} post-warmup sample(s); need at least 2", false);

        var values = samples.Select(selector).ToArray();
        if (values.Any(value => value < 0))
            return new MetricCheck("FAIL", "one or more memory samples are negative", false);

        var start = ToMiB(values[0]);
        var end = ToMiB(values[^1]);
        var peak = ToMiB(values.Max());
        var delta = end - start;
        var peakGrowth = ToMiB(values.Max() - values[0]);
        var passed = peakGrowth <= MemoryGrowthLimitMiB;
        return new MetricCheck(passed ? "PASS" : "FAIL",
            $"post-warmup start={start:F1} MiB, end={end:F1} MiB, peak={peak:F1} MiB, end delta={delta:+0.0;-0.0;0.0} MiB, peak growth={peakGrowth:+0.0;-0.0;0.0} MiB; limit=+{MemoryGrowthLimitMiB:F1} MiB",
            passed);
    }

    private static void AppendResourceTrend(StringBuilder sb, IReadOnlyList<TransitionRecord> samples,
        string label, Func<TransitionRecord, int> selector)
    {
        sb.AppendLine($"### {label}");
        sb.AppendLine();
        sb.AppendLine("| Destination cell | Samples | First | Final | Peak | Net change |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in samples.GroupBy(r => r.ToCell, StringComparer.OrdinalIgnoreCase))
        {
            var values = group.Select(selector).ToArray();
            sb.AppendLine($"| {EscapeCell(group.Key)} | {values.Length} | {values[0]} | {values[^1]} | {values.Max()} | {values[^1] - values[0]:+0;-0;0} |");
        }
        if (samples.Count == 0)
            sb.AppendLine("| — | 0 | — | — | — | — |");
        sb.AppendLine();
    }

    private static void AppendMemoryTrend(StringBuilder sb, string label,
        IReadOnlyList<TransitionRecord> samples, Func<TransitionRecord, long> selector)
    {
        if (samples.Count == 0)
        {
            sb.AppendLine($"- {label}: no post-warmup samples.");
            return;
        }
        var values = samples.Select(selector).ToArray();
        sb.AppendLine($"- {label}: {ToMiB(values[0]):F1} MiB first to {ToMiB(values[^1]):F1} MiB final; peak {ToMiB(values.Max()):F1} MiB; end delta {ToMiB(values[^1] - values[0]):+0.0;-0.0;0.0} MiB; peak growth {ToMiB(values.Max() - values[0]):+0.0;-0.0;0.0} MiB.");
    }

    private void AppendMetadata(StringBuilder sb, string adapter, int width, int height)
    {
        sb.AppendLine($"Run ID: {RunId:N}");
        sb.AppendLine($"Run started: {StartedUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
        sb.AppendLine($"Host adapter: {adapter} ({width}x{height})");
        sb.AppendLine($"Machine: {Environment.MachineName}; {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine("Route: Exterior (0, 0) <-> House A Interior");
    }

    private static void AppendCheck(StringBuilder sb, string name, MetricCheck check) =>
        sb.AppendLine($"| {name} | **{check.Status}** | {EscapeCell(check.Detail)} |");

    private static double Percentile95(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length == 0 ? 0d : sorted[(int)Math.Ceiling(sorted.Length * 0.95d) - 1];
    }

    private static string FormatMilliseconds(double seconds) =>
        double.IsFinite(seconds) ? (seconds * 1000d).ToString("F1", CultureInfo.InvariantCulture) : seconds.ToString(CultureInfo.InvariantCulture);

    private static double ToMiB(long bytes) => bytes / (1024d * 1024d);

    private static string EscapeCell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

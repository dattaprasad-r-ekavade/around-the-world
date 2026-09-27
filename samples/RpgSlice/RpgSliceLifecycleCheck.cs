using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ember.Scene;
using Ember.World;

namespace RpgSlice;

/// <summary>Records fixed-count live lifecycle coverage separately from a timed transition soak.</summary>
internal sealed class RpgSliceLifecycleCheck
{
    public const int MinimumTransitions = 50;
    public const double MemoryGrowthLimitMiB = 30d;

    public sealed record TransitionSample(int Index, string FromCell, string ToCell,
        int ActiveCells, int TerrainChunks, int TrackedGraphicsResources,
        long WorkingSetBytes, long ManagedHeapBytes);
    public sealed record LifecycleError(string Stage, string Message);
    private sealed record Check(string Status, string Detail, bool Passed);

    private readonly int _targetTransitions;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<TransitionSample> _samples = new();
    private readonly List<LifecycleError> _errors = new();
    private readonly Dictionary<(Guid CellId, Guid ObjectId), Guid> _knownIdentities = new();
    private int _expectedLoadFailures;
    private int _retriedLoads;
    private int _identityChecks;
    private int _consistentIdentityChecks;
    private bool _retryPending;
    private bool _timedOut;
    private bool _saveRestartPassed;
    private string? _saveRestartDetail;

    public RpgSliceLifecycleCheck(int targetTransitions = MinimumTransitions)
    {
        if (targetTransitions < MinimumTransitions)
            throw new ArgumentOutOfRangeException(nameof(targetTransitions),
                $"Lifecycle coverage requires at least {MinimumTransitions} transitions.");
        _targetTransitions = targetTransitions;
        RunId = Guid.NewGuid();
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public Guid RunId { get; }
    public DateTimeOffset StartedUtc { get; }
    public int TargetTransitions => _targetTransitions;
    public int CompletedTransitions => _samples.Count;
    public bool IsComplete => _samples.Count >= _targetTransitions;
    public IReadOnlyList<TransitionSample> Samples => _samples;
    public IReadOnlyList<LifecycleError> Errors => _errors;
    public int ExitCode => Passed ? 0 : 1;
    public bool Passed => Evaluate().Passed;

    public void RecordExpectedPreparationFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _expectedLoadFailures++;
        _retryPending = true;
        _errors.Add(new LifecycleError("expected injected load failure",
            $"{failure.GetType().Name}: {failure.Message}"));
    }

    public void RecordUnexpectedError(string stage, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _errors.Add(new LifecycleError(stage, $"{failure.GetType().Name}: {failure.Message}"));
    }

    public void RecordTimeout(string message)
    {
        if (_timedOut) return;
        _timedOut = true;
        _errors.Add(new LifecycleError("timeout", message));
    }

    public void RecordTransition(string fromCell, string toCell, int activeCells,
        int terrainChunks, int trackedGraphicsResources, long workingSetBytes)
    {
        _samples.Add(new TransitionSample(_samples.Count + 1, fromCell, toCell,
            activeCells, terrainChunks, trackedGraphicsResources, workingSetBytes,
            GC.GetTotalMemory(false)));
        if (_retryPending)
        {
            _retriedLoads++;
            _retryPending = false;
        }
        if (string.IsNullOrWhiteSpace(fromCell) || string.IsNullOrWhiteSpace(toCell)
            || activeCells < 0 || terrainChunks < 0 || trackedGraphicsResources < 0
            || workingSetBytes <= 0)
            _errors.Add(new LifecycleError("sample validation", $"Transition {_samples.Count} has invalid cell or resource data."));
    }

    public void RecordCellIdentities(Guid cellId, SceneGraph scene, WorldInstanceIdentityMap identities)
    {
        _identityChecks++;
        var valid = true;
        foreach (var item in scene.Objects)
        {
            if (!identities.TryGet(cellId, item.Id, out var instanceId))
            {
                _errors.Add(new LifecycleError("world identity", $"Cell {cellId} object {item.Id} has no runtime identity."));
                valid = false;
                continue;
            }

            var key = (cellId, item.Id);
            if (_knownIdentities.TryGetValue(key, out var previous) && previous != instanceId.Value)
            {
                _errors.Add(new LifecycleError("world identity", $"Cell {cellId} object {item.Id} changed identity after reload."));
                valid = false;
            }
            else
            {
                _knownIdentities[key] = instanceId.Value;
            }
        }

        var snapshot = identities.ExportSnapshot();
        if (snapshot.Select(entry => entry.InstanceId.Value).Distinct().Count() != snapshot.Count)
        {
            _errors.Add(new LifecycleError("world identity", "Runtime identity IDs are not unique across the loaded world."));
            valid = false;
        }
        if (valid) _consistentIdentityChecks++;
    }

    public void RecordSaveRestart(bool passed, string detail)
    {
        _saveRestartPassed = passed;
        _saveRestartDetail = detail;
    }

    public string BuildReport(string adapter, int viewportWidth, int viewportHeight)
    {
        var evaluation = Evaluate();
        var sb = new StringBuilder();
        sb.AppendLine("# RpgSlice Live Lifecycle Check");
        sb.AppendLine();
        sb.AppendLine($"Run ID: {RunId:N}");
        sb.AppendLine($"Started UTC: {StartedUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");
        sb.AppendLine($"Machine: {Environment.MachineName}; {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Graphics: {adapter} ({viewportWidth}x{viewportHeight})");
        sb.AppendLine($"Target/completed transitions: {_targetTransitions}/{_samples.Count}");
        sb.AppendLine($"Elapsed seconds: {_clock.Elapsed.TotalSeconds:F1}");
        sb.AppendLine();
        sb.AppendLine("## Acceptance Checks");
        sb.AppendLine();
        sb.AppendLine("| Check | Result | Details |");
        sb.AppendLine("| --- | --- | --- |");
        AppendCheck(sb, "Transition route", evaluation.Route);
        AppendCheck(sb, "Delayed load failure and retry", evaluation.Retry);
        AppendCheck(sb, "Stable unique identities", evaluation.Identities);
        AppendCheck(sb, "Per-cell resource trend", evaluation.Resources);
        AppendCheck(sb, "Working-set trend", evaluation.WorkingSet);
        AppendCheck(sb, "Managed-heap trend", evaluation.ManagedHeap);
        AppendCheck(sb, "Save/restart", evaluation.SaveRestart);
        AppendCheck(sb, "Timeout", evaluation.Timeout);
        AppendCheck(sb, "Unexpected errors", evaluation.Errors);
        sb.AppendLine();

        sb.AppendLine("## Per-cell resource samples");
        sb.AppendLine();
        sb.AppendLine("| Cell | Samples | Active cells first/final/peak | Terrain chunks first/final/peak | Tracked resources first/final/peak |");
        sb.AppendLine("| --- | ---: | --- | --- | --- |");
        var trendSamples = PostWarmupSamples(evaluation.WarmupCount);
        foreach (var group in trendSamples.GroupBy(sample => sample.ToCell, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| {Escape(group.Key)} | {group.Count()} | {Format(group.Select(x => x.ActiveCells))} | {Format(group.Select(x => x.TerrainChunks))} | {Format(group.Select(x => x.TrackedGraphicsResources))} |");
        }
        sb.AppendLine();
        sb.AppendLine($"Warmup samples excluded: {evaluation.WarmupCount}; memory growth diagnostic limit: {MemoryGrowthLimitMiB:F1} MiB per metric (provisional until M0.3).");
        sb.AppendLine();

        sb.AppendLine("## Transition samples");
        sb.AppendLine();
        sb.AppendLine("| # | From -> To | Active cells | Terrain chunks | Tracked resources | Working set MiB | Managed heap MiB |");
        sb.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var sample in _samples)
            sb.AppendLine($"| {sample.Index} | {Escape(sample.FromCell)} -> {Escape(sample.ToCell)} | {sample.ActiveCells} | {sample.TerrainChunks} | {sample.TrackedGraphicsResources} | {ToMiB(sample.WorkingSetBytes):F1} | {ToMiB(sample.ManagedHeapBytes):F1} |");
        sb.AppendLine();
        sb.AppendLine("## Expected injected failures");
        sb.AppendLine();
        if (_expectedLoadFailures == 0) sb.AppendLine("None recorded.");
        else foreach (var item in _errors.Where(error => error.Stage == "expected injected load failure"))
            sb.AppendLine($"- {Escape(item.Message)}");
        sb.AppendLine();
        sb.AppendLine("## Unexpected errors");
        sb.AppendLine();
        var unexpected = _errors.Where(error => error.Stage != "expected injected load failure").ToArray();
        if (unexpected.Length == 0) sb.AppendLine("None recorded.");
        else foreach (var item in unexpected) sb.AppendLine($"- **{Escape(item.Stage)}:** {Escape(item.Message)}");
        sb.AppendLine();
        sb.AppendLine("## Verdict");
        sb.AppendLine();
        sb.AppendLine(evaluation.Passed
            ? "**PASS** - live fixed-count lifecycle checks passed. This is not a long-duration soak."
            : "**FAIL** - one or more live lifecycle checks failed or lacked enough evidence.");
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
                machineName = Environment.MachineName,
                operatingSystem = RuntimeInformation.OSDescription,
                framework = RuntimeInformation.FrameworkDescription,
                adapter,
                viewportWidth,
                viewportHeight,
                targetTransitions = _targetTransitions,
                completedTransitions = _samples.Count,
                elapsedSeconds = _clock.Elapsed.TotalSeconds,
                expectedLoadFailures = _expectedLoadFailures,
                retriedLoads = _retriedLoads,
                identityChecks = _identityChecks,
                consistentIdentityChecks = _consistentIdentityChecks,
                saveRestartPassed = _saveRestartPassed,
                saveRestartDetail = _saveRestartDetail,
                passed = evaluation.Passed
            },
            checks = new
            {
                transitionRoute = evaluation.Route,
                retry = evaluation.Retry,
                identities = evaluation.Identities,
                resources = evaluation.Resources,
                workingSet = evaluation.WorkingSet,
                managedHeap = evaluation.ManagedHeap,
                saveRestart = evaluation.SaveRestart,
                timeout = evaluation.Timeout,
                errors = evaluation.Errors
            },
            errors = _errors,
            transitions = _samples
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed record Evaluation(bool Passed, int WarmupCount,
        Check Route, Check Retry, Check Identities, Check Resources,
        Check WorkingSet, Check ManagedHeap, Check SaveRestart, Check Timeout, Check Errors);

    private Evaluation Evaluate()
    {
        var enoughTransitions = _samples.Count >= MinimumTransitions && _samples.Count == _targetTransitions;
        var cells = _samples.Select(sample => sample.ToCell).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var routePassed = enoughTransitions
            && cells.Any(cell => cell.Contains("House_A", StringComparison.OrdinalIgnoreCase))
            && cells.Any(cell => cell.Contains("House_B", StringComparison.OrdinalIgnoreCase))
            && cells.Any(cell => cell.Contains("Exterior", StringComparison.OrdinalIgnoreCase));
        var route = new Check(routePassed ? "PASS" : "FAIL",
            $"target={_targetTransitions}, completed={_samples.Count}, distinct destinations=[{string.Join(", ", cells)}]", routePassed);
        var retryPassed = _expectedLoadFailures == 1 && _retriedLoads == 1 && !_retryPending;
        var retry = new Check(retryPassed ? "PASS" : "FAIL",
            $"delayed injected failures={_expectedLoadFailures}, successful retries={_retriedLoads}", retryPassed);
        var identitiesPassed = _identityChecks >= 2 && _identityChecks == _consistentIdentityChecks
            && _knownIdentities.Count > 0;
        var identities = new Check(identitiesPassed ? "PASS" : "FAIL",
            $"reload checks={_consistentIdentityChecks}/{_identityChecks}, stable object mappings={_knownIdentities.Count}", identitiesPassed);

        var warmupCount = Math.Min(4, _samples.Count / 2);
        var postWarmup = PostWarmupSamples(warmupCount);
        var resources = EvaluateResources(postWarmup);
        var workingSet = EvaluateMemory(postWarmup, sample => sample.WorkingSetBytes, "working-set");
        var managedHeap = EvaluateMemory(postWarmup, sample => sample.ManagedHeapBytes, "managed-heap");
        var saveRestart = new Check(_saveRestartPassed ? "PASS" : "FAIL",
            _saveRestartDetail ?? "save/restart check was not recorded", _saveRestartPassed);
        var timeout = new Check(_timedOut ? "FAIL" : "PASS",
            _timedOut ? "lifecycle run timed out" : "run finished within the time limit", !_timedOut);
        var unexpectedErrors = _errors.Where(error => error.Stage != "expected injected load failure").ToArray();
        var errors = new Check(unexpectedErrors.Length == 0 ? "PASS" : "FAIL",
            $"{unexpectedErrors.Length} unexpected error(s)", unexpectedErrors.Length == 0);
        var passed = route.Passed && retry.Passed && identities.Passed && resources.Passed
            && workingSet.Passed && managedHeap.Passed && saveRestart.Passed && timeout.Passed && errors.Passed;
        return new Evaluation(passed, warmupCount, route, retry, identities, resources,
            workingSet, managedHeap, saveRestart, timeout, errors);
    }

    private IReadOnlyList<TransitionSample> PostWarmupSamples(int warmupCount) => _samples.Skip(warmupCount).ToArray();

    private static Check EvaluateResources(IReadOnlyList<TransitionSample> samples)
    {
        if (samples.Count < 2)
            return new Check("INCONCLUSIVE", $"only {samples.Count} post-warmup samples", false);
        var groups = samples.GroupBy(sample => sample.ToCell, StringComparer.OrdinalIgnoreCase).ToArray();
        var shortGroups = groups.Where(group => group.Count() < 2).Select(group => group.Key).ToArray();
        if (shortGroups.Length > 0)
            return new Check("INCONCLUSIVE", "need repeated samples for " + string.Join(", ", shortGroups), false);
        var grew = groups.SelectMany(group => new[]
        {
            Growth(group, sample => sample.ActiveCells, "active cells"),
            Growth(group, sample => sample.TerrainChunks, "terrain chunks"),
            Growth(group, sample => sample.TrackedGraphicsResources, "tracked resources")
        }).Where(value => value is not null).ToArray();
        return grew.Length == 0
            ? new Check("PASS", $"no per-cell peak growth across {groups.Length} destinations", true)
            : new Check("FAIL", string.Join("; ", grew!), false);
    }

    private static string? Growth(IGrouping<string, TransitionSample> group,
        Func<TransitionSample, int> metric, string label)
    {
        var values = group.Select(metric).ToArray();
        return values.Max() > values[0] ? $"{label} grew at {group.Key} ({values[0]} -> {values.Max()})" : null;
    }

    private static Check EvaluateMemory(IReadOnlyList<TransitionSample> samples,
        Func<TransitionSample, long> metric, string label)
    {
        if (samples.Count < 2)
            return new Check("INCONCLUSIVE", $"only {samples.Count} post-warmup samples", false);
        var values = samples.Select(metric).ToArray();
        var peakGrowth = ToMiB(values.Max() - values[0]);
        var passed = values.All(value => value > 0) && peakGrowth <= MemoryGrowthLimitMiB;
        return new Check(passed ? "PASS" : "FAIL",
            $"first={ToMiB(values[0]):F1} MiB, final={ToMiB(values[^1]):F1} MiB, peak={ToMiB(values.Max()):F1} MiB, peak growth={peakGrowth:+0.0;-0.0;0.0} MiB, limit=+{MemoryGrowthLimitMiB:F1} MiB ({label})", passed);
    }

    private static string Format(IEnumerable<int> source)
    {
        var values = source.ToArray();
        return $"{values[0]}/{values[^1]}/{values.Max()}";
    }

    private static void AppendCheck(StringBuilder sb, string label, Check check) =>
        sb.AppendLine($"| {label} | **{check.Status}** | {Escape(check.Detail)} |");

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    private static double ToMiB(long bytes) => bytes / (1024d * 1024d);
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Ember.World;
using Microsoft.Xna.Framework;

namespace RpgSlice;

internal enum BenchmarkPhase
{
    CellActivation,
    TerrainWork,
    SceneSubmission
}

internal enum FrameSpikeCause
{
    CellActivation,
    TerrainWork,
    SceneSubmission,
    GarbageCollection,
    ExternalScheduling
}

internal sealed class FrameSpikeRecord
{
    public int FrameIndex { get; init; }
    public double TotalMilliseconds { get; init; }
    public FrameSpikeCause PrimaryCause { get; init; }
    public double CellActivationMilliseconds { get; init; }
    public double TerrainWorkMilliseconds { get; init; }
    public double SceneSubmissionMilliseconds { get; init; }
    public double GcPauseMilliseconds { get; init; }
    public int Gen0Delta { get; init; }
    public int Gen1Delta { get; init; }
    public int Gen2Delta { get; init; }
    public double ExternalSchedulingMilliseconds { get; init; }
    public string Details { get; init; } = string.Empty;
}

internal readonly struct PhaseScope : IDisposable
{
    private readonly RpgSliceOutdoorBenchmark? _benchmark;
    private readonly BenchmarkPhase _phase;
    private readonly long _startTimestamp;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public PhaseScope(RpgSliceOutdoorBenchmark? benchmark, BenchmarkPhase phase)
    {
        _benchmark = benchmark;
        _phase = phase;
        _startTimestamp = benchmark is { IsMeasuring: true, IsComplete: false } ? Stopwatch.GetTimestamp() : 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_benchmark is not null && _startTimestamp != 0)
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp;
            _benchmark.RecordPhaseTicks(_phase, elapsedTicks);
        }
    }
}

internal static class BenchmarkPhaseExtensions
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static PhaseScope MeasurePhase(this RpgSliceOutdoorBenchmark? benchmark, BenchmarkPhase phase) =>
        new(benchmark, phase);
}

/// <summary>Drives the fixed outdoor route and collects frame, streaming, and memory measurements.</summary>
internal sealed class RpgSliceOutdoorBenchmark
{
    public const int MeasuredLapCount = 10;
    private const float ArrivalRadius = 0.2f;
    private static readonly TimeSpan LegStallTimeout = TimeSpan.FromSeconds(20);
    private static readonly Vector2[] OutdoorRoute =
    {
        new(48f, 23f),
        new(48f, 48f),
        new(22.5f, 48f),
        new(22.5f, 52f),
        new(16f, 52f),
        new(16f, 23f)
    };
    private static readonly Vector2[] SettlementRoute =
    {
        new(3f, 23f),
        new(3f, 3f),
        new(60f, 3f),
        new(60f, 29f),
        new(16f, 29f),
        new(16f, 23f),
    };
    private readonly Vector2[] _route;

    private readonly List<double> _frameMilliseconds = new();
    private readonly List<double> _lapMilliseconds = new(MeasuredLapCount);
    private readonly List<string> _cellCrossings = new();
    private readonly List<string> _lapResourceTrend = new(MeasuredLapCount);
    private readonly Stopwatch _lapClock = new();
    private readonly Stopwatch _measuredClock = new();
    private readonly Stopwatch _legClock = Stopwatch.StartNew();
    private Vector2 _legStart;
    private Vector2 _legTarget;
    private int _legIndex;
    private int _completedLaps;
    private long _lastFrameTimestamp;
    private ExteriorCellCoordinate? _lastCell;
    private double _nextWorkingSetSampleSeconds;
    private float _furthestLegProgress;
    private int _framesOver50Milliseconds;
    private int _framesOver100Milliseconds;
    private double _lapMaximumFrameMilliseconds;
    private int _lapPeakActiveCells;
    private int _lapPeakTerrainChunks;
    private int _lapPeakTrackedGraphicsResources;
    private long _lapPeakWorkingSetBytes;
    private readonly List<FrameSpikeRecord> _frameSpikes = new();
    private long _currentCellActivationTicks;
    private long _currentTerrainTicks;
    private long _currentSceneSubmissionTicks;
    private TimeSpan _startGcPause;
    private int _startGen0;
    private int _startGen1;
    private int _startGen2;
    private int _totalMeasuredFrames;

    public RpgSliceOutdoorBenchmark(Vector3 startPosition, bool settlementRoute = false)
    {
        _route = settlementRoute ? SettlementRoute : OutdoorRoute;
        ScenarioName = settlementRoute ? "settlement" : "outdoor";
        _legStart = new Vector2(startPosition.X, startPosition.Z);
        _legTarget = _route[0];
        _lapClock.Start();
    }

    public string ScenarioName { get; }
    public bool IsMeasuring { get; private set; }
    public bool IsComplete { get; private set; }
    public bool IsFailed { get; private set; }
    public string? FailureReason { get; private set; }
    public double MeasuredElapsedSeconds => _measuredClock.Elapsed.TotalSeconds;
    public string ProgressLabel => IsFailed ? "route blocked"
        : IsMeasuring ? $"lap {_lapMilliseconds.Count + 1}/{MeasuredLapCount}"
        : "warmup lap";
    public int CurrentLeg => _legIndex + 1;
    public int LegCount => _route.Length;
    public Vector2 CurrentTarget => _legTarget;
    public int CellBoundaryCrossings { get; private set; }
    public int PeakActiveCells { get; private set; }
    public int PeakTerrainChunks { get; private set; }
    public int PeakTrackedGraphicsResources { get; private set; }
    public long PeakWorkingSetBytes { get; private set; }
    public int PeakFoliageInstances { get; private set; }
    public int PeakBatchedDrawSubmissions { get; private set; }
    public int PeakEquivalentIndividualDrawSubmissions { get; private set; }
    public int FramesOver50Milliseconds => _framesOver50Milliseconds;
    public int FramesOver100Milliseconds => _framesOver100Milliseconds;
    public IReadOnlyList<FrameSpikeRecord> FrameSpikes => _frameSpikes;

    public bool ShouldSampleWorkingSet(double elapsedSeconds) =>
        IsMeasuring && !IsComplete && elapsedSeconds >= _nextWorkingSetSampleSeconds;

    public Vector3 GetMoveDirection(Vector3 position)
    {
        if (IsComplete) return Vector3.Zero;

        var current = new Vector2(position.X, position.Z);
        var leg = _legTarget - _legStart;
        var travelled = current - _legStart;
        var routeProgress = leg.LengthSquared() <= 1e-8f
            ? 1f
            : Vector2.Dot(travelled, leg) / leg.LengthSquared();
        if (routeProgress > _furthestLegProgress + 0.01f)
        {
            _furthestLegProgress = routeProgress;
            _legClock.Restart();
        }
        else if (_legClock.Elapsed > LegStallTimeout)
        {
            IsFailed = true;
            IsComplete = true;
            FailureReason = $"no forward progress on leg {_legIndex + 1} toward ({_legTarget.X:0}, {_legTarget.Y:0}) for {LegStallTimeout.TotalSeconds:0} seconds";
            _measuredClock.Stop();
            Console.WriteLine($"RpgSlice benchmark failed: {FailureReason}.");
            Console.Out.Flush();
            return Vector3.Zero;
        }
        if (routeProgress >= 1f || Vector2.Distance(current, _legTarget) <= ArrivalRadius)
            AdvanceWaypoint();
        if (IsComplete) return Vector3.Zero;

        var direction = _legTarget - current;
        if (direction.LengthSquared() <= 1e-8f) return Vector3.Zero;
        direction.Normalize();
        return new Vector3(direction.X, 0f, direction.Y);
    }

    public void RecordPhaseTicks(BenchmarkPhase phase, long ticks)
    {
        if (!IsMeasuring || IsComplete) return;
        switch (phase)
        {
            case BenchmarkPhase.CellActivation:
                _currentCellActivationTicks += ticks;
                break;
            case BenchmarkPhase.TerrainWork:
                _currentTerrainTicks += ticks;
                break;
            case BenchmarkPhase.SceneSubmission:
                _currentSceneSubmissionTicks += ticks;
                break;
        }
    }

    public void RecordFrame(long timestamp)
    {
        if (!IsMeasuring || IsComplete)
        {
            _lastFrameTimestamp = 0;
            ResetFramePhaseCounters();
            return;
        }

        if (_lastFrameTimestamp != 0)
        {
            var milliseconds = Stopwatch.GetElapsedTime(_lastFrameTimestamp, timestamp).TotalMilliseconds;
            _totalMeasuredFrames++;
            _frameMilliseconds.Add(milliseconds);
            _lapMaximumFrameMilliseconds = Math.Max(_lapMaximumFrameMilliseconds, milliseconds);
            var cellActivationMs = (double)_currentCellActivationTicks / Stopwatch.Frequency * 1000.0;
            var terrainWorkMs = (double)_currentTerrainTicks / Stopwatch.Frequency * 1000.0;
            var sceneSubmissionMs = (double)_currentSceneSubmissionTicks / Stopwatch.Frequency * 1000.0;
            var currentGcPause = GC.GetTotalPauseDuration();
            var gcPauseMs = Math.Max(0d, (currentGcPause - _startGcPause).TotalMilliseconds);
            var gen0Delta = GC.CollectionCount(0) - _startGen0;
            var gen1Delta = GC.CollectionCount(1) - _startGen1;
            var gen2Delta = GC.CollectionCount(2) - _startGen2;
            var accountedMs = cellActivationMs + terrainWorkMs + sceneSubmissionMs + (gcPauseMs > 0 ? gcPauseMs : 0);
            var externalSchedulingMs = Math.Max(0d, milliseconds - accountedMs);

            if (milliseconds > 50d) _framesOver50Milliseconds++;
            if (milliseconds > 100d) _framesOver100Milliseconds++;
            if (milliseconds > 50d)
            {
                var cause = ClassifySpike(milliseconds, cellActivationMs, terrainWorkMs,
                    sceneSubmissionMs, gcPauseMs, gen1Delta, gen2Delta, externalSchedulingMs);
                var details = $"total={milliseconds:0.00}ms, activation={cellActivationMs:0.00}ms, "
                    + $"terrain={terrainWorkMs:0.00}ms, submission={sceneSubmissionMs:0.00}ms, "
                    + $"gcPause={gcPauseMs:0.00}ms (Gen0:{gen0Delta}, Gen1:{gen1Delta}, Gen2:{gen2Delta}), "
                    + $"scheduling={externalSchedulingMs:0.00}ms";

                _frameSpikes.Add(new FrameSpikeRecord
                {
                    FrameIndex = _totalMeasuredFrames,
                    TotalMilliseconds = milliseconds,
                    PrimaryCause = cause,
                    CellActivationMilliseconds = cellActivationMs,
                    TerrainWorkMilliseconds = terrainWorkMs,
                    SceneSubmissionMilliseconds = sceneSubmissionMs,
                    GcPauseMilliseconds = gcPauseMs,
                    Gen0Delta = gen0Delta,
                    Gen1Delta = gen1Delta,
                    Gen2Delta = gen2Delta,
                    ExternalSchedulingMilliseconds = externalSchedulingMs,
                    Details = details
                });
            }
        }
        _lastFrameTimestamp = timestamp;
        ResetFramePhaseCounters();
    }

    private void ResetFramePhaseCounters()
    {
        _currentCellActivationTicks = 0;
        _currentTerrainTicks = 0;
        _currentSceneSubmissionTicks = 0;
        _startGcPause = GC.GetTotalPauseDuration();
        _startGen0 = GC.CollectionCount(0);
        _startGen1 = GC.CollectionCount(1);
        _startGen2 = GC.CollectionCount(2);
    }

    private static FrameSpikeCause ClassifySpike(
        double totalMs,
        double cellActivationMs,
        double terrainWorkMs,
        double sceneSubmissionMs,
        double gcPauseMs,
        int gen1Delta,
        int gen2Delta,
        double externalSchedulingMs)
    {
        if (gcPauseMs >= 15d || (gen2Delta > 0 && externalSchedulingMs >= 20d))
            return FrameSpikeCause.GarbageCollection;

        var maxEngineMs = Math.Max(cellActivationMs, Math.Max(terrainWorkMs, sceneSubmissionMs));
        if (maxEngineMs >= 25d || maxEngineMs >= totalMs * 0.4d)
        {
            if (cellActivationMs == maxEngineMs) return FrameSpikeCause.CellActivation;
            if (terrainWorkMs == maxEngineMs) return FrameSpikeCause.TerrainWork;
            return FrameSpikeCause.SceneSubmission;
        }

        if (externalSchedulingMs >= totalMs * 0.5d || externalSchedulingMs >= 25d)
            return FrameSpikeCause.ExternalScheduling;

        if (cellActivationMs >= terrainWorkMs && cellActivationMs >= sceneSubmissionMs && cellActivationMs >= externalSchedulingMs)
            return FrameSpikeCause.CellActivation;
        if (terrainWorkMs >= sceneSubmissionMs && terrainWorkMs >= externalSchedulingMs)
            return FrameSpikeCause.TerrainWork;
        if (sceneSubmissionMs >= externalSchedulingMs)
            return FrameSpikeCause.SceneSubmission;
        return FrameSpikeCause.ExternalScheduling;
    }

    public void ObserveRuntime(ExteriorCellCoordinate cell, int activeCells, int terrainChunks,
        int trackedGraphicsResources, long workingSetBytes, double elapsedSeconds)
    {
        if (!IsMeasuring || IsComplete) return;
        if (_lastCell is { } previous && previous != cell)
        {
            CellBoundaryCrossings++;
            _cellCrossings.Add($"lap {_lapMilliseconds.Count + 1}: ({previous.X}, {previous.Z}) -> ({cell.X}, {cell.Z})");
        }
        _lastCell = cell;
        PeakActiveCells = Math.Max(PeakActiveCells, activeCells);
        PeakTerrainChunks = Math.Max(PeakTerrainChunks, terrainChunks);
        PeakTrackedGraphicsResources = Math.Max(PeakTrackedGraphicsResources, trackedGraphicsResources);
        _lapPeakActiveCells = Math.Max(_lapPeakActiveCells, activeCells);
        _lapPeakTerrainChunks = Math.Max(_lapPeakTerrainChunks, terrainChunks);
        _lapPeakTrackedGraphicsResources = Math.Max(_lapPeakTrackedGraphicsResources, trackedGraphicsResources);

        if (workingSetBytes > 0 && elapsedSeconds >= _nextWorkingSetSampleSeconds)
        {
            PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, workingSetBytes);
            _lapPeakWorkingSetBytes = Math.Max(_lapPeakWorkingSetBytes, workingSetBytes);
            _nextWorkingSetSampleSeconds = elapsedSeconds + 0.5d;
        }
    }

    public void ObserveInstancing(int instances, int batchedDrawSubmissions)
    {
        if (!IsMeasuring || IsComplete) return;
        PeakFoliageInstances = Math.Max(PeakFoliageInstances, instances);
        PeakBatchedDrawSubmissions = Math.Max(PeakBatchedDrawSubmissions, batchedDrawSubmissions);
        PeakEquivalentIndividualDrawSubmissions = Math.Max(PeakEquivalentIndividualDrawSubmissions, instances);
    }

    public string BuildReport(double longestActivationMilliseconds, int activationAttempts,
        bool instancingEnabled, int graphicsWidth, int graphicsHeight, string adapter,
        string? contentSummary = null)
    {
        if (!IsComplete) throw new InvalidOperationException("The outdoor benchmark has not completed.");
        var ordered = _frameMilliseconds.OrderBy(value => value).ToArray();
        var average = ordered.Length == 0 ? 0d : ordered.Average();
        var p95 = ordered.Length == 0 ? 0d : ordered[(int)Math.Ceiling(ordered.Length * 0.95d) - 1];
        var maximum = ordered.Length == 0 ? 0d : ordered[^1];
        var outcome = IsFailed ? "ROUTE FAILED"
            : average <= 16.7d && p95 <= 25d && longestActivationMilliseconds <= 50d
            && PeakWorkingSetBytes <= 1024L * 1024 * 1024 && PeakTerrainChunks <= 25
            ? "PASS"
            : "TARGETS MISSED";

        var spikeSummary = new StringBuilder();
        spikeSummary.AppendLine($"Phase attribution for frames >50ms (total={_frameSpikes.Count}, >100ms={_framesOver100Milliseconds}):");
        foreach (var cause in Enum.GetValues<FrameSpikeCause>())
        {
            var count = _frameSpikes.Count(s => s.PrimaryCause == cause);
            var over100 = _frameSpikes.Count(s => s.PrimaryCause == cause && s.TotalMilliseconds > 100d);
            spikeSummary.AppendLine($"  - {cause}: {count} frames (>100ms: {over100})");
        }
        if (_frameSpikes.Count > 0)
        {
            spikeSummary.AppendLine("Classified frames >100ms:");
            var over100Spikes = _frameSpikes.Where(s => s.TotalMilliseconds > 100d).ToArray();
            if (over100Spikes.Length == 0)
            {
                spikeSummary.AppendLine("  (none)");
            }
            else
            {
                foreach (var spike in over100Spikes)
                {
                    spikeSummary.AppendLine($"  [frame #{spike.FrameIndex}] {spike.TotalMilliseconds:0.00}ms -> {spike.PrimaryCause} ({spike.Details})");
                }
            }

            spikeSummary.AppendLine("Classified frames 50ms-100ms:");
            var under100Spikes = _frameSpikes.Where(s => s.TotalMilliseconds <= 100d).ToArray();
            if (under100Spikes.Length == 0)
            {
                spikeSummary.AppendLine("  (none)");
            }
            else
            {
                foreach (var spike in under100Spikes)
                {
                    spikeSummary.AppendLine($"  [frame #{spike.FrameIndex}] {spike.TotalMilliseconds:0.00}ms -> {spike.PrimaryCause} ({spike.Details})");
                }
            }
        }

        return $"RpgSlice {ScenarioName} benchmark: {outcome}\n"
            + (FailureReason is null ? string.Empty : $"Route failure: {FailureReason}.\n")
            + $"Adapter/resolution: {adapter}, {graphicsWidth}x{graphicsHeight}; instancing={(instancingEnabled ? "on" : "fallback")}.\n"
            + $"Route: {_route.Length} waypoints per lap; 1 warmup lap + {MeasuredLapCount} measured laps; measured time={_measuredClock.Elapsed.TotalSeconds:0.0}s; frames={ordered.Length}.\n"
            + $"Frame time ms: average={average:0.00}, p95={p95:0.00}, max={maximum:0.00}; >50ms={FramesOver50Milliseconds}, >100ms={FramesOver100Milliseconds}.\n"
            + $"Cell activation over startup, warmup, and route: attempts={activationAttempts}, longest-main-thread={longestActivationMilliseconds:0.00}ms.\n"
            + $"Peaks: active-cells={PeakActiveCells}, terrain-chunks={PeakTerrainChunks}, tracked-renderer-resources={PeakTrackedGraphicsResources}, working-set={PeakWorkingSetBytes / (1024d * 1024d):0.0}MiB.\n"
            + $"Foliage batching peak: instances={PeakFoliageInstances}, previous individual submissions={PeakEquivalentIndividualDrawSubmissions}, batched submissions={PeakBatchedDrawSubmissions}.\n"
            + (string.IsNullOrWhiteSpace(contentSummary) ? string.Empty : $"Content: {contentSummary}\n")
            + $"Measured cell-boundary crossings={CellBoundaryCrossings}; lap ms=[{string.Join(", ", _lapMilliseconds.Select(value => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))}].\n"
            + $"Per-lap peak resources: {string.Join("; ", _lapResourceTrend)}.\n"
            + $"Crossings: {string.Join("; ", _cellCrossings)}\n"
            + spikeSummary.ToString().TrimEnd();
    }

    private void AdvanceWaypoint()
    {
        Console.WriteLine($"RpgSlice benchmark: {ProgressLabel}, reached waypoint {_legIndex + 1} at ({_legTarget.X:0}, {_legTarget.Y:0}).");
        Console.Out.Flush();
        _legStart = _legTarget;
        _legIndex++;
        _furthestLegProgress = 0f;
        _legClock.Restart();
        if (_legIndex < _route.Length)
        {
            _legTarget = _route[_legIndex];
            return;
        }

        _legIndex = 0;
        _legTarget = _route[0];
        _completedLaps++;
        if (_completedLaps == 1)
        {
            IsMeasuring = true;
            _lapClock.Restart();
            _measuredClock.Start();
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
            ResetFramePhaseCounters();
            Console.WriteLine("RpgSlice benchmark: warmup complete; ten-lap measurement started.");
            Console.Out.Flush();
            return;
        }

        if (!IsMeasuring) return;
        _lapMilliseconds.Add(_lapClock.Elapsed.TotalMilliseconds);
        _lapResourceTrend.Add($"lap {_lapMilliseconds.Count}: cells={_lapPeakActiveCells}, chunks={_lapPeakTerrainChunks}, "
            + $"tracked={_lapPeakTrackedGraphicsResources}, working-set={_lapPeakWorkingSetBytes / (1024d * 1024d):0.0}MiB, "
            + $"max-frame={_lapMaximumFrameMilliseconds:0.0}ms");
        Console.WriteLine($"RpgSlice benchmark: completed measured lap {_lapMilliseconds.Count}/{MeasuredLapCount} in {_lapMilliseconds[^1] / 1000d:0.0}s.");
        Console.Out.Flush();
        _lapPeakActiveCells = 0;
        _lapPeakTerrainChunks = 0;
        _lapPeakTrackedGraphicsResources = 0;
        _lapPeakWorkingSetBytes = 0;
        _lapMaximumFrameMilliseconds = 0d;
        _lapClock.Restart();
        if (_lapMilliseconds.Count == MeasuredLapCount)
        {
            IsComplete = true;
            _measuredClock.Stop();
        }
    }
}

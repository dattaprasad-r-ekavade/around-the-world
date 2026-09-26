using System;

namespace Ember.Rpg;

/// <summary>Monotonic simulation time measured in seconds from the world's start.</summary>
public sealed record WorldClock
{
    public const double SecondsPerDay = 24d * 60d * 60d;

    public double TotalSeconds { get; }
    public double TimeOfDaySeconds => TotalSeconds % SecondsPerDay;
    public long DayIndex => TotalSeconds / SecondsPerDay >= long.MaxValue
        ? long.MaxValue
        : (long)(TotalSeconds / SecondsPerDay);

    public WorldClock(double totalSeconds = 0)
    {
        if (!double.IsFinite(totalSeconds) || totalSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(totalSeconds), "World time must be finite and nonnegative.");
        TotalSeconds = totalSeconds;
    }

    public WorldClock Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Clock advance must be finite and nonnegative.");
        var total = TotalSeconds + elapsedSeconds;
        if (!double.IsFinite(total))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Clock advance exceeds the supported time range.");
        return new WorldClock(total);
    }
}

/// <summary>A repeating two-destination daily schedule: work during its time window, home otherwise.</summary>
public sealed record NpcDailySchedule(Guid HomeCellId, Guid WorkCellId,
    double WorkStartSeconds, double WorkEndSeconds)
{
    public void Validate()
    {
        if (HomeCellId == Guid.Empty || WorkCellId == Guid.Empty || HomeCellId == WorkCellId)
            throw new ArgumentException("An NPC schedule requires two different nonempty destination cells.");
        if (!double.IsFinite(WorkStartSeconds) || WorkStartSeconds < 0 || WorkStartSeconds >= WorldClock.SecondsPerDay
            || !double.IsFinite(WorkEndSeconds) || WorkEndSeconds < 0 || WorkEndSeconds >= WorldClock.SecondsPerDay
            || WorkStartSeconds == WorkEndSeconds)
            throw new ArgumentOutOfRangeException(nameof(WorkStartSeconds),
                "Schedule boundaries must be different times within one day.");
    }

    public Guid ResolveDestination(WorldClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Validate();
        var time = clock.TimeOfDaySeconds;
        var isWorkTime = WorkStartSeconds < WorkEndSeconds
            ? time >= WorkStartSeconds && time < WorkEndSeconds
            : time >= WorkStartSeconds || time < WorkEndSeconds;
        return isWorkTime ? WorkCellId : HomeCellId;
    }

    /// <summary>Counts daily boundary occurrences in (startExclusive, endInclusive] in constant time.</summary>
    public long CountBoundariesCrossed(double startExclusive, double endInclusive)
    {
        Validate();
        if (!double.IsFinite(startExclusive) || startExclusive < 0
            || !double.IsFinite(endInclusive) || endInclusive < startExclusive)
            throw new ArgumentOutOfRangeException(nameof(startExclusive), "Schedule catch-up range is invalid.");
        return SaturatingAdd(
            CountBoundary(WorkStartSeconds, startExclusive, endInclusive),
            CountBoundary(WorkEndSeconds, startExclusive, endInclusive));
    }

    private static long CountBoundary(double timeOfDay, double start, double end)
    {
        var occurrences = Math.Floor((end - timeOfDay) / WorldClock.SecondsPerDay)
            - Math.Floor((start - timeOfDay) / WorldClock.SecondsPerDay);
        if (occurrences <= 0) return 0;
        return occurrences >= long.MaxValue ? long.MaxValue : (long)occurrences;
    }

    private static long SaturatingAdd(long left, long right) =>
        long.MaxValue - left < right ? long.MaxValue : left + right;
}

/// <summary>Persistent schedule bookkeeping for one stable NPC instance.</summary>
public sealed record NpcScheduleRuntimeState
{
    public Guid CurrentCellId { get; init; }
    public Guid? PendingDestinationCellId { get; init; }
    public double LastEvaluatedSeconds { get; init; }

    public void Validate()
    {
        if (CurrentCellId == Guid.Empty || PendingDestinationCellId == Guid.Empty
            || !double.IsFinite(LastEvaluatedSeconds) || LastEvaluatedSeconds < 0)
            throw new ArgumentException("NPC schedule runtime state has an empty cell or invalid world time.");
    }
}

public sealed record NpcScheduleDecision(NpcScheduleRuntimeState State,
    Guid DesiredCellId, Guid? NewTravelRequestCellId);

public sealed record NpcScheduleCatchUpResult(NpcScheduleRuntimeState State,
    ActorRuntimeState Actor, Guid DesiredCellId, long BoundariesCollapsed, int WorkItemsProcessed);

/// <summary>Creates one deduplicated travel request when an NPC's scheduled destination changes.</summary>
public static class NpcScheduleSystem
{
    public static NpcScheduleDecision Evaluate(NpcDailySchedule schedule, WorldClock clock,
        NpcScheduleRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(state);
        schedule.Validate();
        state.Validate();
        if (clock.TotalSeconds < state.LastEvaluatedSeconds)
            throw new ArgumentException("NPC schedule evaluation cannot move world time backwards.", nameof(clock));

        var desired = schedule.ResolveDestination(clock);
        var next = state with { LastEvaluatedSeconds = clock.TotalSeconds };
        if (state.PendingDestinationCellId.HasValue)
        {
            if (state.PendingDestinationCellId.Value == desired)
                return new NpcScheduleDecision(next, desired, null);

            next = next with
            {
                PendingDestinationCellId = desired == state.CurrentCellId ? null : desired
            };
            return new NpcScheduleDecision(next, desired,
                next.PendingDestinationCellId.HasValue ? desired : null);
        }
        if (desired == state.CurrentCellId)
            return new NpcScheduleDecision(next, desired, null);

        next = next with { PendingDestinationCellId = desired };
        return new NpcScheduleDecision(next, desired, desired);
    }

    public static NpcScheduleRuntimeState CompleteTravel(NpcScheduleRuntimeState state, Guid destinationCellId)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        if (destinationCellId == Guid.Empty || state.PendingDestinationCellId != destinationCellId)
            throw new ArgumentException("Completed NPC travel must match its pending schedule destination.", nameof(destinationCellId));
        return state with { CurrentCellId = destinationCellId, PendingDestinationCellId = null };
    }

    /// <summary>Records an intermediate cell reached while following a multi-cell scheduled route.</summary>
    public static NpcScheduleRuntimeState RecordCellEntered(NpcScheduleRuntimeState state, Guid cellId)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        if (!state.PendingDestinationCellId.HasValue || cellId == Guid.Empty)
            throw new ArgumentException("Intermediate NPC travel needs a pending destination and a nonempty cell ID.", nameof(cellId));
        return state with { CurrentCellId = cellId };
    }

    /// <summary>
    /// Materializes the current scheduled destination in one step and advances actor timers by the
    /// elapsed simulation time. Missed daily boundaries are counted arithmetically, never replayed.
    /// </summary>
    public static NpcScheduleCatchUpResult CatchUpDormant(NpcDailySchedule schedule, WorldClock clock,
        NpcScheduleRuntimeState state, ActorRuntimeState actor)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actor);
        schedule.Validate();
        state.Validate();
        actor.Validate();
        if (clock.TotalSeconds < state.LastEvaluatedSeconds)
            throw new ArgumentException("Dormant schedule catch-up cannot move world time backwards.", nameof(clock));

        var elapsed = clock.TotalSeconds - state.LastEvaluatedSeconds;
        var destination = schedule.ResolveDestination(clock);
        var boundaries = schedule.CountBoundariesCrossed(state.LastEvaluatedSeconds, clock.TotalSeconds);
        var updatedState = state with
        {
            CurrentCellId = destination,
            PendingDestinationCellId = null,
            LastEvaluatedSeconds = clock.TotalSeconds
        };
        return new NpcScheduleCatchUpResult(updatedState, actor.AdvanceTime(elapsed),
            destination, boundaries, WorkItemsProcessed: 1);
    }
}

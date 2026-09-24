using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Ember.World;

public enum ActorUpdateTier
{
    Near,
    Far,
    Dormant
}

/// <summary>One actor's stable world identity and current position for update planning.</summary>
public sealed record ActorUpdateCandidate(Guid WorldInstanceId, Vector3 Position);

/// <summary>A tier assignment and whether this actor receives work in the current frame.</summary>
public sealed record ActorUpdateWorkItem(Guid WorldInstanceId, ActorUpdateTier Tier, bool ShouldUpdate);

/// <summary>Deterministic per-frame actor tiers and the work selected inside each budget.</summary>
public sealed record ActorUpdateBudgetPlan(IReadOnlyList<ActorUpdateWorkItem> Actors,
    int NearUpdates, int FarUpdates)
{
    public int DormantActors => Actors.Count(actor => actor.Tier == ActorUpdateTier.Dormant);
}

/// <summary>Assigns distance tiers and caps actor work without owning or resetting actor state.</summary>
public static class ActorUpdateBudget
{
    public static ActorUpdateBudgetPlan Plan(IEnumerable<ActorUpdateCandidate> candidates,
        Vector3 observerPosition, float nearRadius, float farRadius, int maximumNearUpdates,
        int maximumFarUpdates, long frameIndex)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateFinite(observerPosition, nameof(observerPosition));
        if (!float.IsFinite(nearRadius) || nearRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(nearRadius));
        if (!float.IsFinite(farRadius) || farRadius < nearRadius)
            throw new ArgumentOutOfRangeException(nameof(farRadius));
        var nearRadiusSquared = nearRadius * nearRadius;
        var farRadiusSquared = farRadius * farRadius;
        if (!float.IsFinite(nearRadiusSquared) || !float.IsFinite(farRadiusSquared))
            throw new ArgumentOutOfRangeException(nameof(farRadius), "Actor update radii are too large to compare safely.");
        if (maximumNearUpdates < 0) throw new ArgumentOutOfRangeException(nameof(maximumNearUpdates));
        if (maximumFarUpdates < 0) throw new ArgumentOutOfRangeException(nameof(maximumFarUpdates));
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));

        var seen = new HashSet<Guid>();
        var near = new List<RankedActor>();
        var far = new List<RankedActor>();
        var dormant = new List<ActorUpdateCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.WorldInstanceId == Guid.Empty)
                throw new ArgumentException("Actor update candidates need nonempty world-instance IDs.", nameof(candidates));
            ValidateFinite(candidate.Position, nameof(candidates));
            if (!seen.Add(candidate.WorldInstanceId))
                throw new ArgumentException($"Actor update candidates repeat world instance {candidate.WorldInstanceId}.", nameof(candidates));

            var offset = candidate.Position - observerPosition;
            var distanceSquared = offset.LengthSquared();
            if (!float.IsFinite(distanceSquared))
                throw new ArgumentOutOfRangeException(nameof(candidates), "Actor distance must be finite.");
            if (distanceSquared <= nearRadiusSquared)
                near.Add(new RankedActor(candidate, distanceSquared));
            else if (distanceSquared <= farRadiusSquared)
                far.Add(new RankedActor(candidate, distanceSquared));
            else
                dormant.Add(candidate);
        }

        near.Sort(RankedActor.Compare);
        far.Sort(RankedActor.Compare);
        var updateIds = new HashSet<Guid>();
        var nearUpdates = Math.Min(maximumNearUpdates, near.Count);
        for (var i = 0; i < nearUpdates; i++) updateIds.Add(near[i].Candidate.WorldInstanceId);

        var farUpdates = Math.Min(maximumFarUpdates, far.Count);
        if (farUpdates > 0)
        {
            var first = (int)(frameIndex % far.Count);
            for (var i = 0; i < farUpdates; i++)
                updateIds.Add(far[(first + i) % far.Count].Candidate.WorldInstanceId);
        }

        var results = new List<ActorUpdateWorkItem>(seen.Count);
        results.AddRange(near.Select(actor => new ActorUpdateWorkItem(actor.Candidate.WorldInstanceId,
            ActorUpdateTier.Near, updateIds.Contains(actor.Candidate.WorldInstanceId))));
        results.AddRange(far.Select(actor => new ActorUpdateWorkItem(actor.Candidate.WorldInstanceId,
            ActorUpdateTier.Far, updateIds.Contains(actor.Candidate.WorldInstanceId))));
        results.AddRange(dormant.Select(actor => new ActorUpdateWorkItem(actor.WorldInstanceId,
            ActorUpdateTier.Dormant, false)));
        results.Sort(static (left, right) => left.WorldInstanceId.CompareTo(right.WorldInstanceId));
        return new ActorUpdateBudgetPlan(results, nearUpdates, farUpdates);
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Actor update positions must be finite.");
    }

    private readonly record struct RankedActor(ActorUpdateCandidate Candidate, float DistanceSquared)
    {
        public static int Compare(RankedActor left, RankedActor right)
        {
            var distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            return distance != 0 ? distance : left.Candidate.WorldInstanceId.CompareTo(right.Candidate.WorldInstanceId);
        }
    }
}

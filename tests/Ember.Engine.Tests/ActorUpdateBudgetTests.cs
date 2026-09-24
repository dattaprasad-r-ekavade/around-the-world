using System;
using System.Collections.Generic;
using System.Linq;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ActorUpdateBudgetTests
{
    [Fact]
    public void AssignsNearFarDormantTiersAndHonorsPerFrameBudgetsDeterministically()
    {
        var candidates = new[]
        {
            Candidate("00000000-0000-0000-0000-000000000001", 1),
            Candidate("00000000-0000-0000-0000-000000000002", 2),
            Candidate("00000000-0000-0000-0000-000000000003", 3),
            Candidate("00000000-0000-0000-0000-000000000004", 6),
            Candidate("00000000-0000-0000-0000-000000000005", 7),
            Candidate("00000000-0000-0000-0000-000000000006", 8),
            Candidate("00000000-0000-0000-0000-000000000007", 12)
        };

        var firstFrame = ActorUpdateBudget.Plan(candidates, Vector3.Zero, nearRadius: 5,
            farRadius: 10, maximumNearUpdates: 2, maximumFarUpdates: 1, frameIndex: 0);
        var secondFrame = ActorUpdateBudget.Plan(candidates, Vector3.Zero, nearRadius: 5,
            farRadius: 10, maximumNearUpdates: 2, maximumFarUpdates: 1, frameIndex: 1);

        Assert.Equal(2, firstFrame.NearUpdates);
        Assert.Equal(1, firstFrame.FarUpdates);
        Assert.Equal(1, firstFrame.DormantActors);
        Assert.Equal(2, firstFrame.Actors.Count(actor => actor.Tier == ActorUpdateTier.Near && actor.ShouldUpdate));
        Assert.Equal(1, firstFrame.Actors.Count(actor => actor.Tier == ActorUpdateTier.Far && actor.ShouldUpdate));
        Assert.Single(firstFrame.Actors,
            actor => actor.Tier == ActorUpdateTier.Dormant && !actor.ShouldUpdate);

        var firstFar = Assert.Single(firstFrame.Actors,
            actor => actor.Tier == ActorUpdateTier.Far && actor.ShouldUpdate);
        var secondFar = Assert.Single(secondFrame.Actors,
            actor => actor.Tier == ActorUpdateTier.Far && actor.ShouldUpdate);
        Assert.NotEqual(firstFar.WorldInstanceId, secondFar.WorldInstanceId);
        Assert.Equal(firstFrame.Actors.OrderBy(actor => actor.WorldInstanceId), firstFrame.Actors);
    }

    [Fact]
    public void DormantActorRetainsExternalStateWhenItReentersTheNearTier()
    {
        var id = Guid.NewGuid();
        var state = new Dictionary<Guid, int> { [id] = 17 };
        var dormant = ActorUpdateBudget.Plan(new[] { new ActorUpdateCandidate(id, new Vector3(50, 0, 0)) },
            Vector3.Zero, nearRadius: 5, farRadius: 20, maximumNearUpdates: 2, maximumFarUpdates: 1, frameIndex: 0);
        Assert.Equal(ActorUpdateTier.Dormant, Assert.Single(dormant.Actors).Tier);
        Assert.False(Assert.Single(dormant.Actors).ShouldUpdate);
        Assert.Equal(17, state[id]);

        var reactivated = ActorUpdateBudget.Plan(new[] { new ActorUpdateCandidate(id, Vector3.One) },
            Vector3.Zero, nearRadius: 5, farRadius: 20, maximumNearUpdates: 2, maximumFarUpdates: 1, frameIndex: 1);
        var work = Assert.Single(reactivated.Actors);
        Assert.Equal(ActorUpdateTier.Near, work.Tier);
        Assert.True(work.ShouldUpdate);
        state[id] += 1;
        Assert.Equal(18, state[id]);
    }

    private static ActorUpdateCandidate Candidate(string id, float x) =>
        new(Guid.Parse(id), new Vector3(x, 0, 0));
}

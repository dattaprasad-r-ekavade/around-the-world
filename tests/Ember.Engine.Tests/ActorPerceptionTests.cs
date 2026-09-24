using Ember.Physics;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ActorPerceptionTests
{
    [Fact]
    public void WorldObstacleBlocksSightUntilItIsAbsent()
    {
        var observer = new Vector3(0f, 1f, 0f);
        var target = new Vector3(4f, 1f, 0f);
        using (var obstructedWorld = new PhysicsWorld())
        {
            obstructedWorld.AddStaticBox(new Vector3(2f, 1f, 0f), new Vector3(0.25f, 2f, 4f));
            var blocked = ActorPerception.Evaluate(obstructedWorld, observer, target, sightRange: 8f);
            Assert.Equal(4f, blocked.Distance);
            Assert.True(blocked.InRange);
            Assert.False(blocked.HasLineOfSight);
            Assert.False(blocked.CanSee);
        }

        using var clearWorld = new PhysicsWorld();
        var visible = ActorPerception.Evaluate(clearWorld, observer, target, sightRange: 8f);
        Assert.True(visible.InRange);
        Assert.True(visible.HasLineOfSight);
        Assert.True(visible.CanSee);
    }

    [Fact]
    public void SightQueryHonorsRangeAndDoesNotCountGeometryBehindTarget()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(6f, 1f, 0f), new Vector3(0.5f, 2f, 4f));

        var outOfRange = ActorPerception.Evaluate(world,
            new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f), sightRange: 3f);
        var inRange = ActorPerception.Evaluate(world,
            new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f), sightRange: 4f);

        Assert.False(outOfRange.InRange);
        Assert.False(outOfRange.CanSee);
        Assert.True(inRange.InRange);
        Assert.True(inRange.HasLineOfSight);
    }
}

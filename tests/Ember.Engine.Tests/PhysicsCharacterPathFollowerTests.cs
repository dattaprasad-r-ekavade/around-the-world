using System;
using System.IO;
using Ember.Physics;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class PhysicsCharacterPathFollowerTests
{
    [Fact]
    public void SavedCellPathCanBeReloadedSearchedAndFollowedInPlayPhysics()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ember-path-follow-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "cell.paths.json");
        try
        {
            var graph = StraightGraph(0, 3);
            CellPathGraphFile.SaveAtomic(path, graph);
            var loaded = CellPathGraphFile.Load(path);
            var route = CellRouteSearch.FindShortestRoute(loaded,
                loaded.Nodes[0].Id, loaded.Nodes[1].Id, requiredClearanceRadius: 0.5f)!;

            using var world = new PhysicsWorld();
            world.AddStaticBox(new Vector3(0, -0.5f, 0), new Vector3(20, 1, 20));
            using var character = new PhysicsCharacterController(world, new Vector3(0, 1, 0));
            var follower = new PhysicsCharacterPathFollower(character, loaded, route);
            for (var step = 0; step < 180 && follower.State == CharacterPathFollowState.Following; step++)
            {
                follower.Advance(1f / 60f);
                world.Step(1f / 60f);
            }

            Assert.Equal(CharacterPathFollowState.Arrived, follower.State);
            Assert.InRange(character.Pose.Position.X, 2.75f, 3.25f);
            Assert.True(character.IsGrounded);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CharacterFollowsAuthoredRouteAndStopsNearDestination()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0, -0.5f, 0), new Vector3(20, 1, 20));
        using var character = new PhysicsCharacterController(world, new Vector3(0, 1, 0));
        var graph = StraightGraph(0, 3);
        var route = CellRouteSearch.FindShortestRoute(graph, graph.Nodes[0].Id, graph.Nodes[1].Id)!;
        var follower = new PhysicsCharacterPathFollower(character, graph, route);

        for (var step = 0; step < 180 && follower.State == CharacterPathFollowState.Following; step++)
        {
            follower.Advance(1f / 60f);
            world.Step(1f / 60f);
        }

        Assert.Equal(CharacterPathFollowState.Arrived, follower.State);
        Assert.InRange(character.Pose.Position.X, 2.75f, 3.25f);
        Assert.True(character.IsGrounded);
    }

    [Fact]
    public void CharacterBlockedByCollisionTimesOutAndStopsRequestingMotion()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0, -0.5f, 0), new Vector3(20, 1, 20));
        world.AddStaticBox(new Vector3(1.5f, 1, 0), new Vector3(0.2f, 2, 8));
        using var character = new PhysicsCharacterController(world, new Vector3(0, 1, 0));
        var graph = StraightGraph(0, 4);
        var route = CellRouteSearch.FindShortestRoute(graph, graph.Nodes[0].Id, graph.Nodes[1].Id)!;
        var follower = new PhysicsCharacterPathFollower(character, graph, route,
            waypointRadius: 0.2f, noProgressTimeoutSeconds: 0.5f);

        for (var step = 0; step < 180 && follower.State == CharacterPathFollowState.Following; step++)
        {
            follower.Advance(1f / 60f);
            world.Step(1f / 60f);
        }

        Assert.Equal(CharacterPathFollowState.TimedOut, follower.State);
        Assert.InRange(character.Pose.Position.X, 0.7f, 1.2f);
        var stoppedPosition = character.Pose.Position.X;
        for (var step = 0; step < 30; step++)
        {
            follower.Advance(1f / 60f);
            world.Step(1f / 60f);
        }
        Assert.InRange(MathF.Abs(character.Pose.Position.X - stoppedPosition), 0f, 0.01f);
    }

    private static CellPathGraph StraightGraph(float startX, float targetX)
    {
        var startId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        return new CellPathGraph
        {
            CellId = Guid.NewGuid(),
            Nodes = new[]
            {
                new CellPathNode(startId, new NavigationPoint(startX, 1, 0)),
                new CellPathNode(targetId, new NavigationPoint(targetX, 1, 0))
            },
            Edges = new[] { new CellPathEdge(startId, targetId, 1f) }
        };
    }
}

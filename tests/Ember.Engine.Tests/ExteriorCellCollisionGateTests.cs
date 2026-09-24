using System;
using System.IO;
using Ember.Physics;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ExteriorCellCollisionGateTests
{
    [Fact]
    public void DelayedNeighborCollisionHoldsCapsuleOnLoadedGroundUntilReady()
    {
        var directory = TemporaryDirectory();
        try
        {
            var manifest = new ManifestFixture(directory);
            var world = manifest.World;
            var origin = new ExteriorCellCoordinate(0, 0);
            var neighbor = new ExteriorCellCoordinate(1, 0);
            var gate = new ExteriorCellCollisionGate(world);
            gate.MarkCollisionReady(origin);
            var requests = 0;
            gate.CollisionRequired += coordinate =>
            {
                Assert.Equal(neighbor, coordinate);
                requests++;
            };

            using var physics = new PhysicsWorld();
            physics.AddStaticBox(new Vector3(16.5f, -0.5f, 16f), new Vector3(33f, 1f, 32f));
            using var player = new PhysicsCharacterController(physics, new Vector3(30f, 1f, 16f));
            player.SetHorizontalMovementGate((current, proposed, clearance) =>
                gate.Evaluate(current, proposed, clearance).CanMove);
            player.SetMoveInput(Vector3.UnitX);

            for (var step = 0; step < 240; step++) physics.Step(1f / 60f);

            Assert.True(player.IsMovementWaitingForCell);
            Assert.Equal(ExteriorCellCollisionState.WaitingForCollision, gate.LastMovementResult.State);
            Assert.Equal(neighbor, gate.LastMovementResult.RequiredCoordinate);
            Assert.Equal(1, requests);
            Assert.InRange(player.Pose.Position.X, 31f, 31.6f);
            Assert.InRange(player.Pose.Position.Y, 0.85f, 1.1f);

            gate.MarkCollisionReady(neighbor);
            var readyToEnter = gate.Evaluate(
                player.Pose.Position,
                player.Pose.Position + new Vector3(0.1f, 0f, 0f),
                horizontalClearance: 0.45f);

            Assert.True(readyToEnter.CanMove);
            Assert.Equal(ExteriorCellCollisionState.Ready, readyToEnter.State);
            Assert.Equal(1, requests);
            Assert.True(player.IsGrounded);
            Assert.InRange(player.Pose.Position.Y, 0.85f, 1.1f);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MovementGateDistinguishesMissingAuthoredCellFromLoadingCollision()
    {
        var directory = TemporaryDirectory();
        try
        {
            var manifest = new ManifestFixture(directory);
            var gate = new ExteriorCellCollisionGate(manifest.World);
            gate.MarkCollisionReady(new ExteriorCellCoordinate(0, 0));

            var waiting = gate.Evaluate(
                new Vector3(31.4f, 1f, 16f), new Vector3(31.6f, 1f, 16f), horizontalClearance: 0.45f);
            var missing = gate.Evaluate(
                new Vector3(63.4f, 1f, 16f), new Vector3(63.6f, 1f, 16f), horizontalClearance: 0.45f);

            Assert.Equal(ExteriorCellCollisionState.WaitingForCollision, waiting.State);
            Assert.False(waiting.CanMove);
            Assert.Equal(new ExteriorCellCoordinate(1, 0), waiting.RequiredCoordinate);
            Assert.Equal(ExteriorCellCollisionState.MissingCell, missing.State);
            Assert.False(missing.CanMove);
            Assert.Equal(new ExteriorCellCoordinate(2, 0), missing.RequiredCoordinate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-collision-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class ManifestFixture
    {
        public ManifestFixture(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "origin.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "neighbor.json"), "{}");
            var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var manifestPath = Path.Combine(directory, "world.json");
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = first,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "origin.json"
                },
                new WorldCellDefinition
                {
                    Id = second,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(1, 0),
                    ScenePath = "neighbor.json"
                }
            ]);
            World = WorldManifest.Load(manifestPath);
        }

        public WorldManifest World { get; }
    }
}

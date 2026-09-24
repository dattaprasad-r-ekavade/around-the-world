using System;
using System.Collections.Generic;
using Ember.Physics;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class PhysicsWorldTests
{
    [Fact]
    public void FallingBoxSettlesOnStaticFloorAndWorldDisposesItsSimulation()
    {
        var world = new PhysicsWorld();
        var floor = world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        var box = world.AddDynamicBox(new Vector3(0f, 5f, 0f), Vector3.One, 1f);
        var stepper = new PhysicsFixedStepper();

        for (var frame = 0; frame < 240; frame++)
            stepper.Advance(1d / 60d, world.Step);

        var pose = world.GetPose(box);
        Assert.InRange(pose.Position.Y, 0.48f, 0.54f);
        Assert.InRange(MathF.Abs(pose.Position.X), 0f, 0.01f);
        Assert.InRange(MathF.Abs(pose.Position.Z), 0f, 0.01f);
        Assert.Throws<KeyNotFoundException>(() => world.GetPose(floor));

        world.Dispose();
        Assert.True(world.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => world.Step(1f / 60f));
    }

    [Fact]
    public void FixedStepPhysicsHasConsistentSettledPoseAtThirtySixtyAndOneTwentyRenderFps()
    {
        var at30 = SimulateAtRenderRate(30);
        var at60 = SimulateAtRenderRate(60);
        var at120 = SimulateAtRenderRate(120);

        Assert.InRange(MathF.Abs(at30.Position.Y - at60.Position.Y), 0f, 0.02f);
        Assert.InRange(MathF.Abs(at60.Position.Y - at120.Position.Y), 0f, 0.02f);
        Assert.InRange(MathF.Abs(at30.Position.X - at120.Position.X), 0f, 0.01f);
    }

    [Fact]
    public void FixedStepperCapsCatchUpReportsDiscardedBacklogAndExposesInterpolation()
    {
        var stepper = new PhysicsFixedStepper(fixedDeltaSeconds: 0.1f, maxCatchUpSteps: 4);
        var calls = 0;

        var result = stepper.Advance(1d, _ => calls++);

        Assert.Equal(4, calls);
        Assert.Equal(4, result.Steps);
        Assert.True(result.DroppedSeconds > 0.5d);
        Assert.Equal(result.DroppedSeconds, stepper.TotalDroppedSeconds);
        Assert.InRange(result.InterpolationAlpha, 0f, 1f);
    }

    [Fact]
    public void RaycastReturnsNearestHitAndFiltersByCollisionLayer()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f),
            PhysicsCollisionFilter.DefaultWorld);
        var target = world.AddStaticBox(new Vector3(0f, 2f, 0f), Vector3.One,
            new PhysicsCollisionFilter(PhysicsCollisionLayer.Interaction, PhysicsCollisionLayer.All));

        var interactionHit = world.Raycast(new Vector3(0f, 5f, 0f), -Vector3.Up, 10f,
            PhysicsCollisionLayer.Interaction);
        var worldHit = world.Raycast(new Vector3(0f, 5f, 0f), new Vector3(0f, -4f, 0f), 10f,
            PhysicsCollisionLayer.World);
        var playerHit = world.Raycast(new Vector3(0f, 5f, 0f), -Vector3.Up, 10f,
            PhysicsCollisionLayer.Player);
        var miss = world.Raycast(new Vector3(20f, 5f, 0f), -Vector3.Up, 10f);

        Assert.NotNull(interactionHit);
        Assert.Equal(target, interactionHit!.Value.ObjectId);
        Assert.InRange(interactionHit.Value.Distance, 2.49f, 2.51f);
        Assert.InRange(interactionHit.Value.Position.Y, 2.49f, 2.51f);
        Assert.InRange(worldHit!.Value.Distance, 4.99f, 5.01f);
        Assert.Null(playerHit);
        Assert.Null(miss);
    }

    [Fact]
    public void CollisionFiltersCanKeepADynamicBodyFromContactingWorld()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));
        var filter = new PhysicsCollisionFilter(PhysicsCollisionLayer.Dynamic, PhysicsCollisionLayer.Dynamic);
        var fallingThrough = world.AddDynamicBox(new Vector3(0f, 2f, 0f), Vector3.One, 1f, filter);

        for (var step = 0; step < 120; step++) world.Step(1f / 60f);

        Assert.True(world.GetPose(fallingThrough).Position.Y < -1f);
    }

    [Fact]
    public void EngineAndBepuVectorConversionsRoundTrip()
    {
        var vector = new Vector3(1.25f, -3.5f, 8f);
        var rotation = Quaternion.CreateFromYawPitchRoll(0.2f, -0.3f, 0.4f);

        Assert.Equal(vector, PhysicsConversions.ToXna(PhysicsConversions.ToNumerics(vector)));
        Assert.Equal(rotation, PhysicsConversions.ToXna(PhysicsConversions.ToNumerics(rotation)));
    }

    [Fact]
    public void CapsuleCharacterMovesIntoAWallAndStaysGrounded()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        world.AddStaticBox(new Vector3(2f, 1f, 0f), new Vector3(0.2f, 2f, 8f));
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 1f, 0f));
        character.SetMoveInput(Vector3.UnitX);

        for (var step = 0; step < 180; step++) world.Step(1f / 60f);

        Assert.InRange(character.Pose.Position.X, 1.2f, 1.65f);
        Assert.InRange(character.Pose.Position.Y, 0.85f, 1.05f);
        Assert.True(character.IsGrounded);
        Assert.InRange(MathF.Abs(character.Pose.Orientation.X), 0f, 0.001f);
        Assert.InRange(MathF.Abs(character.Pose.Orientation.Z), 0f, 0.001f);
    }

    [Fact]
    public void CapsuleJumpAndLandingFlagsEachFireForOnePhysicsStep()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 1f, 0f));
        Assert.True(character.IsGrounded);
        character.RequestJump();

        var jumpEvents = 0;
        var landEvents = 0;
        var maximumHeight = character.Pose.Position.Y;
        for (var step = 0; step < 240; step++)
        {
            world.Step(1f / 60f);
            if (character.JumpedThisStep) jumpEvents++;
            if (character.LandedThisStep) landEvents++;
            maximumHeight = MathF.Max(maximumHeight, character.Pose.Position.Y);
        }
        for (var step = 0; step < 60; step++)
        {
            world.Step(1f / 60f);
            if (character.JumpedThisStep) jumpEvents++;
            if (character.LandedThisStep) landEvents++;
        }

        Assert.True(maximumHeight > 2f);
        Assert.True(character.IsGrounded);
        Assert.Equal(1, jumpEvents);
        Assert.Equal(1, landEvents);
    }

    [Fact]
    public void JumpRequestSurvivesCatchUpUntilCharacterLands()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 1.0505f, 0f));
        Assert.False(character.IsGrounded);
        character.RequestJump();

        var stepper = new PhysicsFixedStepper();
        var result = stepper.Advance(stepper.FixedDeltaSeconds * 2d,
            seconds => world.Step(seconds));

        Assert.Equal(2, result.Steps);
        Assert.True(character.JumpedThisStep);
        Assert.True(character.Velocity.Y > 0f);
        Assert.False(character.IsGrounded);
    }

    [Fact]
    public void JumpRequestExpiresWhileCharacterRemainsAirborne()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 1f, 0f));
        character.RequestJump();
        world.Step(1f / 60f);
        Assert.True(character.JumpedThisStep);
        character.RequestJump();

        for (var step = 0; step < 120; step++)
            world.Step(1f / 60f);

        Assert.True(character.IsGrounded);
        Assert.False(character.JumpedThisStep);
        Assert.InRange(character.Pose.Position.Y, 0.85f, 1.05f);
    }

    [Fact]
    public void CapsuleJumpCannotPassThroughLowCeiling()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        world.AddStaticBox(new Vector3(0f, 2.3f, 0f), new Vector3(8f, 0.2f, 8f));
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 1f, 0f));
        character.RequestJump();

        var maximumHeight = character.Pose.Position.Y;
        for (var step = 0; step < 90; step++)
        {
            world.Step(1f / 60f);
            maximumHeight = MathF.Max(maximumHeight, character.Pose.Position.Y);
        }

        Assert.True(maximumHeight < 1.65f);
        Assert.True(character.Pose.Position.Y < 1.05f);
    }

    [Fact]
    public void CapsuleClimbsWalkableRampAndStopsAtTooSteepRamp()
    {
        var walkable = SimulateRamp(MathHelper.ToRadians(30f));
        var tooSteep = SimulateRamp(MathHelper.ToRadians(60f));

        Assert.True(walkable.Position.X > 2.5f, $"Walkable ramp ended at x={walkable.Position.X}, y={walkable.Position.Y}.");
        Assert.True(walkable.Position.Y > 1.5f, $"Walkable ramp ended at x={walkable.Position.X}, y={walkable.Position.Y}.");
        Assert.True(tooSteep.Position.X < 2f, $"Steep ramp ended at x={tooSteep.Position.X}, y={tooSteep.Position.Y}.");
        Assert.True(tooSteep.Position.Y < 1.8f, $"Steep ramp ended at x={tooSteep.Position.X}, y={tooSteep.Position.Y}.");
    }

    [Fact]
    public void CharacterRemainsGroundedAcrossAdjacentTerrainMeshesAndUnloadedMeshStopsColliding()
    {
        using var world = new PhysicsWorld();
        var left = CreateFlatTerrainChunk(0f, 4f);
        var right = CreateFlatTerrainChunk(4f, 4f);
        var leftCollider = world.AddStaticTriangleMesh(left.Vertices, left.Indices);
        world.AddStaticTriangleMesh(right.Vertices, right.Indices);
        Assert.NotNull(world.Raycast(new Vector3(1f, 2f, 1f), -Vector3.UnitY, 4f,
            PhysicsCollisionLayer.World));
        using var character = new PhysicsCharacterController(world, new Vector3(3f, 1.1f, 2f));
        character.SetMoveInput(Vector3.UnitX);

        for (var step = 0; step < 42; step++) world.Step(1f / 60f);
        character.SetMoveInput(Vector3.Zero);
        for (var step = 0; step < 3; step++) world.Step(1f / 60f);

        Assert.True(character.Pose.Position.X > 4.5f,
            $"Character stopped before the shared cell seam at x={character.Pose.Position.X}.");
        Assert.True(character.IsGrounded, $"Character lost terrain contact at {character.Pose.Position}.");
        Assert.InRange(character.Pose.Position.Y, 0.85f, 1.05f);
        Assert.NotNull(world.Raycast(new Vector3(1f, 2f, 1f), -Vector3.UnitY, 4f,
            PhysicsCollisionLayer.World));

        world.RemoveStatic(leftCollider);

        Assert.Null(world.Raycast(new Vector3(1f, 2f, 1f), -Vector3.UnitY, 4f,
            PhysicsCollisionLayer.World));
        var replacementCollider = world.AddStaticTriangleMesh(left.Vertices, left.Indices);
        Assert.NotNull(world.Raycast(new Vector3(1f, 2f, 1f), -Vector3.UnitY, 4f,
            PhysicsCollisionLayer.World));
        world.RemoveStatic(replacementCollider);
    }

    [Fact]
    public void DisposingCharacterRemovesItsBodyAndWorldDisposalInvalidatesController()
    {
        var world = new PhysicsWorld();
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 2f, 0f));
        var id = character.PhysicsBodyId;

        character.Dispose();

        Assert.Throws<KeyNotFoundException>(() => world.GetPose(id));
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => character.SetMoveInput(Vector3.UnitX));
    }

    private static PhysicsPose SimulateAtRenderRate(int framesPerSecond)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        var box = world.AddDynamicBox(new Vector3(0f, 5f, 0f), Vector3.One, 1f);
        var stepper = new PhysicsFixedStepper();
        var elapsed = 1d / framesPerSecond;

        for (var frame = 0; frame < framesPerSecond * 4; frame++)
            stepper.Advance(elapsed, world.Step);

        return world.GetInterpolatedPose(box, stepper.InterpolationAlpha);
    }

    private static PhysicsPose SimulateRamp(float angle)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 16f));
        var rampHeight = 4f * MathF.Sin(angle) - 0.125f * MathF.Cos(angle) + 0.02f;
        world.AddStaticBox(new Vector3(4f, rampHeight, 0f), new Vector3(8f, 0.25f, 8f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle));
        using var character = new PhysicsCharacterController(world, new Vector3(-1f, 1f, 0f),
            new PhysicsCharacterSettings { MaximumSlopeAngleDegrees = 45f, MoveSpeed = 3f });
        character.SetMoveInput(Vector3.UnitX);

        for (var step = 0; step < 180; step++) world.Step(1f / 60f);

        return character.Pose;
    }

    private static (Vector3[] Vertices, int[] Indices) CreateFlatTerrainChunk(float left, float width) =>
    (
        [new Vector3(left, 0f, 0f), new Vector3(left + width, 0f, 0f),
            new Vector3(left, 0f, width), new Vector3(left + width, 0f, width)],
        [0, 1, 2, 1, 3, 2]
    );
}

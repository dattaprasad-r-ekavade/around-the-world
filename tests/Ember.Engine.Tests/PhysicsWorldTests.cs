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
}

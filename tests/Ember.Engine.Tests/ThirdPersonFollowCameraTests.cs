using Ember;
using Ember.Physics;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ThirdPersonFollowCameraTests
{
    [Fact]
    public void FollowCameraTracksCapsuleAndShortensItsBoomBeforeAWall()
    {
        using var world = new PhysicsWorld();
        using var character = new PhysicsCharacterController(world, Vector3.Zero);
        var camera = new ThirdPersonFollowCamera();
        camera.Reset(Vector3.Zero, distance: 4.5f, pitch: 0f);

        camera.Follow(world, character.Pose.Position);
        Assert.False(camera.IsObstructed);
        Assert.InRange(Vector3.Distance(camera.Position, camera.Target), 4.49f, 4.51f);
        Assert.False(camera.ObstructingObject.HasValue);
        Assert.InRange(Vector3.Distance(camera.MoveDirection(Vector2.UnitY), -Vector3.UnitZ), 0f, 0.001f);
        Assert.InRange(Vector3.Distance(camera.MoveDirection(Vector2.UnitX), Vector3.UnitX), 0f, 0.001f);

        var wall = world.AddStaticBox(new Vector3(0f, 0.15f, 2.5f), new Vector3(8f, 8f, 0.2f));
        camera.Follow(world, character.Pose.Position);

        Assert.True(camera.IsObstructed);
        Assert.Equal(wall, camera.ObstructingObject);
        Assert.True(camera.Position.Z < 2.4f);
        Assert.True(camera.Position.Z > 1.5f);

        camera.Follow(world, new Vector3(10f, 0f, 0f));
        Assert.False(camera.IsObstructed);
        Assert.InRange(camera.Position.X, 9.99f, 10.01f);
        Assert.InRange(Vector3.Distance(camera.Position, camera.Target), 4.49f, 4.51f);
    }

    [Fact]
    public void FollowCameraRayIgnoresThePlayerLayer()
    {
        using var world = new PhysicsWorld();
        using var character = new PhysicsCharacterController(world, Vector3.Zero);
        var camera = new ThirdPersonFollowCamera();
        camera.Reset(Vector3.Zero, pitch: 0f);

        camera.Follow(world, character.Pose.Position);

        Assert.False(camera.IsObstructed);
    }
}

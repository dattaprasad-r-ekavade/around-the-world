using Ember;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class OrbitCameraTests
{
    [Fact]
    public void ResetAndOrbitKeepCameraLookingAtTarget()
    {
        var camera = new OrbitCamera { MinDistance = 2f, MaxDistance = 10f };
        camera.Reset(Vector3.Zero, distance: 6f);
        var firstPosition = camera.Position;

        camera.Orbit(new Vector2(30f, -10f));

        Assert.NotEqual(firstPosition, camera.Position);
        var forward = Vector3.Normalize(Vector3.Transform(Vector3.Forward, Matrix.Invert(camera.View)) - camera.Position);
        Assert.True(Vector3.Dot(forward, Vector3.Normalize(-camera.Position)) > 0.99f);
    }

    [Fact]
    public void ZoomIsClamped()
    {
        var camera = new OrbitCamera { MinDistance = 2f, MaxDistance = 10f };
        camera.Reset(Vector3.Zero, distance: 6f);

        camera.Zoom(10000f);
        Assert.Equal(2f, camera.Distance);
        camera.Zoom(-10000f);
        Assert.Equal(10f, camera.Distance);
    }
}

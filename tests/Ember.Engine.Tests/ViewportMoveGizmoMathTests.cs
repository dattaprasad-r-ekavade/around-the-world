using System.Linq;
using Ember;
using Ember.Authoring;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ViewportMoveGizmoMathTests
{
    [Fact]
    public void CreatesScreenHandlesAndPicksTheAxisUnderThePointer()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(16f / 9f);
        camera.Reset(Vector3.Zero, distance: 100f, yaw: 0.3f, pitch: -0.2f);
        var handles = ViewportMoveGizmoMath.CreateHandles(Vector3.Zero, camera.Position,
            camera.View, camera.Projection, 1280, 720);

        Assert.True(handles.Count >= 2);
        var expected = handles.First(handle => handle.Axis == TransformGizmoAxis.X);
        var pointer = Vector2.Lerp(expected.ScreenStart, expected.ScreenEnd, 0.7f)
            + new Vector2(0f, 3f);

        Assert.True(ViewportMoveGizmoMath.TryPick(handles, pointer, out var selected));
        Assert.Equal(expected.Axis, selected.Axis);
    }

    [Fact]
    public void ConvertsPointerMotionIntoWorldDistanceAlongTheSelectedAxis()
    {
        var handle = new TransformGizmoHandle(TransformGizmoAxis.Z, Vector3.UnitZ,
            new Vector2(100f, 100f), new Vector2(100f, 160f), 30f);
        var delta = ViewportMoveGizmoMath.CalculateMoveDelta(handle,
            new Vector2(100f, 100f), new Vector2(100f, 130f));

        Assert.Equal(new Vector3(0f, 0f, 15f), delta);
    }

    [Fact]
    public void ConvertsWorldPositionThroughRotatedAndScaledParent()
    {
        var parentWorld = Matrix.CreateScale(2f, 3f, 4f)
            * Matrix.CreateRotationY(MathHelper.PiOver2)
            * Matrix.CreateTranslation(10f, 20f, 30f);
        var expectedLocal = new Vector3(2f, -1f, 0.5f);
        var world = Vector3.Transform(expectedLocal, parentWorld);

        Assert.True(ViewportMoveGizmoMath.TryConvertWorldPositionToParentSpace(
            world, parentWorld, out var actualLocal));
        Assert.Equal(expectedLocal, actualLocal);
    }

    [Fact]
    public void RejectsSingularParentTransform()
    {
        var parentWorld = Matrix.CreateScale(0f, 1f, 1f);

        Assert.False(ViewportMoveGizmoMath.TryConvertWorldPositionToParentSpace(
            Vector3.One, parentWorld, out _));
    }
}

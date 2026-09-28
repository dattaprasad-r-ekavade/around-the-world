using System.Linq;
using Ember;
using Ember.Authoring;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ViewportTransformGizmoMathTests
{
    [Fact]
    public void CreatesScreenHandlesAndPicksTheAxisUnderThePointer()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(16f / 9f);
        camera.Reset(Vector3.Zero, distance: 100f, yaw: 0.3f, pitch: -0.2f);
        var handles = ViewportTransformGizmoMath.CreateHandles(Vector3.Zero, camera.Position,
            camera.View, camera.Projection, 1280, 720);

        Assert.True(handles.Count >= 2);
        var expected = handles.First(handle => handle.Axis == TransformGizmoAxis.X);
        var pointer = Vector2.Lerp(expected.ScreenStart, expected.ScreenEnd, 0.7f)
            + new Vector2(0f, 3f);

        Assert.True(ViewportTransformGizmoMath.TryPick(handles, pointer, out var selected));
        Assert.Equal(expected.Axis, selected.Axis);
    }

    [Fact]
    public void ConvertsPointerMotionIntoWorldDistanceAlongTheSelectedAxis()
    {
        var handle = new TransformGizmoHandle(TransformGizmoAxis.Z, Vector3.UnitZ,
            new Vector2(100f, 100f), new Vector2(100f, 160f), 30f);
        var delta = ViewportTransformGizmoMath.CalculateMoveDelta(handle,
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

        Assert.True(ViewportTransformGizmoMath.TryConvertWorldPositionToParentSpace(
            world, parentWorld, out var actualLocal));
        Assert.Equal(expectedLocal, actualLocal);
    }

    [Fact]
    public void RejectsSingularParentTransform()
    {
        var parentWorld = Matrix.CreateScale(0f, 1f, 1f);

        Assert.False(ViewportTransformGizmoMath.TryConvertWorldPositionToParentSpace(
            Vector3.One, parentWorld, out _));
    }

    [Fact]
    public void SnapsPositiveAndNegativeWorldCoordinatesToNearestGridPoint()
    {
        var snapped = ViewportTransformGizmoMath.SnapWorldPosition(
            new Vector3(12.4f, -12.6f, 3.9f), 5f);

        Assert.Equal(new Vector3(10f, -15f, 5f), snapped);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void RejectsInvalidGridSteps(float step)
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            ViewportTransformGizmoMath.SnapWorldPosition(Vector3.Zero, step));
    }

    [Fact]
    public void BuildsAndPicksVisibleLocalRotationRings()
    {
        var camera = new OrbitCamera();
        camera.SetProjection(16f / 9f);
        camera.Reset(Vector3.Zero, distance: 100f, yaw: 0.3f, pitch: -0.2f);
        var rings = ViewportTransformGizmoMath.CreateRotationRings(Vector3.Zero,
            camera.Position, camera.View, camera.Projection, 1280, 720,
            new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ });

        Assert.NotEmpty(rings);
        var expected = rings[0];
        var pointer = expected.ScreenPoints[7];

        Assert.True(ViewportTransformGizmoMath.TryPickRotationRing(rings, pointer, out var selected));
        Assert.Equal(expected.Axis, selected.Axis);
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(-1f, -1f)]
    public void ConvertsPointerSweepToSignedRotation(float orientation, float expectedSign)
    {
        var ring = new TransformGizmoRing(TransformGizmoAxis.Y, Vector3.UnitY,
            Vector2.Zero, orientation, 10f, System.Array.Empty<Vector3>(),
            System.Array.Empty<Vector2>());

        var delta = ViewportTransformGizmoMath.CalculateRotationPointerDelta(ring,
            new Vector2(20f, 0f), new Vector2(0f, 20f));

        Assert.Equal(expectedSign * MathHelper.PiOver2, delta, 5);
    }

    [Fact]
    public void BuildsNormalizedLocalAxisDirectionsThroughParent()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathHelper.PiOver2);
        var parent = Matrix.CreateScale(2f, 1f, 3f)
            * Matrix.CreateRotationZ(0.4f)
            * Matrix.CreateTranslation(5f, 6f, 7f);

        var axes = ViewportTransformGizmoMath.CreateObjectAxisDirections(rotation, parent);
        var localToWorld = Matrix.CreateFromQuaternion(rotation) * parent;
        var expectedX = Vector3.TransformNormal(Vector3.UnitX, localToWorld);
        expectedX.Normalize();

        Assert.Equal(3, axes.Count);
        Assert.All(axes, axis => Assert.Equal(1f, axis.Length(), 5));
        Assert.True(Vector3.Distance(expectedX, axes[0]) < 0.00001f);
    }

    [Fact]
    public void MapsAxisDragToPositiveScaleFactor()
    {
        var handle = new TransformGizmoHandle(TransformGizmoAxis.X, Vector3.UnitX,
            Vector2.Zero, new Vector2(100f, 0f), 10f);

        Assert.Equal(1.5f, ViewportTransformGizmoMath.CalculateScaleFactor(handle,
            Vector2.Zero, new Vector2(25f, 0f)));
        Assert.Equal(0.5f, ViewportTransformGizmoMath.CalculateScaleFactor(handle,
            Vector2.Zero, new Vector2(-25f, 0f)));
    }

    [Fact]
    public void SnapsRotationAndScaleByConfiguredIncrements()
    {
        Assert.Equal(MathHelper.ToRadians(30f), ViewportTransformGizmoMath.SnapAngle(
            MathHelper.ToRadians(23f), 15f), 5);
        Assert.Equal(1.3f, ViewportTransformGizmoMath.SnapScaleFactor(1.26f, 0.1f), 5);
    }
}

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Ember.Authoring;

public enum TransformGizmoAxis
{
    X,
    Y,
    Z
}

public enum TransformGizmoMode
{
    Move,
    Turn,
    Size
}

public readonly record struct TransformGizmoHandle(
    TransformGizmoAxis Axis,
    Vector3 WorldDirection,
    Vector2 ScreenStart,
    Vector2 ScreenEnd,
    float WorldLength);

public sealed record TransformGizmoRing(
    TransformGizmoAxis Axis,
    Vector3 WorldAxis,
    Vector2 ScreenCenter,
    float ScreenOrientation,
    float WorldRadius,
    IReadOnlyList<Vector3> WorldPoints,
    IReadOnlyList<Vector2> ScreenPoints);

/// <summary>Projects and hit-tests viewport transform handles without owning graphics resources.</summary>
public static class ViewportTransformGizmoMath
{
    private const float HandleLengthFactor = 0.12f;
    private const float MinimumWorldLength = 8f;
    private const float MaximumWorldLength = 60f;
    private const float MinimumScreenLength = 18f;
    private const int RotationRingSegments = 64;

    private static readonly Vector3[] WorldAxes =
    {
        Vector3.UnitX,
        Vector3.UnitY,
        Vector3.UnitZ
    };

    public static Vector3 LocalAxis(TransformGizmoAxis axis) => axis switch
    {
        TransformGizmoAxis.X => Vector3.UnitX,
        TransformGizmoAxis.Y => Vector3.UnitY,
        _ => Vector3.UnitZ
    };

    public static IReadOnlyList<Vector3> CreateObjectAxisDirections(Quaternion localRotation,
        Matrix parentWorld)
    {
        if (!IsFinite(localRotation) || !IsFinite(parentWorld))
            return new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero };
        var localToWorld = Matrix.CreateFromQuaternion(Quaternion.Normalize(localRotation)) * parentWorld;
        var directions = new Vector3[WorldAxes.Length];
        for (var index = 0; index < WorldAxes.Length; index++)
        {
            var direction = Vector3.TransformNormal(WorldAxes[index], localToWorld);
            if (!IsFinite(direction) || direction.LengthSquared() < 0.000001f)
            {
                directions[index] = Vector3.Zero;
                continue;
            }
            direction.Normalize();
            directions[index] = direction;
        }
        return directions;
    }

    public static IReadOnlyList<TransformGizmoHandle> CreateHandles(Vector3 worldOrigin,
        Vector3 cameraPosition, Matrix view, Matrix projection, int viewportWidth, int viewportHeight)
        => CreateAxisHandles(worldOrigin, cameraPosition, view, projection,
            viewportWidth, viewportHeight, WorldAxes);

    public static IReadOnlyList<TransformGizmoHandle> CreateAxisHandles(Vector3 worldOrigin,
        Vector3 cameraPosition, Matrix view, Matrix projection, int viewportWidth, int viewportHeight,
        IReadOnlyList<Vector3> worldAxisDirections)
    {
        ArgumentNullException.ThrowIfNull(worldAxisDirections);
        if (worldAxisDirections.Count != 3)
            throw new ArgumentException("Exactly three local axis directions are required.", nameof(worldAxisDirections));
        if (!IsFinite(worldOrigin) || !IsFinite(cameraPosition)) return Array.Empty<TransformGizmoHandle>();
        if (viewportWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));

        var viewport = new Viewport(0, 0, viewportWidth, viewportHeight);
        var projectedOrigin = viewport.Project(worldOrigin, projection, view, Matrix.Identity);
        if (!IsVisible(projectedOrigin)) return Array.Empty<TransformGizmoHandle>();

        var distance = Vector3.Distance(cameraPosition, worldOrigin);
        if (!float.IsFinite(distance) || distance <= 0f) return Array.Empty<TransformGizmoHandle>();
        var worldLength = Math.Clamp(distance * HandleLengthFactor,
            MinimumWorldLength, MaximumWorldLength);
        var screenOrigin = new Vector2(projectedOrigin.X, projectedOrigin.Y);
        var handles = new List<TransformGizmoHandle>(WorldAxes.Length);
        for (var index = 0; index < WorldAxes.Length; index++)
        {
            var axis = (TransformGizmoAxis)index;
            var direction = worldAxisDirections[index];
            if (!IsFinite(direction) || direction.LengthSquared() < 0.000001f) continue;
            direction.Normalize();
            var projectedEnd = viewport.Project(worldOrigin + direction * worldLength,
                projection, view, Matrix.Identity);
            if (!IsVisible(projectedEnd)) continue;
            var screenEnd = new Vector2(projectedEnd.X, projectedEnd.Y);
            if (Vector2.DistanceSquared(screenOrigin, screenEnd) < MinimumScreenLength * MinimumScreenLength)
                continue;
            handles.Add(new TransformGizmoHandle(axis, direction, screenOrigin, screenEnd, worldLength));
        }
        return handles;
    }

    public static IReadOnlyList<TransformGizmoRing> CreateRotationRings(Vector3 worldOrigin,
        Vector3 cameraPosition, Matrix view, Matrix projection, int viewportWidth, int viewportHeight,
        IReadOnlyList<Vector3> worldAxisDirections)
    {
        ArgumentNullException.ThrowIfNull(worldAxisDirections);
        if (worldAxisDirections.Count != 3)
            throw new ArgumentException("Exactly three local axis directions are required.", nameof(worldAxisDirections));
        if (!IsFinite(worldOrigin) || !IsFinite(cameraPosition)) return Array.Empty<TransformGizmoRing>();
        if (viewportWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));

        var viewport = new Viewport(0, 0, viewportWidth, viewportHeight);
        var projectedCenter = viewport.Project(worldOrigin, projection, view, Matrix.Identity);
        if (!IsVisible(projectedCenter)) return Array.Empty<TransformGizmoRing>();
        var distance = Vector3.Distance(cameraPosition, worldOrigin);
        if (!float.IsFinite(distance) || distance <= 0f) return Array.Empty<TransformGizmoRing>();
        var radius = Math.Clamp(distance * 0.09f, 7f, 48f);
        var screenCenter = new Vector2(projectedCenter.X, projectedCenter.Y);
        var rings = new List<TransformGizmoRing>(WorldAxes.Length);

        for (var axisIndex = 0; axisIndex < WorldAxes.Length; axisIndex++)
        {
            var axis = worldAxisDirections[axisIndex];
            if (!IsFinite(axis) || axis.LengthSquared() < 0.000001f) continue;
            axis.Normalize();
            var reference = MathF.Abs(Vector3.Dot(axis, Vector3.Up)) > 0.9f
                ? Vector3.UnitX
                : Vector3.UnitY;
            var basisU = Vector3.Cross(reference, axis);
            if (basisU.LengthSquared() < 0.000001f) continue;
            basisU.Normalize();
            var basisV = Vector3.Cross(axis, basisU);
            basisV.Normalize();

            var projectedU = viewport.Project(worldOrigin + basisU * radius,
                projection, view, Matrix.Identity);
            var projectedV = viewport.Project(worldOrigin + basisV * radius,
                projection, view, Matrix.Identity);
            if (!IsVisible(projectedU) || !IsVisible(projectedV)) continue;
            var screenU = new Vector2(projectedU.X, projectedU.Y) - screenCenter;
            var screenV = new Vector2(projectedV.X, projectedV.Y) - screenCenter;
            var orientation = Cross(screenU, screenV);
            if (!float.IsFinite(orientation) || MathF.Abs(orientation) < 0.0001f) continue;

            var worldPoints = new Vector3[RotationRingSegments + 1];
            var screenPoints = new Vector2[RotationRingSegments + 1];
            var allVisible = true;
            var minimumRadiusSquared = float.PositiveInfinity;
            for (var segment = 0; segment <= RotationRingSegments; segment++)
            {
                var angle = MathHelper.TwoPi * segment / RotationRingSegments;
                var worldPoint = worldOrigin
                    + (basisU * MathF.Cos(angle) + basisV * MathF.Sin(angle)) * radius;
                var projected = viewport.Project(worldPoint, projection, view, Matrix.Identity);
                if (!IsVisible(projected))
                {
                    allVisible = false;
                    break;
                }
                worldPoints[segment] = worldPoint;
                screenPoints[segment] = new Vector2(projected.X, projected.Y);
                if (segment < RotationRingSegments)
                    minimumRadiusSquared = MathF.Min(minimumRadiusSquared,
                        Vector2.DistanceSquared(screenCenter, screenPoints[segment]));
            }

            const float MinimumRingRadius = 6f;
            if (!allVisible || minimumRadiusSquared < MinimumRingRadius * MinimumRingRadius) continue;
            rings.Add(new TransformGizmoRing((TransformGizmoAxis)axisIndex, axis, screenCenter,
                MathF.Sign(orientation), radius, worldPoints, screenPoints));
        }
        return rings;
    }

    public static bool TryPick(IReadOnlyList<TransformGizmoHandle> handles, Vector2 pointer,
        out TransformGizmoHandle selected, float hitRadius = 10f)
    {
        ArgumentNullException.ThrowIfNull(handles);
        selected = default;
        if (!IsFinite(pointer) || !float.IsFinite(hitRadius) || hitRadius <= 0f) return false;

        var bestDistanceSquared = hitRadius * hitRadius;
        var found = false;
        foreach (var handle in handles)
        {
            var segment = handle.ScreenEnd - handle.ScreenStart;
            var lengthSquared = segment.LengthSquared();
            if (!float.IsFinite(lengthSquared) || lengthSquared < MinimumScreenLength * MinimumScreenLength)
                continue;

            var along = Vector2.Dot(pointer - handle.ScreenStart, segment) / lengthSquared;
            if (along < 0.18f || along > 1.18f) continue;
            var closest = handle.ScreenStart + segment * Math.Clamp(along, 0f, 1f);
            var distanceSquared = Vector2.DistanceSquared(pointer, closest);
            if (distanceSquared >= bestDistanceSquared) continue;
            selected = handle;
            bestDistanceSquared = distanceSquared;
            found = true;
        }
        return found;
    }

    public static Vector3 CalculateMoveDelta(TransformGizmoHandle handle,
        Vector2 pointerStart, Vector2 pointerCurrent)
    {
        if (!IsFinite(handle.WorldDirection) || !float.IsFinite(handle.WorldLength)
            || handle.WorldLength <= 0f || !IsFinite(pointerStart) || !IsFinite(pointerCurrent))
            return Vector3.Zero;

        var screenAxis = handle.ScreenEnd - handle.ScreenStart;
        var projectedLength = screenAxis.Length();
        if (!float.IsFinite(projectedLength) || projectedLength < MinimumScreenLength)
            return Vector3.Zero;
        var dragPixels = Vector2.Dot(pointerCurrent - pointerStart, screenAxis / projectedLength);
        var worldDistance = dragPixels * handle.WorldLength / projectedLength;
        return float.IsFinite(worldDistance) ? handle.WorldDirection * worldDistance : Vector3.Zero;
    }

    public static bool TryPickRotationRing(IReadOnlyList<TransformGizmoRing> rings,
        Vector2 pointer, out TransformGizmoRing selected, float hitRadius = 9f)
    {
        ArgumentNullException.ThrowIfNull(rings);
        selected = null!;
        if (!IsFinite(pointer) || !float.IsFinite(hitRadius) || hitRadius <= 0f) return false;
        var bestDistanceSquared = hitRadius * hitRadius;
        var found = false;
        foreach (var ring in rings)
        {
            if (ring.ScreenPoints.Count < 2) continue;
            for (var index = 0; index < ring.ScreenPoints.Count - 1; index++)
            {
                var distanceSquared = DistanceToSegmentSquared(pointer,
                    ring.ScreenPoints[index], ring.ScreenPoints[index + 1]);
                if (!float.IsFinite(distanceSquared) || distanceSquared >= bestDistanceSquared) continue;
                selected = ring;
                bestDistanceSquared = distanceSquared;
                found = true;
            }
        }
        return found;
    }

    public static float CalculateRotationPointerDelta(TransformGizmoRing ring,
        Vector2 pointerPrevious, Vector2 pointerCurrent)
    {
        if (!IsFinite(ring.ScreenCenter) || !IsFinite(pointerPrevious) || !IsFinite(pointerCurrent)
            || !float.IsFinite(ring.ScreenOrientation) || ring.ScreenOrientation == 0f)
            return 0f;
        var previous = pointerPrevious - ring.ScreenCenter;
        var current = pointerCurrent - ring.ScreenCenter;
        if (previous.LengthSquared() < 9f || current.LengthSquared() < 9f) return 0f;
        var delta = MathF.Atan2(Cross(previous, current), Vector2.Dot(previous, current));
        return float.IsFinite(delta) ? delta * ring.ScreenOrientation : 0f;
    }

    public static float CalculateScaleFactor(TransformGizmoHandle handle,
        Vector2 pointerStart, Vector2 pointerCurrent)
    {
        if (!IsFinite(pointerStart) || !IsFinite(pointerCurrent)) return 1f;
        var screenAxis = handle.ScreenEnd - handle.ScreenStart;
        var projectedLength = screenAxis.Length();
        if (!float.IsFinite(projectedLength) || projectedLength < MinimumScreenLength) return 1f;
        var pixelDelta = Vector2.Dot(pointerCurrent - pointerStart, screenAxis / projectedLength);
        return Math.Clamp(1f + 2f * pixelDelta / projectedLength, 0.01f, 1_000f);
    }

    public static float SnapAngle(float angleRadians, float stepDegrees)
    {
        if (!float.IsFinite(angleRadians))
            throw new ArgumentOutOfRangeException(nameof(angleRadians), "Angle must be finite.");
        if (!float.IsFinite(stepDegrees) || stepDegrees <= 0f)
            throw new ArgumentOutOfRangeException(nameof(stepDegrees), "Angle snap step must be finite and positive.");
        var stepRadians = MathHelper.ToRadians(stepDegrees);
        return MathF.Round(angleRadians / stepRadians, MidpointRounding.AwayFromZero) * stepRadians;
    }

    public static float SnapScaleFactor(float scaleFactor, float step)
    {
        if (!float.IsFinite(scaleFactor))
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be finite.");
        if (!float.IsFinite(step) || step <= 0f)
            throw new ArgumentOutOfRangeException(nameof(step), "Scale snap step must be finite and positive.");
        return Math.Clamp(MathF.Round(scaleFactor / step, MidpointRounding.AwayFromZero) * step,
            0.01f, 1_000f);
    }

    public static bool TryConvertWorldPositionToParentSpace(Vector3 worldPosition,
        Matrix parentWorld, out Vector3 parentLocalPosition)
    {
        parentLocalPosition = Vector3.Zero;
        if (!IsFinite(worldPosition) || !IsFinite(parentWorld)) return false;
        var determinant = parentWorld.Determinant();
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.0000001f) return false;

        var inverse = Matrix.Invert(parentWorld);
        if (!IsFinite(inverse)) return false;
        parentLocalPosition = Vector3.Transform(worldPosition, inverse);
        return IsFinite(parentLocalPosition);
    }

    public static Vector3 SnapWorldPosition(Vector3 worldPosition, float gridStep)
    {
        if (!IsFinite(worldPosition))
            throw new ArgumentOutOfRangeException(nameof(worldPosition), "World position must be finite.");
        if (!float.IsFinite(gridStep) || gridStep <= 0f)
            throw new ArgumentOutOfRangeException(nameof(gridStep), "Grid step must be finite and positive.");

        return new Vector3(
            SnapCoordinate(worldPosition.X, gridStep),
            SnapCoordinate(worldPosition.Y, gridStep),
            SnapCoordinate(worldPosition.Z, gridStep));
    }

    private static float SnapCoordinate(float value, float gridStep) =>
        MathF.Round(value / gridStep, MidpointRounding.AwayFromZero) * gridStep;

    private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 0.000001f)
            return Vector2.DistanceSquared(point, start);
        var along = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(point, start + segment * along);
    }

    private static float Cross(Vector2 left, Vector2 right) =>
        left.X * right.Y - left.Y * right.X;

    private static bool IsVisible(Vector3 projected) =>
        IsFinite(projected) && projected.Z is >= 0f and <= 1f;

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}

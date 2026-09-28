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

public readonly record struct TransformGizmoHandle(
    TransformGizmoAxis Axis,
    Vector3 WorldDirection,
    Vector2 ScreenStart,
    Vector2 ScreenEnd,
    float WorldLength);

/// <summary>Projects and hit-tests the shared world-axis Move gizmo without graphics resources.</summary>
public static class ViewportMoveGizmoMath
{
    private const float HandleLengthFactor = 0.12f;
    private const float MinimumWorldLength = 8f;
    private const float MaximumWorldLength = 60f;
    private const float MinimumScreenLength = 18f;

    public static IReadOnlyList<TransformGizmoHandle> CreateHandles(Vector3 worldOrigin,
        Vector3 cameraPosition, Matrix view, Matrix projection, int viewportWidth, int viewportHeight)
    {
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
        var axes = new (TransformGizmoAxis Axis, Vector3 Direction)[]
        {
            (TransformGizmoAxis.X, Vector3.UnitX),
            (TransformGizmoAxis.Y, Vector3.UnitY),
            (TransformGizmoAxis.Z, Vector3.UnitZ)
        };
        var handles = new List<TransformGizmoHandle>(axes.Length);
        foreach (var (axis, direction) in axes)
        {
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

    private static bool IsVisible(Vector3 projected) =>
        IsFinite(projected) && projected.Z is >= 0f and <= 1f;

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}

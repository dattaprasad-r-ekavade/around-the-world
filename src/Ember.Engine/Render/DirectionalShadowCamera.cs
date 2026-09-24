using Ember.Assets;
using Microsoft.Xna.Framework;
using System;

namespace Ember.Render;

/// <summary>Builds an orthographic light camera that encloses a scene bounds volume.</summary>
public static class DirectionalShadowCamera
{
    /// <summary>
    /// Fits a stable single shadow volume to the camera's visible frustum. The receiver volume is
    /// limited to <paramref name="shadowDistance"/>; scene bounds extend only its depth range so
    /// off-camera geometry can still cast onto visible receivers.
    /// </summary>
    public static Matrix CreateViewProjection(Bounds3 sceneBounds, Matrix cameraView,
        Matrix cameraProjection, Vector3 lightDirection, int shadowMapSize,
        float shadowDistance = 120f)
    {
        if (!IsFinite(lightDirection) || lightDirection.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(lightDirection), "Light direction must be finite and nonzero.");
        if (shadowMapSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(shadowMapSize), "Shadow-map size must be positive.");
        if (!float.IsFinite(shadowDistance) || shadowDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(shadowDistance), "Shadow distance must be finite and positive.");
        if (!IsFinite(cameraView) || !IsFinite(cameraProjection))
            throw new ArgumentException("Camera matrices must contain only finite values.");

        var viewProjection = cameraView * cameraProjection;
        var inverseViewProjection = Matrix.Invert(viewProjection);
        var inverseView = Matrix.Invert(cameraView);
        var cameraPosition = Vector3.Transform(Vector3.Zero, inverseView);
        var receivers = new Vector3[16];
        var receiverIndex = 0;
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 2; x++)
        {
            var screenX = x == 0 ? -1f : 1f;
            var screenY = y == 0 ? -1f : 1f;
            var near = Unproject(new Vector3(screenX, screenY, 0f), inverseViewProjection);
            var far = Unproject(new Vector3(screenX, screenY, 1f), inverseViewProjection);
            var ray = Vector3.Normalize(far - cameraPosition);
            var nearDistance = Vector3.Dot(near - cameraPosition, ray);
            var farDistance = Vector3.Dot(far - cameraPosition, ray);
            if (!float.IsFinite(nearDistance) || !float.IsFinite(farDistance)
                || nearDistance <= 0f || farDistance <= nearDistance)
                throw new ArgumentException("Camera matrices do not describe a finite perspective frustum.");

            var clippedDistance = Math.Clamp(shadowDistance, nearDistance, farDistance);
            receivers[receiverIndex++] = near;
            receivers[receiverIndex++] = cameraPosition + ray * clippedDistance;
        }

        var center = Vector3.Zero;
        foreach (var point in receivers) center += point;
        center /= receivers.Length;

        // A sphere around the clipped frustum gives a fixed projection size while the camera
        // rotates. Snapping its center in light space then prevents sub-texel camera motion from
        // moving the shadow projection every frame.
        var radius = 1f;
        foreach (var point in receivers)
            radius = MathF.Max(radius, Vector3.Distance(center, point));
        radius = radius * 1.02f + 0.01f;
        var diameter = radius * 2f;

        var direction = Vector3.Normalize(lightDirection);
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f
            ? Vector3.Forward
            : Vector3.Up;
        var lightOrientation = Matrix.CreateLookAt(Vector3.Zero, direction, up);
        var inverseLightOrientation = Matrix.Invert(lightOrientation);
        var centerInLightSpace = Vector3.Transform(center, lightOrientation);
        var texelSize = diameter / shadowMapSize;
        var snappedX = MathF.Round(centerInLightSpace.X / texelSize) * texelSize;
        var snappedY = MathF.Round(centerInLightSpace.Y / texelSize) * texelSize;
        var lightSpaceShift = new Vector3(snappedX - centerInLightSpace.X,
            snappedY - centerInLightSpace.Y, 0f);
        var snappedCenter = center + Vector3.TransformNormal(lightSpaceShift, inverseLightOrientation);

        var minimumDepth = float.PositiveInfinity;
        var maximumDepth = float.NegativeInfinity;
        foreach (var point in receivers)
            ExtendDepth(point, snappedCenter, direction, ref minimumDepth, ref maximumDepth);
        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector3(
                (corner & 1) == 0 ? sceneBounds.Min.X : sceneBounds.Max.X,
                (corner & 2) == 0 ? sceneBounds.Min.Y : sceneBounds.Max.Y,
                (corner & 4) == 0 ? sceneBounds.Min.Z : sceneBounds.Max.Z);
            ExtendDepth(point, snappedCenter, direction, ref minimumDepth, ref maximumDepth);
        }

        var depthPadding = MathF.Max(0.5f, radius * 0.05f);
        var eye = snappedCenter + direction * (minimumDepth - depthPadding);
        var view = Matrix.CreateLookAt(eye, eye + direction, up);
        var nearPlane = 0.1f;
        var farPlane = MathF.Max(nearPlane + 0.1f,
            maximumDepth - minimumDepth + depthPadding * 2f);
        var projection = Matrix.CreateOrthographic(diameter, diameter, nearPlane, farPlane);
        return view * projection;
    }

    public static Matrix CreateViewProjection(Bounds3 bounds, Vector3 lightDirection)
    {
        if (!IsFinite(lightDirection) || lightDirection.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(lightDirection), "Light direction must be finite and nonzero.");

        var direction = Vector3.Normalize(lightDirection);
        var radius = MathF.Max(bounds.Size.Length() * 0.5f, 1f);
        var center = bounds.Center;
        var eye = center - direction * (radius * 2.5f);
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f
            ? Vector3.Forward
            : Vector3.Up;
        var view = Matrix.CreateLookAt(eye, center, up);
        var diameter = radius * 2.2f;
        var projection = Matrix.CreateOrthographic(diameter, diameter, 0.1f, radius * 5f + 2f);
        return view * projection;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 Unproject(Vector3 point, Matrix inverseViewProjection)
    {
        var world = Vector4.Transform(new Vector4(point, 1f), inverseViewProjection);
        if (!float.IsFinite(world.W) || MathF.Abs(world.W) < 1e-8f)
            throw new ArgumentException("Camera matrices produce an invalid frustum corner.");
        var result = new Vector3(world.X, world.Y, world.Z) / world.W;
        if (!IsFinite(result))
            throw new ArgumentException("Camera matrices produce a non-finite frustum corner.");
        return result;
    }

    private static void ExtendDepth(Vector3 point, Vector3 center, Vector3 direction,
        ref float minimumDepth, ref float maximumDepth)
    {
        var depth = Vector3.Dot(point - center, direction);
        minimumDepth = MathF.Min(minimumDepth, depth);
        maximumDepth = MathF.Max(maximumDepth, depth);
    }

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}

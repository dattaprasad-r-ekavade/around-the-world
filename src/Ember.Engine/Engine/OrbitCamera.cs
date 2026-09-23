using System;
using Ember.Assets;
using Microsoft.Xna.Framework;

namespace Ember;

/// <summary>A free orbit camera for scene inspection, independent of first-person movement.</summary>
public sealed class OrbitCamera
{
    private float _aspectRatio = 16f / 9f;
    private float _fieldOfViewRadians = MathHelper.ToRadians(60f);

    public Vector3 Target { get; private set; }
    public float Distance { get; private set; } = 8f;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = -0.25f;
    public float MinDistance { get; set; } = 1f;
    public float MaxDistance { get; set; } = 100f;
    public float OrbitSensitivity { get; set; } = 0.01f;
    public float ZoomSensitivity { get; set; } = 0.01f;
    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;
    public Vector3 Position { get; private set; }

    public void Reset(Vector3 target, float distance = 8f, float yaw = 0f, float pitch = -0.25f)
    {
        Target = target;
        Distance = MathHelper.Clamp(distance, MinDistance, MaxDistance);
        Yaw = yaw;
        Pitch = MathHelper.Clamp(pitch, -1.5f, 1.5f);
        RebuildView();
    }

    /// <summary>Sets a world-space camera transform for sequence or authored-camera evaluation.</summary>
    public void SetWorldTransform(Vector3 position, Quaternion rotation)
    {
        if (!IsFinite(position) || !IsFinite(rotation))
            throw new ArgumentException("Camera transform values must be finite.");
        var lengthSquared = rotation.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 0.0000001f)
            throw new ArgumentException("Camera rotation must be a valid quaternion.", nameof(rotation));

        var world = Matrix.CreateFromQuaternion(Quaternion.Normalize(rotation));
        var forward = Vector3.Transform(Vector3.Forward, world);
        var up = Vector3.Transform(Vector3.Up, world);
        Position = position;
        View = Matrix.CreateLookAt(position, position + forward, up);
    }

    public void SetProjection(float aspect, float fieldOfViewDegrees = 60f,
        float near = 0.05f, float far = 500f)
    {
        if (aspect <= 0f) return;
        _aspectRatio = aspect;
        _fieldOfViewRadians = MathHelper.ToRadians(fieldOfViewDegrees);
        Projection = Matrix.CreatePerspectiveFieldOfView(
            _fieldOfViewRadians, aspect, near, far);
    }

    /// <summary>Point the camera at bounds and choose a distance that fits them in the viewport.</summary>
    public void Frame(Bounds3 bounds, float padding = 1.15f)
    {
        if (!float.IsFinite(padding) || padding < 1f)
            throw new ArgumentOutOfRangeException(nameof(padding), "Camera framing padding must be finite and at least 1.");

        var verticalHalfAngle = _fieldOfViewRadians * 0.5f;
        var horizontalHalfAngle = MathF.Atan(MathF.Tan(verticalHalfAngle) * _aspectRatio);
        var limitingHalfAngle = MathF.Min(verticalHalfAngle, horizontalHalfAngle);
        var radius = bounds.Size.Length() * 0.5f;
        var distance = radius <= 0f
            ? MinDistance
            : radius / MathF.Sin(limitingHalfAngle) * padding;

        Reset(bounds.Center, MathHelper.Clamp(distance, MinDistance, MaxDistance), Yaw, Pitch);
    }

    public void Orbit(Vector2 delta)
    {
        Yaw -= delta.X * OrbitSensitivity;
        Pitch = MathHelper.Clamp(Pitch - delta.Y * OrbitSensitivity, -1.5f, 1.5f);
        RebuildView();
    }

    public void Zoom(float wheelDelta)
    {
        Distance = MathHelper.Clamp(Distance - wheelDelta * ZoomSensitivity, MinDistance, MaxDistance);
        RebuildView();
    }

    public void RebuildView()
    {
        var offset = Vector3.Transform(
            new Vector3(0f, 0f, Distance),
            Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(Yaw));
        Position = Target + offset;
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

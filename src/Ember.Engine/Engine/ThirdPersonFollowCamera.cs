using Ember.Physics;
using Microsoft.Xna.Framework;
using System;

namespace Ember;

/// <summary>Follow camera whose ray-tested boom shortens before world and dynamic colliders.</summary>
public sealed class ThirdPersonFollowCamera
{
    private const float MinimumSafeDistance = 0.05f;
    private const float MaximumPitch = 1.35f;
    private float _minDistance = 0.75f;
    private float _maxDistance = 20f;

    public Vector3 TargetOffset { get; set; } = new(0f, 0.15f, 0f);
    public float Distance { get; private set; } = 4.5f;
    public float MinDistance
    {
        get => _minDistance;
        set
        {
            if (!float.IsFinite(value) || value < MinimumSafeDistance || value > _maxDistance)
                throw new ArgumentOutOfRangeException(nameof(value));
            _minDistance = value;
            Distance = MathHelper.Clamp(Distance, _minDistance, _maxDistance);
        }
    }
    public float MaxDistance
    {
        get => _maxDistance;
        set
        {
            if (!float.IsFinite(value) || value < _minDistance)
                throw new ArgumentOutOfRangeException(nameof(value));
            _maxDistance = value;
            Distance = MathHelper.Clamp(Distance, _minDistance, _maxDistance);
        }
    }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = -0.22f;
    public float OrbitSensitivity { get; set; } = 0.01f;
    public float ZoomSensitivity { get; set; } = 0.01f;
    public float ObstructionPadding { get; set; } = 0.15f;
    public PhysicsCollisionLayer ObstructionLayers { get; set; } =
        PhysicsCollisionLayer.World | PhysicsCollisionLayer.Dynamic;
    public Vector3 Target { get; private set; }
    public Vector3 Position { get; private set; }
    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;
    public bool IsObstructed { get; private set; }
    public PhysicsObjectId? ObstructingObject { get; private set; }

    public void SetProjection(float aspect, float fieldOfViewDegrees = 60f,
        float near = 0.05f, float far = 500f)
    {
        if (!float.IsFinite(aspect) || aspect <= 0f)
            throw new ArgumentOutOfRangeException(nameof(aspect));
        if (!float.IsFinite(fieldOfViewDegrees) || fieldOfViewDegrees <= 0f || fieldOfViewDegrees >= 180f)
            throw new ArgumentOutOfRangeException(nameof(fieldOfViewDegrees));
        if (!float.IsFinite(near) || near <= 0f)
            throw new ArgumentOutOfRangeException(nameof(near));
        if (!float.IsFinite(far) || far <= near)
            throw new ArgumentOutOfRangeException(nameof(far));

        var fieldOfViewRadians = MathHelper.ToRadians(fieldOfViewDegrees);
        Projection = Matrix.CreatePerspectiveFieldOfView(fieldOfViewRadians, aspect, near, far);
    }

    public void Reset(Vector3 followPosition, float distance = 4.5f, float yaw = 0f, float pitch = -0.22f)
    {
        ValidateFinite(followPosition, nameof(followPosition));
        if (!float.IsFinite(distance) || distance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(distance));
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch))
            throw new ArgumentOutOfRangeException(nameof(yaw), "Camera angles must be finite.");

        Yaw = yaw;
        Pitch = MathHelper.Clamp(pitch, -MaximumPitch, MaximumPitch);
        Distance = MathHelper.Clamp(distance, MinDistance, MaxDistance);
        Follow(null, followPosition);
    }

    public void Orbit(Vector2 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y))
            throw new ArgumentOutOfRangeException(nameof(delta));
        Yaw -= delta.X * OrbitSensitivity;
        Pitch = MathHelper.Clamp(Pitch - delta.Y * OrbitSensitivity, -MaximumPitch, MaximumPitch);
    }

    public void Zoom(float wheelDelta)
    {
        if (!float.IsFinite(wheelDelta)) throw new ArgumentOutOfRangeException(nameof(wheelDelta));
        Distance = MathHelper.Clamp(Distance - wheelDelta * ZoomSensitivity, MinDistance, MaxDistance);
    }

    /// <summary>Converts a local right/forward input vector into a horizontal world direction.</summary>
    public Vector3 MoveDirection(Vector2 localMovement)
    {
        if (!float.IsFinite(localMovement.X) || !float.IsFinite(localMovement.Y))
            throw new ArgumentOutOfRangeException(nameof(localMovement));
        if (localMovement.LengthSquared() > 1f) localMovement.Normalize();

        var forward = Target - Position;
        forward.Y = 0f;
        if (forward.LengthSquared() < 1e-8f) return Vector3.Zero;
        forward.Normalize();
        var right = Vector3.Cross(forward, Vector3.Up);
        var direction = right * localMovement.X + forward * localMovement.Y;
        if (direction.LengthSquared() > 1f) direction.Normalize();
        return direction;
    }

    /// <summary>Updates camera matrices around a world-space target, shortened to the nearest obstruction.</summary>
    public void Follow(PhysicsWorld? physics, Vector3 followPosition)
    {
        ValidateFinite(followPosition, nameof(followPosition));
        ValidateFinite(TargetOffset, nameof(TargetOffset));
        if (!float.IsFinite(ObstructionPadding) || ObstructionPadding < 0f)
            throw new InvalidOperationException("Camera obstruction padding must be finite and nonnegative.");

        Target = followPosition + TargetOffset;
        var offset = Vector3.Transform(
            new Vector3(0f, 0f, Distance),
            Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(Yaw));
        var desiredDistance = offset.Length();
        var direction = Vector3.Normalize(offset);
        var actualDistance = desiredDistance;
        IsObstructed = false;
        ObstructingObject = null;

        if (physics is not null)
        {
            var hit = physics.Raycast(Target, direction, desiredDistance, ObstructionLayers);
            if (hit is { } obstruction)
            {
                actualDistance = MathF.Max(MinimumSafeDistance, obstruction.Distance - ObstructionPadding);
                IsObstructed = actualDistance < desiredDistance;
                if (IsObstructed) ObstructingObject = obstruction.ObjectId;
            }
        }

        Position = Target + direction * actualDistance;
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
    }
}

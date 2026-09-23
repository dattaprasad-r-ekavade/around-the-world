using Microsoft.Xna.Framework;
using System;

namespace Ember.Physics;

/// <summary>Movement and collision settings for the first upright capsule character controller.</summary>
public sealed record PhysicsCharacterSettings
{
    public float Radius { get; init; } = 0.45f;
    public float CylinderLength { get; init; } = 0.9f;
    public float Mass { get; init; } = 80f;
    public float MoveSpeed { get; init; } = 5f;
    public float JumpSpeed { get; init; } = 6f;
    public float MaximumSlopeAngleDegrees { get; init; } = 50f;
    public float GroundProbeDistance { get; init; } = 0.15f;
}

/// <summary>
/// Controls one upright capsule in a shared PhysicsWorld. The latest movement direction is read
/// each fixed step and queued jump requests are consumed once; camera state is outside the body.
/// </summary>
public sealed class PhysicsCharacterController : IDisposable
{
    private const float MinimumGroundNormalY = 0.1f;
    private const float GroundPenetrationTolerance = 0.12f;

    private readonly PhysicsWorld _world;
    private readonly PhysicsCharacterSettings _settings;
    private readonly float _cosMaximumSlope;
    private Vector3 _moveInput;
    private Vector3 _groundNormal = Vector3.Up;
    private bool _jumpRequested;
    private bool _groundedBeforeStep;
    private bool _hasWalkableSupport;
    private bool _worldDisposed;
    private bool _disposed;

    public PhysicsCharacterController(PhysicsWorld world, Vector3 position,
        PhysicsCharacterSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _settings = settings ?? new PhysicsCharacterSettings();
        ValidateSettings(_settings);
        _cosMaximumSlope = MathF.Cos(MathHelper.ToRadians(_settings.MaximumSlopeAngleDegrees));

        PhysicsBodyId = _world.AddDynamicCapsule(
            position,
            _settings.Radius,
            _settings.CylinderLength,
            _settings.Mass,
            new PhysicsCollisionFilter(
                PhysicsCollisionLayer.Player,
                PhysicsCollisionLayer.World | PhysicsCollisionLayer.Dynamic));
        try
        {
            _world.RegisterCharacter(this);
            RefreshGrounding(allowGrounded: true);
        }
        catch
        {
            _world.UnregisterCharacter(this);
            _world.RemoveDynamicBody(PhysicsBodyId);
            throw;
        }
    }

    public PhysicsObjectId PhysicsBodyId { get; }
    public PhysicsPose Pose => _world.GetPose(PhysicsBodyId);
    public Vector3 Velocity => _world.GetLinearVelocity(PhysicsBodyId);
    public bool IsGrounded { get; private set; }
    public bool HasGroundSupport { get; private set; }
    public Vector3 GroundNormal => _groundNormal;
    public bool JumpedThisStep { get; private set; }
    public bool LandedThisStep { get; private set; }

    /// <summary>Sets a world-space horizontal direction; magnitude is clamped to one.</summary>
    public void SetMoveInput(Vector3 worldDirection)
    {
        ThrowIfDisposed();
        ValidateFinite(worldDirection, nameof(worldDirection));
        worldDirection.Y = 0f;
        var lengthSquared = worldDirection.LengthSquared();
        if (MathF.Max(MathF.Abs(worldDirection.X), MathF.Abs(worldDirection.Z)) > 1f)
        {
            var scaled = worldDirection / MathF.Max(MathF.Abs(worldDirection.X), MathF.Abs(worldDirection.Z));
            _moveInput = Vector3.Normalize(scaled);
        }
        else
        {
            _moveInput = lengthSquared > 1f ? Vector3.Normalize(worldDirection) : worldDirection;
        }
    }

    /// <summary>Queues one jump attempt for the next fixed step.</summary>
    public void RequestJump()
    {
        ThrowIfDisposed();
        _jumpRequested = true;
    }

    internal void PreparePhysicsStep()
    {
        _groundedBeforeStep = IsGrounded;
        JumpedThisStep = false;
        LandedThisStep = false;

        var velocity = Velocity;
        var jumping = _jumpRequested && IsGrounded;
        _jumpRequested = false;
        if (jumping)
        {
            velocity.Y = _settings.JumpSpeed;
            IsGrounded = false;
            _groundedBeforeStep = false;
            JumpedThisStep = true;
        }

        var desiredVelocity = _moveInput * _settings.MoveSpeed;
        if (_hasWalkableSupport && IsGrounded && !jumping)
        {
            var tangentVelocity = desiredVelocity - _groundNormal * Vector3.Dot(desiredVelocity, _groundNormal);
            if (tangentVelocity.LengthSquared() > 1e-8f && _moveInput.LengthSquared() > 1e-8f)
                tangentVelocity = Vector3.Normalize(tangentVelocity) * (_settings.MoveSpeed * _moveInput.Length());
            velocity = tangentVelocity;
        }
        else
        {
            if (HasGroundSupport && !_hasWalkableSupport)
                desiredVelocity = Vector3.Zero;
            velocity.X = desiredVelocity.X;
            velocity.Z = desiredVelocity.Z;
        }

        _world.SetLinearVelocity(PhysicsBodyId, velocity);
    }

    internal void CompletePhysicsStep()
    {
        var velocity = Velocity;
        RefreshGrounding(allowGrounded: velocity.Y <= 0.2f);
        LandedThisStep = !_groundedBeforeStep && IsGrounded;
    }

    internal void OnWorldDisposed()
    {
        _worldDisposed = true;
        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_worldDisposed) return;
        _world.UnregisterCharacter(this);
        _world.RemoveDynamicBody(PhysicsBodyId);
    }

    private void RefreshGrounding(bool allowGrounded)
    {
        var maximumProbe = _settings.CylinderLength * 0.5f
            + _settings.Radius / MinimumGroundNormalY
            + _settings.GroundProbeDistance;
        var hit = _world.Raycast(Pose.Position, -Vector3.Up, maximumProbe, PhysicsCollisionLayer.World);
        HasGroundSupport = false;
        IsGrounded = false;
        _hasWalkableSupport = false;
        _groundNormal = Vector3.Up;
        if (hit is not { } support || support.Normal.Y < MinimumGroundNormalY)
            return;

        var normal = Vector3.Normalize(support.Normal);
        var capsuleVerticalSupport = _settings.CylinderLength * 0.5f + _settings.Radius / normal.Y;
        var gap = support.Distance - capsuleVerticalSupport;
        if (gap < -GroundPenetrationTolerance || gap > _settings.GroundProbeDistance)
            return;

        HasGroundSupport = true;
        _groundNormal = normal;
        _hasWalkableSupport = normal.Y >= _cosMaximumSlope;
        IsGrounded = allowGrounded && _hasWalkableSupport;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed || _world.IsDisposed)
            throw new ObjectDisposedException(nameof(PhysicsCharacterController));
    }

    private static void ValidateSettings(PhysicsCharacterSettings settings)
    {
        if (!float.IsFinite(settings.Radius) || settings.Radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Character radius must be finite and positive.");
        if (!float.IsFinite(settings.CylinderLength) || settings.CylinderLength < 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Character cylinder length must be finite and nonnegative.");
        if (!float.IsFinite(settings.Mass) || settings.Mass <= 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Character mass must be finite and positive.");
        if (!float.IsFinite(settings.MoveSpeed) || settings.MoveSpeed < 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Character move speed must be finite and nonnegative.");
        if (!float.IsFinite(settings.JumpSpeed) || settings.JumpSpeed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Character jump speed must be finite and positive.");
        if (!float.IsFinite(settings.MaximumSlopeAngleDegrees)
            || settings.MaximumSlopeAngleDegrees <= 0f || settings.MaximumSlopeAngleDegrees >= 90f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Maximum slope angle must be between zero and 90 degrees.");
        if (!float.IsFinite(settings.GroundProbeDistance) || settings.GroundProbeDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(settings), "Ground probe distance must be finite and positive.");
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Movement components must be finite.");
    }
}

using Ember;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Ground height under the feet, in metres. Interiors return a constant floor.</summary>
public delegate float SampleGround(float x, float z);

/// <summary>
/// FirstPersonView with hills. Ember's Step ignores Y from ResolveWalk; this samples ground
/// after the XZ resolve and parks the eye on it. Copied into the game so a Ratna Bay sync
/// cannot wipe the change.
/// </summary>
public sealed class GroundedView
{
    public const float DefaultPitchLimit = 1.4f;

    public float MouseSensitivity { get; set; } = 0.0032f;
    public float KeyboardTurnSpeed { get; set; } = 2.2f;
    public float PitchLimit { get; set; } = DefaultPitchLimit;
    public float WalkSpeed { get; set; } = 6f;
    public float SprintSpeed { get; set; } = 11f;
    public float CollisionRadius { get; set; } = 0.38f;
    public float Gravity { get; set; } = 24f;
    public float JumpSpeed { get; set; } = 8f;
    public float CrouchDrop { get; set; } = 0.9f;
    public float CrouchLerpSpeed { get; set; } = 12f;

    public Vector3 Position { get; set; } = new(0f, WorldScale.EyeHeight, 0f);
    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.12f;
    public float StandingEyeY { get; set; } = WorldScale.EyeHeight;
    public bool Crouching { get; set; }
    public bool NoClip { get; set; }
    public bool Grounded { get; private set; } = true;
    public bool Mounted { get; set; }
    public bool Wagon { get; set; }
    public bool Swimming { get; set; }
    public bool WantClimb { get; set; }
    public float SpeedScale { get; set; } = 1f;

    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;

    private float _verticalOffset;
    private float _verticalVelocity;
    private float _eyeAbove = WorldScale.EyeHeight;

    public Vector3 Forward => Vector3.Transform(
        Vector3.Forward,
        Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(-Yaw));

    public void SetProjection(float aspect, float fieldOfViewDegrees = 65f,
        float near = 0.2f, float far = WorldScale.FarPlane)
    {
        if (aspect <= 0f) return;
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(fieldOfViewDegrees), aspect, near, far);
    }

    public void Reset(Vector3 position, float yaw, float pitch, float standingEyeY)
    {
        Position = position;
        Yaw = yaw;
        Pitch = MathHelper.Clamp(pitch, -PitchLimit, PitchLimit);
        StandingEyeY = standingEyeY;
        Crouching = false;
        Mounted = false;
        Wagon = false;
        Swimming = false;
        CollisionRadius = 0.38f;
        _verticalOffset = 0f;
        _verticalVelocity = 0f;
        _eyeAbove = standingEyeY;
        Grounded = true;
        RebuildView();
    }

    public void Place(Vector3 position)
    {
        Position = position;
        _verticalVelocity = 0f;
    }

    public MoveResult Step(float seconds, WalkInput walk, Vector2 lookPixels,
        ResolveWalk? collide, SampleGround? ground, SampleGround? terrainBed = null)
    {
        if (seconds < 0f) seconds = 0f;

        Yaw += walk.HeldYaw * seconds * KeyboardTurnSpeed;
        Pitch = MathHelper.Clamp(
            Pitch + walk.HeldPitch * seconds * KeyboardTurnSpeed, -PitchLimit, PitchLimit);

        if (lookPixels != Vector2.Zero)
        {
            Yaw += lookPixels.X * MouseSensitivity;
            Pitch = MathHelper.Clamp(
                Pitch - lookPixels.Y * MouseSensitivity, -PitchLimit, PitchLimit);
        }

        if (Swimming)
            return StepSwim(seconds, walk, collide, terrainBed ?? ground);

        var speed = Mounted
            ? Wagon
                ? (walk.Sprint ? WorldScale.WagonSprint : WorldScale.WagonWalk)
                : (walk.Sprint ? WorldScale.HorseSprint : WorldScale.HorseWalk)
            : (walk.Sprint ? SprintSpeed : WalkSpeed);
        speed *= MathHelper.Clamp(SpeedScale, 0.25f, 1.2f);
        if (WantClimb && !Mounted) speed *= 0.42f;
        var forward = Forward;
        var flatForward = new Vector3(forward.X, 0f, forward.Z);
        if (flatForward.LengthSquared() > 0.001f)
            flatForward.Normalize();

        var right = Vector3.Cross(flatForward, Vector3.Up);
        var movement = Vector3.Zero;
        if (walk.Forward) movement += flatForward;
        if (walk.Back) movement -= flatForward;
        if (walk.Left) movement -= right;
        if (walk.Right) movement += right;

        var wasGrounded = Grounded;
        if (walk.Jump && Grounded && !Mounted)
        {
            _verticalVelocity = JumpSpeed;
            Grounded = false;
        }

        _verticalVelocity -= Gravity * seconds;
        _verticalOffset = MathF.Max(0f, _verticalOffset + _verticalVelocity * seconds);
        if (_verticalOffset <= 0.0001f)
        {
            _verticalOffset = 0f;
            _verticalVelocity = 0f;
            Grounded = true;
        }

        var landed = Grounded && !wasGrounded;
        var stand = Mounted ? WorldScale.HorseEye : StandingEyeY;
        var targetEyeY = stand - (!Mounted && Crouching ? CrouchDrop : 0f);
        var crouchBlend = 1f - MathF.Exp(-CrouchLerpSpeed * seconds);
        _eyeAbove = MathHelper.Lerp(_eyeAbove, targetEyeY, crouchBlend);

        var x = Position.X;
        var z = Position.Z;
        var metres = 0f;

        if (movement.LengthSquared() > 0.001f)
        {
            movement.Normalize();
            var delta = movement * speed * seconds;
            var nx = x + delta.X;
            var nz = z + delta.Z;

            if (ground is not null && _verticalOffset < 0.05f && !(WantClimb && !Mounted))
            {
                if (TooSteep(x, z, nx, z, ground)) nx = x;
                if (TooSteep(nx, z, nx, nz, ground)) nz = z;
            }

            if (Mounted && terrainBed is not null)
            {
                var bed = terrainBed(nx, nz);
                if (WorldScale.WaterLevel - bed > WorldScale.WadeDepth)
                {
                    nx = x;
                    nz = z;
                }
            }

            if (collide is not null && !NoClip)
            {
                var resolved = collide(Position, new Vector3(nx - x, 0f, nz - z), CollisionRadius);
                metres = new Vector2(resolved.X - Position.X, resolved.Z - Position.Z).Length();
                x = resolved.X;
                z = resolved.Z;
            }
            else
            {
                metres = new Vector2(nx - x, nz - z).Length();
                x = nx;
                z = nz;
            }
        }

        var groundY = ground?.Invoke(x, z) ?? 0f;
        Position = new Vector3(x, groundY + _eyeAbove + _verticalOffset, z);
        return new MoveResult(metres, landed);
    }

    private MoveResult StepSwim(float seconds, WalkInput walk, ResolveWalk? collide,
        SampleGround? bed)
    {
        _verticalOffset = 0f;
        _verticalVelocity = 0f;
        Grounded = false;
        _eyeAbove = WorldScale.EyeHeight;

        var speed = walk.Sprint ? WorldScale.SwimSprint : WorldScale.SwimSpeed;
        var look = Forward;
        var flat = new Vector3(look.X, 0f, look.Z);
        if (flat.LengthSquared() > 0.001f) flat.Normalize();
        var right = Vector3.Cross(flat, Vector3.Up);

        var move = Vector3.Zero;
        if (walk.Forward) move += look;
        if (walk.Back) move -= look;
        if (walk.Left) move -= right;
        if (walk.Right) move += right;
        if (walk.Jump) move += Vector3.Up;
        if (Crouching) move -= Vector3.Up;

        var x = Position.X;
        var y = Position.Y;
        var z = Position.Z;
        var metres = 0f;

        if (move.LengthSquared() > 0.001f)
        {
            move.Normalize();
            var delta = move * speed * seconds;
            var nx = x + delta.X;
            var nz = z + delta.Z;
            if (collide is not null && !NoClip)
            {
                var resolved = collide(Position, new Vector3(nx - x, 0f, nz - z), CollisionRadius);
                metres = new Vector2(resolved.X - Position.X, resolved.Z - Position.Z).Length();
                x = resolved.X;
                z = resolved.Z;
            }
            else
            {
                metres = new Vector2(delta.X, delta.Z).Length();
                x = nx;
                z = nz;
            }

            y += delta.Y;
        }

        // Drift toward the surface unless you are diving.
        if (!Crouching)
            y += (WorldScale.WaterLevel + 0.22f - y) * MathF.Min(1f, seconds * 1.8f);

        var floor = bed?.Invoke(x, z) ?? 0f;
        var minY = floor + 0.55f;
        var maxY = WorldScale.WaterLevel + 0.38f;
        y = Math.Clamp(y, minY, maxY);
        Position = new Vector3(x, y, z);
        return new MoveResult(metres, Landed: false);
    }

    private static bool TooSteep(float x0, float z0, float x1, float z1, SampleGround ground)
    {
        var dx = x1 - x0;
        var dz = z1 - z0;
        var horiz = MathF.Sqrt(dx * dx + dz * dz);
        if (horiz < 0.0001f) return false;

        var rise = ground(x1, z1) - ground(x0, z0);
        if (rise <= WorldScale.StepHeight) return false;
        return rise / horiz > WorldScale.MaxWalkSlope;
    }

    public void RebuildView(float shakeYaw = 0f, float shakePitch = 0f)
    {
        var shakenPitch = MathHelper.Clamp(Pitch + shakePitch, -PitchLimit, PitchLimit);
        var shakenYaw = Yaw + shakeYaw;
        var forward = Vector3.Transform(
            Vector3.Forward,
            Matrix.CreateRotationX(shakenPitch) * Matrix.CreateRotationY(-shakenYaw));
        View = Matrix.CreateLookAt(Position, Position + forward, Vector3.Up);
    }
}

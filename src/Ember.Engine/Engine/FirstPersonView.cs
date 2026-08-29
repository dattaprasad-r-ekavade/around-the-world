using Microsoft.Xna.Framework;
using System;

namespace Ember;

/// <summary>
/// Resolves a planned walk against solid geometry. Origin and delta are world metres; Y on
/// the result is ignored — the view owns jumping and crouching itself.
/// </summary>
public delegate Vector3 ResolveWalk(Vector3 origin, Vector3 delta, float radius);

/// <summary>Buttons and held-look for one frame. Pixel look is passed separately.</summary>
public readonly record struct WalkInput(
    bool Forward,
    bool Back,
    bool Left,
    bool Right,
    bool Sprint,
    bool Jump,
    float HeldYaw,
    float HeldPitch);

/// <summary>What happened while the view moved, so the game can play a footstep or a land.</summary>
public readonly record struct MoveResult(float MetresWalked, bool Landed);

/// <summary>
/// First-person look, walk, jump and crouch. Nothing here knows about mines, doors, Ratna Bay
/// or a content pipeline — collision is a callback, spawn is a Reset argument.
///
/// **This is the piece a different first-person game would reuse unchanged.** Game1 still
/// decides when to crouch, when noclip is on, and what a collision means; this type turns
/// those facts into a view matrix.
///
/// Yaw increases clockwise (right). CreateRotationY turns the other way, so the look
/// transform negates yaw. Getting that sign wrong inverts mouse and strafe against each other.
/// </summary>
public sealed class FirstPersonView
{
    public const float DefaultPitchLimit = 1.4f;

    /// <summary>Radians of rotation per pixel of mouse travel.</summary>
    public float MouseSensitivity { get; set; } = 0.0032f;

    /// <summary>Radians per second while an arrow key is held.</summary>
    public float KeyboardTurnSpeed { get; set; } = 2.2f;

    public float PitchLimit { get; set; } = DefaultPitchLimit;
    public float WalkSpeed { get; set; } = 6f;
    public float SprintSpeed { get; set; } = 11f;
    public float CollisionRadius { get; set; } = 0.38f;
    public float Gravity { get; set; } = 24f;
    public float JumpSpeed { get; set; } = 8f;
    public float CrouchDrop { get; set; } = 0.9f;
    public float CrouchLerpSpeed { get; set; } = 12f;

    public Vector3 Position { get; set; } = new(0f, 1.7f, 0f);
    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.12f;
    public float StandingEyeY { get; set; } = 1.7f;
    public bool Crouching { get; set; }
    public bool NoClip { get; set; }
    public bool Grounded { get; private set; } = true;

    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;

    private float _verticalOffset;
    private float _verticalVelocity;

    /// <summary>
    /// Where the camera is pointing, unshaken.
    ///
    /// Aim, movement and the weapon read this. The view matrix may be shaken; this must not
    /// be, or a running shake walks the player's aim off by degrees with nobody able to say why.
    /// </summary>
    public Vector3 Forward => Vector3.Transform(
        Vector3.Forward,
        Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(-Yaw));

    public void SetProjection(float aspect, float fieldOfViewDegrees = 65f,
        float near = 0.05f, float far = 200f)
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
        _verticalOffset = 0f;
        _verticalVelocity = 0f;
        Grounded = true;
        RebuildView();
    }

    /// <summary>Put the body somewhere without changing look. Stops a fall in progress.</summary>
    public void Place(Vector3 position)
    {
        Position = position;
        _verticalVelocity = 0f;
    }

    /// <summary>
    /// Look, then walk. Returns how far the body actually moved on the ground, which is not
    /// the same as how far it tried to — walking into a wall must not keep stepping.
    /// </summary>
    public MoveResult Step(float seconds, WalkInput walk, Vector2 lookPixels, ResolveWalk? collide)
    {
        if (seconds < 0f) seconds = 0f;

        Yaw += walk.HeldYaw * seconds * KeyboardTurnSpeed;
        Pitch = MathHelper.Clamp(
            Pitch + walk.HeldPitch * seconds * KeyboardTurnSpeed, -PitchLimit, PitchLimit);

        // Mouse look is framerate-independent by construction: it is pixels moved, not a
        // rate held over time, so it must not be multiplied by the frame duration.
        if (lookPixels != Vector2.Zero)
        {
            Yaw += lookPixels.X * MouseSensitivity;
            Pitch = MathHelper.Clamp(
                Pitch - lookPixels.Y * MouseSensitivity, -PitchLimit, PitchLimit);
        }

        var speed = walk.Sprint ? SprintSpeed : WalkSpeed;
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
        if (walk.Jump && Grounded)
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
        var targetEyeY = StandingEyeY - (Crouching ? CrouchDrop : 0f);
        var currentEyeY = Position.Y - _verticalOffset;
        var crouchBlend = 1f - MathF.Exp(-CrouchLerpSpeed * seconds);
        var nextEyeY = MathHelper.Lerp(currentEyeY, targetEyeY, crouchBlend);

        var metres = 0f;
        if (movement.LengthSquared() > 0.001f)
        {
            movement.Normalize();
            var delta = movement * speed * seconds;
            if (collide is not null && !NoClip)
            {
                var resolved = collide(Position, new Vector3(delta.X, 0f, delta.Z), CollisionRadius);
                metres = new Vector2(resolved.X - Position.X, resolved.Z - Position.Z).Length();
                Position = new Vector3(resolved.X, nextEyeY + _verticalOffset, resolved.Z);
            }
            else
            {
                metres = new Vector2(delta.X, delta.Z).Length();
                Position = new Vector3(Position.X + delta.X,
                    nextEyeY + _verticalOffset, Position.Z + delta.Z);
            }
        }
        else
        {
            Position = new Vector3(Position.X, nextEyeY + _verticalOffset, Position.Z);
        }

        return new MoveResult(metres, landed);
    }

    /// <summary>
    /// Rebuild the view matrix. Shake is added here and nowhere else, so aim stays honest.
    /// </summary>
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

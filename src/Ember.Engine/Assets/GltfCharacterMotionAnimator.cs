using Ember.Physics;
using System;
using System.Numerics;

namespace Ember.Assets;

public enum CharacterMotionState
{
    Idle,
    Walk,
    Jump
}

/// <summary>Chooses and evaluates an imported character clip from post-physics movement.</summary>
public sealed class GltfCharacterMotionAnimator
{
    private readonly PhysicsCharacterController _character;
    private readonly GltfSkinPose _pose;
    private readonly GltfAnimationPlayback _idle;
    private readonly GltfAnimationPlayback _walk;
    private readonly GltfAnimationPlayback _jump;
    private readonly float _movementThreshold;
    private GltfAnimationPlayback _current;

    public GltfCharacterMotionAnimator(PhysicsCharacterController character,
        GltfSkinnedCharacterData asset, GltfAnimationClipData idleClip,
        GltfAnimationClipData walkClip, GltfAnimationClipData jumpClip,
        float movementThreshold = 0.15f, float playbackSpeed = 1f)
    {
        _character = character ?? throw new ArgumentNullException(nameof(character));
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(idleClip);
        ArgumentNullException.ThrowIfNull(walkClip);
        ArgumentNullException.ThrowIfNull(jumpClip);
        if (!float.IsFinite(movementThreshold) || movementThreshold < 0f)
            throw new ArgumentOutOfRangeException(nameof(movementThreshold));
        if (!float.IsFinite(playbackSpeed))
            throw new ArgumentOutOfRangeException(nameof(playbackSpeed));
        if (!ReferenceEquals(idleClip.Skin, asset.Skin)
            || !ReferenceEquals(walkClip.Skin, asset.Skin)
            || !ReferenceEquals(jumpClip.Skin, asset.Skin))
            throw new ArgumentException("All motion clips must belong to the supplied character asset.");

        _movementThreshold = movementThreshold;
        _pose = asset.CreatePose();
        _idle = new GltfAnimationPlayback(idleClip, loop: true, speed: playbackSpeed);
        _walk = new GltfAnimationPlayback(walkClip, loop: true, speed: playbackSpeed);
        _jump = new GltfAnimationPlayback(jumpClip, loop: false, speed: playbackSpeed);
        _current = _idle;
        _idle.Play();
        State = CharacterMotionState.Idle;
        Evaluate();
    }

    public CharacterMotionState State { get; private set; }
    public string CurrentClipName => _current.Clip.Name;
    public GltfAnimationPlayback CurrentPlayback => _current;
    public GltfSkinPose Pose => _pose;

    /// <summary>Call once after each PhysicsWorld.Step, from inside the fixed-step callback.</summary>
    public void AdvanceFixedStep(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        var velocity = _character.Velocity;
        var horizontalSpeedSquared = (velocity.X * velocity.X) + (velocity.Z * velocity.Z);
        var nextState = _character.JumpedThisStep || !_character.IsGrounded
            ? CharacterMotionState.Jump
            : horizontalSpeedSquared > _movementThreshold * _movementThreshold
                ? CharacterMotionState.Walk
                : CharacterMotionState.Idle;

        if (nextState != State)
        {
            _current.Pause();
            State = nextState;
            _current = nextState switch
            {
                CharacterMotionState.Idle => _idle,
                CharacterMotionState.Walk => _walk,
                CharacterMotionState.Jump => _jump,
                _ => throw new ArgumentOutOfRangeException(nameof(nextState))
            };
            _current.Seek(0f);
            _current.Play();
        }

        _current.Advance(seconds);
        Evaluate();
    }

    private void Evaluate() => _current.Clip.Evaluate(_pose, _current.Time);
}

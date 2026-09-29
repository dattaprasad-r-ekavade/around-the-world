using System;
using System.Collections.Generic;
using Ember.Input;
using Microsoft.Xna.Framework.Input;

namespace Ember.Scene;

/// <summary>Persisted movement, camera, and keyboard defaults for a scene's Play session.</summary>
public sealed record ScenePlaySettings
{
    public float CapsuleRadius { get; init; } = 0.45f;
    public float CapsuleCylinderLength { get; init; } = 0.9f;
    public float MoveSpeed { get; init; } = 3.5f;
    public float JumpSpeed { get; init; } = 6f;
    public float CameraTargetOffsetY { get; init; }
    public float CameraDistance { get; init; } = 4.8f;
    public float CameraOrbitSensitivity { get; init; } = 0.01f;

    public Keys MoveForward { get; init; } = Keys.W;
    public Keys MoveForwardAlternate { get; init; } = Keys.Up;
    public Keys MoveBackward { get; init; } = Keys.S;
    public Keys MoveBackwardAlternate { get; init; } = Keys.Down;
    public Keys MoveLeft { get; init; } = Keys.A;
    public Keys MoveLeftAlternate { get; init; } = Keys.Left;
    public Keys MoveRight { get; init; } = Keys.D;
    public Keys MoveRightAlternate { get; init; } = Keys.Right;
    public Keys Jump { get; init; } = Keys.Space;
    public Keys JumpAlternate { get; init; }

    public ScenePlaySettings ValidatedCopy()
    {
        if (!float.IsFinite(CapsuleRadius) || CapsuleRadius < 0.05f || CapsuleRadius > 5f)
            throw new ArgumentOutOfRangeException(nameof(CapsuleRadius), "Character capsule radius must be between 0.05 and 5 metres.");
        if (!float.IsFinite(CapsuleCylinderLength) || CapsuleCylinderLength < 0f || CapsuleCylinderLength > 20f)
            throw new ArgumentOutOfRangeException(nameof(CapsuleCylinderLength), "Character capsule length must be between 0 and 20 metres.");
        if (!float.IsFinite(MoveSpeed) || MoveSpeed < 0f || MoveSpeed > 100f)
            throw new ArgumentOutOfRangeException(nameof(MoveSpeed), "Character move speed must be between 0 and 100 metres per second.");
        if (!float.IsFinite(JumpSpeed) || JumpSpeed <= 0f || JumpSpeed > 100f)
            throw new ArgumentOutOfRangeException(nameof(JumpSpeed), "Character jump speed must be above zero and at most 100 metres per second.");
        if (!float.IsFinite(CameraTargetOffsetY) || MathF.Abs(CameraTargetOffsetY) > 100f)
            throw new ArgumentOutOfRangeException(nameof(CameraTargetOffsetY), "Camera target offset must be finite and within 100 metres.");
        if (!float.IsFinite(CameraDistance) || CameraDistance < 0.5f || CameraDistance > 100f)
            throw new ArgumentOutOfRangeException(nameof(CameraDistance), "Camera distance must be between 0.5 and 100 metres.");
        if (!float.IsFinite(CameraOrbitSensitivity) || CameraOrbitSensitivity <= 0f || CameraOrbitSensitivity > 1f)
            throw new ArgumentOutOfRangeException(nameof(CameraOrbitSensitivity), "Camera sensitivity must be finite and greater than zero, up to 1.");

        var bindings = new (string Action, Keys Primary, Keys Alternate)[]
        {
            (GameplayActionNames.MoveForward, MoveForward, MoveForwardAlternate),
            (GameplayActionNames.MoveBackward, MoveBackward, MoveBackwardAlternate),
            (GameplayActionNames.MoveLeft, MoveLeft, MoveLeftAlternate),
            (GameplayActionNames.MoveRight, MoveRight, MoveRightAlternate),
            (GameplayActionNames.Jump, Jump, JumpAlternate)
        };
        var usedKeys = new HashSet<Keys>();
        foreach (var (action, primary, alternate) in bindings)
        {
            ValidateKey(action, primary);
            if (!usedKeys.Add(primary))
                throw new ArgumentException($"The {primary} key is assigned to more than one gameplay action.");
            if (alternate == Keys.None) continue;
            ValidateKey(action, alternate);
            if (!usedKeys.Add(alternate))
                throw new ArgumentException($"The {alternate} key is assigned to more than one gameplay action.");
        }

        return this with { };
    }

    /// <summary>Install these saved bindings in a fresh runtime action map.</summary>
    public void ApplyInputBindings(InputActionMap actionMap)
    {
        ArgumentNullException.ThrowIfNull(actionMap);
        var settings = ValidatedCopy();
        actionMap.Bind(GameplayActionNames.MoveForward, KeysFor(settings.MoveForward, settings.MoveForwardAlternate));
        actionMap.Bind(GameplayActionNames.MoveBackward, KeysFor(settings.MoveBackward, settings.MoveBackwardAlternate));
        actionMap.Bind(GameplayActionNames.MoveLeft, KeysFor(settings.MoveLeft, settings.MoveLeftAlternate));
        actionMap.Bind(GameplayActionNames.MoveRight, KeysFor(settings.MoveRight, settings.MoveRightAlternate));
        actionMap.Bind(GameplayActionNames.Jump, KeysFor(settings.Jump, settings.JumpAlternate));
    }

    private static Keys[] KeysFor(Keys primary, Keys alternate) =>
        alternate == Keys.None ? [primary] : [primary, alternate];

    private static void ValidateKey(string action, Keys key)
    {
        if (!Enum.IsDefined(key) || key == Keys.None)
            throw new ArgumentOutOfRangeException(action, key, "A gameplay action must use a valid keyboard key.");
    }
}

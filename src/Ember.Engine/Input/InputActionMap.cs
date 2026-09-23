using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ember.Input;

public static class GameplayActionNames
{
    public const string MoveForward = "MoveForward";
    public const string MoveBackward = "MoveBackward";
    public const string MoveLeft = "MoveLeft";
    public const string MoveRight = "MoveRight";
    public const string Jump = "Jump";
}

public readonly record struct InputActionState(bool IsDown, bool WasPressed);

/// <summary>Immutable action values sampled once from one keyboard frame.</summary>
public sealed class InputActionSnapshot
{
    private readonly IReadOnlyDictionary<string, InputActionState> _states;

    internal InputActionSnapshot(Dictionary<string, InputActionState> states) => _states = states;

    public InputActionState this[string actionName] =>
        _states.TryGetValue(actionName, out var state) ? state : default;

    /// <summary>Returns a normalized 2D direction from negative/positive X and Y actions.</summary>
    public Vector2 ReadVector2(string negativeX, string positiveX, string negativeY, string positiveY)
    {
        var value = new Vector2(
            (this[positiveX].IsDown ? 1f : 0f) - (this[negativeX].IsDown ? 1f : 0f),
            (this[positiveY].IsDown ? 1f : 0f) - (this[negativeY].IsDown ? 1f : 0f));
        if (value.LengthSquared() > 1f) value.Normalize();
        return value;
    }

    public Vector2 ReadMovement() => ReadVector2(
        GameplayActionNames.MoveLeft,
        GameplayActionNames.MoveRight,
        GameplayActionNames.MoveBackward,
        GameplayActionNames.MoveForward);
}

/// <summary>
/// Maps named actions to keys and samples edge-triggered values once per render frame. Inputs
/// captured by UI or held during focus loss stay suppressed until released.
/// </summary>
public sealed class InputActionMap
{
    private readonly Dictionary<string, Keys[]> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _previousDown = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Keys> _suppressedUntilReleased = new();

    public InputActionMap(bool bindDefaultGameplayActions = true)
    {
        if (!bindDefaultGameplayActions) return;
        Bind(GameplayActionNames.MoveForward, Keys.W, Keys.Up);
        Bind(GameplayActionNames.MoveBackward, Keys.S, Keys.Down);
        Bind(GameplayActionNames.MoveLeft, Keys.A, Keys.Left);
        Bind(GameplayActionNames.MoveRight, Keys.D, Keys.Right);
        Bind(GameplayActionNames.Jump, Keys.Space);
    }

    public IReadOnlyCollection<string> ActionNames => _bindings.Keys;

    public void Bind(string actionName, params Keys[] keys)
    {
        if (string.IsNullOrWhiteSpace(actionName))
            throw new ArgumentException("Action name is required.", nameof(actionName));
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0 || keys.Any(key => key == Keys.None))
            throw new ArgumentException("An action needs at least one non-None key.", nameof(keys));

        _bindings[actionName] = keys.Distinct().ToArray();
        _previousDown[actionName] = false;
        _pendingPresses.Remove(actionName);
    }

    /// <summary>Consumes a press once, retaining it until a fixed-step consumer reads it.</summary>
    public bool ConsumePressed(string actionName) => _pendingPresses.Remove(actionName);

    public InputActionSnapshot Sample(KeyboardState keyboard, bool windowFocused, bool uiCapturesKeyboard)
    {
        var readable = windowFocused && !uiCapturesKeyboard;
        var pressedKeys = keyboard.GetPressedKeys();
        if (!readable)
        {
            _pendingPresses.Clear();
            foreach (var key in pressedKeys) _suppressedUntilReleased.Add(key);
        }
        else
        {
            _suppressedUntilReleased.RemoveWhere(key => !keyboard.IsKeyDown(key));
        }

        var states = new Dictionary<string, InputActionState>(_bindings.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (action, keys) in _bindings)
        {
            var isDown = readable && keys.Any(key =>
                keyboard.IsKeyDown(key) && !_suppressedUntilReleased.Contains(key));
            var wasDown = _previousDown.GetValueOrDefault(action);
            var wasPressed = isDown && !wasDown;
            states.Add(action, new InputActionState(isDown, wasPressed));
            if (wasPressed) _pendingPresses.Add(action);
            _previousDown[action] = isDown;
        }

        return new InputActionSnapshot(states);
    }
}

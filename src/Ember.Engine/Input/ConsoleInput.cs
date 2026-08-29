using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace Ember.Input;

/// <summary>What the console asked the coordinator to do this frame.</summary>
public enum ConsoleAction
{
    None,
    Toggle,
    Close,
    Submit,
    Complete,
    HistoryUp,
    HistoryDown
}

/// <summary>
/// Typing, history and toggle for a developer console.
///
/// **This is the piece a different game would reuse unchanged.** Completing a word and
/// running a line stay with the game — this type only owns the buffer and which keys mean
/// edit vs submit. Game1 must not pass itself in.
/// </summary>
public sealed class ConsoleInput
{
    public const int MaxLength = 160;

    public string Buffer { get; set; } = string.Empty;
    public int HistoryCursor { get; set; } = -1;
    public bool Open { get; set; }

    public ConsoleAction Step(InputRouter input, KeyboardState keyboard)
    {
        if (input.Pressed(keyboard, Keys.OemTilde) || input.Pressed(keyboard, Keys.Oem8))
        {
            Open = !Open;
            return ConsoleAction.Toggle;
        }

        if (!Open) return ConsoleAction.None;

        if (input.Pressed(keyboard, Keys.Escape))
        {
            Open = false;
            return ConsoleAction.Close;
        }

        if (input.Pressed(keyboard, Keys.Enter)) return ConsoleAction.Submit;
        if (input.Pressed(keyboard, Keys.Tab)) return ConsoleAction.Complete;
        if (input.Pressed(keyboard, Keys.Up)) return ConsoleAction.HistoryUp;
        if (input.Pressed(keyboard, Keys.Down)) return ConsoleAction.HistoryDown;

        if (input.Pressed(keyboard, Keys.Back) && Buffer.Length > 0)
        {
            Buffer = Buffer[..^1];
            return ConsoleAction.None;
        }

        var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        foreach (var key in keyboard.GetPressedKeys())
        {
            if (input.WasDown(key)) continue;

            var character = CharacterFor(key, shift);
            if (character != '\0' && Buffer.Length < MaxLength) Buffer += character;
        }

        return ConsoleAction.None;
    }

    public void Clear()
    {
        Buffer = string.Empty;
        HistoryCursor = -1;
    }

    /// <summary>Walk the submitted-line history. Passing null is a no-op.</summary>
    public void WalkHistory(IReadOnlyList<string>? history, int direction)
    {
        if (history is null || history.Count == 0) return;

        if (HistoryCursor < 0) HistoryCursor = history.Count;
        HistoryCursor = Math.Clamp(HistoryCursor + direction, 0, history.Count);
        Buffer = HistoryCursor >= history.Count ? string.Empty : history[HistoryCursor];
    }

    /// <summary>What a key types. Only what a command line needs.</summary>
    public static char CharacterFor(Keys key, bool shift)
    {
        if (key is >= Keys.A and <= Keys.Z)
        {
            var letter = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpperInvariant(letter) : letter;
        }

        if (key is >= Keys.D0 and <= Keys.D9 && !shift) return (char)('0' + (key - Keys.D0));
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return (char)('0' + (key - Keys.NumPad0));

        return key switch
        {
            Keys.Space => ' ',
            Keys.OemPeriod or Keys.Decimal => '.',
            Keys.OemMinus or Keys.Subtract => '-',
            Keys.OemQuotes => '"',
            Keys.OemSemicolon => shift ? ':' : ';',
            Keys.OemComma => ',',
            _ => '\0'
        };
    }
}

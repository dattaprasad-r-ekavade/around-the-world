using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace Ember.Input;

/// <summary>Which pair of keys walks a list.</summary>
public enum ListAxis
{
    Vertical,
    Horizontal
}

/// <summary>
/// One frame of list navigation: the row that is selected, the row the pointer is over,
/// and whether a key moved the selection.
///
/// Confirming a row is a separate question — settings sliders click without "activating",
/// and a click that missed every row must not fire the current one.
/// </summary>
public readonly record struct ListPick(int Selection, int Hovered, bool KeyboardMoved)
{
    public bool Hovering => Hovered >= 0;

    public bool ClickedOnRow(InputRouter input, MouseState mouse) =>
        Hovering && input.Clicked(mouse);

    /// <summary>Enter, Space, or a click that landed on a row.</summary>
    public bool Confirmed(InputRouter input, KeyboardState keyboard, MouseState mouse) =>
        input.Pressed(keyboard, Keys.Enter)
        || input.Pressed(keyboard, Keys.Space)
        || ClickedOnRow(input, mouse);
}

/// <summary>
/// Wrap or clamp a selected row from an <see cref="InputRouter"/> snapshot.
///
/// **This is the piece a different game would reuse unchanged.** Row bounds are a callback,
/// not this game's <c>UiLayout</c>. The caller owns the selected index and what confirming
/// a row means.
/// </summary>
public static class ListPicker
{
    public static ListPick Step(
        int selection,
        InputRouter input,
        KeyboardState keyboard,
        MouseState mouse,
        Vector2 pointer,
        int count,
        Func<int, Rectangle> row,
        ListAxis axis = ListAxis.Vertical,
        bool wrap = true)
    {
        if (count <= 0) return new ListPick(0, -1, false);

        selection = Math.Clamp(selection, 0, count - 1);

        var prev = axis == ListAxis.Vertical ? Keys.Up : Keys.Left;
        var next = axis == ListAxis.Vertical ? Keys.Down : Keys.Right;
        var moved = false;

        if (input.Pressed(keyboard, prev))
        {
            selection = wrap ? (selection + count - 1) % count : Math.Max(0, selection - 1);
            moved = true;
        }

        if (input.Pressed(keyboard, next))
        {
            selection = wrap ? (selection + 1) % count : Math.Min(count - 1, selection + 1);
            moved = true;
        }

        var hovered = Hovered(pointer, count, row);
        if (hovered >= 0) selection = hovered;

        return new ListPick(selection, hovered, moved);
    }

    /// <summary>
    /// Walk a grid with arrows (and optionally WASD). Up/down jump by <paramref name="columns"/>.
    /// A cell callback may return null when that index is scrolled off-screen — hover skips it.
    /// </summary>
    public static ListPick StepGrid(
        int selection,
        InputRouter input,
        KeyboardState keyboard,
        MouseState mouse,
        Vector2 pointer,
        int count,
        int columns,
        Func<int, Rectangle?> cell,
        bool wrap = true,
        bool wasd = false)
    {
        if (count <= 0) return new ListPick(0, -1, false);

        columns = Math.Max(1, columns);
        selection = Math.Clamp(selection, 0, count - 1);
        var moved = false;

        if (PressedLeft(input, keyboard, wasd))
        {
            selection = wrap ? (selection + count - 1) % count : Math.Max(0, selection - 1);
            moved = true;
        }

        if (PressedRight(input, keyboard, wasd))
        {
            selection = wrap ? (selection + 1) % count : Math.Min(count - 1, selection + 1);
            moved = true;
        }

        if (PressedUp(input, keyboard, wasd))
        {
            selection = wrap
                ? (selection + count - columns) % count
                : Math.Max(0, selection - columns);
            moved = true;
        }

        if (PressedDown(input, keyboard, wasd))
        {
            selection = wrap
                ? (selection + columns) % count
                : Math.Min(count - 1, selection + columns);
            moved = true;
        }

        var hovered = Hovered(pointer, count, cell);
        if (hovered >= 0) selection = hovered;

        return new ListPick(selection, hovered, moved);
    }

    /// <summary>1–9 select the matching index, or -1. A second game uses this for any numbered list.</summary>
    public static int DigitIndex(InputRouter input, KeyboardState keyboard, int count)
    {
        var last = Math.Min(count, 9);
        for (var index = 0; index < last; index++)
            if (input.Pressed(keyboard, Keys.D1 + index)) return index;

        return -1;
    }

    public static int Hovered(Vector2 pointer, int count, Func<int, Rectangle> row) =>
        Hovered(pointer, count, index => (Rectangle?)row(index));

    public static int Hovered(Vector2 pointer, int count, Func<int, Rectangle?> row)
    {
        var point = new Point((int)pointer.X, (int)pointer.Y);
        for (var index = 0; index < count; index++)
            if (row(index) is { } bounds && bounds.Contains(point)) return index;

        return -1;
    }

    private static bool PressedLeft(InputRouter input, KeyboardState keyboard, bool wasd) =>
        input.Pressed(keyboard, Keys.Left) || (wasd && input.Pressed(keyboard, Keys.A));

    private static bool PressedRight(InputRouter input, KeyboardState keyboard, bool wasd) =>
        input.Pressed(keyboard, Keys.Right) || (wasd && input.Pressed(keyboard, Keys.D));

    private static bool PressedUp(InputRouter input, KeyboardState keyboard, bool wasd) =>
        input.Pressed(keyboard, Keys.Up) || (wasd && input.Pressed(keyboard, Keys.W));

    private static bool PressedDown(InputRouter input, KeyboardState keyboard, bool wasd) =>
        input.Pressed(keyboard, Keys.Down) || (wasd && input.Pressed(keyboard, Keys.S));
}

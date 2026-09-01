using Microsoft.Xna.Framework;

namespace Ember.Ui;

/// <summary>
/// The logical canvas, and the few rectangles the engine's own renderers need.
///
/// **Owned by this repository, not synced.** The game this engine came out of has a class of
/// the same name holding a rectangle for every panel it has — the shop grid, the depth choice,
/// the inventory tiles. None of that belongs to a framework, so it is deliberately left
/// behind; see tools/sync-from-ratnabay.ps1.
///
/// The pattern is worth taking even though the numbers are not: **one table of rectangles that
/// both the renderer and the hit test read.** A clickable row that is not exactly the row on
/// screen is how a menu starts ignoring the mouse, and the way that happens is one set of
/// numbers in the drawing code and another in the input code, drifting apart one edit at a
/// time. Write your own UiLayout for your own screens and let nothing else hold a rectangle.
/// </summary>
public static class UiLayout
{
    /// <summary>
    /// The canvas everything is laid out on, in logical pixels.
    ///
    /// Layout is written against this size and scaled to whatever the display is, so a panel
    /// is placed once rather than recomputed per resolution. Change it here if your game wants
    /// a different working size, and pass the same numbers to the UiCanvas constructor.
    /// </summary>
    public const int Width = 1280;

    public const int Height = 720;

    public static Rectangle FullScreen => new(0, 0, Width, Height);

    /// <summary>The default single prompt strip, low and centred: "press E to open".</summary>
    public static Rectangle SinglePrompt => new(340, 478, 600, 40);

    /// <summary>Text inset for a prompt chip. Kept here so every chip insets alike.</summary>
    public static Vector2 PromptText(Rectangle prompt) => new(prompt.X + 16, prompt.Y + 12);

    /// <summary>How much width that text has once both insets are taken off.</summary>
    public static float PromptTextWidth(Rectangle prompt) => prompt.Width - 32;
}

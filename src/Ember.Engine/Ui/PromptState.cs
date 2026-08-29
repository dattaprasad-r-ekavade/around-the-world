using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Ember.Ui;

/// <summary>
/// Why a prompt chip is on screen. The renderer maps this to a colour; Game1 only decides
/// what the player can do.
/// </summary>
public enum PromptRole
{
    /// <summary>A door, a shaft, a stall — the ordinary "press E" verb.</summary>
    Interact,

    /// <summary>Talking to someone, or taking something they left.</summary>
    Talk,

    /// <summary>A pocket worth picking. Distinct so it is not mistaken for talking.</summary>
    Pocket,

    /// <summary>The way is shut until the room is clear.</summary>
    Barred
}

/// <summary>One chip of the world prompt: a talk line, a shop, a pocket, a door.</summary>
public readonly record struct PromptChip(
    string Line,
    Rectangle Bounds,
    PromptRole Role,
    bool Fit = false);

/// <summary>
/// What the player can do with whatever is under the crosshair.
///
/// Built by Game1 because it has to look at the world, the session and the run. The renderer
/// only paints the chips it is given, so a change to how a locked door is worded cannot
/// wander into FindDoor.
/// </summary>
public sealed record PromptState(IReadOnlyList<PromptChip> Chips)
{
    public static readonly PromptState Empty = new(Array.Empty<PromptChip>());

    public bool IsEmpty => Chips.Count == 0;
}

using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>One beat of a conversation: who is speaking, what they say, and what you can say back.</summary>
public sealed record DialogueNode
{
    public string Id { get; init; } = "";

    public string Speaker { get; init; } = "";

    public string Text { get; init; } = "";

    public IReadOnlyList<DialogueOption> Options { get; init; } = Array.Empty<DialogueOption>();
}

/// <summary>
/// One reply. <see cref="Next"/> is the node it opens — null ends the conversation —
/// <see cref="Sets"/> are the flags saying it writes, and <see cref="Requires"/> are the
/// flags it is offered under: every condition must hold, and an option with no conditions is
/// always on offer.
/// </summary>
public sealed record DialogueOption
{
    /// <summary>What the player picks. Presentation is the game's business; this is the words.</summary>
    public string Label { get; init; } = "";

    public string? Next { get; init; }

    public IReadOnlyList<FlagCondition> Requires { get; init; } = Array.Empty<FlagCondition>();

    /// <summary>Flags written when this option is taken, as plain values: true, 120, "text".</summary>
    public IReadOnlyDictionary<string, FlagValue> Sets { get; init; } = new Dictionary<string, FlagValue>();
}

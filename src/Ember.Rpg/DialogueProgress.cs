using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>
/// Where a conversation has got to: which tree is open and which node is showing. Both null
/// when nobody is talking.
///
/// The tree itself is content, loaded from JSON and shared; only this — the player's place in
/// it — travels in the save, alongside the flags the conversation has been writing.
/// </summary>
public sealed class DialogueProgress
{
    public ContentId<DialogueContentKind>? Tree { get; set; }

    public string? Node { get; set; }

    /// <summary>Choice keys already executed in this save; protects self-looping options from replay.</summary>
    public List<string> TakenChoiceIds { get; set; } = new();
}

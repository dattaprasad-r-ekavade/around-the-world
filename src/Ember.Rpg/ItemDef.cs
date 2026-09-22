namespace Ember.Rpg;

/// <summary>
/// One item kind, as data: what it is called, where it can be worn, and whether identical
/// ones share a stack.
///
/// Definitions are game content — this type only carries them. <see cref="Slot"/> is a game's
/// vocabulary ("head", "mainhand"), null or empty for anything that cannot be equipped.
/// </summary>
public sealed record ItemDef(string Id, string Name, string? Slot, bool Stackable);

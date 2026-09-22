namespace Ember.Rpg;

/// <summary>
/// The player as data: the bag they carry and what they have equipped.
///
/// A record so a save can carry it like the rest of <see cref="SaveState"/>, and so nothing
/// in the engine or the RPG layer needs to know what a player is beyond these two things.
/// </summary>
public sealed record PlayerRecord
{
    public Bag Bag { get; init; } = new();

    public EquipSlots Equip { get; init; } = new();
}

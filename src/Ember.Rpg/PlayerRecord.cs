using System.Text.Json.Serialization;
using System.IO;

namespace Ember.Rpg;

/// <summary>
/// The player as data: the bag they carry and what they have equipped.
///
/// A record so a save can carry it like the rest of <see cref="SaveState"/>, and so nothing
/// in the engine or the RPG layer needs to know what a player is beyond these two things.
/// </summary>
public sealed record PlayerRecord
{
    public long Currency { get; init; }

    public Bag Bag { get; init; } = new();

    public EquipSlots Equip { get; init; } = new();

    public ActorStats Stats { get; init; } = new();

    public ActorStatModifiers Modifiers { get; init; } = new();

    [JsonIgnore]
    public ActorStats EffectiveStats => Stats.WithModifiers(Modifiers);

    public void Validate()
    {
        if (Currency < 0) throw new InvalidDataException("Player currency cannot be negative.");
        if (Bag is null || Equip is null || Stats is null || Modifiers is null)
            throw new InvalidDataException("Player inventory, equipment, stats, and modifiers are required.");
        if (Modifiers.Active is null) throw new InvalidDataException("Player modifier list is required.");
        Stats.Validate();
        foreach (var modifier in Modifiers.Active) modifier.Validate();
    }
}

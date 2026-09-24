using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>
/// One entity as data: an id, a kind, and string fields.
///
/// A record, so a change is <c>entity with { ... }</c> rather than a mutable object two
/// systems fight over. Fields are strings on purpose: parsing a number, clamping a health or
/// deciding what "hp" even means is a game rule, and the save format is not in the business
/// of anyone's rules but the game's.
/// </summary>
public sealed record EntityRecord(string Id, string Kind, IReadOnlyDictionary<string, string> Fields)
{
    /// <summary>Content actor definition for this saved world instance, when it is an actor.</summary>
    public ContentId<ActorContentKind>? ActorId { get; init; }

    public static EntityRecord Create(string id, string kind) =>
        new(id, kind, new Dictionary<string, string>());

    /// <summary>A new record with <paramref name="name"/> set. This one is left unchanged.</summary>
    public EntityRecord WithField(string name, string value)
    {
        var fields = new Dictionary<string, string>(Fields) { [name] = value };
        return this with { Fields = fields };
    }

    public string Field(string name, string fallback = "") =>
        Fields.TryGetValue(name, out var value) ? value : fallback;
}

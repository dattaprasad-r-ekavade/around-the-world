namespace Ember.Rpg;

/// <summary>
/// One item kind, as data: what it is called, where it can be worn, and whether identical
/// ones share a stack.
///
/// Definitions are game content — this type only carries them. <see cref="Slot"/> is a game's
/// vocabulary ("head", "mainhand"), null or empty for anything that cannot be equipped.
/// </summary>
public sealed record ItemDef
{
    public ContentId<ItemContentKind> Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Slot { get; init; }
    public bool Stackable { get; init; }

    public ItemDef() { }

    public ItemDef(ContentId<ItemContentKind> id, string name, string? slot, bool stackable)
    {
        Id = id;
        Name = name;
        Slot = slot;
        Stackable = stackable;
    }

    public ItemDef(string Id, string Name, string? Slot, bool Stackable)
        : this(new ContentId<ItemContentKind>(Id), Name, Slot, Stackable) { }
}

using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>
/// Every item kind the game knows, by id.
///
/// Carried inside the save so a save describes its own items: load an old file after the
/// game renamed a potion and the bag still says what it was holding. Upsert on
/// <see cref="Add"/> — last definition wins, so a game can load its base catalogue and then
/// a patch file over the top of it.
/// </summary>
public sealed class ItemCatalogue
{
    private readonly Dictionary<string, ItemDef> _defs = new(StringComparer.Ordinal);

    /// <summary>Every definition, by id. Read it; use <see cref="Add"/> to change it.</summary>
    public IReadOnlyDictionary<string, ItemDef> All => _defs;

    public int Count => _defs.Count;

    public void Add(ItemDef def) => _defs[def.Id] = def;

    public bool Has(string id) => _defs.ContainsKey(id);

    public bool TryGet(string id, out ItemDef def)
    {
        if (_defs.TryGetValue(id, out var found))
        {
            def = found;
            return true;
        }

        def = null!;
        return false;
    }

    /// <summary>The definition, or null if nothing wears this id.</summary>
    public ItemDef? Get(string id) => _defs.TryGetValue(id, out var def) ? def : null;
}

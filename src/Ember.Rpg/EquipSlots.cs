using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>
/// What is worn: slot name to item id. The slot vocabulary is the game's ("head",
/// "mainhand"); the store only keeps the mapping.
///
/// Slots are independent of the bag — moving an item between them is the game's rule, not
/// the store's, so nothing here empties or fills anything else behind your back.
/// </summary>
public sealed class EquipSlots
{
    private readonly Dictionary<string, string> _slots = new(StringComparer.Ordinal);

    /// <summary>Slot name to item id, for iteration and the save converter. Use <see cref="Set"/> to change it.</summary>
    public IReadOnlyDictionary<string, string> All => _slots;

    public int Count => _slots.Count;

    public bool Has(string slot) => _slots.ContainsKey(slot);

    /// <summary>The item in this slot, or the fallback when the slot is empty.</summary>
    public string Get(string slot, string fallback = "") =>
        _slots.TryGetValue(slot, out var itemId) ? itemId : fallback;

    public void Set(string slot, string itemId) => _slots[slot] = itemId;

    public bool Remove(string slot) => _slots.Remove(slot);

    public void Clear() => _slots.Clear();

    /// <summary>A detached copy for equipment changes that must not partially mutate a player save.</summary>
    public EquipSlots Copy()
    {
        var copy = new EquipSlots();
        foreach (var (slot, itemId) in _slots) copy.Set(slot, itemId);
        return copy;
    }
}

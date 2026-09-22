using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>What is in the bag: an item id and how many of it.</summary>
public sealed record BagEntry(string ItemId, int Count);

/// <summary>
/// The bag: item ids and counts, nothing more.
///
/// Stackable kinds merge into one entry; non-stackable kinds get an entry each, so two iron
/// swords are two entries rather than one sword times two. Which kinds stack is the
/// definition's call (<see cref="ItemDef.Stackable"/>), passed in at the moment of the add —
/// the bag never looks anything up itself.
///
/// Counts only; there are no item instances with per-item state here. A game that needs a
/// sword with its own history puts that sword on an entity record.
/// </summary>
public sealed class Bag
{
    private readonly List<BagEntry> _entries = new();

    /// <summary>Every entry, in the order it was added. Read it; use <see cref="Add"/>/<see cref="Remove"/> to change it.</summary>
    public IReadOnlyList<BagEntry> Entries => _entries;

    public int Kinds => _entries.Count;

    /// <summary>How many of this item the bag holds, across all of its entries.</summary>
    public int Count(string itemId)
    {
        var total = 0;
        foreach (var entry in _entries)
            if (string.Equals(entry.ItemId, itemId, StringComparison.Ordinal))
                total += entry.Count;
        return total;
    }

    public bool Has(string itemId, int count = 1) => Count(itemId) >= count;

    public void Add(ItemDef def, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Add at least one.");

        if (def.Stackable)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (!string.Equals(_entries[i].ItemId, def.Id, StringComparison.Ordinal)) continue;

                _entries[i] = _entries[i] with { Count = _entries[i].Count + count };
                return;
            }
        }

        // Non-stackable (or first of its kind): one entry per add, never merged.
        _entries.Add(new BagEntry(def.Id, count));
    }

    /// <summary>
    /// Take up to <paramref name="count"/> of an item. False, and nothing changed, when the
    /// bag does not hold that many — a half-taken remove is worse than a refused one.
    /// </summary>
    public bool Remove(string itemId, int count = 1)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Remove at least one.");
        if (Count(itemId) < count) return false;

        var remaining = count;
        for (var i = _entries.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (!string.Equals(_entries[i].ItemId, itemId, StringComparison.Ordinal)) continue;

            var entry = _entries[i];
            if (entry.Count <= remaining)
            {
                remaining -= entry.Count;
                _entries.RemoveAt(i);
            }
            else
            {
                _entries[i] = entry with { Count = entry.Count - remaining };
                remaining = 0;
            }
        }

        return true;
    }

    /// <summary>
    /// Empty the bag. For rewrite paths that rebuild from gear lists on every save — clear,
    /// then Add, rather than merging into whatever was already there.
    /// </summary>
    public void Clear() => _entries.Clear();

    /// <summary>Load path only: put an entry back exactly as written — order, count, no stacking policy.</summary>
    internal void Restore(BagEntry entry) => _entries.Add(entry);
}

using System.Collections.Generic;
using Ember.Rpg;

namespace Campaign;

/// <summary>
/// The seam between Campaign's save — gold as an int, OwnedGear as gear indices — and an
/// Ember.Rpg SaveState: gold as a flag, gear as bag entries over item definitions.
/// SaveFile now carries the full SaveState in SaveData.Rpg; these methods seed a fresh
/// state from classic fields (first load, or a pre-RPG save file).
/// </summary>
public static class RpgAdapter
{
    /// <summary>Campaign's gold and owned gear into the RPG save's flag store and bag.</summary>
    public static void ToRpg(SaveState save, SaveData data)
    {
        save.Flags.Set("gold", data.Gold);
        EnsureDefs(save);
        save.Player.Bag.Clear();

        foreach (var id in data.OwnedGear)
        {
            var itemId = Gear.Of(id).Id;
            if (itemId.Length == 0) continue;   // Gear.None and anything out of range: not an item.
            save.Player.Bag.Add(save.ItemDefs.Get(itemId)!);
        }
    }

    /// <summary>The RPG save's flag store and bag back into Campaign's gold and owned gear.</summary>
    public static void FromRpg(SaveData data, SaveState save)
    {
        data.Gold = (int)save.Flags.GetNumber("gold");
        data.OwnedGear = OwnedGear(save.Player.Bag);
    }

    /// <summary>Every gear kind as an item definition, so the bag can hold it. Upsert, so calling it twice is harmless.</summary>
    public static void EnsureDefs(SaveState save)
    {
        foreach (var gear in Gear.All)
            if (gear.Id.Length > 0)
                save.ItemDefs.Add(new ItemDef(gear.Id, gear.Name, gear.Slot, Stackable: false));
    }

    /// <summary>
    /// Bag entries that name a gear id, back to gear ids. Order kept, gear never owned twice,
    /// and anything in the bag that is not gear is left alone.
    /// </summary>
    private static int[] OwnedGear(Bag bag)
    {
        var ids = new List<int>();
        foreach (var entry in bag.Entries)
        {
            var id = Gear.FromShop(entry.ItemId);
            if (id == Gear.None) continue;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return ids.ToArray();
    }
}

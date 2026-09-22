using System;
using System.Collections.Generic;
using System.IO;
using Ember.Rpg;

namespace Ember.Rpg.Check;

/// <summary>
/// Save, load, and compare against the original. Exits 0 only when they are equal — so it
/// can sit in a build step the day someone changes the format.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var original = Original();
        var path = Path.Combine(Path.GetTempPath(), $"ember-rpg-check-{Guid.NewGuid():N}.json");

        try
        {
            original.Write(path);
            var loaded = SaveState.Read(path);

            var problems = new List<string>();
            Compare(original, loaded, problems);

            if (loaded.Flags.GetBool("met_elder") != true)
                problems.Add("GetBool(met_elder) should be true after a load");
            if (loaded.Flags.GetNumber("gold") != 120d)
                problems.Add("GetNumber(gold) should be 120 after a load");
            if (loaded.Flags.GetText("place") != "oak_hall")
                problems.Add("GetText(place) should be 'oak_hall' after a load");
            if (loaded.Entities[0].Field("name") != "Rowan")
                problems.Add("elder_01 field 'name' should be 'Rowan' after a load");

            // The inventory test: an item was added before the save; it must still be there,
            // still stackable, still named, and still worn after the load.
            if (loaded.Player.Bag.Count("potion_heal") != 3)
                problems.Add($"bag should hold 3 potion_heal after a load, holds {loaded.Player.Bag.Count("potion_heal")}");
            if (loaded.Player.Bag.Count("sword_iron") != 1)
                problems.Add("bag should hold 1 sword_iron after a load");
            if (loaded.Player.Equip.Get("mainhand") != "sword_iron")
                problems.Add("mainhand should still hold sword_iron after a load");
            if (loaded.ItemDefs.Get("potion_heal") is not { Name: "Healing Potion", Stackable: true, Slot: null })
                problems.Add("potion_heal definition should survive the load unchanged");

            if (problems.Count == 0)
            {
                Console.WriteLine("[OK] save then load equals original");
                return 0;
            }

            foreach (var problem in problems) Console.Error.WriteLine("  " + problem);
            Console.Error.WriteLine("[FAIL] save then load does not equal the original");
            return 1;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static SaveState Original()
    {
        var state = new SaveState
        {
            Entities = new[]
            {
                EntityRecord.Create("elder_01", "npc")
                    .WithField("name", "Rowan")
                    .WithField("hp", "12"),
                EntityRecord.Create("gate_01", "prop")
                    .WithField("state", "shut")
            },
            Flags = Flags()
        };

        state.ItemDefs.Add(new ItemDef("potion_heal", "Healing Potion", Slot: null, Stackable: true));
        state.ItemDefs.Add(new ItemDef("sword_iron", "Iron Sword", Slot: "mainhand", Stackable: false));

        // Add, then the save happens around it — this is the sequence the check exists for.
        state.Player.Bag.Add(state.ItemDefs.Get("potion_heal")!, 3);
        state.Player.Bag.Add(state.ItemDefs.Get("sword_iron")!);
        state.Player.Equip.Set("mainhand", "sword_iron");

        return state;
    }

    private static FlagStore Flags()
    {
        var flags = new FlagStore();
        flags.Set("met_elder", true);
        flags.Set("gate_locked", false);
        flags.Set("gold", 120);
        flags.Set("moon", 0.5);
        flags.Set("place", "oak_hall");
        return flags;
    }

    private static void Compare(SaveState expected, SaveState actual, List<string> problems)
    {
        if (expected.Version != actual.Version)
            problems.Add($"Version {expected.Version} != {actual.Version}");

        if (expected.Entities.Count != actual.Entities.Count)
        {
            problems.Add($"Entity count {expected.Entities.Count} != {actual.Entities.Count}");
            return;
        }

        for (var i = 0; i < expected.Entities.Count; i++)
        {
            var want = expected.Entities[i];
            var got = actual.Entities[i];

            if (want.Id != got.Id) problems.Add($"Entity {i}: id {want.Id} != {got.Id}");
            if (want.Kind != got.Kind) problems.Add($"Entity {want.Id}: kind {want.Kind} != {got.Kind}");

            if (want.Fields.Count != got.Fields.Count)
            {
                problems.Add($"Entity {want.Id}: {got.Fields.Count} fields, expected {want.Fields.Count}");
                continue;
            }

            foreach (var (name, value) in want.Fields)
            {
                if (!got.Fields.TryGetValue(name, out var other))
                    problems.Add($"Entity {want.Id}: field '{name}' is missing");
                else if (other != value)
                    problems.Add($"Entity {want.Id}: field '{name}' '{value}' != '{other}'");
            }
        }

        if (expected.Flags.Count != actual.Flags.Count)
            problems.Add($"Flag count {expected.Flags.Count} != {actual.Flags.Count}");

        foreach (var (name, value) in expected.Flags.All)
        {
            if (!actual.Flags.TryGet(name, out var got))
                problems.Add($"Flag '{name}' is missing");
            else if (!value.Equals(got))
                problems.Add($"Flag '{name}': {value} != {got}");
        }

        // Item definitions.
        if (expected.ItemDefs.Count != actual.ItemDefs.Count)
            problems.Add($"ItemDef count {expected.ItemDefs.Count} != {actual.ItemDefs.Count}");

        foreach (var (id, def) in expected.ItemDefs.All)
        {
            if (!actual.ItemDefs.TryGet(id, out var got))
                problems.Add($"ItemDef '{id}' is missing");
            else if (!def.Equals(got))
                problems.Add($"ItemDef '{id}': {def} != {got}");
        }

        // The bag, entry for entry — order and counts included.
        var wantBag = expected.Player.Bag.Entries;
        var gotBag = actual.Player.Bag.Entries;
        if (wantBag.Count != gotBag.Count)
        {
            problems.Add($"Bag has {gotBag.Count} entries, expected {wantBag.Count}");
        }
        else
        {
            for (var i = 0; i < wantBag.Count; i++)
            {
                if (wantBag[i].ItemId != gotBag[i].ItemId)
                    problems.Add($"Bag entry {i}: {wantBag[i].ItemId} != {gotBag[i].ItemId}");
                else if (wantBag[i].Count != gotBag[i].Count)
                    problems.Add($"Bag entry {i} ({wantBag[i].ItemId}): count {wantBag[i].Count} != {gotBag[i].Count}");
            }
        }

        // What is worn.
        if (expected.Player.Equip.Count != actual.Player.Equip.Count)
            problems.Add($"Equip has {actual.Player.Equip.Count} slots, expected {expected.Player.Equip.Count}");

        foreach (var (slot, itemId) in expected.Player.Equip.All)
        {
            if (!actual.Player.Equip.Has(slot))
                problems.Add($"Equip slot '{slot}' is missing");
            else if (actual.Player.Equip.Get(slot) != itemId)
                problems.Add($"Equip slot '{slot}': {itemId} != {actual.Player.Equip.Get(slot)}");
        }
    }
}

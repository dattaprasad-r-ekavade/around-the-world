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
        var problems = new List<string>();
        var path = Path.Combine(Path.GetTempPath(), $"ember-rpg-check-{Guid.NewGuid():N}.json");

        try
        {
            PlayDialogue(original, problems);
            QuestRoundTrip(original, path, problems);

            original.Write(path);
            var loaded = SaveState.Read(path);

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

            // The dialogue test, after the round trip: still on the node the pick advanced
            // to, and the flag the pick wrote is still set.
            if (loaded.Dialogue.Tree != "elder")
                problems.Add($"dialogue should still be in the 'elder' tree after a load, is '{loaded.Dialogue.Tree}'");
            if (loaded.Dialogue.Node != "gate")
                problems.Add($"dialogue should still be at 'gate' after a load, is '{loaded.Dialogue.Node}'");
            if (!loaded.Flags.GetBool("gate_topic"))
                problems.Add("gate_topic should still be set after a load");

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

    /// <summary>
    /// The dialogue test: load a conversation from JSON, then — given the flags the state
    /// already carries — take the gated option and let it advance the node and write its
    /// flag. The state is saved after this, so the load half of the test is Main's.
    /// </summary>
    private static void PlayDialogue(SaveState state, List<string> problems)
    {
        var tree = DialogueTree.FromJson(ElderDialogue);
        state.Dialogue.Tree = tree.Id;
        state.Dialogue.Node = "greet";

        if (tree.Available("greet", new FlagStore()).Count != 1)
            problems.Add("with no flags set, only the ungated option should be offered");

        var open = tree.Available("greet", state.Flags);
        if (open.Count != 2)
        {
            problems.Add($"with met_elder set, both options should be offered (got {open.Count})");
            return;
        }

        // open[1] is the gated option: Next = "gate", Sets = { gate_topic: true }.
        tree.Pick(state.Dialogue, state.Flags, open[1]);

        if (state.Dialogue.Node != "gate")
            problems.Add($"picking the gated option should advance to 'gate', not '{state.Dialogue.Node}'");
        if (!state.Flags.GetBool("gate_topic"))
            problems.Add("picking the gated option should write the gate_topic flag");
    }

    /// <summary>
    /// The quest test: load a quest from JSON, start it, walk its stages as the flags
    /// arrive, and prove a save taken mid-quest comes back at the same stage — then
    /// finish it and prove a save taken after completion still reads Complete.
    /// </summary>
    private static void QuestRoundTrip(SaveState state, string path, List<string> problems)
    {
        var catalogue = QuestCatalogue.FromJson(ElderQuest);
        if (catalogue.Count != 1 || !catalogue.TryGet("relic", out var quest))
        {
            problems.Add("the quest document should load one quest with id 'relic'");
            return;
        }

        if (quest.StatusIn(state.Flags) != QuestStatus.NotStarted)
            problems.Add("relic should be NotStarted before Start");

        quest.Start(state.Flags);
        if (quest.StatusIn(state.Flags) != QuestStatus.Active)
            problems.Add("relic should be Active after Start");
        if (quest.StageIn(state.Flags)?.Id != "fetch")
            problems.Add("the first stage should be 'fetch'");

        // Mid-quest save: stage one is still open, stage two has not begun.
        state.Write(path);
        var mid = SaveState.Read(path);
        var midStage = quest.StageIn(mid.Flags);
        if (midStage?.Id != "fetch")
            problems.Add($"a mid-quest load should still be on stage 'fetch', is '{midStage?.Id}'");

        // Arrive at the first stage's completion flag — the stage advances with no
        // quest write, because progress is pure derivation.
        mid.Flags.Set("relic_taken", true);
        if (quest.StageIn(mid.Flags)?.Id != "deliver")
            problems.Add("with relic_taken set, the stage should advance to 'deliver'");

        // Finish the quest.
        mid.Flags.Set("relic_delivered", true);
        if (quest.StatusIn(mid.Flags) != QuestStatus.Complete)
            problems.Add("relic should be Complete once both stages are done");
        if (quest.StageIn(mid.Flags) is not null)
            problems.Add("StageIn should be null when the quest is Complete");

        // Completed-quest save still reads Complete after a round trip.
        mid.Write(path);
        var done = SaveState.Read(path);
        if (quest.StatusIn(done.Flags) != QuestStatus.Complete)
            problems.Add("a completed quest should still be Complete after a load");
        if (!done.Flags.GetBool("quest.relic.started"))
            problems.Add("the start flag should survive the load");
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

    /// <summary>A quest loaded from JSON by the check: two stages, flag-gated completion.</summary>
    private const string ElderQuest = """
    {
      "Quests":
      [
        {
          "Id": "relic",
          "Title": "The Elder's Relic",
          "Stages":
          [
            {
              "Id": "fetch",
              "Journal": "Recover the relic from the old vault.",
              "DoneWhen": [ { "Flag": "relic_taken", "Bool": true } ]
            },
            {
              "Id": "deliver",
              "Journal": "Bring the relic back to the elder.",
              "DoneWhen": [ { "Flag": "relic_delivered", "Bool": true } ]
            }
          ]
        }
      ]
    }
    """;

    /// <summary>A conversation loaded from JSON by the check: one gated option, one flag written.</summary>
    private const string ElderDialogue = """
    {
      "Id": "elder",
      "Nodes":
      [
        {
          "Id": "greet",
          "Speaker": "Rowan",
          "Text": "Well met, wanderer.",
          "Options":
          [
            { "Label": "Who are you?", "Next": "who" },
            {
              "Label": "Ask about the gate.",
              "Next": "gate",
              "Requires": [ { "Flag": "met_elder", "Bool": true } ],
              "Sets": { "gate_topic": true }
            }
          ]
        },
        {
          "Id": "who",
          "Speaker": "Rowan",
          "Text": "Rowan, keeper of this hall.",
          "Options": [ { "Label": "Farewell.", "Next": null } ]
        },
        {
          "Id": "gate",
          "Speaker": "Rowan",
          "Text": "Shut since the winter. Why do you ask?",
          "Options": [ { "Label": "Farewell.", "Next": null } ]
        }
      ]
    }
    """;

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

        // Where the conversation has got to.
        if (expected.Dialogue.Tree != actual.Dialogue.Tree)
            problems.Add($"Dialogue tree '{expected.Dialogue.Tree}' != '{actual.Dialogue.Tree}'");
        if (expected.Dialogue.Node != actual.Dialogue.Node)
            problems.Add($"Dialogue node '{expected.Dialogue.Node}' != '{actual.Dialogue.Node}'");
    }
}

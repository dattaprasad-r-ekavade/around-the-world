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

    private static SaveState Original() => new()
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
    }
}

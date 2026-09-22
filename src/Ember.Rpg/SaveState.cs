using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ember.Rpg;

/// <summary>
/// A save as data: the entities and the flags, and nothing else.
///
/// No inventory, no quests, no dialogue, no maps — those are a game's systems, built out of
/// these two (and out of the engine) rather than into this file's format. The format only has
/// to answer: who is here, and what is true right now.
/// </summary>
public sealed record SaveState
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new FlagStoreJson() }
    };

    /// <summary>
    /// Bumped when the shape of a save changes on purpose. What to do about an old version is
    /// the game's call; this type only carries the number.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>Who is here. Order is preserved on save and load; identity is the id, not the position.</summary>
    public IReadOnlyList<EntityRecord> Entities { get; init; } = Array.Empty<EntityRecord>();

    public FlagStore Flags { get; init; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static SaveState FromJson(string json) =>
        JsonSerializer.Deserialize<SaveState>(json, Json)
        ?? throw new JsonException("The save was empty.");

    public void Write(string path) => File.WriteAllText(path, ToJson());

    public static SaveState Read(string path) => FromJson(File.ReadAllText(path));
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace Ember.Rpg;

/// <summary>
/// A save as data: entities, flags, item definitions, player inventory and stats, persistent
/// container/world-item inventories, and where the conversation has got to — and nothing else.
///
/// No quests, no dialogue trees, no maps — those are a game's systems or content, built out
/// of these (and out of the engine) rather than into this file's format. The format only has
/// to answer: who is here, what is true, what exists to hold, what the player is carrying,
/// and which node of the talk they are on right now.
/// </summary>
public sealed record SaveState
{
    public const int CurrentVersion = 2;
    /// <summary>
    /// How a save is written and read. Public so a game embedding SaveState in its own file
    /// (Campaign's SaveFile) can reuse the same converters instead of rediscovering them.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new FlagStoreJson(),
            new ItemCatalogueJson(),
            new BagJson(),
            new EquipSlotsJson(),
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Bumped when the shape of a save changes on purpose. What to do about an old version is
    /// the game's call; this type only carries the number.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Monotonic simulation time in seconds, shared by schedules and timed effects.</summary>
    public double WorldTimeSeconds { get; init; }

    /// <summary>Schedule state keyed by stable world-instance ID.</summary>
    public NpcScheduleStore NpcSchedules { get; init; } = new();

    /// <summary>Who is here. Order is preserved on save and load; identity is the id, not the position.</summary>
    public IReadOnlyList<EntityRecord> Entities { get; init; } = Array.Empty<EntityRecord>();

    public FlagStore Flags { get; init; } = new();

    /// <summary>Every item kind the bags in this save can hold, so a save describes its own items.</summary>
    public ItemCatalogue ItemDefs { get; init; } = new();

    public PlayerRecord Player { get; init; } = new();

    /// <summary>Which conversation is open and which node is showing; both null when nobody is talking.</summary>
    public DialogueProgress Dialogue { get; init; } = new();

    /// <summary>Container bags keyed by the stable world-instance GUID value.</summary>
    public ContainerInventoryStore ContainerInventories { get; init; } = new();

    /// <summary>Loose world items keyed by the stable world-instance GUID value.</summary>
    public WorldItemStore WorldItems { get; init; } = new();

    /// <summary>Persistent actor health, death, cooldown, and inventory keyed by world instance.</summary>
    public ActorRuntimeStore ActorStates { get; init; } = new();

    public string ToJson()
    {
        if (Player is null) throw new InvalidDataException("Save state has no player record.");
        Validate();
        Player.Validate();
        ContainerInventories.Validate();
        WorldItems.Validate();
        ActorStates.Validate();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static SaveState FromJson(string json)
    {
        var state = JsonSerializer.Deserialize<SaveState>(json, JsonOptions)
            ?? throw new JsonException("The save was empty.");
        if (state.Version == 1)
            state = state with { Version = CurrentVersion };
        else if (state.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported RPG save version {state.Version}; expected 1 or {CurrentVersion}.");
        if (state.Player is null) throw new InvalidDataException("Save state has no player record.");
        state.Validate();
        state.Player.Validate();
        state.ContainerInventories.Validate();
        state.WorldItems.Validate();
        state.ActorStates.Validate();
        return state;
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("RPG save file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(ToJson());
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static SaveState Read(string path) => FromJson(File.ReadAllText(path));

    private void Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException($"RPG save version must be {CurrentVersion} before writing.");
        if (!double.IsFinite(WorldTimeSeconds) || WorldTimeSeconds < 0)
            throw new InvalidDataException("RPG save world time must be finite and nonnegative.");
        if (NpcSchedules is null) throw new InvalidDataException("RPG save has no NPC schedule store.");
        NpcSchedules.Validate();
    }
}

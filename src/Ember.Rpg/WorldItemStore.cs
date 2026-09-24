using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>One item stack represented by a stable world-instance GUID.</summary>
public sealed record WorldItemEntry(Guid WorldInstanceId, ContentId<ItemContentKind> ItemId, int Count);

/// <summary>Save-friendly item data keyed by the same GUID value as an engine world instance.</summary>
public sealed record WorldItemStore
{
    public IReadOnlyList<WorldItemEntry> Entries { get; init; } = Array.Empty<WorldItemEntry>();
    [JsonIgnore]
    public int Count => Entries.Count;

    public bool TryGet(Guid worldInstanceId, out WorldItemEntry entry)
    {
        foreach (var candidate in Entries)
            if (candidate.WorldInstanceId == worldInstanceId)
            {
                entry = candidate;
                return true;
            }
        entry = null!;
        return false;
    }

    public bool TryAdd(WorldItemEntry entry, out WorldItemStore updated)
    {
        ArgumentNullException.ThrowIfNull(entry);
        updated = this;
        if (!IsValid(entry) || TryGet(entry.WorldInstanceId, out _)) return false;
        var entries = new List<WorldItemEntry>(Entries) { entry };
        updated = new WorldItemStore { Entries = entries };
        return true;
    }

    public bool TryRemove(Guid worldInstanceId, out WorldItemStore updated)
    {
        updated = this;
        if (!TryGet(worldInstanceId, out _)) return false;
        var entries = new List<WorldItemEntry>(Entries.Count);
        foreach (var entry in Entries)
            if (entry.WorldInstanceId != worldInstanceId)
                entries.Add(entry);
        updated = new WorldItemStore { Entries = entries };
        return true;
    }

    public void Validate()
    {
        var ids = new HashSet<Guid>();
        foreach (var entry in Entries)
        {
            if (entry is null || !IsValid(entry))
                throw new InvalidDataException("World item entry has an empty instance/item ID or a non-positive count.");
            if (!ids.Add(entry.WorldInstanceId))
                throw new InvalidDataException($"World item store repeats instance {entry.WorldInstanceId}.");
        }
    }

    private static bool IsValid(WorldItemEntry entry) => entry.WorldInstanceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(entry.ItemId.Value) && entry.Count > 0;
}

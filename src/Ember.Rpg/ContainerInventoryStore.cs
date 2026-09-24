using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>Inventory contents owned by one stable world object identity (the engine ID's Guid value).</summary>
public sealed record ContainerInventoryEntry(Guid WorldInstanceId, Bag Contents);

/// <summary>
/// Persistent container contents keyed by world-instance GUID. Reads and writes copy bags so
/// editing a returned inventory cannot silently mutate the saved container record.
/// </summary>
public sealed record ContainerInventoryStore
{
    public IReadOnlyList<ContainerInventoryEntry> Entries { get; init; } = Array.Empty<ContainerInventoryEntry>();

    [JsonIgnore]
    public int Count => Entries.Count;

    public Bag? GetContents(Guid worldInstanceId)
    {
        foreach (var entry in Entries)
            if (entry.WorldInstanceId == worldInstanceId)
                return entry.Contents.Copy();
        return null;
    }

    public ContainerInventoryStore SetContents(Guid worldInstanceId, Bag contents)
    {
        ValidateWorldId(worldInstanceId);
        ArgumentNullException.ThrowIfNull(contents);
        var entries = new List<ContainerInventoryEntry>(Entries.Count + 1);
        var replaced = false;
        foreach (var entry in Entries)
        {
            if (entry.WorldInstanceId == worldInstanceId)
            {
                if (replaced) throw new InvalidDataException($"Container inventory repeats world instance {worldInstanceId}.");
                entries.Add(new ContainerInventoryEntry(worldInstanceId, contents.Copy()));
                replaced = true;
            }
            else entries.Add(entry);
        }
        if (!replaced) entries.Add(new ContainerInventoryEntry(worldInstanceId, contents.Copy()));
        return new ContainerInventoryStore { Entries = entries };
    }

    public ContainerInventoryStore Remove(Guid worldInstanceId)
    {
        ValidateWorldId(worldInstanceId);
        var entries = new List<ContainerInventoryEntry>(Entries.Count);
        foreach (var entry in Entries)
            if (entry.WorldInstanceId != worldInstanceId)
                entries.Add(entry);
        return entries.Count == Entries.Count ? this : new ContainerInventoryStore { Entries = entries };
    }

    public void Validate()
    {
        var ids = new HashSet<Guid>();
        foreach (var entry in Entries)
        {
            if (entry is null) throw new InvalidDataException("Container inventory record cannot be null.");
            if (entry.WorldInstanceId == Guid.Empty)
                throw new InvalidDataException("Container inventory world instance ID cannot be empty.");
            if (!ids.Add(entry.WorldInstanceId))
                throw new InvalidDataException($"Container inventory repeats world instance {entry.WorldInstanceId}.");
            if (entry.Contents is null) throw new InvalidDataException($"Container {entry.WorldInstanceId} has no contents bag.");
            foreach (var item in entry.Contents.Entries)
                if (string.IsNullOrWhiteSpace(item.ItemId.Value) || item.Count < 1)
                    throw new InvalidDataException($"Container {entry.WorldInstanceId} has an invalid item entry.");
        }
    }

    private static void ValidateWorldId(Guid worldInstanceId)
    {
        if (worldInstanceId == Guid.Empty)
            throw new ArgumentException("Container world instance ID cannot be empty.", nameof(worldInstanceId));
    }
}

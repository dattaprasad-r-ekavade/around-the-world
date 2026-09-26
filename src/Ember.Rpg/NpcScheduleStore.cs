using System;
using System.Collections.Generic;
using System.IO;

namespace Ember.Rpg;

/// <summary>Persistent NPC schedule state keyed by the engine's stable world-instance GUID.</summary>
public sealed record NpcScheduleEntry(Guid WorldInstanceId, NpcScheduleRuntimeState State);

public sealed record NpcScheduleStore
{
    public IReadOnlyList<NpcScheduleEntry> Entries { get; init; } = Array.Empty<NpcScheduleEntry>();

    public bool TryGet(Guid worldInstanceId, out NpcScheduleRuntimeState state)
    {
        foreach (var entry in Entries)
            if (entry.WorldInstanceId == worldInstanceId)
            {
                state = entry.State;
                return true;
            }
        state = null!;
        return false;
    }

    public NpcScheduleStore Set(Guid worldInstanceId, NpcScheduleRuntimeState state)
    {
        if (worldInstanceId == Guid.Empty)
            throw new ArgumentException("NPC schedule needs a nonempty world-instance ID.", nameof(worldInstanceId));
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();

        var entries = new List<NpcScheduleEntry>(Entries.Count + 1);
        var replaced = false;
        foreach (var entry in Entries)
        {
            if (entry.WorldInstanceId == worldInstanceId)
            {
                if (replaced)
                    throw new InvalidDataException($"NPC schedule repeats world instance {worldInstanceId}.");
                entries.Add(new NpcScheduleEntry(worldInstanceId, state with { }));
                replaced = true;
            }
            else entries.Add(entry);
        }
        if (!replaced) entries.Add(new NpcScheduleEntry(worldInstanceId, state with { }));
        return new NpcScheduleStore { Entries = entries };
    }

    public void Validate()
    {
        if (Entries is null) throw new InvalidDataException("NPC schedule entries are required.");
        var ids = new HashSet<Guid>();
        foreach (var entry in Entries)
        {
            if (entry is null || entry.WorldInstanceId == Guid.Empty || entry.State is null)
                throw new InvalidDataException("NPC schedule entry has an empty ID or missing runtime state.");
            entry.State.Validate();
            if (!ids.Add(entry.WorldInstanceId))
                throw new InvalidDataException($"NPC schedule repeats world instance {entry.WorldInstanceId}.");
        }
    }
}

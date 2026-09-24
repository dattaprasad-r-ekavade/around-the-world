using System;

namespace Ember.World;

public enum WorldEntityKind
{
    Actor,
    Item
}

/// <summary>A placed RPG definition with a stable ID reserved for its runtime instance state.</summary>
public sealed class WorldEntityPlacementComponent
{
    public WorldEntityPlacementComponent(WorldEntityKind kind, string definitionId, Guid instanceId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown world entity kind.");
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("A world entity definition ID is required.", nameof(definitionId));
        if (instanceId == Guid.Empty)
            throw new ArgumentException("World entity instance ID cannot be empty.", nameof(instanceId));

        Kind = kind;
        DefinitionId = definitionId;
        InstanceId = instanceId;
    }

    public WorldEntityKind Kind { get; }
    public string DefinitionId { get; }
    public Guid InstanceId { get; }
}

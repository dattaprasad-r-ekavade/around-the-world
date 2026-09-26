using System;

namespace Ember.World;

public enum WorldEntityKind
{
    Actor,
    Item
}

[Flags]
public enum PlacementTemplateOverrideFlags
{
    None = 0,
    Definition = 1,
    Position = 2,
    Rotation = 4,
    Scale = 8
}

/// <summary>A placed RPG definition with a stable ID reserved for its runtime instance state.</summary>
public sealed class WorldEntityPlacementComponent
{
    public WorldEntityPlacementComponent(WorldEntityKind kind, string definitionId, Guid instanceId,
        Guid? templateId = null, PlacementTemplateOverrideFlags templateOverrides = PlacementTemplateOverrideFlags.None)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown world entity kind.");
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException("A world entity definition ID is required.", nameof(definitionId));
        if (instanceId == Guid.Empty)
            throw new ArgumentException("World entity instance ID cannot be empty.", nameof(instanceId));
        if (templateId == Guid.Empty)
            throw new ArgumentException("Placement template ID cannot be empty.", nameof(templateId));
        const PlacementTemplateOverrideFlags allOverrides = PlacementTemplateOverrideFlags.Definition
            | PlacementTemplateOverrideFlags.Position | PlacementTemplateOverrideFlags.Rotation
            | PlacementTemplateOverrideFlags.Scale;
        if ((templateOverrides & ~allOverrides) != 0)
            throw new ArgumentOutOfRangeException(nameof(templateOverrides), templateOverrides,
                "Unknown placement-template override flags.");
        if (templateId is null && templateOverrides != PlacementTemplateOverrideFlags.None)
            throw new ArgumentException("Explicit template overrides need a template ID.", nameof(templateOverrides));

        Kind = kind;
        DefinitionId = definitionId;
        InstanceId = instanceId;
        TemplateId = templateId;
        TemplateOverrides = templateOverrides;
    }

    public WorldEntityKind Kind { get; }
    public string DefinitionId { get; }
    public Guid InstanceId { get; }
    public Guid? TemplateId { get; }
    public PlacementTemplateOverrideFlags TemplateOverrides { get; }
}

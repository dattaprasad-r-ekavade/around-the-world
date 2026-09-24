using System;
using System.Collections.Generic;

namespace Ember.Rpg;

public sealed record DialogueStatRequirement(ActorAttribute Attribute, double MinimumValue)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Attribute)) throw new ArgumentOutOfRangeException(nameof(Attribute));
        if (!double.IsFinite(MinimumValue) || MinimumValue < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumValue));
    }
}

public sealed record DialogueFactionRequirement(
    ContentId<FactionContentKind> FactionId,
    int MinimumReputation = int.MinValue,
    bool RequiresMembership = false);

/// <summary>The live values a dialogue choice is allowed to inspect.</summary>
public sealed record DialogueContext
{
    public FlagStore Flags { get; }
    public ActorStats Stats { get; }
    public IReadOnlyList<FactionStanding> Factions { get; }

    public DialogueContext(FlagStore flags, ActorStats stats, IReadOnlyList<FactionStanding>? factions = null)
    {
        Flags = flags ?? throw new ArgumentNullException(nameof(flags));
        Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        Factions = factions ?? Array.Empty<FactionStanding>();
    }

    public bool Meets(DialogueOption option)
    {
        foreach (var requirement in option.Requires)
            if (!requirement.Matches(Flags)) return false;
        foreach (var requirement in option.RequiresStats)
        {
            requirement.Validate();
            if (Stats.Attributes.Get(requirement.Attribute) < requirement.MinimumValue) return false;
        }
        foreach (var requirement in option.RequiresFactions)
        {
            var found = false;
            foreach (var standing in Factions)
                if (standing.FactionId == requirement.FactionId)
                {
                    found = (!requirement.RequiresMembership || standing.IsMember)
                        && standing.Reputation >= requirement.MinimumReputation;
                    break;
                }
            if (!found) return false;
        }
        return true;
    }
}

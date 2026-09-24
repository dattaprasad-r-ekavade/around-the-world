using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>One qualifying action and the number of uses required for a skill rank.</summary>
public sealed record SkillUseRule
{
    public string ActionId { get; init; } = string.Empty;
    public string SkillName { get; init; } = string.Empty;
    public int UsesPerRank { get; init; }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ActionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SkillName);
        if (UsesPerRank < 1) throw new ArgumentOutOfRangeException(nameof(UsesPerRank));
    }
}

/// <summary>Validated progression rules keyed by stable gameplay action IDs.</summary>
public sealed class SkillProgressionCatalogue
{
    private readonly Dictionary<string, SkillUseRule> _rules = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SkillUseRule> All => _rules;

    public void Add(SkillUseRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        if (!_rules.TryAdd(rule.ActionId, rule))
            throw new ArgumentException($"Skill progression repeats action '{rule.ActionId}'.", nameof(rule));
    }

    public bool TryGet(string actionId, out SkillUseRule rule) => _rules.TryGetValue(actionId, out rule!);
}

/// <summary>Deterministic use-based skill ranks. Only actions present in the data rules count.</summary>
public static class SkillUseSystem
{
    public static bool TryRecordUse(ActorStats stats, string actionId, SkillProgressionCatalogue rules,
        out ActorStats updated, out bool rankedUp)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(rules);
        stats.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        updated = stats;
        rankedUp = false;
        if (!rules.TryGet(actionId, out var rule)) return false;

        var rank = stats.Skills.GetValueOrDefault(rule.SkillName);
        var progress = stats.SkillUseProgress.GetValueOrDefault(rule.SkillName);
        var accumulated = (long)progress + 1;
        var ranksGained = accumulated / rule.UsesPerRank;
        if (ranksGained > int.MaxValue - (long)rank) return false;

        var nextSkills = new Dictionary<string, int>(stats.Skills, StringComparer.Ordinal);
        if (ranksGained > 0) nextSkills[rule.SkillName] = rank + (int)ranksGained;
        var nextProgress = new Dictionary<string, int>(stats.SkillUseProgress, StringComparer.Ordinal)
        {
            [rule.SkillName] = (int)(accumulated % rule.UsesPerRank)
        };
        updated = stats with { Skills = nextSkills, SkillUseProgress = nextProgress };
        rankedUp = ranksGained > 0;
        return true;
    }
}

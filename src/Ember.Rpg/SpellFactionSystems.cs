using System;
using System.Collections.Generic;
using System.Linq;

namespace Ember.Rpg;

/// <summary>A targeted attribute effect with explicit resource, reach, and cooldown costs.</summary>
public sealed record TargetedSpellDef
{
    public ContentId<SpellContentKind> Id { get; init; }
    public double Range { get; init; }
    public double MagickaCost { get; init; }
    public double CooldownSeconds { get; init; }
    public ActorAttribute Attribute { get; init; }
    public double Magnitude { get; init; }
    public double DurationSeconds { get; init; }
    public ModifierStackingRule StackingRule { get; init; } = ModifierStackingRule.ReplaceSameSource;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id.Value)) throw new ArgumentException("A spell needs an id.", nameof(Id));
        if (!double.IsFinite(Range) || Range <= 0) throw new ArgumentOutOfRangeException(nameof(Range));
        if (!double.IsFinite(MagickaCost) || MagickaCost <= 0) throw new ArgumentOutOfRangeException(nameof(MagickaCost));
        if (!double.IsFinite(CooldownSeconds) || CooldownSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(CooldownSeconds));
        if (!Enum.IsDefined(Attribute)) throw new ArgumentOutOfRangeException(nameof(Attribute));
        if (!double.IsFinite(Magnitude) || Magnitude == 0) throw new ArgumentOutOfRangeException(nameof(Magnitude));
        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(DurationSeconds));
        if (!Enum.IsDefined(StackingRule)) throw new ArgumentOutOfRangeException(nameof(StackingRule));
    }
}

public sealed class SpellCatalogue
{
    private readonly Dictionary<ContentId<SpellContentKind>, TargetedSpellDef> _spells = new();
    public IReadOnlyDictionary<ContentId<SpellContentKind>, TargetedSpellDef> All => _spells;
    public int Count => _spells.Count;

    public void Add(TargetedSpellDef spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        spell.Validate();
        _spells[spell.Id] = spell;
    }

    public bool TryGet(ContentId<SpellContentKind> id, out TargetedSpellDef spell) =>
        _spells.TryGetValue(id, out spell!);
}

/// <summary>Cost and cooldown are consumed only when the target effect can be applied.</summary>
public static class TargetedSpellSystem
{
    public static bool TryCast(Guid castId, ActorRuntimeState caster, ActorRuntimeState target,
        double distance, TargetedSpellDef spell,
        out ActorRuntimeState updatedCaster, out ActorRuntimeState updatedTarget)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(spell);
        caster.Validate();
        target.Validate();
        spell.Validate();
        updatedCaster = caster;
        updatedTarget = target;
        if (castId == Guid.Empty || caster.WorldInstanceId == target.WorldInstanceId
            || caster.IsDead || target.IsDead || !double.IsFinite(distance) || distance < 0
            || distance > spell.Range || caster.SpellCooldownRemaining > 0
            || caster.CurrentMagicka < spell.MagickaCost || caster.AppliedSpellCastIds.Contains(castId))
            return false;

        ActorStatModifiers modifiers;
        try
        {
            modifiers = target.Modifiers.Apply(new ActorStatModifier(
                $"cast:{castId:N}", $"spell:{spell.Id.Value}", spell.Attribute,
                spell.Magnitude, spell.DurationSeconds, spell.StackingRule));
        }
        catch (ArgumentException)
        {
            return false;
        }

        updatedCaster = caster with
        {
            CurrentMagicka = caster.CurrentMagicka - spell.MagickaCost,
            SpellCooldownRemaining = spell.CooldownSeconds,
            AppliedSpellCastIds = new List<Guid>(caster.AppliedSpellCastIds) { castId }
        };
        updatedTarget = target with { Modifiers = modifiers };
        return true;
    }

    public static ActorRuntimeState AdvanceSpellCooldown(ActorRuntimeState actor, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.Validate();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return actor with
        {
            SpellCooldownRemaining = Math.Max(0, actor.SpellCooldownRemaining - elapsedSeconds)
        };
    }
}

public sealed record FactionStanding(
    ContentId<FactionContentKind> FactionId, bool IsMember, int Reputation);

/// <summary>Immutable faction membership and reputation changes for one actor instance.</summary>
public static class FactionSystem
{
    public static int Reputation(ActorRuntimeState actor, ContentId<FactionContentKind> factionId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        foreach (var standing in actor.Factions)
            if (standing.FactionId == factionId) return standing.Reputation;
        return 0;
    }

    public static bool IsMember(ActorRuntimeState actor, ContentId<FactionContentKind> factionId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        foreach (var standing in actor.Factions)
            if (standing.FactionId == factionId) return standing.IsMember;
        return false;
    }

    public static ActorRuntimeState SetMembership(ActorRuntimeState actor,
        ContentId<FactionContentKind> factionId, bool isMember) =>
        Update(actor, factionId, standing => standing with { IsMember = isMember });

    public static ActorRuntimeState AdjustReputation(ActorRuntimeState actor,
        ContentId<FactionContentKind> factionId, int delta) =>
        Update(actor, factionId, standing => standing with { Reputation = checked(standing.Reputation + delta) });

    private static ActorRuntimeState Update(ActorRuntimeState actor,
        ContentId<FactionContentKind> factionId, Func<FactionStanding, FactionStanding> change)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(factionId.Value)) throw new ArgumentException("Faction id cannot be empty.", nameof(factionId));
        var next = new List<FactionStanding>(actor.Factions.Count + 1);
        var found = false;
        foreach (var standing in actor.Factions)
        {
            if (standing.FactionId == factionId)
            {
                if (found) throw new InvalidOperationException($"Actor state repeats faction '{factionId.Value}'.");
                next.Add(change(standing));
                found = true;
            }
            else next.Add(standing);
        }
        if (!found) next.Add(change(new FactionStanding(factionId, false, 0)));
        return actor with { Factions = next };
    }
}

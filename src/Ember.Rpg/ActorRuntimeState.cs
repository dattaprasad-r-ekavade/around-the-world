using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>Persistent state for one actor world instance.</summary>
public sealed record ActorRuntimeState
{
    public Guid WorldInstanceId { get; init; }
    public ContentId<ActorContentKind> ActorId { get; init; }
    public double CurrentHealth { get; init; }
    public double CurrentMagicka { get; init; }
    public bool IsDead { get; init; }
    public double MeleeCooldownRemaining { get; init; }
    public double SpellCooldownRemaining { get; init; }
    public Bag Inventory { get; init; } = new();
    public ActorStatModifiers Modifiers { get; init; } = new();
    public IReadOnlyList<FactionStanding> Factions { get; init; } = Array.Empty<FactionStanding>();
    public IReadOnlyList<Guid> AppliedSpellCastIds { get; init; } = Array.Empty<Guid>();

    public ActorRuntimeState() { }

    public ActorRuntimeState(Guid worldInstanceId, ContentId<ActorContentKind> actorId,
        double currentHealth, Bag? inventory = null, double meleeCooldownRemaining = 0,
        double currentMagicka = 0, double spellCooldownRemaining = 0)
    {
        WorldInstanceId = worldInstanceId;
        ActorId = actorId;
        CurrentHealth = currentHealth;
        CurrentMagicka = currentMagicka;
        IsDead = currentHealth <= 0;
        Inventory = inventory?.Copy() ?? new Bag();
        MeleeCooldownRemaining = meleeCooldownRemaining;
        SpellCooldownRemaining = spellCooldownRemaining;
        Validate();
    }

    public static ActorRuntimeState Create(Guid worldInstanceId, ActorDef actor, Bag? inventory = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.Stats.Validate();
        return new ActorRuntimeState(worldInstanceId, actor.Id, actor.Stats.MaximumHealth,
            inventory, currentMagicka: actor.Stats.MaximumMagicka);
    }

    public void Validate()
    {
        if (WorldInstanceId == Guid.Empty) throw new InvalidDataException("Actor world instance ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(ActorId.Value)) throw new InvalidDataException("Actor content ID cannot be empty.");
        if (!double.IsFinite(CurrentHealth) || CurrentHealth < 0 || IsDead != (CurrentHealth == 0))
            throw new InvalidDataException($"Actor {WorldInstanceId} has inconsistent health/death state.");
        if (!double.IsFinite(CurrentMagicka) || CurrentMagicka < 0)
            throw new InvalidDataException($"Actor {WorldInstanceId} has invalid magicka.");
        if (!double.IsFinite(MeleeCooldownRemaining) || MeleeCooldownRemaining < 0)
            throw new InvalidDataException($"Actor {WorldInstanceId} has an invalid melee cooldown.");
        if (!double.IsFinite(SpellCooldownRemaining) || SpellCooldownRemaining < 0)
            throw new InvalidDataException($"Actor {WorldInstanceId} has an invalid spell cooldown.");
        if (Inventory is null) throw new InvalidDataException($"Actor {WorldInstanceId} has no inventory.");
        if (Modifiers is null) throw new InvalidDataException($"Actor {WorldInstanceId} has no stat modifier set.");
        foreach (var modifier in Modifiers.Active) modifier.Validate();
        var factionIds = new HashSet<ContentId<FactionContentKind>>();
        foreach (var standing in Factions)
            if (standing is null || string.IsNullOrWhiteSpace(standing.FactionId.Value)
                || !factionIds.Add(standing.FactionId))
                throw new InvalidDataException($"Actor {WorldInstanceId} has an invalid or duplicate faction record.");
        var castIds = new HashSet<Guid>();
        foreach (var castId in AppliedSpellCastIds)
            if (castId == Guid.Empty || !castIds.Add(castId))
                throw new InvalidDataException($"Actor {WorldInstanceId} has an invalid or duplicate spell cast ID.");
        foreach (var item in Inventory.Entries)
            if (string.IsNullOrWhiteSpace(item.ItemId.Value) || item.Count < 1)
                throw new InvalidDataException($"Actor {WorldInstanceId} has an invalid inventory entry.");
    }

    internal ActorRuntimeState Copy() => this with
    {
        Inventory = Inventory.Copy(),
        Modifiers = new ActorStatModifiers { Active = new List<ActorStatModifier>(Modifiers.Active) },
        Factions = new List<FactionStanding>(Factions),
        AppliedSpellCastIds = new List<Guid>(AppliedSpellCastIds)
    };

    public ActorStats EffectiveStats(ActorStats baseStats)
    {
        ArgumentNullException.ThrowIfNull(baseStats);
        return baseStats.WithModifiers(Modifiers);
    }

    public ActorRuntimeState AdvanceTime(double elapsedSeconds)
    {
        Validate();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return this with
        {
            MeleeCooldownRemaining = Math.Max(0, MeleeCooldownRemaining - elapsedSeconds),
            SpellCooldownRemaining = Math.Max(0, SpellCooldownRemaining - elapsedSeconds),
            Modifiers = Modifiers.Advance(elapsedSeconds)
        };
    }
}

/// <summary>Save-friendly actor states keyed by stable world-instance identity.</summary>
public sealed record ActorRuntimeStore
{
    public IReadOnlyList<ActorRuntimeState> Entries { get; init; } = Array.Empty<ActorRuntimeState>();

    [JsonIgnore]
    public int Count => Entries.Count;

    public bool TryGet(Guid worldInstanceId, out ActorRuntimeState state)
    {
        foreach (var candidate in Entries)
            if (candidate.WorldInstanceId == worldInstanceId)
            {
                state = candidate.Copy();
                return true;
            }
        state = null!;
        return false;
    }

    public ActorRuntimeStore Set(ActorRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        var entries = new List<ActorRuntimeState>(Entries.Count + 1);
        var replaced = false;
        foreach (var entry in Entries)
        {
            if (entry.WorldInstanceId == state.WorldInstanceId)
            {
                if (replaced) throw new InvalidDataException($"Actor state repeats world instance {state.WorldInstanceId}.");
                entries.Add(state.Copy());
                replaced = true;
            }
            else entries.Add(entry);
        }
        if (!replaced) entries.Add(state.Copy());
        return new ActorRuntimeStore { Entries = entries };
    }

    public void Validate()
    {
        var ids = new HashSet<Guid>();
        foreach (var entry in Entries)
        {
            if (entry is null) throw new InvalidDataException("Actor runtime state cannot be null.");
            entry.Validate();
            if (!ids.Add(entry.WorldInstanceId))
                throw new InvalidDataException($"Actor state repeats world instance {entry.WorldInstanceId}.");
        }
    }
}

/// <summary>Data-driven reach, damage, and cooldown for one basic melee attack.</summary>
public sealed record MeleeAttackProfile
{
    public double Range { get; init; }
    public double Damage { get; init; }
    public double CooldownSeconds { get; init; }

    public MeleeAttackProfile() { }

    public MeleeAttackProfile(double range, double damage, double cooldownSeconds)
    {
        Range = range;
        Damage = damage;
        CooldownSeconds = cooldownSeconds;
        Validate();
    }

    public void Validate()
    {
        if (!double.IsFinite(Range) || Range <= 0) throw new ArgumentOutOfRangeException(nameof(Range));
        if (!double.IsFinite(Damage) || Damage <= 0) throw new ArgumentOutOfRangeException(nameof(Damage));
        if (!double.IsFinite(CooldownSeconds) || CooldownSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(CooldownSeconds));
    }
}

/// <summary>Deterministic, data-only melee rules. Invalid attempts change neither actor.</summary>
public static class MeleeCombat
{
    public static bool TryAttack(ActorRuntimeState attacker, ActorRuntimeState target,
        double distance, MeleeAttackProfile profile,
        out ActorRuntimeState updatedAttacker, out ActorRuntimeState updatedTarget)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(profile);
        attacker.Validate();
        target.Validate();
        profile.Validate();
        updatedAttacker = attacker;
        updatedTarget = target;
        if (!double.IsFinite(distance) || distance < 0 || distance > profile.Range
            || attacker.WorldInstanceId == target.WorldInstanceId
            || attacker.IsDead || target.IsDead || attacker.MeleeCooldownRemaining > 0)
            return false;

        updatedAttacker = attacker with { MeleeCooldownRemaining = profile.CooldownSeconds };
        var health = Math.Max(0, target.CurrentHealth - profile.Damage);
        updatedTarget = target with { CurrentHealth = health, IsDead = health == 0 };
        return true;
    }

    public static ActorRuntimeState AdvanceCooldown(ActorRuntimeState actor, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.Validate();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        return actor with { MeleeCooldownRemaining = Math.Max(0, actor.MeleeCooldownRemaining - elapsedSeconds) };
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

public enum ActorAttribute
{
    Strength,
    Intelligence,
    Willpower,
    Agility,
    Speed,
    Endurance,
    Personality,
    Luck
}

/// <summary>Base attributes. Derived maxima are recalculated from these values, never stored back here.</summary>
public sealed record ActorAttributes
{
    public double Strength { get; init; }
    public double Intelligence { get; init; }
    public double Willpower { get; init; }
    public double Agility { get; init; }
    public double Speed { get; init; }
    public double Endurance { get; init; }
    public double Personality { get; init; }
    public double Luck { get; init; }

    public double Get(ActorAttribute attribute) => attribute switch
    {
        ActorAttribute.Strength => Strength,
        ActorAttribute.Intelligence => Intelligence,
        ActorAttribute.Willpower => Willpower,
        ActorAttribute.Agility => Agility,
        ActorAttribute.Speed => Speed,
        ActorAttribute.Endurance => Endurance,
        ActorAttribute.Personality => Personality,
        ActorAttribute.Luck => Luck,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };

    public ActorAttributes With(ActorAttribute attribute, double value)
    {
        ValidateValue(value, nameof(value));
        return attribute switch
        {
            ActorAttribute.Strength => this with { Strength = value },
            ActorAttribute.Intelligence => this with { Intelligence = value },
            ActorAttribute.Willpower => this with { Willpower = value },
            ActorAttribute.Agility => this with { Agility = value },
            ActorAttribute.Speed => this with { Speed = value },
            ActorAttribute.Endurance => this with { Endurance = value },
            ActorAttribute.Personality => this with { Personality = value },
            ActorAttribute.Luck => this with { Luck = value },
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };
    }

    public void Validate()
    {
        ValidateValue(Strength, nameof(Strength));
        ValidateValue(Intelligence, nameof(Intelligence));
        ValidateValue(Willpower, nameof(Willpower));
        ValidateValue(Agility, nameof(Agility));
        ValidateValue(Speed, nameof(Speed));
        ValidateValue(Endurance, nameof(Endurance));
        ValidateValue(Personality, nameof(Personality));
        ValidateValue(Luck, nameof(Luck));
    }

    private static void ValidateValue(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Attributes must be finite and non-negative.");
    }
}

/// <summary>
/// Explicit, game-tunable formulas for derived maximum resources. Defaults are
/// Health = Strength + 2*Endurance, Magicka = 2*Intelligence + Willpower,
/// and Stamina = Endurance + 2*Agility.
/// </summary>
public sealed record ActorStatFormulas
{
    public double HealthStrength { get; init; } = 1;
    public double HealthEndurance { get; init; } = 2;
    public double HealthFlat { get; init; }
    public double MagickaIntelligence { get; init; } = 2;
    public double MagickaWillpower { get; init; } = 1;
    public double MagickaFlat { get; init; }
    public double StaminaEndurance { get; init; } = 1;
    public double StaminaAgility { get; init; } = 2;
    public double StaminaFlat { get; init; }

    public double MaximumHealth(ActorAttributes attributes) =>
        HealthFlat + attributes.Strength * HealthStrength + attributes.Endurance * HealthEndurance;
    public double MaximumMagicka(ActorAttributes attributes) =>
        MagickaFlat + attributes.Intelligence * MagickaIntelligence + attributes.Willpower * MagickaWillpower;
    public double MaximumStamina(ActorAttributes attributes) =>
        StaminaFlat + attributes.Endurance * StaminaEndurance + attributes.Agility * StaminaAgility;

    public void Validate()
    {
        ValidateCoefficient(HealthStrength, nameof(HealthStrength));
        ValidateCoefficient(HealthEndurance, nameof(HealthEndurance));
        ValidateCoefficient(HealthFlat, nameof(HealthFlat));
        ValidateCoefficient(MagickaIntelligence, nameof(MagickaIntelligence));
        ValidateCoefficient(MagickaWillpower, nameof(MagickaWillpower));
        ValidateCoefficient(MagickaFlat, nameof(MagickaFlat));
        ValidateCoefficient(StaminaEndurance, nameof(StaminaEndurance));
        ValidateCoefficient(StaminaAgility, nameof(StaminaAgility));
        ValidateCoefficient(StaminaFlat, nameof(StaminaFlat));
    }

    private static void ValidateCoefficient(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Formula coefficients must be finite and non-negative.");
    }
}

/// <summary>Base actor attributes and skills plus formula-derived maximum resources.</summary>
public sealed record ActorStats
{
    public ActorAttributes Attributes { get; init; } = new();
    public IReadOnlyDictionary<string, int> Skills { get; init; } = new Dictionary<string, int>();
    public ActorStatFormulas Formulas { get; init; } = new();

    [JsonIgnore]
    public double MaximumHealth => Formulas.MaximumHealth(Attributes);
    [JsonIgnore]
    public double MaximumMagicka => Formulas.MaximumMagicka(Attributes);
    [JsonIgnore]
    public double MaximumStamina => Formulas.MaximumStamina(Attributes);

    public ActorStats WithAttribute(ActorAttribute attribute, double value) =>
        this with { Attributes = Attributes.With(attribute, value) };

    public ActorStats WithModifiers(ActorStatModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        return this with { Attributes = modifiers.ApplyTo(Attributes) };
    }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Attributes);
        ArgumentNullException.ThrowIfNull(Formulas);
        Attributes.Validate();
        Formulas.Validate();
        foreach (var (name, value) in Skills)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A skill needs a name.", nameof(Skills));
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(Skills), value, $"Skill '{name}' cannot be negative.");
        }
    }
}

public enum ModifierStackingRule
{
    Stack,
    ReplaceSameSource,
    RefreshDurationSameSource
}

/// <summary>A timed additive modifier to one base attribute.</summary>
public sealed record ActorStatModifier
{
    public string Id { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public ActorAttribute Attribute { get; init; }
    public double Amount { get; init; }
    public double RemainingSeconds { get; init; }
    public ModifierStackingRule StackingRule { get; init; } = ModifierStackingRule.Stack;

    public ActorStatModifier() { }

    public ActorStatModifier(string id, string source, ActorAttribute attribute, double amount,
        double durationSeconds, ModifierStackingRule stackingRule = ModifierStackingRule.Stack)
    {
        Id = id;
        Source = source;
        Attribute = attribute;
        Amount = amount;
        RemainingSeconds = durationSeconds;
        StackingRule = stackingRule;
        Validate();
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source);
        if (!Enum.IsDefined(Attribute)) throw new ArgumentOutOfRangeException(nameof(Attribute));
        if (!double.IsFinite(Amount)) throw new ArgumentOutOfRangeException(nameof(Amount));
        if (!double.IsFinite(RemainingSeconds) || RemainingSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(RemainingSeconds), "A timed modifier needs a finite duration greater than zero.");
        if (!Enum.IsDefined(StackingRule)) throw new ArgumentOutOfRangeException(nameof(StackingRule));
    }
}

/// <summary>Immutable active effects with deterministic stack, replace, refresh, and expiry rules.</summary>
public sealed record ActorStatModifiers
{
    public IReadOnlyList<ActorStatModifier> Active { get; init; } = Array.Empty<ActorStatModifier>();

    public ActorStatModifiers Apply(ActorStatModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        modifier.Validate();
        var next = new List<ActorStatModifier>(Active);
        var matching = -1;
        if (modifier.StackingRule != ModifierStackingRule.Stack)
        {
            for (var i = next.Count - 1; i >= 0; i--)
                if (next[i].Source == modifier.Source && next[i].Attribute == modifier.Attribute)
                {
                    matching = i;
                    break;
                }
        }

        if (modifier.StackingRule == ModifierStackingRule.RefreshDurationSameSource && matching >= 0)
        {
            var old = next[matching];
            next.RemoveAll(existing => existing.Source == modifier.Source && existing.Attribute == modifier.Attribute
                && existing.Id != old.Id);
            matching = next.FindIndex(existing => existing.Id == old.Id);
            next[matching] = old with { RemainingSeconds = Math.Max(old.RemainingSeconds, modifier.RemainingSeconds) };
            return new ActorStatModifiers { Active = next };
        }

        if (modifier.StackingRule == ModifierStackingRule.ReplaceSameSource && matching >= 0)
            next.RemoveAll(existing => existing.Source == modifier.Source && existing.Attribute == modifier.Attribute);
        if (next.Exists(existing => existing.Id == modifier.Id))
            throw new ArgumentException($"Modifier id '{modifier.Id}' is already active.", nameof(modifier));
        next.Add(modifier);
        return new ActorStatModifiers { Active = next };
    }

    public ActorStatModifiers Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds == 0) return this;
        var next = new List<ActorStatModifier>(Active.Count);
        foreach (var modifier in Active)
        {
            modifier.Validate();
            var remaining = modifier.RemainingSeconds - elapsedSeconds;
            if (remaining > 0) next.Add(modifier with { RemainingSeconds = remaining });
        }
        return new ActorStatModifiers { Active = next };
    }

    public ActorAttributes ApplyTo(ActorAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var effective = attributes;
        foreach (var modifier in Active) modifier.Validate();
        foreach (ActorAttribute attribute in Enum.GetValues<ActorAttribute>())
            effective = effective.With(attribute, Math.Max(0, attributes.Get(attribute) + TotalFor(attribute)));
        return effective;
    }

    public double TotalFor(ActorAttribute attribute)
    {
        var total = 0d;
        foreach (var modifier in Active)
            if (modifier.Attribute == attribute) total += modifier.Amount;
        return total;
    }
}

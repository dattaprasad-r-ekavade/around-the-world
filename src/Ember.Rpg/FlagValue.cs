using System;
using System.Globalization;

namespace Ember.Rpg;

/// <summary>What a flag is holding: a boolean, a number, or text.</summary>
public enum FlagKind
{
    Boolean,
    Number,
    Text
}

/// <summary>
/// One flag's value. Three kinds and no more — booleans for what is true, numbers for how
/// many, text for where. A fourth kind is a sign that the value belongs on an entity record
/// instead.
/// </summary>
public readonly struct FlagValue : IEquatable<FlagValue>
{
    private readonly bool _boolean;
    private readonly double _number;
    private readonly string _text;

    private FlagValue(FlagKind kind, bool boolean, double number, string text)
    {
        Kind = kind;
        _boolean = boolean;
        _number = number;
        _text = text;
    }

    public FlagKind Kind { get; }

    public static FlagValue From(bool value) => new(FlagKind.Boolean, value, 0d, string.Empty);
    public static FlagValue From(double value) => new(FlagKind.Number, false, value, string.Empty);
    public static FlagValue From(string value) => new(FlagKind.Text, false, 0d, value ?? string.Empty);

    // Implicit, so Set("gold", 120) reads the way a game author expects. An int reaches the
    // double overload through the standard numeric widening.
    public static implicit operator FlagValue(bool value) => From(value);
    public static implicit operator FlagValue(double value) => From(value);
    public static implicit operator FlagValue(string value) => From(value ?? string.Empty);

    /// <summary>The value if it is a boolean; a missing flag or another kind reads as the fallback.</summary>
    public bool AsBool(bool fallback = false) => Kind == FlagKind.Boolean ? _boolean : fallback;

    /// <summary>The value if it is a number; a missing flag or another kind reads as the fallback.</summary>
    public double AsNumber(double fallback = 0d) => Kind == FlagKind.Number ? _number : fallback;

    /// <summary>The value if it is text; a missing flag or another kind reads as the fallback.</summary>
    public string AsText(string fallback = "") => Kind == FlagKind.Text ? _text : fallback;

    public bool Equals(FlagValue other) => Kind == other.Kind && Kind switch
    {
        FlagKind.Boolean => _boolean == other._boolean,
        FlagKind.Number => _number.Equals(other._number),
        _ => string.Equals(_text, other._text, StringComparison.Ordinal)
    };

    public override bool Equals(object? obj) => obj is FlagValue other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        FlagKind.Boolean => _boolean.GetHashCode(),
        FlagKind.Number => _number.GetHashCode(),
        _ => _text?.GetHashCode() ?? 0
    };

    public static bool operator ==(FlagValue left, FlagValue right) => left.Equals(right);
    public static bool operator !=(FlagValue left, FlagValue right) => !left.Equals(right);

    public override string ToString() => Kind switch
    {
        FlagKind.Boolean => _boolean ? "true" : "false",
        FlagKind.Number => _number.ToString(CultureInfo.InvariantCulture),
        _ => _text ?? string.Empty
    };
}

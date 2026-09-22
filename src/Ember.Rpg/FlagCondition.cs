namespace Ember.Rpg;

/// <summary>
/// A requirement on one flag, used to gate a dialogue option: the flag must be present, and
/// if a constraint is given, it must hold — several constraints at once are ANDed.
///
/// A condition with no constraints is a presence check ("you have met him"), which is why a
/// missing flag fails everything: a requirement that a save file can silently satisfy by
/// never having been written is not a requirement.
/// </summary>
public sealed record FlagCondition
{
    public string Flag { get; init; } = "";

    /// <summary>The flag must be a boolean with this exact value.</summary>
    public bool? Bool { get; init; }

    /// <summary>The flag must be a number at least this big.</summary>
    public double? AtLeast { get; init; }

    /// <summary>The flag must be a number at most this big.</summary>
    public double? AtMost { get; init; }

    /// <summary>The flag must be text, equal to this.</summary>
    public string? Text { get; init; }

    public bool Matches(FlagStore flags)
    {
        if (!flags.TryGet(Flag, out var value)) return false;

        if (Bool is bool wantBool &&
            (value.Kind != FlagKind.Boolean || value.AsBool() != wantBool))
            return false;

        if (AtLeast is double min &&
            (value.Kind != FlagKind.Number || value.AsNumber() < min))
            return false;

        if (AtMost is double max &&
            (value.Kind != FlagKind.Number || value.AsNumber() > max))
            return false;

        if (Text is string wantText &&
            (value.Kind != FlagKind.Text || value.AsText() != wantText))
            return false;

        return true;
    }
}

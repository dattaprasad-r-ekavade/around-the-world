using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>
/// Named flags: "met_elder", "gate_open", "gold" — what is true right now, in one place.
///
/// Mutable during play and owned by a <see cref="SaveState"/>. Reads are total: a missing
/// flag, or one holding a different kind than you asked for, comes back as the fallback
/// rather than an exception, because a game should not be able to crash on a typo'd flag
/// name in a save file it is trying to load.
/// </summary>
public sealed class FlagStore
{
    private readonly Dictionary<string, FlagValue> _flags = new(StringComparer.Ordinal);

    /// <summary>
    /// Every flag, for iteration and for the save converter. Read it; do not mutate it —
    /// use <see cref="Set"/>, <see cref="Remove"/> or <see cref="Clear"/>.
    /// </summary>
    public IReadOnlyDictionary<string, FlagValue> All => _flags;

    public int Count => _flags.Count;

    public bool Has(string name) => _flags.ContainsKey(name);

    public void Set(string name, FlagValue value) => _flags[name] = value;

    public bool Remove(string name) => _flags.Remove(name);

    public void Clear() => _flags.Clear();

    public bool TryGet(string name, out FlagValue value) => _flags.TryGetValue(name, out value);

    public bool GetBool(string name, bool fallback = false) =>
        _flags.TryGetValue(name, out var value) ? value.AsBool(fallback) : fallback;

    public double GetNumber(string name, double fallback = 0d) =>
        _flags.TryGetValue(name, out var value) ? value.AsNumber(fallback) : fallback;

    public string GetText(string name, string fallback = "") =>
        _flags.TryGetValue(name, out var value) ? value.AsText(fallback) : fallback;
}

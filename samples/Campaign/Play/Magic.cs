using System;

namespace Campaign;

public enum SpellEffect
{
    Heal,
    Spark,
    Light,
    Chameleon
}

public readonly record struct Spell(string Name, SpellEffect Effect, int Magnitude, float Cost)
{
    public static Spell Heal => new("Minor heal", SpellEffect.Heal, 18, 12f);
    public static Spell Spark => new("Spark", SpellEffect.Spark, 14, 16f);
    public static Spell Light => new("Light", SpellEffect.Light, 1, 8f);
    public static Spell Hide => new("Chameleon", SpellEffect.Chameleon, 1, 22f);

    public static float CostOf(SpellEffect effect, int magnitude)
    {
        var mag = Math.Clamp(magnitude, 1, 40);
        return effect switch
        {
            SpellEffect.Heal => 6f + mag * 0.45f,
            SpellEffect.Spark => 8f + mag * 0.7f,
            SpellEffect.Light => 8f,
            _ => 18f + mag * 0.4f
        };
    }

    public static string Label(SpellEffect effect) => effect switch
    {
        SpellEffect.Heal => "Restore health",
        SpellEffect.Spark => "Damage health",
        SpellEffect.Light => "Light",
        _ => "Chameleon"
    };
}

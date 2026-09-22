using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>
/// Iliac Bay time. A day is short enough to feel, long enough to ride somewhere.
/// </summary>
public sealed class WorldClock
{
    public float Hours { get; private set; } = 10f;
    public int Day { get; private set; } = 1;

    public bool IsNight => DayFactor < 0.28f;
    public bool ShopsOpen => Hours >= 7f && Hours < 19f;

    /// <summary>1 at noon, near 0 after dark. Moonlight keeps a little sight.</summary>
    public float DayFactor
    {
        get
        {
            if (Hours >= 7.5f && Hours <= 17.5f) return 1f;
            if (Hours > 17.5f && Hours < 21f)
                return Smooth(1f - (Hours - 17.5f) / 3.5f);
            if (Hours >= 5f && Hours < 7.5f)
                return Smooth((Hours - 5f) / 2.5f);
            return 0.1f;
        }
    }

    public string Stamp
    {
        get
        {
            var h = (int)Hours;
            var m = (int)((Hours - h) * 60f);
            var name = Hours switch
            {
                < 5f => "night",
                < 7.5f => "dawn",
                < 11.5f => "morning",
                < 14f => "noon",
                < 17.5f => "afternoon",
                < 21f => "dusk",
                _ => "night"
            };
            return $"Day {Day}  {h:00}:{m:00}  {name}";
        }
    }

    public void Reset()
    {
        Hours = 10f;
        Day = 1;
    }

    public void Load(int day, float hours)
    {
        Day = Math.Max(1, day);
        Hours = Math.Clamp(hours, 0f, 23.99f);
    }

    public void AdvanceReal(float seconds) =>
        AddHours(seconds * WorldScale.GameSecondsPerReal / 3600f);

    public void AddHours(float hours)
    {
        if (hours <= 0f) return;
        Hours += hours;
        while (Hours >= 24f)
        {
            Hours -= 24f;
            Day++;
        }
    }

    public float HoursUntilMorning()
    {
        const float Wake = 7f;
        if (Hours < Wake) return Wake - Hours;
        return 24f - Hours + Wake;
    }

    public Color TintSky(Color day)
    {
        var night = new Color(10, 14, 32);
        var dusk = new Color(72, 42, 58);
        var t = DayFactor;
        var blend = t < 0.45f
            ? Color.Lerp(night, dusk, t / 0.45f)
            : Color.Lerp(dusk, day, (t - 0.45f) / 0.55f);
        return Color.Lerp(blend, day, t * 0.35f + 0.08f);
    }

    public Color TintFog(Color day)
    {
        var night = new Color(18, 22, 36);
        return Color.Lerp(night, day, DayFactor);
    }

    public Color TintClear(Color day, bool cave)
    {
        if (cave) return day;
        return TintSky(day);
    }

    public byte NightVeil =>
        (byte)Math.Clamp((int)((1f - DayFactor) * 96f), 0, 110);

    private static float Smooth(float t)
    {
        t = MathHelper.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

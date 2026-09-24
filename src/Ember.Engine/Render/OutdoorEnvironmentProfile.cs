using System;
using Microsoft.Xna.Framework;

namespace Ember.Render;

/// <summary>Shared, deterministic outdoor sky, fog, and directional-light values for one clock time.</summary>
public readonly record struct OutdoorEnvironmentState(
    Color SkyColor,
    Color FogColor,
    float FogStart,
    float FogEnd,
    Vector3 LightDirection,
    Vector3 DirectionalLightColor,
    Vector3 AmbientLightColor);

public static class OutdoorEnvironmentProfile
{
    public static OutdoorEnvironmentState Evaluate(float timeOfDayHours,
        float fogStart = 40f, float fogEnd = 96f)
    {
        if (!float.IsFinite(timeOfDayHours))
            throw new ArgumentOutOfRangeException(nameof(timeOfDayHours), "Outdoor time must be finite.");
        if (!float.IsFinite(fogStart) || fogStart < 0f)
            throw new ArgumentOutOfRangeException(nameof(fogStart), "Fog start must be finite and nonnegative.");
        if (!float.IsFinite(fogEnd) || fogEnd <= fogStart)
            throw new ArgumentOutOfRangeException(nameof(fogEnd), "Fog end must be finite and greater than fog start.");

        var hour = timeOfDayHours % 24f;
        if (hour < 0f) hour += 24f;
        var phase = (hour - 6f) * (MathF.PI / 12f);
        var sunHeight = MathF.Sin(phase);
        var daylight = SmoothStep(-0.12f, 0.22f, sunHeight);
        var twilight = 1f - SmoothStep(0f, 0.35f, MathF.Abs(sunHeight));

        var nightSky = new Color(16, 27, 52);
        var daySky = new Color(116, 174, 214);
        var duskSky = new Color(230, 132, 91);
        var nightFog = new Color(24, 36, 58);
        var dayFog = new Color(139, 173, 188);
        var duskFog = new Color(180, 126, 108);
        var sky = Color.Lerp(nightSky, daySky, daylight);
        var fog = Color.Lerp(nightFog, dayFog, daylight);
        sky = Color.Lerp(sky, duskSky, twilight * 0.7f);
        fog = Color.Lerp(fog, duskFog, twilight * 0.7f);

        var direction = new Vector3(MathF.Cos(phase), -MathF.Max(0.12f, sunHeight),
            MathF.Sin(phase) * 0.35f);
        direction.Normalize();
        var nightLight = new Vector3(0.035f, 0.05f, 0.1f);
        var dayLight = new Vector3(1f, 0.88f, 0.72f);
        var duskLight = new Vector3(1f, 0.5f, 0.29f);
        var directional = Vector3.Lerp(nightLight, dayLight, daylight);
        directional = Vector3.Lerp(directional, duskLight, twilight * 0.65f);
        var ambient = Vector3.Lerp(new Vector3(0.12f, 0.15f, 0.24f),
            new Vector3(0.5f, 0.54f, 0.57f), daylight);

        return new OutdoorEnvironmentState(sky, fog, fogStart, fogEnd,
            direction, directional, ambient);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }
}

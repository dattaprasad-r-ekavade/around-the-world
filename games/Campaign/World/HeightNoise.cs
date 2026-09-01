using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Continent, climate, height. Deterministic from a seed.</summary>
public sealed class HeightNoise
{
    private readonly int _seed;

    public HeightNoise(int seed) => _seed = seed;

    private static float Macro(float cycles) => cycles / WorldScale.WorldMetres;

    public float Continent(float x, float z)
    {
        x = EarthGlobe.Wrap(x);
        z = EarthGlobe.Wrap(z);
        return EarthLand.Field(x, z);
    }

    public float Moisture(float x, float z)
    {
        x = EarthGlobe.Wrap(x);
        z = EarthGlobe.Wrap(z);
        var n = 0.62f * Fbm(x * Macro(9f) + 19f, z * Macro(9f) + 8f, 4)
            + 0.38f * Fbm(x * 0.00031f + 19f, z * 0.00031f + 8f, 4);
        var lon = EarthGlobe.Lon(x);
        var lat = EarthGlobe.Lat(z);
        var arid = 0f;
        if (lat is > 12f and < 36f && lon is > -18f and < 62f) arid = 0.52f;
        if (lat is > 36f and < 50f && lon is > 75f and < 120f) arid = 0.42f;
        if (lat is < -18f and > -32f && lon is > 112f and < 150f) arid = 0.48f;
        if (lat is > 22f and < 42f && lon is > -124f and < -104f) arid = 0.38f;
        return MathHelper.Clamp(n - arid, 0f, 1f);
    }

    public float Heat(float x, float z)
    {
        x = EarthGlobe.Wrap(x);
        z = EarthGlobe.Wrap(z);
        var polar = MathF.Abs(EarthGlobe.Lat(z)) / 90f;
        return MathHelper.Clamp((1f - polar) * 0.62f + Fbm(x * Macro(6f), z * Macro(6f), 3) * 0.38f,
            0f, 1f);
    }

    public float Relief(float x, float z)
    {
        x = EarthGlobe.Wrap(x);
        z = EarthGlobe.Wrap(z);
        var n = 0.58f * Fbm(x * Macro(16f) - 11f, z * Macro(16f) + 4f, 4)
            + 0.42f * Fbm(x * 0.00055f - 11f, z * 0.00055f + 4f, 5);
        var lon = EarthGlobe.Lon(x);
        var lat = EarthGlobe.Lat(z);
        if (lat is > 26f and < 38f && lon is > 72f and < 96f) n += 0.28f;
        if (lat is > -40f and < 8f && lon is > -80f and < -64f) n += 0.22f;
        if (lat is > 30f and < 48f && lon is > 6f and < 16f) n += 0.16f;
        return MathHelper.Clamp(n, 0f, 1f);
    }

    public BiomeKind BiomeAt(float x, float z)
    {
        var land = Continent(x, z);
        if (land < 0.40f) return BiomeKind.Ocean;
        if (land < 0.46f) return BiomeKind.Coast;

        var relief = Relief(x, z);
        var moist = Moisture(x, z);
        var heat = Heat(x, z);

        if (relief > 0.74f) return heat < 0.42f ? BiomeKind.Snow : BiomeKind.Mountain;
        if (relief > 0.58f) return BiomeKind.Hills;
        if (heat < 0.30f) return BiomeKind.Snow;
        if (moist < 0.34f && heat > 0.48f) return BiomeKind.Desert;
        if (moist > 0.72f && relief < 0.38f) return BiomeKind.Marsh;
        if (moist > 0.56f) return BiomeKind.Forest;
        return BiomeKind.Grass;
    }

    public float Height(float x, float z)
    {
        x = EarthGlobe.Wrap(x);
        z = EarthGlobe.Wrap(z);
        var n = Fbm(x * 0.0016f, z * 0.0016f, 5);
        var ridge = 1f - MathF.Abs(Fbm(x * 0.00085f + 40f, z * 0.00085f - 17f, 4) * 2f - 1f);
        var biome = BiomeAt(x, z);
        var moist = Moisture(x, z);
        var relief = Relief(x, z);
        return biome switch
        {
            BiomeKind.Ocean => 1.4f,
            BiomeKind.Coast => 4.6f + n * 3.5f,
            BiomeKind.Marsh => moist > 0.84f ? 2.1f : 4.4f + n * 3.2f,
            BiomeKind.Grass => moist > 0.86f && relief < 0.30f ? 2.3f : 8f + n * 16f + ridge * 3f,
            BiomeKind.Forest => 9f + n * 18f + ridge * 5f,
            BiomeKind.Desert => 7f + n * 11f + ridge * 2f,
            BiomeKind.Hills => 18f + n * 24f + ridge * 12f,
            BiomeKind.Mountain => 32f + n * 40f + ridge * 34f,
            BiomeKind.Snow => 24f + n * 28f + ridge * 20f,
            _ => 8f + n * 12f
        };
    }

    public float Fbm(float x, float z, int octaves)
    {
        var sum = 0f;
        var amp = 1f;
        var norm = 0f;
        var fx = x;
        var fz = z;
        for (var i = 0; i < octaves; i++)
        {
            sum += Value(fx, fz) * amp;
            norm += amp;
            amp *= 0.5f;
            fx *= 2.03f;
            fz *= 2.03f;
        }

        return sum / MathF.Max(0.0001f, norm);
    }

    private float Value(float x, float z)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var tx = Smooth(x - x0);
        var tz = Smooth(z - z0);
        var a = Hash(x0, z0);
        var b = Hash(x0 + 1, z0);
        var c = Hash(x0, z0 + 1);
        var d = Hash(x0 + 1, z0 + 1);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, tx), MathHelper.Lerp(c, d, tx), tz);
    }

    private float Hash(int x, int z)
    {
        var n = x * 374761393 + z * 668265263 + _seed * 1274126177;
        n = (n ^ (n >> 13)) * 1274126177;
        n ^= n >> 16;
        return (n & 0xFFFFFF) / 16777215f;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}

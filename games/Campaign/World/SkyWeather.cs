using Microsoft.Xna.Framework;
using System;

namespace Campaign;

public enum WeatherKind
{
    Clear,
    Cloud,
    Fog,
    Rain,
    Snow
}

/// <summary>Bay weather. Slots change every six hours from biome and the day.</summary>
public sealed class SkyWeather
{
    public WeatherKind Kind { get; private set; } = WeatherKind.Clear;
    public string Label => Kind switch
    {
        WeatherKind.Cloud => "overcast",
        WeatherKind.Fog => "fog",
        WeatherKind.Rain => "rain",
        WeatherKind.Snow => "snow",
        _ => "clear"
    };

    public bool Wetting => Kind is WeatherKind.Rain or WeatherKind.Snow;
    public float ColdBias => Kind switch
    {
        WeatherKind.Rain => 16f,
        WeatherKind.Snow => 28f,
        WeatherKind.Fog => 8f,
        WeatherKind.Cloud => 4f,
        _ => 0f
    };

    public byte Veil => Kind switch
    {
        WeatherKind.Rain => 38,
        WeatherKind.Snow => 44,
        WeatherKind.Fog => 52,
        WeatherKind.Cloud => 16,
        _ => 0
    };

    public void Sync(int seed, int day, float hours, BiomeKind biome)
    {
        var slot = day * 4 + (int)(hours / 6f);
        var n = Hash(seed, slot, (int)biome);
        Kind = Pick(biome, (n & 255) / 255f);
    }

    public Color TintFog(Color fog)
    {
        return Kind switch
        {
            WeatherKind.Rain => Color.Lerp(fog, new Color(48, 58, 68), 0.42f),
            WeatherKind.Snow => Color.Lerp(fog, new Color(186, 196, 208), 0.5f),
            WeatherKind.Fog => Color.Lerp(fog, new Color(140, 150, 148), 0.62f),
            WeatherKind.Cloud => Color.Lerp(fog, new Color(90, 100, 112), 0.28f),
            _ => fog
        };
    }

    public Color TintSky(Color sky)
    {
        return Kind switch
        {
            WeatherKind.Rain => Color.Lerp(sky, new Color(36, 44, 56), 0.45f),
            WeatherKind.Snow => Color.Lerp(sky, new Color(168, 178, 190), 0.35f),
            WeatherKind.Fog => Color.Lerp(sky, new Color(120, 128, 124), 0.4f),
            WeatherKind.Cloud => Color.Lerp(sky, new Color(70, 82, 96), 0.3f),
            _ => sky
        };
    }

    private static WeatherKind Pick(BiomeKind biome, float roll) => biome switch
    {
        BiomeKind.Snow or BiomeKind.Mountain => roll < 0.55f ? WeatherKind.Snow
            : roll < 0.8f ? WeatherKind.Cloud : WeatherKind.Clear,
        BiomeKind.Desert => roll < 0.12f ? WeatherKind.Cloud : WeatherKind.Clear,
        BiomeKind.Marsh => roll < 0.4f ? WeatherKind.Fog
            : roll < 0.75f ? WeatherKind.Rain : WeatherKind.Cloud,
        BiomeKind.Ocean or BiomeKind.Coast => roll < 0.28f ? WeatherKind.Rain
            : roll < 0.5f ? WeatherKind.Cloud : WeatherKind.Fog,
        BiomeKind.Forest => roll < 0.32f ? WeatherKind.Rain
            : roll < 0.55f ? WeatherKind.Cloud : WeatherKind.Clear,
        _ => roll < 0.22f ? WeatherKind.Rain
            : roll < 0.45f ? WeatherKind.Cloud : WeatherKind.Clear
    };

    private static int Hash(int a, int b, int c)
    {
        unchecked
        {
            var n = (uint)(a * 374761393 + b * 668265263 + c * 1274126177);
            n = (n ^ (n >> 13)) * 1274126177u;
            return (int)(n ^ (n >> 16));
        }
    }
}

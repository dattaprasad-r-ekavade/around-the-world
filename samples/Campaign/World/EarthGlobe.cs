using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>
/// The map is a torus: walk off the east and the west returns, north meets south.
/// Longitude −180..180 maps to X; latitude 90..−90 maps to Z (north is −Z).
/// </summary>
public static class EarthGlobe
{
    public static float Wrap(float metres)
    {
        var w = WorldScale.WorldMetres;
        var t = metres % w;
        return t < 0f ? t + w : t;
    }

    /// <summary>Shortest signed delta on the circle, in metres.</summary>
    public static float Delta(float from, float to)
    {
        var w = WorldScale.WorldMetres;
        var d = to - from;
        if (d > w * 0.5f) d -= w;
        if (d < -w * 0.5f) d += w;
        return d;
    }

    public static float DistanceSq(float x0, float z0, float x1, float z1)
    {
        var dx = Delta(x0, x1);
        var dz = Delta(z0, z1);
        return dx * dx + dz * dz;
    }

    public static float Lon(float x) => Wrap(x) / WorldScale.WorldMetres * 360f - 180f;

    public static float Lat(float z) => 90f - Wrap(z) / WorldScale.WorldMetres * 180f;

    public static Vector3 FromLonLat(float lon, float lat)
    {
        var x = (lon + 180f) / 360f * WorldScale.WorldMetres;
        var z = (90f - lat) / 180f * WorldScale.WorldMetres;
        return new Vector3(Wrap(x), 0f, Wrap(z));
    }

    public static float LonDelta(float a, float b)
    {
        var d = b - a;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }
}

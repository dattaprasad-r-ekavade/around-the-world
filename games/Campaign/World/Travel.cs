using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Hours on the road. Adjacent towns sit about 7 km apart.</summary>
public static class Travel
{
    public static float Hours(Vector3 from, Vector3 dest, bool ship, bool fromCoast, bool toCoast)
    {
        var km = Kilometres(from, dest);
        var kph = ship && (fromCoast || toCoast) ? WorldScale.RoadShipKph : WorldScale.RoadHorseKph;
        return Math.Clamp(km / kph, WorldScale.RoadHoursMin, WorldScale.RoadHoursMax);
    }

    public static float Kilometres(Vector3 from, Vector3 dest)
    {
        var dx = EarthGlobe.Delta(from.X, dest.X);
        var dz = EarthGlobe.Delta(from.Z, dest.Z);
        return MathF.Sqrt(dx * dx + dz * dz) / 1000f;
    }

    public static bool Coastal(HeightNoise noise, Vector3 p)
    {
        var biome = noise.BiomeAt(p.X, p.Z);
        return biome is BiomeKind.Coast or BiomeKind.Ocean;
    }
}

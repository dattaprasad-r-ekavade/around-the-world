using Ember.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Campaign;

public sealed class WorldState
{
    public required int Seed { get; init; }
    public required HeightNoise Noise { get; init; }
    public required WorldHeights Heights { get; init; }
    public required Vector3 Spawn { get; init; }
    public required Vector3[] TownPads { get; init; }
    public required Vector3[] DungeonMouths { get; init; }
    public required string[] TownNames { get; init; }
    public required string[] DungeonNames { get; init; }
    public required BiomeKind[] TownBiomes { get; init; }
    public required WildernessLocation Wilderness { get; init; }
    public HeightmapTerrain Terrain { get; set; } = null!;

    private BoxLocation?[]? _towns;
    private BoxLocation[]?[]? _interiors;
    private BoxLocation?[]? _dungeons;

    public int TownCount => TownPads.Length;
    public int DungeonCount => DungeonMouths.Length;

    public BoxLocation Town(int index)
    {
        _towns ??= new BoxLocation?[TownCount];
        return _towns[index] ??= WorldGenerator.MakeTown(this, index);
    }

    public BoxLocation[] InteriorsFor(int index)
    {
        _interiors ??= new BoxLocation[TownCount][];
        return _interiors[index] ??= WorldGenerator.MakeInteriors(this, index);
    }

    public BoxLocation Dungeon(int index)
    {
        _dungeons ??= new BoxLocation?[DungeonCount];
        return _dungeons[index] ??= WorldGenerator.MakeDungeon(this, index);
    }

    public void AttachTerrain(GraphicsDevice device)
    {
        Terrain?.Dispose();
        Terrain = new HeightmapTerrain(device, Heights, Noise, Seed);
        Wilderness.Terrain = Terrain;
    }
}

public static class WorldGenerator
{
    private static readonly Color TownClear = new(148, 132, 108);
    private static readonly Color InteriorClear = new(48, 40, 32);
    private static readonly Color DungeonClear = new(18, 17, 16);

    public static WorldState Generate(int seed)
    {
        var noise = new HeightNoise(seed);
        var rng = new Random(seed);

        var historic = EarthPlaces.Cities;
        var marks = EarthPlaces.Marks;
        var named = historic.Length + marks.Length;
        var pads = new Vector3[named + WorldScale.VillageFill];
        for (var i = 0; i < historic.Length; i++)
            pads[i] = PlaceExact(noise, historic[i]);
        for (var i = 0; i < marks.Length; i++)
            pads[historic.Length + i] = PlaceExact(noise, marks[i]);

        var fill = PlaceGrid(WorldScale.VillageFill, noise, rng, towns: true);
        for (var i = 0; i < fill.Length; i++)
            pads[named + i] = fill[i];

        var site = EarthPlaces.Sites;
        var mouths = new Vector3[site.Length + WorldScale.SiteFill];
        var holeRng = new Random(seed ^ 0x27d4eb2d);
        for (var i = 0; i < site.Length; i++)
            mouths[i] = PlaceExact(noise, site[i]);

        var holes = PlaceGrid(WorldScale.SiteFill, noise, holeRng, towns: false);
        for (var i = 0; i < holes.Length; i++)
            mouths[site.Length + i] = holes[i];

        RelocateWet(pads, noise, rng, towns: true, start: named);
        RelocateWet(mouths, noise, holeRng, towns: false, start: site.Length);

        var heights = new WorldHeights(noise, pads, WorldScale.TownPadRadius);
        for (var i = 0; i < pads.Length; i++)
        {
            var pad = pads[i];
            pads[i] = new Vector3(EarthGlobe.Wrap(pad.X), heights.Sample(pad.X, pad.Z),
                EarthGlobe.Wrap(pad.Z));
        }

        for (var i = 0; i < mouths.Length; i++)
        {
            var mouth = mouths[i];
            mouths[i] = new Vector3(EarthGlobe.Wrap(mouth.X), heights.Sample(mouth.X, mouth.Z),
                EarthGlobe.Wrap(mouth.Z));
        }

        var townNames = new string[pads.Length];
        var townBiomes = new BiomeKind[pads.Length];
        for (var i = 0; i < pads.Length; i++)
        {
            townBiomes[i] = noise.BiomeAt(pads[i].X, pads[i].Z);
            townNames[i] = i < named
                ? EarthPlaces.StopAt(i).Name
                : PlaceNames.Town(i, seed, townBiomes[i]);
        }

        var dungeonNames = new string[mouths.Length];
        for (var i = 0; i < mouths.Length; i++)
            dungeonNames[i] = i < site.Length ? site[i].Name : PlaceNames.Hole(i, seed);

        var spawnPad = pads[EarthPlaces.Alexandria];
        var spawn = new Vector3(EarthGlobe.Wrap(spawnPad.X), 0f, EarthGlobe.Wrap(spawnPad.Z + 72f));

        var wilderness = new WildernessLocation(heights, noise, seed, pads, mouths,
            townNames, dungeonNames, townBiomes);

        return new WorldState
        {
            Seed = seed,
            Noise = noise,
            Heights = heights,
            Spawn = spawn,
            TownPads = pads,
            DungeonMouths = mouths,
            TownNames = townNames,
            DungeonNames = dungeonNames,
            TownBiomes = townBiomes,
            Wilderness = wilderness
        };
    }

    public static BoxLocation MakeTown(WorldState world, int index)
    {
        var layout = TownLayout.Build(world.Seed + 17 * (index + 1), world.TownNames[index]);
        return new BoxLocation(world.TownNames[index], LocationKind.Town, layout, 0f,
            StoneTextures.StonePalette.Sandstone, TownClear);
    }

    public static BoxLocation[] MakeInteriors(WorldState world, int index)
    {
        var layouts = index == 0 ? HandMaps.TownInteriors() : TownGenerator.Interiors();
        var names = new[] { "shop", "inn", "house", "Fighters Guild", "Mages Guild", "temple" };
        var rooms = new BoxLocation[layouts.Length];
        for (var d = 0; d < layouts.Length; d++)
        {
            var room = d < names.Length ? names[d] : $"interior {d}";
            rooms[d] = new BoxLocation(
                $"{world.TownNames[index]} {room}",
                LocationKind.Interior,
                layouts[d],
                0f, StoneTextures.StonePalette.Sandstone, InteriorClear);
        }

        return rooms;
    }

    public static BoxLocation MakeDungeon(WorldState world, int index)
    {
        var layout = index == 0
            ? HandMaps.Dungeon()
            : DungeonGenerator.Generate(world.Seed + 91 * (index + 3),
                WorldScale.DungeonRoomMin
                + (world.Seed + index) % (WorldScale.DungeonRoomMax - WorldScale.DungeonRoomMin + 1));
        return new BoxLocation(world.DungeonNames[index], LocationKind.Dungeon, layout, 0f,
            StoneTextures.StonePalette.Granite, DungeonClear);
    }

    private static Vector3[] PlaceGrid(int count, HeightNoise noise, Random rng, bool towns)
    {
        var pads = new Vector3[count];
        var grid = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        var cell = (WorldScale.WorldMetres - 1200f) / grid;

        for (var i = 0; i < count; i++)
        {
            if (i == 0 && towns)
            {
                pads[i] = FindLand(noise, WorldScale.WorldMetres * 0.5f, WorldScale.WorldMetres * 0.4f,
                    towns, rng, 1);
                continue;
            }

            var col = i % grid;
            var row = i / grid;
            var gx = 600f + (col + 0.22f + (float)rng.NextDouble() * 0.56f) * cell;
            var gz = 600f + (row + 0.22f + (float)rng.NextDouble() * 0.56f) * cell;
            pads[i] = FindLand(noise, gx, gz, towns, rng, i);
        }

        return pads;
    }

    /// <summary>Pin a historic site to its coordinates. Only a short coast step if the cell is wet.</summary>
    private static Vector3 PlaceExact(HeightNoise noise, EarthPlace place)
    {
        var at = EarthGlobe.FromLonLat(place.Lon, place.Lat);
        var x = at.X;
        var z = at.Z;
        if (!IsHabitable(noise, x, z))
        {
            const float Max = 420f;
            const float Step = 14f;
            var found = false;
            for (var r = Step; r <= Max && !found; r += Step)
            {
                var n = Math.Max(8, (int)(r * 0.45f));
                for (var k = 0; k < n; k++)
                {
                    var a = k * MathF.Tau / n;
                    var cx = EarthGlobe.Wrap(x + MathF.Cos(a) * r);
                    var cz = EarthGlobe.Wrap(z + MathF.Sin(a) * r);
                    if (!IsHabitable(noise, cx, cz)) continue;
                    x = cx;
                    z = cz;
                    found = true;
                    break;
                }
            }
        }

        var y = noise.Height(x, z);
        if (y < WorldScale.WaterLevel + 1.2f)
            y = WorldScale.WaterLevel + 1.6f;
        return new Vector3(x, y, z);
    }

    private static void RelocateWet(Vector3[] pads, HeightNoise noise, Random rng, bool towns,
        int start = 0)
    {
        for (var i = start; i < pads.Length; i++)
        {
            for (var attempt = 0; attempt < 4 && !IsHabitable(noise, pads[i].X, pads[i].Z); attempt++)
                pads[i] = FindLand(noise, pads[i].X, pads[i].Z, towns, rng, i + 17 + attempt * 97);
        }
    }

    /// <summary>
    /// Dry ground above the water. Ocean is never a town; a real island is land biome
    /// with height above sea level, not a fake pad in the drink.
    /// </summary>
    public static bool IsHabitable(HeightNoise noise, float x, float z)
    {
        var biome = noise.BiomeAt(x, z);
        if (biome == BiomeKind.Ocean) return false;
        return noise.Height(x, z) >= WorldScale.WaterLevel + 1f;
    }

    private static Vector3 FindBestLand(HeightNoise noise, bool preferSettled)
    {
        var step = WorldScale.WorldMetres / 40f;
        var best = new Vector3(WorldScale.WorldMetres * 0.5f, 12f, WorldScale.WorldMetres * 0.5f);
        var bestScore = float.MinValue;
        for (var z = 3; z <= 36; z++)
        for (var x = 3; x <= 36; x++)
        {
            var wx = x * step;
            var wz = z * step;
            var biome = noise.BiomeAt(wx, wz);
            if (!IsHabitable(noise, wx, wz)) continue;
            if (biome == BiomeKind.Coast) continue;
            var score = biome switch
            {
                BiomeKind.Grass when preferSettled => 6f,
                BiomeKind.Forest when preferSettled => 5.5f,
                BiomeKind.Hills => 4.5f,
                BiomeKind.Desert => 3.5f,
                BiomeKind.Marsh => 3f,
                BiomeKind.Mountain or BiomeKind.Snow => 1.5f,
                _ => 2.5f
            };
            score += noise.Height(wx, wz) * 0.01f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = new Vector3(wx, noise.Height(wx, wz), wz);
        }

        if (bestScore > float.MinValue) return best;

        for (var z = 3; z <= 36; z++)
        for (var x = 3; x <= 36; x++)
        {
            var wx = x * step;
            var wz = z * step;
            if (!IsHabitable(noise, wx, wz)) continue;
            return new Vector3(wx, noise.Height(wx, wz), wz);
        }

        return best;
    }

    private static Vector3 FindLand(HeightNoise noise, float x, float z, bool town, Random rng, int salt)
    {
        x = EarthGlobe.Wrap(x);
        z = EarthGlobe.Wrap(z);
        const float Step = 640f;
        for (var t = 0; t < 720; t++)
        {
            var angle = t * 0.61803399f * MathF.Tau + salt * 0.13f + (float)rng.NextDouble() * 0.2f;
            var radius = (t + (salt % 7)) * Step * 0.12f;
            var cx = EarthGlobe.Wrap(x + MathF.Cos(angle) * radius);
            var cz = EarthGlobe.Wrap(z + MathF.Sin(angle) * radius);
            if (!IsHabitable(noise, cx, cz)) continue;
            var biome = noise.BiomeAt(cx, cz);
            if (town && biome is BiomeKind.Mountain or BiomeKind.Snow && t < 40) continue;
            return new Vector3(cx, noise.Height(cx, cz), cz);
        }

        return FindHabitableScan(noise, salt, town);
    }

    private static Vector3 FindHabitableScan(HeightNoise noise, int salt, bool town)
    {
        const int Grid = 48;
        var step = WorldScale.WorldMetres / Grid;
        var start = Math.Abs(salt * 17) % ((Grid - 6) * (Grid - 6));
        var cells = (Grid - 6) * (Grid - 6);
        for (var n = 0; n < cells; n++)
        {
            var idx = (start + n) % cells;
            var col = 3 + idx % (Grid - 6);
            var row = 3 + idx / (Grid - 6);
            var cx = col * step;
            var cz = row * step;
            if (!IsHabitable(noise, cx, cz)) continue;
            var biome = noise.BiomeAt(cx, cz);
            if (town && biome is BiomeKind.Mountain or BiomeKind.Snow) continue;
            return new Vector3(cx, noise.Height(cx, cz), cz);
        }

        return FindBestLand(noise, preferSettled: town);
    }
}

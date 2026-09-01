using Ember.Render;
using Ember.Ui;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

public sealed class WildernessLocation : ILocation
{
    private readonly WorldHeights _heights;
    private readonly HeightNoise _noise;
    private readonly int _seed;
    private readonly Vector3[] _townPads;
    private readonly Vector3[] _mouths;
    private readonly string[] _townNames;
    private readonly string[] _dungeonNames;
    private readonly BiomeKind[] _townBiomes;
    private readonly PadHash _townHash;
    private readonly PadHash _mouthHash;
    private readonly Dictionary<int, Settlement> _townCache = new();
    private readonly Dictionary<(int, int), Settlement> _croftCache = new();
    private readonly List<int> _nearTowns = new();
    private readonly List<int> _nearMouths = new();
    private readonly List<Marker> _probe = new();
    private readonly List<PointLight> _lights = new();
    private readonly BoxCollider _collider = new();
    private readonly List<WorldBox> _solids = new();
    private readonly List<BillboardProp> _decor = new();
    private int _solidCell = int.MinValue;
    private string _region = "Wilderness";
    private Color _sky = Biomes.Sky(BiomeKind.Grass);
    private Color _fog = Biomes.Horizon(BiomeKind.Grass);

    public WildernessLocation(WorldHeights heights, HeightNoise noise, int seed,
        Vector3[] townPads, Vector3[] mouths,
        string[] townNames, string[] dungeonNames, BiomeKind[] townBiomes)
    {
        _heights = heights;
        _noise = noise;
        _seed = seed;
        _townPads = townPads;
        _mouths = mouths;
        _townNames = townNames;
        _dungeonNames = dungeonNames;
        _townBiomes = townBiomes;
        _townHash = new PadHash(townPads);
        _mouthHash = new PadHash(mouths);
    }

    public string Name => _region;
    public StoneTextures.StonePalette Palette => StoneTextures.StonePalette.Sandstone;
    public Color ClearColour => _sky;
    public Color FogColour => _fog;
    public IReadOnlyList<PointLight> Lights => _lights;
    public HeightmapTerrain Terrain { get; set; } = null!;

    public void TickSky(Vector3 eye)
    {
        var biome = _noise.BiomeAt(eye.X, eye.Z);
        _sky = Biomes.Sky(biome);
        _fog = Biomes.Horizon(biome);
        _region = Biomes.Label(biome);
        RefreshSolids(eye);
    }

    public float SampleGround(float x, float z) => _heights.SampleWalk(x, z);

    public Vector3 Collide(Vector3 origin, Vector3 delta, float radius)
    {
        var wanted = origin + delta;
        var limit = WorldScale.WorldMetres - 4f;
        wanted = new Vector3(
            Math.Clamp(wanted.X, 4f, limit),
            wanted.Y,
            Math.Clamp(wanted.Z, 4f, limit));
        return _collider.Resolve(origin, wanted - origin, radius);
    }

    public void Draw(SceneRenderer scene, BillboardRenderer billboards, GroundedView view,
        CampaignSprites sprites)
    {
        var maxDistSq = WorldScale.FogEnd * WorldScale.FogEnd;

        // Sprites first, walls after: a camera-facing tree beside a house used to draw on top
        // of the wall when you looked diagonally. Opaque walls win the depth test instead.
        billboards.Begin(view.View, view.Projection);
        _decor.Clear();
        Terrain?.CollectDecor(view.Position, WorldScale.FogEnd - 24f, _decor);
        foreach (var prop in _decor)
        {
            if (HitsWall(prop)) continue;
            billboards.Draw(sprites.Get(prop.Sprite, view.Yaw, prop.FacingYaw),
                prop.Feet, prop.Height, view.Yaw, prop.Tint);
        }

        foreach (var index in _nearTowns)
        {
            if (_townBiomes[index] == BiomeKind.Ocean) continue;
            var town = TownAt(index);
            foreach (var prop in town.Props)
            {
                var dx = prop.Feet.X - view.Position.X;
                var dz = prop.Feet.Z - view.Position.Z;
                if (dx * dx + dz * dz > maxDistSq) continue;
                if (HitsWall(prop)) continue;
                billboards.Draw(sprites.Get(prop.Sprite, view.Yaw, prop.FacingYaw),
                    prop.Feet, prop.Height, view.Yaw, prop.Tint);
            }
        }

        foreach (var croft in _croftCache.Values)
        {
            foreach (var prop in croft.Props)
            {
                var dx = prop.Feet.X - view.Position.X;
                var dz = prop.Feet.Z - view.Position.Z;
                if (dx * dx + dz * dz > maxDistSq) continue;
                if (HitsWall(prop)) continue;
                billboards.Draw(sprites.Get(prop.Sprite, view.Yaw, prop.FacingYaw),
                    prop.Feet, prop.Height, view.Yaw, prop.Tint);
            }
        }

        foreach (var index in _nearTowns)
        {
            if (_townBiomes[index] == BiomeKind.Ocean) continue;
            var town = TownAt(index);
            var dx = town.Gate.X - view.Position.X;
            var dz = town.Gate.Z - view.Position.Z;
            if (dx * dx + dz * dz > maxDistSq) continue;
            billboards.Draw(sprites.Get("mappin", view.Yaw, 0f), town.Gate, 2.4f, view.Yaw, Color.White);
        }

        foreach (var index in _nearMouths)
        {
            var mouth = _mouths[index];
            var dx = mouth.X - view.Position.X;
            var dz = mouth.Z - view.Position.Z;
            if (dx * dx + dz * dz > maxDistSq) continue;
            billboards.Draw(sprites.Get("mouth", view.Yaw, 0f), mouth, 3.6f, view.Yaw, Color.White);
        }

        foreach (var index in _nearTowns)
        {
            if (_townBiomes[index] == BiomeKind.Ocean) continue;
            DrawSettlement(scene, view.Position, maxDistSq, TownAt(index), plazas: false);
        }

        foreach (var croft in _croftCache.Values)
            DrawSettlement(scene, view.Position, maxDistSq, croft, plazas: false);
    }

    private bool HitsWall(BillboardProp prop)
    {
        var radius = prop.Height < 2.6f ? 0.55f : prop.Height * 0.45f + 0.8f;
        return Settlement.HitsWall(_solids, prop.Feet.X, prop.Feet.Y, prop.Feet.Z,
            radius, prop.Height);
    }

    public LookHint? Probe(Vector3 eye, Vector3 forward)
    {
        _probe.Clear();
        foreach (var index in _nearTowns)
        {
            if (_townBiomes[index] == BiomeKind.Ocean) continue;
            var town = TownAt(index);
            _probe.Add(new Marker(MarkerKind.EnterTown, town.Gate, 5.4f,
                $"Enter {_townNames[index]}", index));
        }

        foreach (var index in _nearMouths)
        {
            var mouth = _mouths[index];
            _probe.Add(new Marker(MarkerKind.EnterDungeon, mouth, 5.5f,
                $"Enter {_dungeonNames[index]}", index));
        }

        return Look.Nearest(_probe, eye, forward);
    }

    private void RefreshSolids(Vector3 eye)
    {
        var cell = ((int)MathF.Floor(eye.X / 24f) * 4099) ^ (int)MathF.Floor(eye.Z / 24f);
        if (cell == _solidCell) return;
        _solidCell = cell;

        _townHash.Query(eye.X, eye.Z, WorldScale.FogEnd + 40f, _nearTowns);
        _mouthHash.Query(eye.X, eye.Z, WorldScale.FogEnd + 40f, _nearMouths);

        _solids.Clear();
        foreach (var index in _nearTowns)
        {
            if (_townBiomes[index] == BiomeKind.Ocean) continue;
            _solids.AddRange(TownAt(index).Buildings);
        }

        _croftCache.Clear();
        var ccx = (int)MathF.Floor(eye.X / WorldScale.ChunkMetres);
        var ccz = (int)MathF.Floor(eye.Z / WorldScale.ChunkMetres);
        for (var z = ccz - WorldScale.ChunkRing; z <= ccz + WorldScale.ChunkRing; z++)
        for (var x = ccx - WorldScale.ChunkRing; x <= ccx + WorldScale.ChunkRing; x++)
        {
            var croft = CroftAt(x, z);
            if (croft is null) continue;
            _croftCache[(x, z)] = croft;
            _solids.AddRange(croft.Buildings);
        }

        _collider.Rebuild(_solids);
        TrimTownCache();
    }

    public Vector3 TownGate(int index) => TownAt(index).Gate;

    private Settlement TownAt(int index)
    {
        if (_townCache.TryGetValue(index, out var town)) return town;
        town = Settlement.Build(index, _townNames[index], _townPads[index],
            _townBiomes[index], _seed + 31 * (index + 2),
            city: index == 0 || index % WorldScale.MapCityStride == 0);
        _townCache[index] = town;
        return town;
    }

    private Settlement? CroftAt(int cx, int cz)
    {
        if (cx < 0 || cz < 0) return null;
        var n = Hash(_seed, cx, cz);
        if ((n & 7) != 0) return null;

        var x = (cx + 0.35f + ((n >> 8) & 255) / 255f * 0.3f) * WorldScale.ChunkMetres;
        var z = (cz + 0.35f + ((n >> 16) & 255) / 255f * 0.3f) * WorldScale.ChunkMetres;
        if (x < 40f || z < 40f || x > WorldScale.WorldMetres - 40f || z > WorldScale.WorldMetres - 40f)
            return null;
        var biome = _noise.BiomeAt(x, z);
        if (biome is BiomeKind.Ocean or BiomeKind.Mountain) return null;
        if (_heights.PadNear(x, z, 90f)) return null;
        var y = _heights.Sample(x, z);
        if (y <= WorldScale.WaterLevel + 1f) return null;
        return Settlement.Build(10_000 + cx * 4096 + cz, "croft", new Vector3(x, y, z),
            biome, n, cottage: true);
    }

    private void TrimTownCache()
    {
        if (_townCache.Count < 48) return;
        var keep = new HashSet<int>(_nearTowns);
        var drop = new List<int>();
        foreach (var key in _townCache.Keys)
        {
            if (!keep.Contains(key)) drop.Add(key);
        }

        foreach (var key in drop)
            _townCache.Remove(key);
    }

    private static void DrawSettlement(SceneRenderer scene, Vector3 camera, float maxDistSq,
        Settlement town, bool plazas)
    {
        var dx = town.Pad.X - camera.X;
        var dz = town.Pad.Z - camera.Z;
        if (dx * dx + dz * dz > maxDistSq) return;

        if (plazas)
        {
            var y = town.Pad.Y;
            scene.DrawWorldBox(
                new Vector3(town.Pad.X - 7f, y - 0.05f, town.Pad.Z - 7f),
                new Vector3(town.Pad.X + 7f, y + 0.22f, town.Pad.Z + 7f),
                new Color(120, 102, 78), "earth");
        }

        foreach (var box in town.Buildings)
            scene.DrawWorldBox(box.Min, box.Max, box.Colour, box.Material);
    }

    private static int Hash(int seed, int x, int z)
    {
        var n = seed * 374761393 + x * 668265263 + z * 1274126177;
        n = (n ^ (n >> 13)) * 1274126177;
        return n ^ (n >> 16);
    }
}

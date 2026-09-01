using Ember.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Campaign;

/// <summary>
/// Chunked heightmesh with its own effect and GPU buffers. Sharing EngineHost's LitEffect
/// with SceneRenderer left the permutation on the untextured-lit path, so the ground often
/// drew as nothing while town boxes still looked fine.
/// </summary>
public sealed class HeightmapTerrain : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly WorldHeights _heights;
    private readonly HeightNoise _noise;
    private readonly int _seed;
    private readonly BasicEffect _effect;
    private readonly IndexBuffer _indices;
    private readonly Dictionary<(int, int), Chunk> _chunks = new();
    private readonly int _stride;
    private readonly int _triangleCount;
    private readonly int _cacheLimit;

    public HeightmapTerrain(GraphicsDevice device, WorldHeights heights, HeightNoise noise,
        int seed)
    {
        _device = device;
        _heights = heights;
        _noise = noise;
        _seed = seed;
        _stride = (int)(WorldScale.ChunkMetres / WorldScale.VertexSpacing) + 1;
        _triangleCount = (_stride - 1) * (_stride - 1) * 2;
        var ring = WorldScale.ChunkRing * 2 + 1;
        _cacheLimit = ring * ring + 16;

        _effect = new BasicEffect(device)
        {
            LightingEnabled = false,
            TextureEnabled = true,
            VertexColorEnabled = true,
            FogEnabled = true,
            FogStart = WorldScale.FogStart,
            FogEnd = WorldScale.FogEnd,
            DiffuseColor = Vector3.One,
            Alpha = 1f
        };

        var indexData = BuildIndices(_stride);
        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, indexData.Length,
            BufferUsage.WriteOnly);
        _indices.SetData(indexData);
    }

    public float Sample(float x, float z) => _heights.Sample(x, z);

    public void CollectDecor(Vector3 camera, float maxDist, List<BillboardProp> into)
    {
        const int Cap = 160;
        var maxDistSq = maxDist * maxDist;
        var ranked = new List<(float Dist, BillboardProp Prop)>();
        foreach (var chunk in _chunks.Values)
        {
            foreach (var prop in chunk.Flora)
            {
                var dx = prop.Feet.X - camera.X;
                var dz = prop.Feet.Z - camera.Z;
                var d = dx * dx + dz * dz;
                if (d > maxDistSq) continue;
                ranked.Add((d, prop));
            }
        }

        ranked.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var take = Math.Min(Cap, ranked.Count);
        for (var i = 0; i < take; i++)
            into.Add(ranked[i].Prop);
    }

    public void Draw(Matrix view, Matrix projection, Vector3 camera, Vector3 fogColour)
    {
        var cx = (int)MathF.Floor(camera.X / WorldScale.ChunkMetres);
        var cz = (int)MathF.Floor(camera.Z / WorldScale.ChunkMetres);

        _effect.World = Matrix.Identity;
        _effect.View = view;
        _effect.Projection = projection;
        _effect.FogColor = fogColour;
        _effect.Texture = StoneTextures.Earth(_device);

        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.LinearWrap;
        _device.Indices = _indices;

        var ring = WorldScale.ChunkRing;
        for (var z = cz - ring; z <= cz + ring; z++)
        for (var x = cx - ring; x <= cx + ring; x++)
        {
            if (!InWorld(x, z)) continue;
            var chunk = GetChunk(x, z);
            _device.SetVertexBuffer(chunk.Buffer);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _triangleCount);
            }
        }

        _device.SetVertexBuffer(null);
        _device.Indices = null;
    }

    private Chunk GetChunk(int cx, int cz)
    {
        var key = (cx, cz);
        if (_chunks.TryGetValue(key, out var chunk)) return chunk;

        chunk = BuildChunk(cx, cz);
        _chunks[key] = chunk;
        if (_chunks.Count > _cacheLimit)
            DropFar(cx, cz);
        return chunk;
    }

    private void DropFar(int cx, int cz)
    {
        (int, int)? victim = null;
        var worst = 0;
        var keep = WorldScale.ChunkRing + 1;
        foreach (var key in _chunks.Keys)
        {
            var d = Math.Abs(key.Item1 - cx) + Math.Abs(key.Item2 - cz);
            if (d <= keep) continue;
            if (d > worst)
            {
                worst = d;
                victim = key;
            }
        }

        if (victim is not { } drop) return;
        _chunks[drop].Dispose();
        _chunks.Remove(drop);
    }

    private Chunk BuildChunk(int cx, int cz)
    {
        var originX = cx * WorldScale.ChunkMetres;
        var originZ = cz * WorldScale.ChunkMetres;
        var vertices = new VertexPositionColorTexture[_stride * _stride];
        var spacing = WorldScale.VertexSpacing;

        for (var z = 0; z < _stride; z++)
        for (var x = 0; x < _stride; x++)
        {
            var wx = originX + x * spacing;
            var wz = originZ + z * spacing;
            var wy = _heights.Sample(wx, wz);
            var biome = _noise.BiomeAt(wx, wz);
            Color tint;
            if (_heights.PadNear(wx, wz, 50f))
                tint = new Color(168, 148, 116);
            else if (wy < WorldScale.WaterLevel)
                tint = Color.Lerp(new Color(70, 96, 88), Biomes.Ground(BiomeKind.Ocean), 0.45f);
            else
                tint = Color.Lerp(Color.White, Biomes.Ground(biome), 0.62f);
            vertices[z * _stride + x] = new VertexPositionColorTexture(
                new Vector3(wx, wy, wz),
                tint,
                new Vector2(wx / 6f, wz / 6f));
        }

        var buffer = new VertexBuffer(_device, typeof(VertexPositionColorTexture), vertices.Length,
            BufferUsage.WriteOnly);
        buffer.SetData(vertices);
        return new Chunk(buffer, ScatterFlora(cx, cz, originX, originZ));
    }

    private List<BillboardProp> ScatterFlora(int cx, int cz, float originX, float originZ)
    {
        var flora = new List<BillboardProp>();
        var rng = new Random(Hash(_seed, cx, cz));
        var midX = originX + WorldScale.ChunkMetres * 0.5f;
        var midZ = originZ + WorldScale.ChunkMetres * 0.5f;
        var biome = _noise.BiomeAt(midX, midZ);
        var count = biome switch
        {
            BiomeKind.Ocean => 0,
            BiomeKind.Coast => 8,
            BiomeKind.Marsh => 12,
            BiomeKind.Grass => 14,
            BiomeKind.Forest => 22,
            BiomeKind.Desert => 12,
            BiomeKind.Hills => 12,
            BiomeKind.Mountain => 5,
            BiomeKind.Snow => 5,
            _ => 10
        };

        for (var i = 0; i < count; i++)
        {
            var x = originX + 6f + (float)rng.NextDouble() * (WorldScale.ChunkMetres - 12f);
            var z = originZ + 6f + (float)rng.NextDouble() * (WorldScale.ChunkMetres - 12f);
            if (_heights.PadNear(x, z, WorldScale.TownPadRadius + 8f)) continue;
            var y = _heights.Sample(x, z);
            if (y <= WorldScale.WaterLevel + 0.45f) continue;

            var (sprite, height, tint) = PickPlant(biome, rng);
            flora.Add(new BillboardProp(sprite, new Vector3(x, y, z), height, 0f, tint));
        }

        return flora;
    }

    private static (string Sprite, float Height, Color Tint) PickPlant(BiomeKind biome, Random rng)
    {
        return biome switch
        {
            BiomeKind.Desert => rng.NextDouble() < 0.22
                ? ("boulder", 0.9f + (float)rng.NextDouble() * 0.7f, new Color(196, 168, 118))
                : ("palm", 5.8f + (float)rng.NextDouble() * 2.2f, Color.White),
            BiomeKind.Snow => rng.NextDouble() < 0.35
                ? ("boulder", 1.0f + (float)rng.NextDouble() * 0.8f, new Color(210, 216, 222))
                : ("pine", 5.4f + (float)rng.NextDouble() * 2.4f, new Color(186, 204, 198)),
            BiomeKind.Mountain => rng.NextDouble() < 0.55
                ? ("boulder", 1.3f + (float)rng.NextDouble() * 1.1f, new Color(148, 148, 152))
                : ("pine", 4.6f + (float)rng.NextDouble() * 1.8f, new Color(120, 140, 118)),
            BiomeKind.Marsh => rng.NextDouble() < 0.3
                ? ("boulder", 0.8f + (float)rng.NextDouble() * 0.5f, new Color(90, 96, 70))
                : ("oak", 3.6f + (float)rng.NextDouble() * 1.4f, new Color(70, 102, 58)),
            BiomeKind.Coast => rng.NextDouble() < 0.4
                ? ("boulder", 0.85f + (float)rng.NextDouble() * 0.7f, new Color(176, 164, 128))
                : ("palm", 5.2f + (float)rng.NextDouble() * 1.8f, Color.White),
            BiomeKind.Forest => rng.NextDouble() < 0.28
                ? ("oak", 4.4f + (float)rng.NextDouble() * 1.8f, new Color(72, 108, 58))
                : ("pine", 5.8f + (float)rng.NextDouble() * 2.4f, new Color(52, 92, 48)),
            BiomeKind.Hills => rng.NextDouble() < 0.4
                ? ("boulder", 1.1f + (float)rng.NextDouble() * 0.8f, new Color(132, 124, 98))
                : ("pine", 5.0f + (float)rng.NextDouble() * 2.0f, new Color(88, 114, 70)),
            _ => rng.NextDouble() switch
            {
                < 0.18 => ("boulder", 1.0f + (float)rng.NextDouble() * 0.6f, Color.White),
                < 0.55 => ("oak", 4.0f + (float)rng.NextDouble() * 1.6f, new Color(98, 128, 62)),
                _ => ("pine", 5.2f + (float)rng.NextDouble() * 2.0f, new Color(70, 108, 58))
            }
        };
    }

    private static int Hash(int seed, int x, int z)
    {
        var n = seed * 374761393 + x * 668265263 + z * 1274126177;
        n = (n ^ (n >> 13)) * 1274126177;
        return n ^ (n >> 16);
    }

    private static bool InWorld(int cx, int cz)
    {
        var chunks = (int)(WorldScale.WorldMetres / WorldScale.ChunkMetres);
        return cx >= 0 && cz >= 0 && cx < chunks && cz < chunks;
    }

    private static short[] BuildIndices(int stride)
    {
        var indices = new short[(stride - 1) * (stride - 1) * 6];
        var i = 0;
        for (var z = 0; z < stride - 1; z++)
        for (var x = 0; x < stride - 1; x++)
        {
            var s = (short)(z * stride + x);
            indices[i++] = s;
            indices[i++] = (short)(s + stride);
            indices[i++] = (short)(s + 1);
            indices[i++] = (short)(s + 1);
            indices[i++] = (short)(s + stride);
            indices[i++] = (short)(s + stride + 1);
        }

        return indices;
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks.Values) chunk.Dispose();
        _chunks.Clear();
        _indices.Dispose();
        _effect.Dispose();
    }

    private sealed class Chunk : IDisposable
    {
        public VertexBuffer Buffer { get; }
        public List<BillboardProp> Flora { get; }

        public Chunk(VertexBuffer buffer, List<BillboardProp> flora)
        {
            Buffer = buffer;
            Flora = flora;
        }

        public void Dispose() => Buffer.Dispose();
    }
}

/// <summary>
/// Heights on the same 8 m lattice the mesh uses, so walk Y matches the triangles.
/// Town pads are hashed; a Daggerfall-scale site list is not scanned per sample.
/// </summary>
public sealed class WorldHeights
{
    private readonly HeightNoise _noise;
    private readonly PadHash _pads;
    private readonly float _padRadius;
    private readonly List<int> _scratch = new();
    private readonly int _maxIndex;

    public WorldHeights(HeightNoise noise, IReadOnlyList<Vector3> flattenPads, float padRadius)
    {
        _noise = noise;
        var copy = new Vector3[flattenPads.Count];
        for (var i = 0; i < flattenPads.Count; i++)
            copy[i] = flattenPads[i];
        _pads = new PadHash(copy);
        _padRadius = padRadius;
        _maxIndex = (int)(WorldScale.WorldMetres / WorldScale.VertexSpacing);
    }

    public bool PadNear(float x, float z, float radius) => _pads.Any(x, z, radius);

    public float Sample(float x, float z)
    {
        x = Math.Clamp(x, 0f, WorldScale.WorldMetres);
        z = Math.Clamp(z, 0f, WorldScale.WorldMetres);
        var spacing = WorldScale.VertexSpacing;
        var gx = x / spacing;
        var gz = z / spacing;
        var x0 = Math.Clamp((int)MathF.Floor(gx), 0, _maxIndex - 1);
        var z0 = Math.Clamp((int)MathF.Floor(gz), 0, _maxIndex - 1);
        var tx = Math.Clamp(gx - x0, 0f, 1f);
        var tz = Math.Clamp(gz - z0, 0f, 1f);
        if (tx <= 0.0001f && tz <= 0.0001f)
            return Vertex(x0, z0);

        var h00 = Vertex(x0, z0);
        var h10 = Vertex(x0 + 1, z0);
        var h01 = Vertex(x0, z0 + 1);
        var h11 = Vertex(x0 + 1, z0 + 1);
        return MathHelper.Lerp(MathHelper.Lerp(h00, h10, tx), MathHelper.Lerp(h01, h11, tx), tz);
    }

    private float Vertex(int ix, int iz)
    {
        ix = Math.Clamp(ix, 0, _maxIndex);
        iz = Math.Clamp(iz, 0, _maxIndex);
        return Raw(ix * WorldScale.VertexSpacing, iz * WorldScale.VertexSpacing);
    }

    private float Raw(float x, float z)
    {
        var h = _noise.Height(x, z);
        _pads.Query(x, z, _padRadius, _scratch);
        foreach (var index in _scratch)
        {
            var pad = _pads.Pads[index];
            var dx = x - pad.X;
            var dz = z - pad.Z;
            var d2 = dx * dx + dz * dz;
            var t = 1f - MathHelper.Clamp(MathF.Sqrt(d2) / _padRadius, 0f, 1f);
            if (t > 0f)
                h = MathHelper.Lerp(h, pad.Y, t * t * (3f - 2f * t));
        }

        return h;
    }

    public float SampleWalk(float x, float z)
    {
        var h = Sample(x, z);
        return h < WorldScale.WaterLevel ? WorldScale.WaterLevel : h;
    }
}

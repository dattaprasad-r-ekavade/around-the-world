using System;
using System.Collections.Generic;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Ember.Render;

/// <summary>Draws a cached square of source-driven terrain chunks using vertex material tints.</summary>
public sealed class HeightmapTerrainRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ITerrainHeightMaterialSource _source;
    private readonly TerrainChunkSettings _settings;
    private readonly int _chunksAroundCamera;
    private readonly BasicEffect _effect;
    private readonly IndexBuffer _indices;
    private readonly int _primitiveCount;
    private readonly Dictionary<(int X, int Z), TerrainGpuChunk> _chunks = new();
    private bool _disposed;

    public HeightmapTerrainRenderer(GraphicsDevice device, ITerrainHeightMaterialSource source,
        TerrainChunkSettings settings, int chunksAroundCamera = 1)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (chunksAroundCamera < 0 || chunksAroundCamera > 16)
            throw new ArgumentOutOfRangeException(nameof(chunksAroundCamera));
        _chunksAroundCamera = chunksAroundCamera;
        var stride = settings.GetVertexStride();
        var indices = TerrainChunkMeshBuilder.BuildTriangleIndices(stride);
        _primitiveCount = indices.Length / 3;
        var maximumIndex = stride * stride - 1;
        if (maximumIndex <= ushort.MaxValue)
        {
            var compact = new ushort[indices.Length];
            for (var index = 0; index < compact.Length; index++) compact[index] = (ushort)indices[index];
            _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, compact.Length, BufferUsage.WriteOnly);
            _indices.SetData(compact);
        }
        else
        {
            var wide = new uint[indices.Length];
            for (var index = 0; index < wide.Length; index++) wide[index] = (uint)indices[index];
            _indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, wide.Length, BufferUsage.WriteOnly);
            _indices.SetData(wide);
        }

        _effect = new BasicEffect(device)
        {
            LightingEnabled = false,
            TextureEnabled = false,
            VertexColorEnabled = true,
            FogEnabled = false
        };
    }

    public int CachedChunkCount => _chunks.Count;

    public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition)
    {
        ThrowIfDisposed();
        if (!IsFinite(cameraPosition))
            throw new ArgumentOutOfRangeException(nameof(cameraPosition), "Terrain camera position must be finite.");
        var cameraX = MathF.Floor(cameraPosition.X / _settings.ChunkSize);
        var cameraZ = MathF.Floor(cameraPosition.Z / _settings.ChunkSize);
        if (cameraX < int.MinValue || cameraX > int.MaxValue || cameraZ < int.MinValue || cameraZ > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cameraPosition), "Terrain camera position exceeds chunk coordinates.");
        var centerX = (int)cameraX;
        var centerZ = (int)cameraZ;
        TrimCache(centerX, centerZ);

        var oldBlend = _device.BlendState;
        var oldDepth = _device.DepthStencilState;
        var oldRasterizer = _device.RasterizerState;
        try
        {
            _device.BlendState = BlendState.Opaque;
            _device.DepthStencilState = DepthStencilState.Default;
            _device.RasterizerState = RasterizerState.CullNone;
            _device.Indices = _indices;
            _effect.World = Matrix.Identity;
            _effect.View = view;
            _effect.Projection = projection;

            for (var z = centerZ - _chunksAroundCamera; z <= centerZ + _chunksAroundCamera; z++)
            for (var x = centerX - _chunksAroundCamera; x <= centerX + _chunksAroundCamera; x++)
            {
                var chunk = GetChunk(x, z);
                _device.SetVertexBuffer(chunk.Vertices);
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
                }
            }
        }
        finally
        {
            _device.SetVertexBuffer(null);
            _device.Indices = null;
            _device.BlendState = oldBlend;
            _device.DepthStencilState = oldDepth;
            _device.RasterizerState = oldRasterizer;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var chunk in _chunks.Values) chunk.Dispose();
        _chunks.Clear();
        _indices.Dispose();
        _effect.Dispose();
    }

    private TerrainGpuChunk GetChunk(int x, int z)
    {
        if (_chunks.TryGetValue((x, z), out var chunk)) return chunk;
        var data = TerrainChunkMeshBuilder.Build(_source, x, z, _settings);
        var vertices = new VertexPositionColorTexture[data.Vertices.Count];
        for (var index = 0; index < vertices.Length; index++)
        {
            var vertex = data.Vertices[index];
            vertices[index] = new VertexPositionColorTexture(vertex.Position, vertex.Tint, vertex.TextureCoordinate);
        }
        var buffer = new VertexBuffer(_device, VertexPositionColorTexture.VertexDeclaration,
            vertices.Length, BufferUsage.WriteOnly);
        try
        {
            buffer.SetData(vertices);
            chunk = new TerrainGpuChunk(buffer);
            _chunks.Add((x, z), chunk);
            return chunk;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private void TrimCache(int centerX, int centerZ)
    {
        var retention = _chunksAroundCamera + 1;
        List<(int X, int Z)>? expired = null;
        foreach (var key in _chunks.Keys)
            if (Math.Abs((long)key.X - centerX) > retention || Math.Abs((long)key.Z - centerZ) > retention)
                (expired ??= new()).Add(key);
        if (expired is null) return;
        foreach (var key in expired)
        {
            _chunks[key].Dispose();
            _chunks.Remove(key);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HeightmapTerrainRenderer));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private sealed class TerrainGpuChunk(VertexBuffer vertices) : IDisposable
    {
        public VertexBuffer Vertices { get; } = vertices;
        public void Dispose() => Vertices.Dispose();
    }
}

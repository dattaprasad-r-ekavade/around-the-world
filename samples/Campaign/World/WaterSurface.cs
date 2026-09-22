using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Campaign;

/// <summary>
/// Opaque ripple plane at sea level. Land taller than this hides it; basins and ocean show it.
/// </summary>
public sealed class WaterSurface : IDisposable
{
    private const int Extent = 14;
    private const float Tile = 16f;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly Texture2D _ripple;
    private readonly VertexBuffer _buffer;
    private readonly IndexBuffer _indices;
    private readonly int _triangles;

    public WaterSurface(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device)
        {
            LightingEnabled = false,
            TextureEnabled = true,
            VertexColorEnabled = true,
            FogEnabled = true,
            FogStart = WorldScale.FogStart,
            FogEnd = WorldScale.FogEnd,
            DiffuseColor = Vector3.One
        };
        _ripple = BuildRipple(device);

        var stride = Extent * 2 + 1;
        var verts = new VertexPositionColorTexture[stride * stride];
        var water = new Color(48, 118, 196);
        for (var z = 0; z < stride; z++)
        for (var x = 0; x < stride; x++)
        {
            var lx = (x - Extent) * Tile;
            var lz = (z - Extent) * Tile;
            verts[z * stride + x] = new VertexPositionColorTexture(
                new Vector3(lx, 0f, lz),
                water,
                new Vector2(lx / 10f, lz / 10f));
        }

        _buffer = new VertexBuffer(device, typeof(VertexPositionColorTexture), verts.Length,
            BufferUsage.WriteOnly);
        _buffer.SetData(verts);

        var indexData = new short[(stride - 1) * (stride - 1) * 6];
        var i = 0;
        for (var z = 0; z < stride - 1; z++)
        for (var x = 0; x < stride - 1; x++)
        {
            var s = (short)(z * stride + x);
            indexData[i++] = s;
            indexData[i++] = (short)(s + stride);
            indexData[i++] = (short)(s + 1);
            indexData[i++] = (short)(s + 1);
            indexData[i++] = (short)(s + stride);
            indexData[i++] = (short)(s + stride + 1);
        }

        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, indexData.Length,
            BufferUsage.WriteOnly);
        _indices.SetData(indexData);
        _triangles = indexData.Length / 3;
    }

    public void Draw(Matrix view, Matrix projection, Vector3 camera, Vector3 fog, float seconds)
    {
        var snapX = MathF.Floor(camera.X / Tile) * Tile;
        var snapZ = MathF.Floor(camera.Z / Tile) * Tile;

        _effect.World = Matrix.CreateTranslation(snapX, WorldScale.WaterLevel, snapZ);
        _effect.View = view;
        _effect.Projection = projection;
        _effect.FogColor = fog;
        _effect.Texture = _ripple;
        _effect.TextureEnabled = true;

        var scroll = seconds * 0.035f;
        // BasicEffect has no UV offset; bake scroll into World XZ is wrong. Tint pulse instead,
        // and slide by rebuilding is expensive — use Texture transform via World on texture
        // matrix is not available. Scroll by rotating a tiny amount of vertex colour pulse.
        var pulse = 0.92f + MathF.Sin(seconds * 1.4f) * 0.06f;
        _effect.DiffuseColor = new Vector3(0.55f * pulse, 0.82f * pulse, 1f);

        var previousBlend = _device.BlendState;
        var previousRaster = _device.RasterizerState;
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.PointWrap;
        _device.SetVertexBuffer(_buffer);
        _device.Indices = _indices;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _triangles);
        }

        _device.SetVertexBuffer(null);
        _device.Indices = null;
        _device.BlendState = previousBlend;
        _device.RasterizerState = previousRaster;
        _ = scroll;
    }

    private static Texture2D BuildRipple(GraphicsDevice device)
    {
        const int Size = 64;
        var pixels = new Color[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var u = x / (float)Size;
            var v = y / (float)Size;
            var wave = MathF.Sin((u * 9f + v * 3f) * MathF.Tau) * 0.5f
                + MathF.Sin((u * 3f - v * 11f) * MathF.Tau) * 0.35f;
            var t = MathHelper.Clamp(0.55f + wave * 0.22f, 0.28f, 0.95f);
            pixels[y * Size + x] = new Color(
                (byte)(36 + t * 40),
                (byte)(90 + t * 70),
                (byte)(150 + t * 90));
        }

        var texture = new Texture2D(device, Size, Size);
        texture.SetData(pixels);
        return texture;
    }

    public void Dispose()
    {
        _buffer.Dispose();
        _indices.Dispose();
        _ripple.Dispose();
        _effect.Dispose();
    }
}

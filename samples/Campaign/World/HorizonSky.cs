using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Campaign;

/// <summary>
/// Painted skydome and a mountain strip that sits on the far plane — Daggerfall's horizon,
/// not reachable terrain.
/// </summary>
public sealed class HorizonSky : IDisposable
{
    private const int Slices = 24;
    private const int Stacks = 8;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly Texture2D _sky;
    private readonly Texture2D _peaks;
    private readonly VertexBuffer _dome;
    private readonly IndexBuffer _domeIndex;
    private readonly int _domeTris;
    private readonly VertexBuffer _ring;
    private readonly int _ringVerts;

    public HorizonSky(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device)
        {
            LightingEnabled = false,
            TextureEnabled = true,
            VertexColorEnabled = true,
            FogEnabled = false
        };
        _sky = BuildSky(device);
        _peaks = BuildPeaks(device);
        (_dome, _domeIndex, _domeTris) = BuildDome(device);
        (_ring, _ringVerts) = BuildRing(device);
    }

    public void Draw(Matrix view, Matrix projection, Vector3 camera, Color zenith, Color haze)
    {
        var previousDepth = _device.DepthStencilState;
        var previousBlend = _device.BlendState;
        var previousRaster = _device.RasterizerState;

        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.PointWrap;

        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = Matrix.CreateTranslation(camera.X, camera.Y, camera.Z);
        _effect.DiffuseColor = new Vector3(zenith.R, zenith.G, zenith.B) / 255f;

        _device.BlendState = BlendState.Opaque;
        _effect.Texture = _sky;
        _device.SetVertexBuffer(_dome);
        _device.Indices = _domeIndex;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _domeTris);
        }

        _device.BlendState = BlendState.AlphaBlend;
        _effect.Texture = _peaks;
        _effect.DiffuseColor = new Vector3(haze.R, haze.G, haze.B) / 255f;
        _device.SetVertexBuffer(_ring);
        _device.Indices = null;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawPrimitives(PrimitiveType.TriangleList, 0, _ringVerts / 3);
        }

        _effect.DiffuseColor = Vector3.One;
        _device.SetVertexBuffer(null);
        _device.Indices = null;
        _device.DepthStencilState = previousDepth;
        _device.BlendState = previousBlend;
        _device.RasterizerState = previousRaster;
    }

    private static (VertexBuffer, IndexBuffer, int) BuildDome(GraphicsDevice device)
    {
        var radius = WorldScale.FarPlane * 0.96f;
        var verts = new VertexPositionColorTexture[(Stacks + 1) * (Slices + 1)];
        for (var y = 0; y <= Stacks; y++)
        {
            var v = y / (float)Stacks;
            var pitch = MathHelper.Lerp(0f, MathHelper.PiOver2, v);
            var cy = MathF.Sin(pitch);
            var cr = MathF.Cos(pitch);
            var shade = MathHelper.Lerp(1f, 0.72f, v);
            var colour = new Color(shade, shade, shade);
            for (var x = 0; x <= Slices; x++)
            {
                var u = x / (float)Slices;
                var yaw = u * MathF.Tau;
                verts[y * (Slices + 1) + x] = new VertexPositionColorTexture(
                    new Vector3(MathF.Cos(yaw) * cr * radius, cy * radius, MathF.Sin(yaw) * cr * radius),
                    colour,
                    new Vector2(u * 2f, 1f - v));
            }
        }

        var indices = new short[Stacks * Slices * 6];
        var i = 0;
        for (var y = 0; y < Stacks; y++)
        for (var x = 0; x < Slices; x++)
        {
            var s = (short)(y * (Slices + 1) + x);
            indices[i++] = s;
            indices[i++] = (short)(s + Slices + 1);
            indices[i++] = (short)(s + 1);
            indices[i++] = (short)(s + 1);
            indices[i++] = (short)(s + Slices + 1);
            indices[i++] = (short)(s + Slices + 2);
        }

        var vb = new VertexBuffer(device, typeof(VertexPositionColorTexture), verts.Length,
            BufferUsage.WriteOnly);
        vb.SetData(verts);
        var ib = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length,
            BufferUsage.WriteOnly);
        ib.SetData(indices);
        return (vb, ib, indices.Length / 3);
    }

    private static (VertexBuffer, int) BuildRing(GraphicsDevice device)
    {
        var radius = WorldScale.FarPlane * 0.90f;
        var baseY = -12f;
        var peakY = 52f;
        var verts = new VertexPositionColorTexture[Slices * 6];
        var i = 0;
        for (var s = 0; s < Slices; s++)
        {
            var u0 = s / (float)Slices;
            var u1 = (s + 1) / (float)Slices;
            var a0 = u0 * MathF.Tau;
            var a1 = u1 * MathF.Tau;
            var p00 = new Vector3(MathF.Cos(a0) * radius, baseY, MathF.Sin(a0) * radius);
            var p10 = new Vector3(MathF.Cos(a1) * radius, baseY, MathF.Sin(a1) * radius);
            var p01 = new Vector3(MathF.Cos(a0) * radius, peakY, MathF.Sin(a0) * radius);
            var p11 = new Vector3(MathF.Cos(a1) * radius, peakY, MathF.Sin(a1) * radius);
            var c = Color.White;
            verts[i++] = new VertexPositionColorTexture(p00, c, new Vector2(u0 * 3f, 1f));
            verts[i++] = new VertexPositionColorTexture(p01, c, new Vector2(u0 * 3f, 0f));
            verts[i++] = new VertexPositionColorTexture(p10, c, new Vector2(u1 * 3f, 1f));
            verts[i++] = new VertexPositionColorTexture(p10, c, new Vector2(u1 * 3f, 1f));
            verts[i++] = new VertexPositionColorTexture(p01, c, new Vector2(u0 * 3f, 0f));
            verts[i++] = new VertexPositionColorTexture(p11, c, new Vector2(u1 * 3f, 0f));
        }

        var vb = new VertexBuffer(device, typeof(VertexPositionColorTexture), verts.Length,
            BufferUsage.WriteOnly);
        vb.SetData(verts);
        return (vb, verts.Length);
    }

    private static Texture2D BuildSky(GraphicsDevice device)
    {
        const int W = 128;
        const int H = 96;
        var pixels = new Color[W * H];
        var rng = new Random(0x51);
        for (var y = 0; y < H; y++)
        {
            var t = y / (float)(H - 1);
            var zenith = new Color(92, 148, 206);
            var mid = new Color(168, 196, 220);
            var haze = new Color(214, 214, 210);
            var row = t < 0.55f
                ? Color.Lerp(zenith, mid, t / 0.55f)
                : Color.Lerp(mid, haze, (t - 0.55f) / 0.45f);
            for (var x = 0; x < W; x++)
            {
                var cloud = 0f;
                if (t > 0.48f && t < 0.96f)
                {
                    var nx = x / 14f + t * 4f;
                    var n = MathF.Sin(nx * 2.1f) * 0.5f + MathF.Sin(nx * 5.3f + 1.7f) * 0.4f;
                    n += (rng.Next(0, 18) - 9) * 0.012f;
                    var band = 1f - MathF.Abs((t - 0.72f) / 0.24f);
                    cloud = MathHelper.Clamp(n * MathF.Max(0f, band) + 0.15f, 0f, 1f);
                }

                var colour = Color.Lerp(row, new Color(236, 236, 232), cloud * 0.72f);
                pixels[y * W + x] = colour;
            }
        }

        var texture = new Texture2D(device, W, H);
        texture.SetData(pixels);
        return texture;
    }

    private static Texture2D BuildPeaks(GraphicsDevice device)
    {
        const int W = 256;
        const int H = 64;
        var pixels = new Color[W * H];
        var rng = new Random(0xA41);
        var ridge = new float[W];
        var h = 0.45f;
        for (var x = 0; x < W; x++)
        {
            h += (float)(rng.NextDouble() - 0.5) * 0.12f;
            h = MathHelper.Clamp(h, 0.18f, 0.85f);
            ridge[x] = h;
        }

        for (var i = 0; i < 8; i++)
        {
            var copy = (float[])ridge.Clone();
            for (var x = 0; x < W; x++)
                ridge[x] = (copy[(x + W - 1) % W] + copy[x] + copy[(x + 1) % W]) / 3f;
        }

        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var v = 1f - y / (float)(H - 1);
            if (v > ridge[x])
            {
                pixels[y * W + x] = Color.Transparent;
                continue;
            }

            var shade = MathHelper.Lerp(0.55f, 1f, v / Math.Max(0.01f, ridge[x]));
            pixels[y * W + x] = new Color(
                (byte)(118 * shade + 40),
                (byte)(132 * shade + 48),
                (byte)(158 * shade + 52),
                (byte)220);
        }

        var texture = new Texture2D(device, W, H);
        texture.SetData(pixels);
        return texture;
    }

    public void Dispose()
    {
        _dome.Dispose();
        _domeIndex.Dispose();
        _ring.Dispose();
        _sky.Dispose();
        _peaks.Dispose();
        _effect.Dispose();
    }
}

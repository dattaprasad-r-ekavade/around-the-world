using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Ember.Render;

/// <summary>Builds a tiled, horizontal water plane in local space.</summary>
public static class WaterSurfaceGeometry
{
    public static (VertexPositionColorTexture[] Vertices, short[] Indices) CreatePlane(
        float halfExtent, float textureTileMetres)
    {
        if (!float.IsFinite(halfExtent) || halfExtent <= 0f)
            throw new ArgumentOutOfRangeException(nameof(halfExtent), "Water extent must be finite and positive.");
        if (!float.IsFinite(textureTileMetres) || textureTileMetres <= 0f)
            throw new ArgumentOutOfRangeException(nameof(textureTileMetres), "Water texture tile size must be finite and positive.");

        var uvExtent = halfExtent / textureTileMetres;
        var vertices = new[]
        {
            new VertexPositionColorTexture(new Vector3(-halfExtent, 0f, -halfExtent), Color.White, new Vector2(0f, 0f)),
            new VertexPositionColorTexture(new Vector3(-halfExtent, 0f, halfExtent), Color.White, new Vector2(0f, uvExtent * 2f)),
            new VertexPositionColorTexture(new Vector3(halfExtent, 0f, -halfExtent), Color.White, new Vector2(uvExtent * 2f, 0f)),
            new VertexPositionColorTexture(new Vector3(halfExtent, 0f, halfExtent), Color.White, new Vector2(uvExtent * 2f, uvExtent * 2f))
        };
        return (vertices, new short[] { 0, 1, 2, 2, 1, 3 });
    }
}

/// <summary>
/// Draws a translucent, camera-following water plane over opaque terrain. Opaque geometry must
/// be drawn first; this pass reads the scene depth but never writes water depth.
/// </summary>
public sealed class WaterSurfaceRenderer : IDisposable
{
    private const float Alpha = 0.72f;
    private const int RippleTextureSize = 32;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly Texture2D _ripple;
    private readonly VertexBuffer _vertices;
    private readonly IndexBuffer _indices;
    private bool _disposed;

    public WaterSurfaceRenderer(GraphicsDevice device, float halfExtent, float textureTileMetres = 8f)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!float.IsFinite(halfExtent) || halfExtent <= 0f)
            throw new ArgumentOutOfRangeException(nameof(halfExtent), "Water extent must be finite and positive.");
        if (!float.IsFinite(textureTileMetres) || textureTileMetres <= 0f)
            throw new ArgumentOutOfRangeException(nameof(textureTileMetres), "Water texture tile size must be finite and positive.");

        _device = device;
        TextureTileMetres = textureTileMetres;
        HalfExtent = halfExtent;
        var effect = new BasicEffect(device)
        {
            LightingEnabled = false,
            TextureEnabled = true,
            VertexColorEnabled = true,
            FogEnabled = true,
            DiffuseColor = new Vector3(0.76f, 0.91f, 1f),
            Alpha = Alpha
        };
        Texture2D? ripple = null;
        VertexBuffer? vertexBuffer = null;
        IndexBuffer? indexBuffer = null;
        try
        {
            ripple = BuildRipple(device);
            var (vertices, indices) = WaterSurfaceGeometry.CreatePlane(halfExtent, textureTileMetres);
            vertexBuffer = new VertexBuffer(device, typeof(VertexPositionColorTexture), vertices.Length,
                BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);
            indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length,
                BufferUsage.WriteOnly);
            indexBuffer.SetData(indices);
        }
        catch
        {
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            ripple?.Dispose();
            effect.Dispose();
            throw;
        }

        _effect = effect;
        _ripple = ripple ?? throw new InvalidOperationException("Water ripple texture was not created.");
        _vertices = vertexBuffer ?? throw new InvalidOperationException("Water vertex buffer was not created.");
        _indices = indexBuffer ?? throw new InvalidOperationException("Water index buffer was not created.");
    }

    public float HalfExtent { get; }
    public float TextureTileMetres { get; }

    /// <summary>Draw water at one sea level with depth-read, no-depth-write alpha blending.</summary>
    public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition,
        OutdoorEnvironmentState environment, float waterLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsFinite(cameraPosition))
            throw new ArgumentOutOfRangeException(nameof(cameraPosition), "Camera position must be finite.");
        if (!float.IsFinite(waterLevel))
            throw new ArgumentOutOfRangeException(nameof(waterLevel), "Water level must be finite.");

        _effect.View = view;
        _effect.Projection = projection;
        _effect.FogColor = environment.FogColor.ToVector3();
        _effect.FogStart = environment.FogStart;
        _effect.FogEnd = environment.FogEnd;
        _effect.Texture = _ripple;
        _effect.Alpha = Alpha;

        // Snap by one full texture tile so the plane follows the camera without visible UV jumps.
        var snapX = MathF.Floor(cameraPosition.X / TextureTileMetres) * TextureTileMetres;
        var snapZ = MathF.Floor(cameraPosition.Z / TextureTileMetres) * TextureTileMetres;
        _effect.World = Matrix.CreateTranslation(snapX, waterLevel, snapZ);

        var previousBlend = _device.BlendState;
        var previousDepth = _device.DepthStencilState;
        var previousRasterizer = _device.RasterizerState;
        var previousSampler = _device.SamplerStates[0];
        try
        {
            _device.BlendState = BlendState.AlphaBlend;
            _device.DepthStencilState = DepthStencilState.DepthRead;
            _device.RasterizerState = RasterizerState.CullNone;
            _device.SamplerStates[0] = SamplerState.LinearWrap;
            _device.SetVertexBuffer(_vertices);
            _device.Indices = _indices;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
            }
        }
        finally
        {
            _device.SetVertexBuffer(null);
            _device.Indices = null;
            _device.BlendState = previousBlend;
            _device.DepthStencilState = previousDepth;
            _device.RasterizerState = previousRasterizer;
            _device.SamplerStates[0] = previousSampler;
        }
    }

    private static Texture2D BuildRipple(GraphicsDevice device)
    {
        var pixels = new Color[RippleTextureSize * RippleTextureSize];
        for (var y = 0; y < RippleTextureSize; y++)
        for (var x = 0; x < RippleTextureSize; x++)
        {
            var u = x / (float)RippleTextureSize;
            var v = y / (float)RippleTextureSize;
            var wave = MathF.Sin((u * 7f + v * 3f) * MathF.Tau) * 0.55f
                + MathF.Sin((u * 2f - v * 9f) * MathF.Tau) * 0.45f;
            var amount = MathHelper.Clamp(0.56f + wave * 0.18f, 0.25f, 0.9f);
            pixels[y * RippleTextureSize + x] = new Color(
                (byte)(23f + amount * 24f), (byte)(80f + amount * 68f), (byte)(128f + amount * 92f));
        }

        var texture = new Texture2D(device, RippleTextureSize, RippleTextureSize);
        texture.SetData(pixels);
        return texture;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vertices.Dispose();
        _indices.Dispose();
        _ripple.Dispose();
        _effect.Dispose();
    }
}

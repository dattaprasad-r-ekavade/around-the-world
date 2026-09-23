using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CharacterStudio;

/// <summary>Small MonoGame backend for the textured triangles emitted by ImGui.NET.</summary>
internal sealed class ImGuiMonoGameRenderer : IDisposable
{
    private static readonly IntPtr FontTextureId = new(1);

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly RasterizerState _rasterizer = new()
    {
        CullMode = CullMode.None,
        ScissorTestEnable = true
    };
    private readonly Dictionary<IntPtr, Texture2D> _textures = new();
    private Texture2D? _fontTexture;
    private DynamicVertexBuffer? _vertexBuffer;
    private DynamicIndexBuffer? _indexBuffer;
    private int _vertexCapacity;
    private int _indexCapacity;
    private bool _disposed;

    public ImGuiMonoGameRenderer(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _effect = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            LightingEnabled = false
        };

        try
        {
            CreateFontTexture();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Viewport Viewport => _device.Viewport;

    public void Draw(ImDrawDataPtr drawData, int letterboxOffsetX, int letterboxOffsetY)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ImGuiMonoGameRenderer));
        if (!drawData.Valid || drawData.CmdListsCount == 0) return;

        var framebufferScale = drawData.FramebufferScale;
        var framebufferWidth = (int)(drawData.DisplaySize.X * framebufferScale.X);
        var framebufferHeight = (int)(drawData.DisplaySize.Y * framebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0) return;

        var oldBlend = _device.BlendState;
        var oldDepth = _device.DepthStencilState;
        var oldRasterizer = _device.RasterizerState;
        var oldScissor = _device.ScissorRectangle;
        var oldSampler = _device.SamplerStates[0];
        var oldViewport = _device.Viewport;
        try
        {
            var viewportX = oldViewport.X + letterboxOffsetX;
            var viewportY = oldViewport.Y + letterboxOffsetY;
            _device.Viewport = new Viewport(viewportX, viewportY,
                Math.Min(framebufferWidth, oldViewport.Width), Math.Min(framebufferHeight, oldViewport.Height));
            _device.BlendState = BlendState.NonPremultiplied;
            _device.DepthStencilState = DepthStencilState.None;
            _device.RasterizerState = _rasterizer;
            _device.SamplerStates[0] = SamplerState.LinearClamp;
            _effect.World = Matrix.Identity;
            _effect.View = Matrix.Identity;
            _effect.Projection = Matrix.CreateOrthographicOffCenter(
                drawData.DisplayPos.X,
                drawData.DisplayPos.X + drawData.DisplaySize.X,
                drawData.DisplayPos.Y + drawData.DisplaySize.Y,
                drawData.DisplayPos.Y,
                0f,
                1f);

            for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
                DrawList(drawData.CmdLists[listIndex], drawData, viewportX, viewportY,
                    framebufferWidth, framebufferHeight);
        }
        finally
        {
            _device.BlendState = oldBlend;
            _device.DepthStencilState = oldDepth;
            _device.RasterizerState = oldRasterizer;
            _device.ScissorRectangle = oldScissor;
            _device.SamplerStates[0] = oldSampler;
            _device.Viewport = oldViewport;
        }
    }

    private void DrawList(ImDrawListPtr drawList, ImDrawDataPtr drawData,
        int viewportX, int viewportY, int framebufferWidth, int framebufferHeight)
    {
        var vertices = new VertexPositionColorTexture[drawList.VtxBuffer.Size];
        for (var index = 0; index < vertices.Length; index++)
        {
            var source = drawList.VtxBuffer[index];
            var color = source.col;
            vertices[index] = new VertexPositionColorTexture(
                new Microsoft.Xna.Framework.Vector3(source.pos.X, source.pos.Y, 0f),
                new Color((byte)color, (byte)(color >> 8), (byte)(color >> 16), (byte)(color >> 24)),
                new Microsoft.Xna.Framework.Vector2(source.uv.X, source.uv.Y));
        }

        var indices = new ushort[drawList.IdxBuffer.Size];
        for (var index = 0; index < indices.Length; index++)
            indices[index] = drawList.IdxBuffer[index];

        EnsureBuffers(vertices.Length, indices.Length);
        if (vertices.Length > 0) _vertexBuffer!.SetData(vertices, 0, vertices.Length, SetDataOptions.Discard);
        if (indices.Length > 0) _indexBuffer!.SetData(indices, 0, indices.Length, SetDataOptions.Discard);
        _device.SetVertexBuffer(_vertexBuffer);
        _device.Indices = _indexBuffer;

        for (var commandIndex = 0; commandIndex < drawList.CmdBuffer.Size; commandIndex++)
        {
            var command = drawList.CmdBuffer[commandIndex];
            if (command.UserCallback != IntPtr.Zero || command.ElemCount == 0) continue;
            if (!_textures.TryGetValue(command.GetTexID(), out var texture)) continue;

            var clip = command.ClipRect;
            var left = viewportX + Math.Clamp((int)MathF.Floor((clip.X - drawData.DisplayPos.X) * drawData.FramebufferScale.X), 0, framebufferWidth);
            var top = viewportY + Math.Clamp((int)MathF.Floor((clip.Y - drawData.DisplayPos.Y) * drawData.FramebufferScale.Y), 0, framebufferHeight);
            var right = viewportX + Math.Clamp((int)MathF.Ceiling((clip.Z - drawData.DisplayPos.X) * drawData.FramebufferScale.X), 0, framebufferWidth);
            var bottom = viewportY + Math.Clamp((int)MathF.Ceiling((clip.W - drawData.DisplayPos.Y) * drawData.FramebufferScale.Y), 0, framebufferHeight);
            if (right <= left || bottom <= top) continue;

            _device.ScissorRectangle = new Rectangle(left, top, right - left, bottom - top);
            _effect.Texture = texture;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                    (int)command.VtxOffset, (int)command.IdxOffset, (int)command.ElemCount / 3);
            }
        }
    }

    private void EnsureBuffers(int vertexCount, int indexCount)
    {
        if (vertexCount > _vertexCapacity)
        {
            _vertexBuffer?.Dispose();
            _vertexCapacity = GrowCapacity(vertexCount);
            _vertexBuffer = new DynamicVertexBuffer(_device,
                VertexPositionColorTexture.VertexDeclaration, _vertexCapacity, BufferUsage.WriteOnly);
        }

        if (indexCount > _indexCapacity)
        {
            _indexBuffer?.Dispose();
            _indexCapacity = GrowCapacity(indexCount);
            _indexBuffer = new DynamicIndexBuffer(_device, IndexElementSize.SixteenBits,
                _indexCapacity, BufferUsage.WriteOnly);
        }
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        IntPtr pixels;
        int width;
        int height;
        int bytesPerPixel;
        io.Fonts.GetTexDataAsRGBA32(out pixels, out width, out height, out bytesPerPixel);
        if (pixels == IntPtr.Zero || width <= 0 || height <= 0 || bytesPerPixel != 4)
            throw new InvalidOperationException("ImGui did not produce a valid RGBA font atlas.");

        var bytes = new byte[checked(width * height * bytesPerPixel)];
        Marshal.Copy(pixels, bytes, 0, bytes.Length);
        _fontTexture = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
        _fontTexture.SetData(bytes);
        _textures.Add(FontTextureId, _fontTexture);
        io.Fonts.SetTexID(FontTextureId);
        io.Fonts.ClearTexData();
    }

    private static int GrowCapacity(int required)
    {
        var capacity = 256;
        while (capacity < required) capacity = checked(capacity * 2);
        return capacity;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        _fontTexture?.Dispose();
        _effect.Dispose();
        _rasterizer.Dispose();
        _indexBuffer = null;
        _vertexBuffer = null;
        _fontTexture = null;
        _textures.Clear();
    }
}

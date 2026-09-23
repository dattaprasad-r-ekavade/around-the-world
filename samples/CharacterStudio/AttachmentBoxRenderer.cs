using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterStudio;

/// <summary>Small GPU-backed colored cube used to make a bone attachment visible in the preview.</summary>
internal sealed class AttachmentBoxRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _vertices;
    private readonly IndexBuffer _indices;
    private bool _disposed;

    public AttachmentBoxRenderer(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            TextureEnabled = false,
            LightingEnabled = false
        };

        var vertices = new[]
        {
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), Color.Cyan),
            new VertexPositionColor(new Vector3( 0.5f, -0.5f, -0.5f), Color.DeepPink),
            new VertexPositionColor(new Vector3( 0.5f,  0.5f, -0.5f), Color.Cyan),
            new VertexPositionColor(new Vector3(-0.5f,  0.5f, -0.5f), Color.DeepPink),
            new VertexPositionColor(new Vector3(-0.5f, -0.5f,  0.5f), Color.DeepPink),
            new VertexPositionColor(new Vector3( 0.5f, -0.5f,  0.5f), Color.Cyan),
            new VertexPositionColor(new Vector3( 0.5f,  0.5f,  0.5f), Color.DeepPink),
            new VertexPositionColor(new Vector3(-0.5f,  0.5f,  0.5f), Color.Cyan)
        };
        var indices = new ushort[]
        {
            0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1, 3, 2, 6, 3, 6, 7,
            1, 5, 6, 1, 6, 2, 0, 3, 7, 0, 7, 4
        };

        VertexBuffer? vertexBuffer = null;
        IndexBuffer? indexBuffer = null;
        try
        {
            vertexBuffer = new VertexBuffer(device, VertexPositionColor.VertexDeclaration,
                vertices.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);
            indexBuffer = new IndexBuffer(device, IndexElementSize.SixteenBits,
                indices.Length, BufferUsage.WriteOnly);
            indexBuffer.SetData(indices);
            _vertices = vertexBuffer;
            _indices = indexBuffer;
        }
        catch
        {
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            _effect.Dispose();
            throw;
        }
    }

    public void Draw(Matrix world, Matrix view, Matrix projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _effect.World = world;
        _effect.View = view;
        _effect.Projection = projection;
        var previousRasterizer = _device.RasterizerState;
        try
        {
            _device.RasterizerState = RasterizerState.CullNone;
            _device.SetVertexBuffer(_vertices);
            _device.Indices = _indices;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 12);
            }
        }
        finally
        {
            _device.RasterizerState = previousRasterizer;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _indices.Dispose();
        _vertices.Dispose();
        _effect.Dispose();
    }
}

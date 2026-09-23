using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Ember.Assets;

public sealed class StaticMeshGpuBuffer : IDisposable
{
    private readonly GraphicsDevice _device;
    private VertexBuffer? _vertices;
    private IndexBuffer? _indices;
    private readonly int _vertexCount;
    private readonly int _primitiveCount;
    private bool _disposed;

    public StaticMeshGpuBuffer(GraphicsDevice device, StaticMeshData mesh)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (mesh is null) throw new ArgumentNullException(nameof(mesh));
        if (mesh.Vertices.Count == 0 || mesh.TriangleIndices.Count == 0)
            throw new ArgumentException("A GPU mesh requires vertices and triangle indices.", nameof(mesh));

        _vertexCount = mesh.Vertices.Count;
        _primitiveCount = mesh.TriangleIndices.Count / 3;

        var vertices = new VertexPositionNormalTexture[_vertexCount];
        for (var i = 0; i < vertices.Length; i++)
        {
            var source = mesh.Vertices[i];
            vertices[i] = new VertexPositionNormalTexture(source.Position, source.Normal, source.TextureCoordinate0);
        }

        try
        {
            _vertices = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            _vertices.SetData(vertices);

            var maximumIndex = mesh.TriangleIndices.Max();
            if (maximumIndex <= ushort.MaxValue)
            {
                var indices = new ushort[mesh.TriangleIndices.Count];
                for (var i = 0; i < indices.Length; i++) indices[i] = (ushort)mesh.TriangleIndices[i];
                _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                _indices.SetData(indices);
            }
            else
            {
                var indices = new uint[mesh.TriangleIndices.Count];
                for (var i = 0; i < indices.Length; i++) indices[i] = (uint)mesh.TriangleIndices[i];
                _indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
                _indices.SetData(indices);
            }
        }
        catch
        {
            _indices?.Dispose();
            _vertices?.Dispose();
            _indices = null;
            _vertices = null;
            throw;
        }
    }

    public bool IsDisposed => _disposed;

    public void Draw(BasicEffect effect, Matrix world, Matrix view, Matrix projection)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StaticMeshGpuBuffer));
        if (effect is null) throw new ArgumentNullException(nameof(effect));

        effect.World = world;
        effect.View = view;
        effect.Projection = projection;
        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    /// <summary>Draws this mesh with caller-supplied effect parameters and technique.</summary>
    public void Draw(Effect effect)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StaticMeshGpuBuffer));
        ArgumentNullException.ThrowIfNull(effect);

        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _indices?.Dispose();
        _vertices?.Dispose();
        _indices = null;
        _vertices = null;
    }
}

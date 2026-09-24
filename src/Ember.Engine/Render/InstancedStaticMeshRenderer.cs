using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ember.Render;

/// <summary>One transform and tint for a shared static mesh.</summary>
public readonly record struct StaticMeshInstance(Matrix World, Color Tint);

/// <summary>
/// Draws many copies of one opaque static mesh with a single instanced submission. The supplied
/// effect is borrowed and must implement the parameters documented by <see cref="Draw"/>.
/// </summary>
public sealed class InstancedStaticMeshRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly VertexDeclaration _instanceDeclaration;
    private readonly VertexBuffer _meshVertices;
    private readonly IndexBuffer _meshIndices;
    private readonly int _vertexCount;
    private readonly int _primitiveCount;
    private DynamicVertexBuffer _instanceBuffer;
    private InstanceVertex[] _instanceData;
    private bool _disposed;

    public InstancedStaticMeshRenderer(GraphicsDevice device, Effect effect,
        IReadOnlyList<VertexPositionNormalTexture> vertices, IReadOnlyList<short> indices,
        int initialInstanceCapacity = 64)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (device.GraphicsProfile != GraphicsProfile.HiDef)
            throw new PlatformNotSupportedException("Hardware static-mesh instancing requires the HiDef graphics profile.");
        if (vertices.Count < 3)
            throw new ArgumentException("An instanced mesh needs at least three vertices.", nameof(vertices));
        if (indices.Count < 3 || indices.Count % 3 != 0)
            throw new ArgumentException("Instanced mesh indices must contain complete triangles.", nameof(indices));
        if (initialInstanceCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(initialInstanceCapacity));
        foreach (var index in indices)
            if (index < 0 || index >= vertices.Count)
                throw new ArgumentOutOfRangeException(nameof(indices), "Mesh index is outside the vertex range.");

        _device = device;
        _effect = effect;
        _vertexCount = vertices.Count;
        _primitiveCount = indices.Count / 3;
        _instanceData = new InstanceVertex[initialInstanceCapacity];

        VertexDeclaration? declaration = null;
        VertexBuffer? meshVertices = null;
        IndexBuffer? meshIndices = null;
        DynamicVertexBuffer? instanceBuffer = null;
        try
        {
            declaration = CreateInstanceDeclaration();
            meshVertices = new VertexBuffer(device, typeof(VertexPositionNormalTexture), vertices.Count,
                BufferUsage.WriteOnly);
            var vertexData = new VertexPositionNormalTexture[vertices.Count];
            for (var i = 0; i < vertexData.Length; i++) vertexData[i] = vertices[i];
            meshVertices.SetData(vertexData);

            meshIndices = new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Count,
                BufferUsage.WriteOnly);
            var indexData = new short[indices.Count];
            for (var i = 0; i < indexData.Length; i++) indexData[i] = indices[i];
            meshIndices.SetData(indexData);
            instanceBuffer = new DynamicVertexBuffer(device, declaration,
                initialInstanceCapacity, BufferUsage.WriteOnly);
        }
        catch
        {
            instanceBuffer?.Dispose();
            meshIndices?.Dispose();
            meshVertices?.Dispose();
            declaration?.Dispose();
            throw;
        }

        _instanceDeclaration = declaration!;
        _meshVertices = meshVertices!;
        _meshIndices = meshIndices!;
        _instanceBuffer = instanceBuffer!;
    }

    /// <summary>GPU resources owned by this renderer, excluding the borrowed effect.</summary>
    public int OwnedGraphicsResourceCount => 4;

    /// <summary>Instance count and draw submissions from the most recent call.</summary>
    public int LastInstanceCount { get; private set; }
    public int LastDrawCallCount { get; private set; }

    /// <summary>
    /// Draws with these effect parameters: ViewProjection, CameraPosition, LightDirection,
    /// DirectionalLightColor, AmbientLightColor, FogColor, FogStart, and FogEnd.
    /// </summary>
    public void Draw(IReadOnlyList<StaticMeshInstance> instances, Matrix viewProjection,
        Vector3 cameraPosition, Vector3 lightDirection, Vector3 directionalLightColor,
        Vector3 ambientLightColor, Vector3 fogColor, float fogStart, float fogEnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(instances);
        if (!float.IsFinite(cameraPosition.X) || !float.IsFinite(cameraPosition.Y) || !float.IsFinite(cameraPosition.Z))
            throw new ArgumentOutOfRangeException(nameof(cameraPosition), "Camera position must be finite.");
        if (!float.IsFinite(lightDirection.X) || !float.IsFinite(lightDirection.Y) || !float.IsFinite(lightDirection.Z)
            || lightDirection.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(lightDirection), "Light direction must be finite and nonzero.");
        if (!float.IsFinite(fogStart) || fogStart < 0f
            || !float.IsFinite(fogEnd) || fogEnd <= fogStart)
            throw new ArgumentOutOfRangeException(nameof(fogEnd), "Fog end must be finite and greater than fog start.");

        LastInstanceCount = instances.Count;
        LastDrawCallCount = 0;
        if (instances.Count == 0) return;

        EnsureInstanceCapacity(instances.Count);
        for (var i = 0; i < instances.Count; i++)
            _instanceData[i] = InstanceVertex.Create(instances[i]);
        _instanceBuffer.SetData(_instanceData, 0, instances.Count, SetDataOptions.Discard);

        SetParameter("ViewProjection", viewProjection);
        SetParameter("CameraPosition", cameraPosition);
        SetParameter("LightDirection", Vector3.Normalize(lightDirection));
        SetParameter("DirectionalLightColor", directionalLightColor);
        SetParameter("AmbientLightColor", ambientLightColor);
        SetParameter("FogColor", fogColor);
        SetParameter("FogStart", fogStart);
        SetParameter("FogEnd", fogEnd);

        try
        {
            _device.SetVertexBuffers(
                new VertexBufferBinding(_meshVertices),
                new VertexBufferBinding(_instanceBuffer, 0, 1));
            _device.Indices = _meshIndices;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawInstancedPrimitives(PrimitiveType.TriangleList,
                    baseVertex: 0, startIndex: 0, primitiveCount: _primitiveCount,
                    instanceCount: instances.Count);
                LastDrawCallCount++;
            }
        }
        finally
        {
            _device.SetVertexBuffers(Array.Empty<VertexBufferBinding>());
            _device.Indices = null;
        }
    }

    private void EnsureInstanceCapacity(int required)
    {
        if (required <= _instanceData.Length) return;
        var capacity = Math.Max(required, checked(_instanceData.Length * 2));
        var replacement = new InstanceVertex[capacity];
        Array.Copy(_instanceData, replacement, _instanceData.Length);
        var buffer = new DynamicVertexBuffer(_device, _instanceDeclaration, capacity, BufferUsage.WriteOnly);
        _instanceBuffer.Dispose();
        _instanceBuffer = buffer;
        _instanceData = replacement;
    }

    private void SetParameter(string name, Matrix value) => RequireParameter(name).SetValue(value);
    private void SetParameter(string name, Vector3 value) => RequireParameter(name).SetValue(value);
    private void SetParameter(string name, float value) => RequireParameter(name).SetValue(value);

    private EffectParameter RequireParameter(string name) => _effect.Parameters[name]
        ?? throw new InvalidOperationException($"Instanced mesh effect is missing the {name} parameter.");

    private static VertexDeclaration CreateInstanceDeclaration()
    {
        var elements = new VertexElement[8];
        for (var row = 0; row < 4; row++)
            elements[row] = new VertexElement(row * 16, VertexElementFormat.Vector4,
                VertexElementUsage.TextureCoordinate, row + 1);
        for (var row = 0; row < 3; row++)
            elements[4 + row] = new VertexElement((4 + row) * 16, VertexElementFormat.Vector4,
                VertexElementUsage.TextureCoordinate, row + 5);
        elements[7] = new VertexElement(112, VertexElementFormat.Color,
            VertexElementUsage.Color, 1);
        return new VertexDeclaration(elements);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct InstanceVertex
    {
        public Vector4 World0;
        public Vector4 World1;
        public Vector4 World2;
        public Vector4 World3;
        public Vector4 Normal0;
        public Vector4 Normal1;
        public Vector4 Normal2;
        public Color Tint;

        public static InstanceVertex Create(StaticMeshInstance instance)
        {
            var world = instance.World;
            var normal = Matrix.Transpose(Matrix.Invert(world));
            if (!IsFinite(world) || !IsFinite(normal) || MathF.Abs(world.Determinant()) < 1e-8f)
                throw new ArgumentException("Instance transform must be finite and invertible.", nameof(instance));

            return new InstanceVertex
            {
                World0 = new Vector4(world.M11, world.M12, world.M13, world.M14),
                World1 = new Vector4(world.M21, world.M22, world.M23, world.M24),
                World2 = new Vector4(world.M31, world.M32, world.M33, world.M34),
                World3 = new Vector4(world.M41, world.M42, world.M43, world.M44),
                Normal0 = new Vector4(normal.M11, normal.M12, normal.M13, 0f),
                Normal1 = new Vector4(normal.M21, normal.M22, normal.M23, 0f),
                Normal2 = new Vector4(normal.M31, normal.M32, normal.M33, 0f),
                Tint = instance.Tint
            };
        }

        private static bool IsFinite(Matrix value) =>
            float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
            && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
            && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
            && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instanceBuffer.Dispose();
        _meshIndices.Dispose();
        _meshVertices.Dispose();
        _instanceDeclaration.Dispose();
    }
}

using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Ember.Assets;

public static class SkinnedEffectCompatibility
{
    public static void Validate(GraphicsProfile graphicsProfile, int jointCount)
    {
        if (graphicsProfile != GraphicsProfile.Reach && graphicsProfile != GraphicsProfile.HiDef)
            throw new NotSupportedException($"SkinnedEffect is not supported for graphics profile '{graphicsProfile}'.");
        if (jointCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(jointCount), "A skinned draw requires at least one joint.");
        if (jointCount > SkinnedEffect.MaxBones)
            throw new NotSupportedException(
                $"The current SkinnedEffect supports at most {SkinnedEffect.MaxBones} joints; this skin has {jointCount}.");
    }

    /// <summary>Rejects a pose calculated for another skin, even when both skins have the same joint count.</summary>
    public static void ValidatePose(GltfSkinData skin, GltfSkinPose pose)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(pose);
        if (!pose.UsesSkin(skin))
            throw new ArgumentException("The pose belongs to a different skin.", nameof(pose));
    }
}

public sealed class SkinnedMeshGpuBuffer : IDisposable
{
    private const int VertexStride = 52;
    private readonly GraphicsDevice _device;
    private readonly VertexDeclaration _vertexDeclaration;
    private VertexBuffer? _vertices;
    private IndexBuffer? _indices;
    private readonly int _primitiveCount;
    private readonly int _jointCount;
    private readonly GltfSkinData _skin;
    private readonly Matrix[] _boneTransforms;
    private bool _disposed;

    public SkinnedMeshGpuBuffer(GraphicsDevice device, GltfSkinnedMeshData mesh, GltfSkinData skin)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        if (mesh is null) throw new ArgumentNullException(nameof(mesh));
        _skin = skin ?? throw new ArgumentNullException(nameof(skin));
        var jointCount = skin.JointNodeIndices.Count;
        SkinnedEffectCompatibility.Validate(device.GraphicsProfile, jointCount);
        if (mesh.Vertices.Count == 0 || mesh.TriangleIndices.Count == 0)
            throw new ArgumentException("A GPU skinned mesh requires vertices and triangle indices.", nameof(mesh));

        _jointCount = jointCount;
        _boneTransforms = new Matrix[jointCount];
        _primitiveCount = mesh.TriangleIndices.Count / 3;
        _vertexDeclaration = CreateVertexDeclaration();

        var vertices = new SkinnedVertex[mesh.Vertices.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var source = mesh.Vertices[i];
            var influences = source.JointWeights;
            vertices[i] = new SkinnedVertex
            {
                Position = source.Position,
                Normal = source.Normal,
                TextureCoordinate0 = source.TextureCoordinate0,
                BlendIndices = new Byte4(
                    influences.Joint0, influences.Joint1, influences.Joint2, influences.Joint3),
                BlendWeights = new Vector4(
                    influences.Weights.X, influences.Weights.Y, influences.Weights.Z, influences.Weights.W)
            };
        }

        try
        {
            _vertices = new VertexBuffer(device, _vertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            _vertices.SetData(vertices);

            var maximumIndex = 0;
            for (var i = 0; i < mesh.TriangleIndices.Count; i++)
                maximumIndex = Math.Max(maximumIndex, mesh.TriangleIndices[i]);

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
            Dispose();
            throw;
        }
    }

    public bool IsDisposed => _disposed;

    public void Draw(SkinnedEffect effect, GltfSkinPose pose, Matrix world, Matrix view,
        Matrix projection, Texture2D? texture)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SkinnedMeshGpuBuffer));
        if (effect is null) throw new ArgumentNullException(nameof(effect));
        if (pose is null) throw new ArgumentNullException(nameof(pose));
        SkinnedEffectCompatibility.ValidatePose(_skin, pose);
        if (pose.JointCount != _jointCount)
            throw new ArgumentException($"Pose has {pose.JointCount} joints; this buffer expects {_jointCount}.", nameof(pose));

        effect.World = world;
        effect.View = view;
        effect.Projection = projection;
        effect.WeightsPerVertex = 4;
        effect.Texture = texture;
        CopyBoneTransforms(pose);
        effect.SetBoneTransforms(_boneTransforms);

        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    /// <summary>Draws this posed mesh with caller-supplied effect parameters and technique.</summary>
    public void Draw(Effect effect, GltfSkinPose pose)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SkinnedMeshGpuBuffer));
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(pose);
        SkinnedEffectCompatibility.ValidatePose(_skin, pose);
        if (pose.JointCount != _jointCount)
            throw new ArgumentException($"Pose has {pose.JointCount} joints; this buffer expects {_jointCount}.", nameof(pose));

        CopyBoneTransforms(pose);
        var boneParameter = effect.Parameters["BoneTransforms"]
            ?? throw new InvalidOperationException("Effect is missing the BoneTransforms parameter.");
        boneParameter.SetValue(_boneTransforms);

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
        _vertexDeclaration.Dispose();
        _indices = null;
        _vertices = null;
    }

    private static VertexDeclaration CreateVertexDeclaration() => new(VertexStride,
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(32, VertexElementFormat.Byte4, VertexElementUsage.BlendIndices, 0),
        new VertexElement(36, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0));

    private void CopyBoneTransforms(GltfSkinPose pose)
    {
        for (var i = 0; i < _boneTransforms.Length; i++)
            _boneTransforms[i] = pose.SkinMatrices[i];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = VertexStride)]
    private struct SkinnedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TextureCoordinate0;
        public Byte4 BlendIndices;
        public Vector4 BlendWeights;
    }
}

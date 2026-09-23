using System;
using System.Collections.ObjectModel;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Assets;

/// <summary>A mutable per-instance pose and its calculated skin matrices.</summary>
public sealed class GltfSkinPose
{
    private readonly GltfSkinData _skin;
    private readonly GltfLocalTransform[] _localTransforms;
    private readonly Matrix[] _skinMatrices;
    private readonly ReadOnlyCollection<Matrix> _readOnlySkinMatrices;
    private Matrix _meshNodeWorldMatrix;

    internal GltfSkinPose(GltfSkinData skin)
    {
        _skin = skin ?? throw new ArgumentNullException(nameof(skin));
        _localTransforms = new GltfLocalTransform[skin.Nodes.Count];
        for (var i = 0; i < _localTransforms.Length; i++)
            _localTransforms[i] = skin.Nodes[i].RestTransform;

        _skinMatrices = new Matrix[skin.JointNodeIndices.Count];
        _readOnlySkinMatrices = Array.AsReadOnly(_skinMatrices);
        ComputeSkinMatrices();
    }

    public int NodeCount => _localTransforms.Length;
    public int JointCount => _skinMatrices.Length;
    /// <summary>Mesh-node world transform from the most recent skin-matrix calculation.</summary>
    public Matrix MeshNodeWorldMatrix => _meshNodeWorldMatrix;
    public ReadOnlyCollection<Matrix> SkinMatrices => _readOnlySkinMatrices;

    public GltfLocalTransform GetLocalTransform(int nodeIndex)
    {
        ValidateNodeIndex(nodeIndex);
        return _localTransforms[nodeIndex];
    }

    public void SetLocalTransform(int nodeIndex, GltfLocalTransform transform)
    {
        ValidateNodeIndex(nodeIndex);
        _localTransforms[nodeIndex] = NormalizeAndValidate(transform, nodeIndex);
    }

    /// <summary>Restores all local transforms from the immutable skin asset.</summary>
    public void ResetToRestPose()
    {
        for (var i = 0; i < _localTransforms.Length; i++)
            _localTransforms[i] = _skin.Nodes[i].RestTransform;
    }

    internal bool UsesSkin(GltfSkinData skin) => ReferenceEquals(_skin, skin);

    /// <summary>Rebuilds all node worlds and skin matrices from this pose's absolute local transforms.</summary>
    public ReadOnlyCollection<Matrix> ComputeSkinMatrices()
    {
        var nodeWorldMatrices = new Matrix[_localTransforms.Length];
        for (var i = 0; i < nodeWorldMatrices.Length; i++)
        {
            var local = NormalizeAndValidate(_localTransforms[i], i).Matrix;
            var parentIndex = _skin.Nodes[i].ParentNodeIndex;
            nodeWorldMatrices[i] = parentIndex is { } parent
                ? local * nodeWorldMatrices[parent]
                : local;
        }

        var meshWorld = nodeWorldMatrices[_skin.MeshNodeIndex];
        _meshNodeWorldMatrix = meshWorld;
        var determinant = meshWorld.Determinant();
        if (!float.IsFinite(determinant) || determinant == 0f)
            throw new InvalidOperationException("The posed mesh-node transform is not invertible; skin matrices cannot be calculated.");
        var inverseMeshWorld = Matrix.Invert(meshWorld);

        for (var jointIndex = 0; jointIndex < _skinMatrices.Length; jointIndex++)
        {
            var jointWorld = nodeWorldMatrices[_skin.JointNodeIndices[jointIndex]];
            _skinMatrices[jointIndex] = _skin.InverseBindMatrices[jointIndex] * jointWorld * inverseMeshWorld;
        }

        return _readOnlySkinMatrices;
    }

    private void ValidateNodeIndex(int nodeIndex)
    {
        if ((uint)nodeIndex >= (uint)_localTransforms.Length)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));
    }

    private static GltfLocalTransform NormalizeAndValidate(GltfLocalTransform transform, int nodeIndex)
    {
        if (!IsFinite(transform.Position) || !IsFinite(transform.Rotation) || !IsFinite(transform.Scale))
            throw new ArgumentException($"Pose node {nodeIndex} must contain only finite position, rotation, and scale values.", nameof(transform));

        var rotationLengthSquared = transform.Rotation.LengthSquared();
        if (!float.IsFinite(rotationLengthSquared) || rotationLengthSquared < 0.0000001f)
            throw new ArgumentException($"Pose node {nodeIndex} has a zero-length rotation quaternion.", nameof(transform));

        return transform with { Rotation = Quaternion.Normalize(transform.Rotation) };
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

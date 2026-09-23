using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Scene;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public readonly record struct GltfLocalTransform(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public Matrix Matrix => Microsoft.Xna.Framework.Matrix.CreateScale(Scale)
        * Microsoft.Xna.Framework.Matrix.CreateFromQuaternion(Rotation)
        * Microsoft.Xna.Framework.Matrix.CreateTranslation(Position);
}

public sealed class GltfSkeletonNode
{
    internal GltfSkeletonNode(int sourceNodeIndex, string name, int? parentNodeIndex,
        GltfLocalTransform restTransform, bool isJoint)
    {
        SourceNodeIndex = sourceNodeIndex;
        Name = name;
        ParentNodeIndex = parentNodeIndex;
        RestTransform = restTransform;
        IsJoint = isJoint;
    }

    public int SourceNodeIndex { get; }
    public string Name { get; }
    public int? ParentNodeIndex { get; }
    public GltfLocalTransform RestTransform { get; }
    public bool IsJoint { get; }
}

/// <summary>Immutable skin hierarchy and bind data for one skinned mesh node.</summary>
public sealed class GltfSkinData
{
    private GltfSkinData(int skinIndex, int meshNodeIndex, Matrix meshNodeRestWorldMatrix,
        IReadOnlyList<GltfSkeletonNode> nodes, IReadOnlyList<int> jointNodeIndices,
        IReadOnlyList<Matrix> inverseBindMatrices)
    {
        SkinIndex = skinIndex;
        MeshNodeIndex = meshNodeIndex;
        MeshNodeRestWorldMatrix = meshNodeRestWorldMatrix;
        Nodes = nodes;
        JointNodeIndices = jointNodeIndices;
        InverseBindMatrices = inverseBindMatrices;
    }

    public int SkinIndex { get; }
    public int MeshNodeIndex { get; }
    public Matrix MeshNodeRestWorldMatrix { get; }
    public IReadOnlyList<GltfSkeletonNode> Nodes { get; }
    /// <summary>Maps glTF skin joint order to indexes in <see cref="Nodes"/>.</summary>
    public IReadOnlyList<int> JointNodeIndices { get; }
    /// <summary>Inverse bind matrices in the original glTF skin joint order.</summary>
    public IReadOnlyList<Matrix> InverseBindMatrices { get; }

    public static GltfSkinData Import(ModelRoot model, Node meshNode)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (meshNode is null) throw new ArgumentNullException(nameof(meshNode));
        if (meshNode.LogicalParent != model)
            throw new ArgumentException("The mesh node must belong to the supplied glTF model.", nameof(meshNode));

        var skin = meshNode.Skin
            ?? throw new NotSupportedException($"Node '{DisplayName(meshNode)}' has no skin to import.");
        if (skin.JointsCount == 0)
            throw new NotSupportedException($"Skin {skin.LogicalIndex} has no joints.");
        if (skin.InverseBindMatrices.Count != 0 && skin.InverseBindMatrices.Count != skin.JointsCount)
            throw new InvalidDataException(
                $"Skin {skin.LogicalIndex} has {skin.InverseBindMatrices.Count} inverse-bind matrices for {skin.JointsCount} joints.");

        var orderedSourceNodes = new List<Node>();
        var includedNodeIndices = new HashSet<int>();
        foreach (var joint in skin.Joints) AddAncestors(joint);
        AddAncestors(meshNode);

        var nodeIndexBySourceIndex = new Dictionary<int, int>(orderedSourceNodes.Count);
        for (var i = 0; i < orderedSourceNodes.Count; i++)
            nodeIndexBySourceIndex.Add(orderedSourceNodes[i].LogicalIndex, i);

        var jointSourceIndices = new HashSet<int>(skin.Joints.Select(joint => joint.LogicalIndex));
        var nodes = new GltfSkeletonNode[orderedSourceNodes.Count];
        for (var i = 0; i < orderedSourceNodes.Count; i++)
        {
            var source = orderedSourceNodes[i];
            var sourceTransform = source.LocalTransform;
            if (!sourceTransform.IsSRT)
                throw new NotSupportedException(
                    $"Skeleton node '{DisplayName(source)}' uses a matrix transform; Ember stores joint transforms as position, rotation, and scale.");

            int? parentNodeIndex = null;
            if (source.VisualParent is { } parent)
                parentNodeIndex = nodeIndexBySourceIndex[parent.LogicalIndex];

            nodes[i] = new GltfSkeletonNode(
                source.LogicalIndex,
                DisplayName(source),
                parentNodeIndex,
                new GltfLocalTransform(
                    new Vector3(sourceTransform.Translation.X, sourceTransform.Translation.Y, sourceTransform.Translation.Z),
                    new Quaternion(sourceTransform.Rotation.X, sourceTransform.Rotation.Y,
                        sourceTransform.Rotation.Z, sourceTransform.Rotation.W),
                    new Vector3(sourceTransform.Scale.X, sourceTransform.Scale.Y, sourceTransform.Scale.Z)),
                jointSourceIndices.Contains(source.LogicalIndex));
        }

        var jointNodeIndices = skin.Joints
            .Select(joint => nodeIndexBySourceIndex[joint.LogicalIndex])
            .ToArray();
        var inverseBindMatrices = new Matrix[skin.JointsCount];
        for (var i = 0; i < inverseBindMatrices.Length; i++)
            inverseBindMatrices[i] = skin.InverseBindMatrices.Count == 0
                ? Matrix.Identity
                : ToXnaMatrix(skin.InverseBindMatrices[i]);

        return new GltfSkinData(
            skin.LogicalIndex,
            nodeIndexBySourceIndex[meshNode.LogicalIndex],
            ToXnaMatrix(meshNode.WorldMatrix),
            Array.AsReadOnly(nodes),
            Array.AsReadOnly(jointNodeIndices),
            Array.AsReadOnly(inverseBindMatrices));

        void AddAncestors(Node source)
        {
            if (source.VisualParent is { } parent) AddAncestors(parent);
            if (includedNodeIndices.Add(source.LogicalIndex)) orderedSourceNodes.Add(source);
        }
    }

    private static Matrix ToXnaMatrix(System.Numerics.Matrix4x4 value) => new Matrix(
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44);

    private static string DisplayName(Node node) =>
        string.IsNullOrWhiteSpace(node.Name) ? $"Node {node.LogicalIndex}" : node.Name;
}

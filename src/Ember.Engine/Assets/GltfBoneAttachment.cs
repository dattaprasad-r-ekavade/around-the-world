using System;
using System.Linq;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Assets;

/// <summary>A named joint and local offset for a prop attached to an imported character.</summary>
public sealed class GltfBoneAttachment
{
    private readonly GltfSkinData _skin;

    public GltfBoneAttachment(GltfSkinData skin, string boneName, Matrix localOffset)
    {
        _skin = skin ?? throw new ArgumentNullException(nameof(skin));
        if (string.IsNullOrWhiteSpace(boneName)) throw new ArgumentException("Bone name is required.", nameof(boneName));
        if (!IsFinite(localOffset)) throw new ArgumentException("Attachment offset must be finite.", nameof(localOffset));

        var matches = skin.Nodes
            .Select((node, index) => (node, index))
            .Where(pair => pair.node.IsJoint && string.Equals(pair.node.Name, boneName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
            throw new ArgumentException($"Joint '{boneName}' was not found in this skin.", nameof(boneName));
        if (matches.Length > 1)
            throw new ArgumentException($"Joint name '{boneName}' is ambiguous in this skin.", nameof(boneName));

        BoneNodeIndex = matches[0].index;
        BoneName = boneName;
        LocalOffset = localOffset;
    }

    public int BoneNodeIndex { get; }
    public string BoneName { get; }
    public Matrix LocalOffset { get; }

    /// <summary>Returns the attachment transform in scene world space using row-vector order.</summary>
    public Matrix GetWorldMatrix(GltfSkinPose pose, Matrix instanceWorld)
    {
        if (pose is null) throw new ArgumentNullException(nameof(pose));
        if (!pose.UsesSkin(_skin))
            throw new ArgumentException("The pose was created from a different skin than this attachment.", nameof(pose));
        return LocalOffset * pose.GetNodeWorldMatrix(BoneNodeIndex) * instanceWorld;
    }

    private static bool IsFinite(Matrix matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
}

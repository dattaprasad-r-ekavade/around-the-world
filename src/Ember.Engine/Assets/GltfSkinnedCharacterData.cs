using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public readonly record struct GltfSkinnedPrimitiveData(GltfSkinnedMeshData Mesh, GltfMaterialData Material);

/// <summary>CPU data for a single skinned character mesh in a glTF default scene.</summary>
public sealed class GltfSkinnedCharacterData
{
    private GltfSkinnedCharacterData(GltfSkinData skin, IReadOnlyList<GltfSkinnedPrimitiveData> primitives,
        IReadOnlyList<GltfAnimationClipData> animations, Bounds3 localBounds)
    {
        Skin = skin;
        Primitives = primitives;
        Animations = animations;
        LocalBounds = localBounds;
    }

    public GltfSkinData Skin { get; }
    public IReadOnlyList<GltfSkinnedPrimitiveData> Primitives { get; }
    public IReadOnlyList<GltfAnimationClipData> Animations { get; }
    public Bounds3 LocalBounds { get; }

    public GltfSkinPose CreatePose() => Skin.CreatePose();

    public static GltfSkinnedCharacterData Import(ModelRoot model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (model.DefaultScene is null)
            throw new NotSupportedException("A skinned character GLB must define a default scene.");

        var unsupportedExtension = model.ExtensionsRequired
            .Concat(model.IncompatibleExtensions)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (unsupportedExtension is not null)
            throw new NotSupportedException(
                $"The skinned character requires extension '{unsupportedExtension}', which Ember does not support.");

        var skinnedNodes = new List<Node>();
        foreach (var root in model.DefaultScene.VisualChildren) FindSkinnedNodes(root);
        if (skinnedNodes.Count != 1)
            throw new NotSupportedException(
                $"The current character importer supports one skinned mesh node in the default scene; found {skinnedNodes.Count}.");

        var meshNode = skinnedNodes[0];
        var skin = GltfSkinData.Import(model, meshNode);
        var parts = new List<GltfSkinnedPrimitiveData>(meshNode.Mesh!.Primitives.Count);
        Bounds3? bounds = null;
        foreach (var primitive in meshNode.Mesh.Primitives)
        {
            if (primitive.MorphTargetsCount > 0)
                throw new NotSupportedException("Morph targets are not supported by the skinned character importer.");

            var material = GltfMaterialData.Import(primitive.Material);
            var mesh = GltfSkinnedMeshData.Import(primitive, skin, material.HasBaseColorImage);
            parts.Add(new GltfSkinnedPrimitiveData(mesh, material));
            bounds = bounds is { } current ? current.Encapsulate(mesh.LocalBounds) : mesh.LocalBounds;
        }

        if (parts.Count == 0 || bounds is null)
            throw new NotSupportedException("The skinned mesh node has no triangle primitives to render.");

        var animations = model.LogicalAnimations
            .Select(animation => GltfAnimationClipData.Import(animation, skin))
            .ToArray();
        return new GltfSkinnedCharacterData(skin, Array.AsReadOnly(parts.ToArray()),
            Array.AsReadOnly(animations), bounds.Value);

        void FindSkinnedNodes(Node node)
        {
            if (node.Mesh is not null && node.Skin is null)
                throw new NotSupportedException(
                    $"The current character importer does not support unskinned mesh node '{DisplayName(node)}'.");

            if (node.Skin is not null)
            {
                if (node.Mesh is null)
                    throw new NotSupportedException($"Skinned node '{DisplayName(node)}' has no mesh.");
                skinnedNodes.Add(node);
            }

            foreach (var child in node.VisualChildren) FindSkinnedNodes(child);
        }
    }

    private static string DisplayName(Node node) =>
        string.IsNullOrWhiteSpace(node.Name) ? $"Node {node.LogicalIndex}" : node.Name;
}

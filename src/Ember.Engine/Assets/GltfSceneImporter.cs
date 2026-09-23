using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ember.Scene;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;

namespace Ember.Assets;

public sealed class ImportedGltfPrimitive
{
    internal ImportedGltfPrimitive(int meshIndex, int primitiveIndex, StaticMeshData mesh, GltfMaterialData material)
    {
        MeshIndex = meshIndex;
        PrimitiveIndex = primitiveIndex;
        Mesh = mesh;
        Material = material;
    }

    public int MeshIndex { get; }
    public int PrimitiveIndex { get; }
    public StaticMeshData Mesh { get; }
    public GltfMaterialData Material { get; }
}

public sealed class ImportedGltfScene
{
    internal ImportedGltfScene(SceneGraph scene, Dictionary<Guid, IReadOnlyList<ImportedGltfPrimitive>> meshesByNodeId)
    {
        Scene = scene;
        MeshesByNodeId = new ReadOnlyDictionary<Guid, IReadOnlyList<ImportedGltfPrimitive>>(meshesByNodeId);
    }

    public SceneGraph Scene { get; }
    public IReadOnlyDictionary<Guid, IReadOnlyList<ImportedGltfPrimitive>> MeshesByNodeId { get; }
}

public static class GltfSceneImporter
{
    public static ImportedGltfScene Import(ModelRoot model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (model.DefaultScene is null)
            throw new NotSupportedException("The GLB must define a default scene.");
        if (model.LogicalAnimations.Count > 0)
            throw new NotSupportedException("Animated GLBs are not supported by the static-scene importer.");

        var scene = new SceneGraph();
        var meshesByNodeId = new Dictionary<Guid, IReadOnlyList<ImportedGltfPrimitive>>();
        var meshCache = new Dictionary<int, IReadOnlyList<ImportedGltfPrimitive>>();

        foreach (var root in model.DefaultScene.VisualChildren)
            AddNode(root, parentId: null);

        return new ImportedGltfScene(scene, meshesByNodeId);

        void AddNode(Node source, Guid? parentId)
        {
            if (source.Skin is not null)
                throw new NotSupportedException($"Node '{DisplayName(source)}' has a skin; skinned meshes are not supported by the static importer.");

            var local = source.LocalTransform;
            if (!local.IsSRT)
                throw new NotSupportedException($"Node '{DisplayName(source)}' uses a matrix transform that Ember cannot represent as position, rotation, and scale.");

            var sceneObject = new SceneObject(Guid.NewGuid(), DisplayName(source))
            {
                Transform = new Transform
                {
                    Position = new Vector3(local.Translation.X, local.Translation.Y, local.Translation.Z),
                    Rotation = new Quaternion(local.Rotation.X, local.Rotation.Y, local.Rotation.Z, local.Rotation.W),
                    Scale = new Vector3(local.Scale.X, local.Scale.Y, local.Scale.Z)
                }
            };
            scene.Add(sceneObject);
            if (parentId is not null) scene.SetParent(sceneObject.Id, parentId);

            if (source.Mesh is { } mesh)
            {
                if (!meshCache.TryGetValue(mesh.LogicalIndex, out var primitives))
                {
                    var imported = new List<ImportedGltfPrimitive>(mesh.Primitives.Count);
                    for (var i = 0; i < mesh.Primitives.Count; i++)
                    {
                        var primitive = mesh.Primitives[i];
                        var material = GltfMaterialData.Import(primitive.Material);
                        var meshData = GltfPrimitiveImporter.Import(
                            primitive,
                            allowMissingTextureCoordinates: !material.HasBaseColorImage);
                        imported.Add(new ImportedGltfPrimitive(mesh.LogicalIndex, i, meshData, material));
                    }

                    primitives = imported.AsReadOnly();
                    meshCache.Add(mesh.LogicalIndex, primitives);
                }

                meshesByNodeId.Add(sceneObject.Id, primitives);
            }

            foreach (var child in source.VisualChildren)
                AddNode(child, sceneObject.Id);
        }
    }

    public static ImportedGltfScene Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("GLB path is required.", nameof(path));
        return Import(ModelRoot.Load(path));
    }

    private static string DisplayName(Node node) =>
        string.IsNullOrWhiteSpace(node.Name) ? $"Node {node.LogicalIndex}" : node.Name;
}

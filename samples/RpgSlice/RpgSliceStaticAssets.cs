using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Assets;
using Ember.Physics;
using Ember.Render;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpGLTF.Schema2;

namespace RpgSlice;

/// <summary>Loads the static GLBs referenced by a world and shares their GPU meshes across cells.</summary>
internal sealed class RpgSliceStaticAssets : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly SceneResourceScope _resources = new();
    private BasicEffect? _effect;
    private readonly Dictionary<Guid, StaticAsset> _assets = new();
    private bool _disposed;

    private RpgSliceStaticAssets(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public int AssetCount => _assets.Count;
    public int PrimitiveCount => _assets.Values.Sum(asset => asset.Primitives.Count);
    public int OwnedGraphicsResourceCount => _disposed || _effect is null ? 0 : 1 + _assets.Values.Sum(asset =>
        asset.Primitives.Select(primitive => primitive.Buffer).Distinct().Count() + asset.Textures.Count);

    public static RpgSliceStaticAssets Load(GraphicsDevice device, WorldManifest world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var result = new RpgSliceStaticAssets(device);
        try
        {
            var references = new Dictionary<Guid, string>();
            foreach (var cell in world.Cells.OrderBy(cell => cell.Id))
            {
                var scene = SceneFile.Load(world.ResolveScenePath(cell.Id));
                foreach (var sceneObject in scene.Objects)
                {
                    if (sceneObject.StaticMeshLod is not null)
                        throw new NotSupportedException($"RpgSlice does not load static mesh LODs yet ('{sceneObject.Name}').");
                    if (sceneObject.GltfAsset is not { } reference) continue;
                    if (references.TryGetValue(reference.AssetId, out var existing)
                        && !string.Equals(existing, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"GLB asset ID {reference.AssetId} refers to multiple paths.");
                    references[reference.AssetId] = reference.SourcePath;
                }
            }

            if (references.Count > 0)
            {
                result._effect = result._resources.Own(new BasicEffect(device)
                {
                    LightingEnabled = true,
                    PreferPerPixelLighting = true,
                    TextureEnabled = false,
                    VertexColorEnabled = false
                });
                result._effect.EnableDefaultLighting();
            }
            foreach (var (assetId, relativePath) in references.OrderBy(pair => pair.Key))
            {
                var fullPath = Path.GetFullPath(Path.Combine(world.RootDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
                var relativeToRoot = Path.GetRelativePath(world.RootDirectory, fullPath);
                if (Path.IsPathRooted(relativeToRoot) || relativeToRoot == ".."
                    || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || relativeToRoot.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"GLB asset path '{relativePath}' escapes the world project.");
                if (!File.Exists(fullPath)) throw new FileNotFoundException($"GLB asset '{relativePath}' was not found.", fullPath);
                result._assets.Add(assetId, result.LoadAsset(fullPath));
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public int GetCollisionPrimitiveCount(Guid assetId) => GetAsset(assetId).Primitives.Count;

    public PhysicsObjectId AddCollisionMesh(Guid assetId, int primitiveIndex, SceneGraph instanceScene,
        SceneObject instance, Matrix cellWorld, PhysicsWorld physics)
    {
        ArgumentNullException.ThrowIfNull(instanceScene);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(physics);
        var asset = GetAsset(assetId);
        var primitive = GetPrimitive(asset, primitiveIndex);
        var world = asset.Scene.Scene.GetWorldMatrix(primitive.NodeId)
            * instanceScene.GetWorldMatrix(instance.Id) * cellWorld;
        var vertices = primitive.Mesh.Vertices
            .Select(vertex => Vector3.Transform(vertex.Position, world)).ToArray();
        return physics.AddStaticTriangleMesh(vertices, primitive.Mesh.TriangleIndices);
    }

    public void Draw(SceneGraph scene, SceneObject instance, Matrix cellWorld,
        Matrix view, Matrix projection, OutdoorEnvironmentState environment, bool fogEnabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.GltfAsset is not { } reference) return;
        var asset = GetAsset(reference.AssetId);
        var effect = _effect ?? throw new InvalidOperationException("No static GLB effect is available.");
        var instanceWorld = scene.GetWorldMatrix(instance.Id) * cellWorld;
        effect.View = view;
        effect.Projection = projection;
        effect.FogEnabled = fogEnabled;
        effect.FogColor = environment.FogColor.ToVector3();
        effect.FogStart = environment.FogStart;
        effect.FogEnd = environment.FogEnd;
        effect.AmbientLightColor = environment.AmbientLightColor;
        effect.DirectionalLight0.Direction = environment.LightDirection;
        effect.DirectionalLight0.DiffuseColor = environment.DirectionalLightColor;
        effect.DirectionalLight0.SpecularColor = environment.DirectionalLightColor * 0.2f;

        var previousRasterizer = _device.RasterizerState;
        var previousSampler = _device.SamplerStates[0];
        try
        {
            _device.SamplerStates[0] = SamplerState.LinearWrap;
            foreach (var primitive in asset.Primitives)
            {
                var material = primitive.Material;
                effect.DiffuseColor = new Vector3(material.BaseColorFactor.X,
                    material.BaseColorFactor.Y, material.BaseColorFactor.Z);
                effect.Alpha = material.BaseColorFactor.W;
                effect.TextureEnabled = material.HasBaseColorImage;
                effect.Texture = material.HasBaseColorImage
                    ? asset.Textures[material.BaseColorImageIndex!.Value]
                    : null;
                _device.RasterizerState = material.DoubleSided
                    ? RasterizerState.CullNone
                    : RasterizerState.CullCounterClockwise;
                var nodeWorld = asset.Scene.Scene.GetWorldMatrix(primitive.NodeId) * instanceWorld;
                primitive.Buffer.Draw(effect, nodeWorld, view, projection);
            }
        }
        finally
        {
            _device.RasterizerState = previousRasterizer;
            _device.SamplerStates[0] = previousSampler;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resources.Dispose();
        _assets.Clear();
    }

    private StaticAsset LoadAsset(string path)
    {
        var imported = GltfSceneImporter.Import(ModelRoot.Load(path));
        var buffers = new Dictionary<StaticMeshData, StaticMeshGpuBuffer>();
        var textures = new Dictionary<int, Texture2D>();
        var primitives = new List<StaticPrimitive>();
        foreach (var node in imported.Scene.Objects)
        {
            if (!imported.MeshesByNodeId.TryGetValue(node.Id, out var nodePrimitives)) continue;
            foreach (var primitive in nodePrimitives)
            {
                if (primitive.Material.AlphaMode != GltfAlphaMode.Opaque)
                    throw new NotSupportedException($"RpgSlice GLB '{path}' uses alpha mask material '{primitive.Material.Name}'.");
                if (!buffers.TryGetValue(primitive.Mesh, out var buffer))
                    buffers.Add(primitive.Mesh, buffer = _resources.Own(new StaticMeshGpuBuffer(_device, primitive.Mesh)));
                if (primitive.Material.HasBaseColorImage
                    && primitive.Material.BaseColorImageIndex is { } imageIndex
                    && !textures.ContainsKey(imageIndex))
                {
                    using var imageStream = new MemoryStream(primitive.Material.BaseColorImage.ToArray(), writable: false);
                    textures.Add(imageIndex, _resources.Own(Texture2D.FromStream(_device, imageStream)));
                }
                primitives.Add(new StaticPrimitive(node.Id, primitive.Mesh, primitive.Material, buffer));
            }
        }

        if (primitives.Count == 0) throw new InvalidDataException($"Static GLB '{path}' contains no drawable primitives.");
        return new StaticAsset(imported, primitives.AsReadOnly(), textures);
    }

    private StaticAsset GetAsset(Guid assetId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _assets.TryGetValue(assetId, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Static GLB asset {assetId} was not loaded from the world manifest.");
    }

    private static StaticPrimitive GetPrimitive(StaticAsset asset, int primitiveIndex)
    {
        if ((uint)primitiveIndex >= (uint)asset.Primitives.Count)
            throw new ArgumentOutOfRangeException(nameof(primitiveIndex));
        return asset.Primitives[primitiveIndex];
    }

    private sealed record StaticAsset(ImportedGltfScene Scene,
        IReadOnlyList<StaticPrimitive> Primitives, IReadOnlyDictionary<int, Texture2D> Textures);
    private sealed record StaticPrimitive(Guid NodeId, StaticMeshData Mesh,
        GltfMaterialData Material, StaticMeshGpuBuffer Buffer);
}

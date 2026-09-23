using System;
using System.Collections.Generic;
using System.IO;
using Ember.Assets;
using Ember;
using Ember.Input;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CharacterStudio;

/// <summary>
/// The first external consumer of the scene foundation: one saved scene object, an orbit
/// camera, and screenshot/open/save command-line paths. It deliberately has no game rules.
/// </summary>
public sealed class CharacterStudioGame : EngineHost
{
    private static readonly Guid PreviewInstanceId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly GltfAssetReference DefaultAsset = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"), "Assets/TextureCoordinateTest.glb");

    private readonly SceneGraph _sceneData;
    private readonly OrbitCamera _camera = new();
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private SceneResourceScope? _sceneResources;
    private ReloadableAsset<PreviewResources>? _preview;
    private string? _assetPath;
    private string _reimportStatus = "R: reimport current GLB";
    private BasicEffect _studioEffect = null!;
    private MouseState _lastMouse;
    private bool _hasMouse;

    public CharacterStudioGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Ember Character Studio")
    {
        _savePath = ParseOption(args, "--save");
        var openPath = ParseOption(args, "--open");

        if (openPath is null)
        {
            _sceneData = CreateDefaultScene();
        }
        else
        {
            try
            {
                _sceneData = SceneFile.Load(openPath);
            }
            catch (Exception exception)
            {
                _sceneData = CreateDefaultScene();
                _faults.Add($"open {openPath}: {exception.Message}");
            }
        }

        if (_savePath is not null)
        {
            try
            {
                SceneFile.SaveAtomic(_sceneData, _savePath);
                Console.WriteLine($"Saved scene to {System.IO.Path.GetFullPath(_savePath)}");
            }
            catch (Exception exception)
            {
                _faults.Add($"save {_savePath}: {exception.Message}");
            }
        }
    }

    protected override void LoadContent()
    {
        _sceneResources = new SceneResourceScope();
        try
        {
            AttachCanvas();
            _studioEffect = _sceneResources.Own(new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = false,
                TextureEnabled = false,
                LightingEnabled = true,
                PreferPerPixelLighting = true
            });
            _studioEffect.EnableDefaultLighting();
            _studioEffect.AmbientLightColor = new Vector3(0.62f, 0.64f, 0.68f);
            _studioEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.25f));
            _studioEffect.DirectionalLight0.DiffuseColor = new Vector3(0.9f);
            _studioEffect.DirectionalLight0.SpecularColor = new Vector3(0.12f);

            _assetPath = Path.GetFullPath(ResolveSceneAsset().SourcePath, AppContext.BaseDirectory);
            _preview = new ReloadableAsset<PreviewResources>(PreviewResources.Load(GraphicsDevice, _assetPath));

            _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);
            var sceneBounds = GetSceneBounds();
            if (sceneBounds is { } bounds)
            {
                _camera.Reset(bounds.Center, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);
                _camera.Frame(bounds);
            }
            else
                _camera.Reset(Vector3.Zero, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);

            foreach (var fault in _faults) Console.WriteLine($"character studio: {fault}");
        }
        catch
        {
            _preview?.Dispose();
            _preview = null;
            _sceneResources.Dispose();
            _sceneResources = null;
            throw;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();
        _input.Sample();
        var mouse = _input.CurrentMouse;

        if (_input.Pressed(_input.CurrentKeyboard, Keys.Escape)) Exit();
        if (_input.Pressed(_input.CurrentKeyboard, Keys.R)) ReimportAsset();
        if (!_hasMouse)
        {
            _lastMouse = mouse;
            _hasMouse = true;
        }

        if (mouse.LeftButton == ButtonState.Pressed)
        {
            _camera.Orbit(new Vector2(mouse.X - _lastMouse.X, mouse.Y - _lastMouse.Y));
        }

        _camera.Zoom(mouse.ScrollWheelValue - _lastMouse.ScrollWheelValue);
        _lastMouse = mouse;
        _input.Commit();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(12, 16, 24));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled) continue;
            var instanceWorld = _sceneData.GetWorldMatrix(item.Id);
            var preview = _preview!.Current;
            foreach (var (nodeId, parts) in preview.Scene.MeshesByNodeId)
            {
                var world = preview.Scene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var part in parts)
                {
                    var factor = part.Material.BaseColorFactor;
                    _studioEffect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                    _studioEffect.Alpha = 1f;
                    _studioEffect.TextureEnabled = part.Material.HasBaseColorImage;
                    _studioEffect.Texture = part.Material.HasBaseColorImage
                        ? preview.Textures[part.Material.BaseColorImageIndex!.Value]
                        : null;
                    GraphicsDevice.RasterizerState = part.Material.DoubleSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullCounterClockwise;
                    preview.MeshBuffers[part.Mesh].Draw(_studioEffect, world, _camera.View, _camera.Projection);
                }
            }
        }

        _ui.Begin();
        _ui.Panel(new Rectangle(16, 16, 620, 48), new Color(12, 16, 24, 230), new Color(82, 101, 122));
        _ui.TextFit(_reimportStatus, new Vector2(28, 31), 596f, 1f, Color.White);
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        _preview?.Dispose();
        _preview = null;
        _sceneResources?.Dispose();
        _sceneResources = null;
        DisposeHost();
        base.UnloadContent();
    }

    private static SceneGraph CreateDefaultScene()
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(PreviewInstanceId, "GLB Preview")
        {
            Transform = new Transform(),
            GltfAsset = DefaultAsset
        });
        return scene;
    }

    private GltfAssetReference ResolveSceneAsset()
    {
        GltfAssetReference? result = null;
        foreach (var item in _sceneData.Objects)
        {
            if (item.GltfAsset is not { } reference) continue;
            if (result is null)
            {
                result = reference;
                continue;
            }

            if (result.AssetId != reference.AssetId)
                throw new NotSupportedException("CharacterStudio currently previews one unique GLB asset per scene.");
            if (!string.Equals(result.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"GLB asset ID {reference.AssetId} refers to conflicting paths.");
        }

        return result ?? DefaultAsset;
    }

    private Bounds3? GetSceneBounds()
    {
        if (_preview is null) return null;
        Bounds3? result = null;
        var importedScene = _preview.Current.Scene;
        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled) continue;
            var instanceWorld = _sceneData.GetWorldMatrix(item.Id);
            foreach (var (nodeId, parts) in importedScene.MeshesByNodeId)
            {
                var world = importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var part in parts)
                {
                    if (part.Mesh.LocalBounds is not { } localBounds) continue;
                    var bounds = localBounds.Transform(world);
                    result = result is { } current ? current.Encapsulate(bounds) : bounds;
                }
            }
        }

        return result;
    }

    private void ReimportAsset()
    {
        if (_preview is null || _assetPath is null) return;
        try
        {
            var cleanupError = _preview.Reload(() => PreviewResources.Load(GraphicsDevice, _assetPath));
            if (cleanupError is null)
            {
                _reimportStatus = "GLB reimport succeeded.";
                Console.WriteLine($"Reimported GLB asset from {_assetPath}.");
            }
            else
            {
                _reimportStatus = $"Reimport succeeded; old resource cleanup failed: {cleanupError.Message}";
                Console.WriteLine($"Reimport succeeded, but previous asset cleanup failed: {cleanupError.Message}");
            }

            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Reimport failed; previous asset still active: {exception.Message}";
            Console.WriteLine($"GLB reimport failed; the previous asset remains active: {exception.Message}");
        }
    }

    private sealed class PreviewResources : IDisposable
    {
        private readonly SceneResourceScope _resources;

        private PreviewResources(ImportedGltfScene scene, SceneResourceScope resources,
            Dictionary<StaticMeshData, StaticMeshGpuBuffer> meshBuffers, Dictionary<int, Texture2D> textures)
        {
            Scene = scene;
            _resources = resources;
            MeshBuffers = meshBuffers;
            Textures = textures;
        }

        public ImportedGltfScene Scene { get; }
        public Dictionary<StaticMeshData, StaticMeshGpuBuffer> MeshBuffers { get; }
        public Dictionary<int, Texture2D> Textures { get; }

        public static PreviewResources Load(GraphicsDevice device, string assetPath)
        {
            var resources = new SceneResourceScope();
            try
            {
                var scene = GltfSceneImporter.Load(assetPath);
                var meshBuffers = new Dictionary<StaticMeshData, StaticMeshGpuBuffer>();
                var textures = new Dictionary<int, Texture2D>();
                foreach (var parts in scene.MeshesByNodeId.Values)
                foreach (var part in parts)
                {
                    if (!meshBuffers.ContainsKey(part.Mesh))
                    {
                        var buffer = resources.Own(new StaticMeshGpuBuffer(device, part.Mesh));
                        meshBuffers.Add(part.Mesh, buffer);
                    }

                    if (part.Material.HasBaseColorImage && part.Material.BaseColorImageIndex is { } imageIndex
                        && !textures.ContainsKey(imageIndex))
                    {
                        using var imageStream = new MemoryStream(part.Material.BaseColorImage.ToArray(), writable: false);
                        var texture = resources.Own(Texture2D.FromStream(device, imageStream));
                        textures.Add(imageIndex, texture);
                    }
                }

                return new PreviewResources(scene, resources, meshBuffers, textures);
            }
            catch
            {
                resources.Dispose();
                throw;
            }
        }

        public void Dispose() => _resources.Dispose();
    }
}

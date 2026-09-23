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
    private static readonly Guid CubeId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    private readonly SceneGraph _sceneData;
    private readonly OrbitCamera _camera = new();
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private readonly Dictionary<StaticMeshData, StaticMeshGpuBuffer> _meshBuffers = new();
    private readonly Dictionary<int, Texture2D> _textures = new();
    private SceneResourceScope? _sceneResources;
    private ImportedGltfScene _importedScene = null!;
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
        _meshBuffers.Clear();
        _textures.Clear();
        _sceneResources = new SceneResourceScope();
        try
        {
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

            _importedScene = GltfSceneImporter.Load(Path.Combine(AppContext.BaseDirectory,
                "Assets", "TextureCoordinateTest.glb"));
            foreach (var parts in _importedScene.MeshesByNodeId.Values)
            foreach (var part in parts)
            {
                if (!_meshBuffers.ContainsKey(part.Mesh))
                {
                    var buffer = _sceneResources.Own(new StaticMeshGpuBuffer(GraphicsDevice, part.Mesh));
                    _meshBuffers.Add(part.Mesh, buffer);
                }

                if (part.Material.HasBaseColorImage && part.Material.BaseColorImageIndex is { } imageIndex
                    && !_textures.ContainsKey(imageIndex))
                {
                    using var imageStream = new MemoryStream(part.Material.BaseColorImage.ToArray(), writable: false);
                    var texture = _sceneResources.Own(Texture2D.FromStream(GraphicsDevice, imageStream));
                    _textures.Add(imageIndex, texture);
                }
            }

            _camera.Reset(Vector3.Zero, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);
            _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);

            foreach (var fault in _faults) Console.WriteLine($"character studio: {fault}");
        }
        catch
        {
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
            foreach (var (nodeId, parts) in _importedScene.MeshesByNodeId)
            {
                var world = _importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var part in parts)
                {
                    var factor = part.Material.BaseColorFactor;
                    _studioEffect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                    _studioEffect.Alpha = 1f;
                    _studioEffect.TextureEnabled = part.Material.HasBaseColorImage;
                    _studioEffect.Texture = part.Material.HasBaseColorImage
                        ? _textures[part.Material.BaseColorImageIndex!.Value]
                        : null;
                    GraphicsDevice.RasterizerState = part.Material.DoubleSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullCounterClockwise;
                    _meshBuffers[part.Mesh].Draw(_studioEffect, world, _camera.View, _camera.Projection);
                }
            }
        }

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        _sceneResources?.Dispose();
        _sceneResources = null;
        _meshBuffers.Clear();
        _textures.Clear();
        DisposeHost();
        base.UnloadContent();
    }

    private static SceneGraph CreateDefaultScene()
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(CubeId, "GLB Preview")
        {
            Transform = new Transform()
        });
        return scene;
    }
}

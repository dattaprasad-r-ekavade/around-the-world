using System;
using System.Collections.Generic;
using Ember;
using Ember.Input;
using Ember.Render;
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
    private readonly List<PointLight> _lights = new();
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private SceneRenderer _scene = null!;
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
        _scene = new SceneRenderer(GraphicsDevice);
        AttachScene(_faults);
        _camera.Reset(Vector3.Zero, distance: 7f, yaw: 0.65f, pitch: -0.25f);
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);
        _lights.Add(new PointLight(new Vector3(3f, 5f, 4f), new Vector3(1f, 0.86f, 0.68f) * 2f, 18f));

        foreach (var fault in _faults) Console.WriteLine($"character studio: {fault}");
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
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        _scene.Begin(LitEffect, _camera.View, _camera.Projection, _camera.Position,
            cameraYaw: 0f, StoneTextures.StonePalette.Sandstone, _lights);

        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled) continue;
            var colour = item.Id == CubeId ? new Color(90, 170, 230) : new Color(210, 170, 90);
            _scene.DrawCube(_sceneData.GetWorldMatrix(item.Id), colour);
        }

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        DisposeHost();
        base.UnloadContent();
    }

    private static SceneGraph CreateDefaultScene()
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(CubeId, "Studio Cube")
        {
            Transform = new Transform
            {
                Position = Vector3.Zero,
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(25f)),
                Scale = new Vector3(2f)
            }
        });
        return scene;
    }
}

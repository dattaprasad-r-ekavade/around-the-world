using Ember;
using Ember.Input;
using Ember.Physics;
using Ember.Render;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RpgSlice;

/// <summary>One manifest-loaded outdoor cell using only reusable Ember.Engine systems.</summary>
public sealed class RpgSliceGame : EngineHost
{
    private const string GameWindowTitle = "RPG Slice — Exterior Cell";
    private readonly string _worldManifestPath;
    private readonly bool _smokeControls;
    private readonly bool _streamingSmokeRequested;
    private readonly InputActionMap _actions = new();
    private readonly PhysicsFixedStepper _physicsStepper = new();
    private readonly List<PointLight> _lights = new();
    private readonly List<string> _faults = new();
    private SceneRenderer _renderer = null!;
    private WorldManifest _world = null!;
    private WorldCellDefinition _cell = null!;
    private SceneGraph _scene = null!;
    private HeightmapTerrainRenderer _terrain = null!;
    private PhysicsWorld _physics = null!;
    private PhysicsCharacterController _player = null!;
    private ExteriorCellCollisionGate _collisionGate = null!;
    private ThirdPersonFollowCamera _camera = null!;
    private RpgSliceStreamingSmoke? _streamingSmoke;
    private bool _smokeRan;

    public RpgSliceGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: GameWindowTitle)
    {
        _worldManifestPath = ParseOption(args, "--world")
            ?? Path.Combine(AppContext.BaseDirectory, "Content", "World", WorldManifest.DefaultFileName);
        _smokeControls = HasArgument(args, "--smoke-controls");
        _streamingSmokeRequested = HasArgument(args, "--streaming-smoke");
        if (_smokeControls && _streamingSmokeRequested)
            throw new ArgumentException("Choose either --smoke-controls or --streaming-smoke.", nameof(args));
        _actions.Bind("Exit", Keys.Escape);
    }

    protected override void LoadContent()
    {
        _world = WorldManifest.Load(_worldManifestPath);
        var originCell = _world.GetExteriorCoordinate(Vector3.Zero);
        if (!_world.TryGetExterior(originCell, out var cell) || cell is null)
            throw new InvalidDataException($"World manifest needs an exterior cell at coordinate ({originCell.X}, {originCell.Z}).");
        _cell = cell;
        _scene = SceneFile.Load(_world.ResolveScenePath(_cell.Id));

        _renderer = new SceneRenderer(GraphicsDevice);
        _terrain = new HeightmapTerrainRenderer(GraphicsDevice, new RpgSliceTerrainSource(),
            new TerrainChunkSettings(_world.ExteriorCellWidth, VertexSpacing: 4f, TextureRepeatMetres: 6f),
            chunksAroundCamera: 1);
        AttachScene(_faults);
        foreach (var fault in _faults) Console.WriteLine($"RpgSlice: {fault}");
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);

        _physics = new PhysicsWorld();
        LoadCellGeometry();
        _collisionGate = new ExteriorCellCollisionGate(_world);
        _collisionGate.MarkCollisionReady(originCell);
        _collisionGate.CollisionRequired += coordinate => Console.WriteLine(
            $"RpgSlice: waiting for collision at cell ({coordinate.X}, {coordinate.Z}); movement is held at the boundary.");
        _player = new PhysicsCharacterController(_physics, new Vector3(16f, 1.1f, 23f));
        _player.SetHorizontalMovementGate((current, proposed, clearance) =>
            _collisionGate.Evaluate(current, proposed, clearance).CanMove);
        _camera = new ThirdPersonFollowCamera { TargetOffset = new Vector3(0f, 0.2f, 0f) };
        _camera.Reset(_player.Pose.Position, distance: 9f, yaw: 0f, pitch: -0.18f);
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);
        _camera.Follow(_physics, _player.Pose.Position);
        _lights.Add(new PointLight(new Vector3(12f, 12f, 19f), new Vector3(0.9f, 0.82f, 0.65f) * 1.7f, 28f));

        Console.WriteLine($"RpgSlice: loaded exterior cell {_cell.Id} at ({originCell.X}, {originCell.Z}) from {_cell.ScenePath}");
        if (_smokeControls)
        {
            RunMovementSmoke();
            _smokeRan = true;
            Exit();
        }
        else if (_streamingSmokeRequested)
        {
            _streamingSmoke = new RpgSliceStreamingSmoke(_world);
        }
    }

    private void LoadCellGeometry()
    {
        foreach (var sceneObject in _scene.Objects.Where(item => item.Enabled))
        {
            if (sceneObject.GltfAsset is not null || sceneObject.CharacterSettings is not null)
                throw new InvalidDataException(
                    $"RpgSlice blockout cell does not render GLB scene object '{sceneObject.Name}'.");

            var world = _scene.GetWorldMatrix(sceneObject.Id);
            if (!world.Decompose(out var scale, out var rotation, out var position))
                throw new InvalidDataException($"Cell object '{sceneObject.Name}' has a transform that cannot be decomposed.");
            scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
            if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
                throw new InvalidDataException($"Cell object '{sceneObject.Name}' must have positive dimensions.");

            _physics.AddStaticBox(position, scale, rotation);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();
        if (!_smokeRan)
        {
            if (_streamingSmoke is null)
                UpdateMovement(Keyboard.GetState(), RealSeconds(gameTime), IsActive);
            else if (_streamingSmoke.Tick())
            {
                _streamingSmoke.Dispose();
                _streamingSmoke = null;
                RpgSliceRetentionSmoke.Run(_world);
                _smokeRan = true;
                Exit();
            }
        }
        base.Update(gameTime);
    }

    private void UpdateMovement(KeyboardState keyboard, float elapsedSeconds, bool focused)
    {
        var input = _actions.Sample(keyboard, focused, uiCapturesKeyboard: false);
        if (_actions.ConsumePressed("Exit")) Exit();
        if (_actions.ConsumePressed(GameplayActionNames.Jump)) _player.RequestJump();

        _player.SetMoveInput(_camera.MoveDirection(input.ReadMovement()));
        var result = _physicsStepper.Advance(elapsedSeconds, seconds => _physics.Step(seconds));
        var position = _physics.GetInterpolatedPose(_player.PhysicsBodyId, result.InterpolationAlpha).Position;
        _camera.Follow(_physics, position);
        Window.Title = _player.IsMovementWaitingForCell
            ? _collisionGate.LastMovementResult.State == ExteriorCellCollisionState.MissingCell
                ? "RPG Slice — No exterior cell at boundary"
                : "RPG Slice — Waiting for cell collision"
            : GameWindowTitle;
    }

    private void RunMovementSmoke()
    {
        var start = _player.Pose.Position;
        for (var index = 0; index < 30; index++)
            UpdateMovement(new KeyboardState(Keys.W), 1f / 60f, focused: true);
        UpdateMovement(new KeyboardState(), 1f / 60f, focused: true);
        var end = _player.Pose.Position;
        var distance = Vector3.Distance(start, end);
        if (distance < 0.5f)
            throw new InvalidOperationException($"WASD movement smoke failed: player moved only {distance:0.00}m.");
        Console.WriteLine($"RpgSlice: WASD movement smoke passed ({distance:0.00}m). ");
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(119, 157, 190));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        _renderer.Begin(LitEffect, _camera.View, _camera.Projection, _camera.Position,
            _camera.Yaw, StoneTextures.StonePalette.Sandstone, _lights);
        _terrain.Draw(_camera.View, _camera.Projection, _camera.Position);
        foreach (var sceneObject in _scene.Objects.Where(item => item.Enabled))
        {
            if (sceneObject.Name == "Ground") continue;
            var colour = sceneObject.Name switch
            {
                "Ground" => new Color(132, 133, 106),
                "Foliage" => new Color(60, 111, 71),
                "Trunk" => new Color(117, 82, 54),
                _ => new Color(137, 125, 108)
            };
            _renderer.DrawCube(_scene.GetWorldMatrix(sceneObject.Id), colour);
        }

        var playerPosition = _player.Pose.Position;
        _renderer.DrawCube(playerPosition, new Vector3(0.7f, 1.7f, 0.7f), new Color(65, 112, 178), 0f);
        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera?.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        _streamingSmoke?.Dispose();
        _streamingSmoke = null;
        _terrain?.Dispose();
        _player?.Dispose();
        _physics?.Dispose();
        DisposeHost();
        base.UnloadContent();
    }

    private sealed class RpgSliceTerrainSource : ITerrainHeightMaterialSource
    {
        public TerrainSurfaceSample Sample(float worldX, float worldZ)
        {
            var height = 0.3f + MathF.Sin(worldX * 0.08f) * 0.12f + MathF.Cos(worldZ * 0.07f) * 0.1f;
            var variation = (MathF.Sin((worldX + worldZ) * 0.035f) + 1f) * 0.5f;
            var tint = Color.Lerp(new Color(103, 119, 73), new Color(137, 133, 86), variation * 0.45f);
            return new TerrainSurfaceSample(height, tint);
        }
    }
}

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
    private HeightmapTerrainRenderer _terrain = null!;
    private RpgSliceTerrainSource _terrainSource = null!;
    private TerrainChunkSettings _terrainSettings = null!;
    private PhysicsWorld _physics = null!;
    private PhysicsCharacterController _player = null!;
    private ExteriorCellCollisionGate _collisionGate = null!;
    private RpgSliceCellStreamer _cellStreamer = null!;
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
        if (!_world.TryGetExterior(originCell, out _))
            throw new InvalidDataException($"World manifest needs an exterior cell at coordinate ({originCell.X}, {originCell.Z}).");

        _renderer = new SceneRenderer(GraphicsDevice);
        _terrainSource = new RpgSliceTerrainSource();
        _terrainSettings = new TerrainChunkSettings(_world.ExteriorCellWidth,
            VertexSpacing: 4f, TextureRepeatMetres: 6f);
        _terrain = new HeightmapTerrainRenderer(GraphicsDevice, _terrainSource, _terrainSettings,
            chunksAroundCamera: 1);
        AttachScene(_faults);
        foreach (var fault in _faults) Console.WriteLine($"RpgSlice: {fault}");
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);

        _physics = new PhysicsWorld();
        _collisionGate = new ExteriorCellCollisionGate(_world);
        _collisionGate.CollisionRequired += coordinate => Console.WriteLine(
            $"RpgSlice: waiting for collision at cell ({coordinate.X}, {coordinate.Z}); movement is held at the boundary.");
        _cellStreamer = new RpgSliceCellStreamer(
            _world, _physics, _collisionGate, _terrainSource, _terrainSettings);
        _collisionGate.CollisionRequired += _cellStreamer.Request;
        _cellStreamer.Start(Vector3.Zero);
        _player = new PhysicsCharacterController(_physics, new Vector3(16f, 1.1f, 23f));
        _player.SetHorizontalMovementGate((current, proposed, clearance) =>
            _collisionGate.Evaluate(current, proposed, clearance).CanMove);
        _camera = new ThirdPersonFollowCamera { TargetOffset = new Vector3(0f, 0.2f, 0f) };
        _camera.Reset(_player.Pose.Position, distance: 9f, yaw: 0f, pitch: -0.18f);
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);
        _camera.Follow(_physics, _player.Pose.Position);
        _lights.Add(new PointLight(new Vector3(12f, 12f, 19f), new Vector3(0.9f, 0.82f, 0.65f) * 1.7f, 28f));

        Console.WriteLine($"RpgSlice: loaded exterior cell at ({originCell.X}, {originCell.Z}) from {_world.FilePath}");
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

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();
        if (!_smokeRan)
        {
            if (_streamingSmoke is null)
            {
                _cellStreamer.Update(_player.Pose.Position);
                UpdateMovement(Keyboard.GetState(), RealSeconds(gameTime), IsActive);
            }
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
        var startCell = _world.GetExteriorCoordinate(start);
        var endCell = startCell;
        for (var index = 0; index < 360; index++)
        {
            _cellStreamer.Update(_player.Pose.Position);
            UpdateMovement(new KeyboardState(Keys.W), 1f / 60f, focused: true);
            endCell = _world.GetExteriorCoordinate(_player.Pose.Position);
            if (endCell != startCell) break;
        }
        _cellStreamer.Update(_player.Pose.Position);
        UpdateMovement(new KeyboardState(), 1f / 60f, focused: true);
        var end = _player.Pose.Position;
        var distance = Vector3.Distance(start, end);
        if (distance < 0.5f)
            throw new InvalidOperationException($"WASD movement smoke failed: player moved only {distance:0.00}m.");
        if (endCell == startCell)
            throw new InvalidOperationException("Terrain streaming smoke did not cross an exterior cell boundary.");
        if (!_player.IsGrounded)
            throw new InvalidOperationException($"Terrain streaming smoke lost ground contact at {end}.");
        Console.WriteLine($"RpgSlice: terrain cell-seam smoke passed ({distance:0.00}m, "
            + $"({startCell.X}, {startCell.Z}) to ({endCell.X}, {endCell.Z}), grounded).");
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(119, 157, 190));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        _renderer.Begin(LitEffect, _camera.View, _camera.Projection, _camera.Position,
            _camera.Yaw, StoneTextures.StonePalette.Sandstone, _lights);
        _terrain.Draw(_camera.View, _camera.Projection, _camera.Position);
        foreach (var activeCell in _cellStreamer.ActiveCells)
        foreach (var sceneObject in activeCell.Scene.Objects.Where(item => item.Enabled))
        {
            if (sceneObject.Name == "Ground") continue;
            var colour = sceneObject.Name switch
            {
                "Foliage" => new Color(60, 111, 71),
                "Trunk" => new Color(117, 82, 54),
                _ => new Color(137, 125, 108)
            };
            _renderer.DrawCube(activeCell.Scene.GetWorldMatrix(sceneObject.Id), colour);
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
        _cellStreamer?.Dispose();
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

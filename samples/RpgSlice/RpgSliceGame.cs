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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RpgSlice;

/// <summary>One manifest-loaded outdoor cell using only reusable Ember.Engine systems.</summary>
public sealed class RpgSliceGame : EngineHost
{
    private const string GameWindowTitle = "RPG Slice — Exterior Cell";
    private readonly string _worldManifestPath;
    private readonly bool _smokeControls;
    private readonly bool _streamingSmokeRequested;
    private readonly bool _benchmarkRequested;
    private readonly bool _timePaused;
    private readonly InputActionMap _actions = new();
    private readonly PhysicsFixedStepper _physicsStepper = new();
    private readonly List<PointLight> _lights = new();
    private readonly List<string> _faults = new();
    private readonly List<StaticMeshInstance> _foliageInstances = new(32);
    private SceneRenderer _renderer = null!;
    private InstancedStaticMeshRenderer? _foliageInstancer;
    private WorldManifest _world = null!;
    private HeightmapTerrainRenderer _terrain = null!;
    private WaterSurfaceRenderer _water = null!;
    private RpgSliceTerrainSource _terrainSource = null!;
    private TerrainChunkSettings _terrainSettings = null!;
    private PhysicsWorld _physics = null!;
    private PhysicsCharacterController _player = null!;
    private ExteriorCellCollisionGate _collisionGate = null!;
    private RpgSliceCellStreamer _cellStreamer = null!;
    private ThirdPersonFollowCamera _camera = null!;
    private float _timeOfDayHours = 12f;
    private Vector3 _renderPlayerPosition;
    private RpgSliceStreamingSmoke? _streamingSmoke;
    private RpgSliceOutdoorBenchmark? _benchmark;
    private bool _benchmarkReportWritten;
    private bool _smokeRan;

    public RpgSliceGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: GameWindowTitle)
    {
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _worldManifestPath = ParseOption(args, "--world")
            ?? Path.Combine(AppContext.BaseDirectory, "Content", "World", WorldManifest.DefaultFileName);
        _smokeControls = HasArgument(args, "--smoke-controls");
        _streamingSmokeRequested = HasArgument(args, "--streaming-smoke");
        _benchmarkRequested = HasArgument(args, "--benchmark");
        _timePaused = HasArgument(args, "--time-paused");
        if ((_smokeControls ? 1 : 0) + (_streamingSmokeRequested ? 1 : 0) + (_benchmarkRequested ? 1 : 0) > 1)
            throw new ArgumentException("Choose one of --smoke-controls, --streaming-smoke, or --benchmark.", nameof(args));
        if (_benchmarkRequested) _graphics.SynchronizeWithVerticalRetrace = false;
        if (ParseOption(args, "--time-hours") is { } timeText)
        {
            if (!float.TryParse(timeText, NumberStyles.Float, CultureInfo.InvariantCulture, out _timeOfDayHours)
                || !float.IsFinite(_timeOfDayHours))
                throw new ArgumentException("--time-hours must be a finite number.", nameof(args));
        }
        _actions.Bind("Exit", Keys.Escape);
        _actions.Bind("TimeEarlier", Keys.PageDown);
        _actions.Bind("TimeLater", Keys.PageUp);
    }

    protected override void LoadContent()
    {
        _world = WorldManifest.Load(_worldManifestPath);
        var originCell = _world.GetExteriorCoordinate(Vector3.Zero);
        if (!_world.TryGetExterior(originCell, out _))
            throw new InvalidDataException($"World manifest needs an exterior cell at coordinate ({originCell.X}, {originCell.Z}).");

        _renderer = new SceneRenderer(GraphicsDevice);
        if (GraphicsDevice.GraphicsProfile == GraphicsProfile.HiDef)
        {
            try
            {
                var (vertices, indices) = SceneRenderer.CreateCubeMesh();
                _foliageInstancer = new InstancedStaticMeshRenderer(GraphicsDevice,
                    Content.Load<Effect>("Effects/InstancedStaticMesh"), vertices, indices);
            }
            catch (Exception exception)
            {
                _faults.Add($"static-mesh instancing unavailable; foliage uses individual draws ({exception.GetType().Name})");
            }
        }
        else
        {
            _faults.Add("static-mesh instancing requires HiDef; foliage uses individual draws");
        }
        _terrainSource = new RpgSliceTerrainSource();
        _terrainSettings = new TerrainChunkSettings(_world.ExteriorCellWidth,
            VertexSpacing: 4f, TextureRepeatMetres: 6f);
        _terrain = new HeightmapTerrainRenderer(GraphicsDevice, _terrainSource, _terrainSettings,
            chunksAroundCamera: 1);
        _water = new WaterSurfaceRenderer(GraphicsDevice,
            halfExtent: _world.ExteriorCellWidth * 5f, textureTileMetres: 8f);
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
        _renderPlayerPosition = _player.Pose.Position;
        _player.SetHorizontalMovementGate((current, proposed, clearance) =>
            _collisionGate.Evaluate(current, proposed, clearance).CanMove);
        _camera = new ThirdPersonFollowCamera { TargetOffset = new Vector3(0f, 0.2f, 0f) };
        _camera.Reset(_player.Pose.Position, distance: 9f, yaw: 0f, pitch: -0.18f);
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio);
        _camera.Follow(_physics, _player.Pose.Position);
        _lights.Add(new PointLight(new Vector3(12f, 12f, 19f), new Vector3(0.9f, 0.82f, 0.65f) * 1.7f, 28f));

        Console.WriteLine($"RpgSlice: loaded exterior cell at ({originCell.X}, {originCell.Z}) from {_world.FilePath}");
        Console.WriteLine($"RpgSlice: graphics profile {GraphicsDevice.GraphicsProfile}; "
            + $"static-mesh instancing={(_foliageInstancer is null ? "unavailable" : "enabled")}.");
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
        else if (_benchmarkRequested)
        {
            _benchmark = new RpgSliceOutdoorBenchmark(_player.Pose.Position);
            Console.WriteLine($"RpgSlice: outdoor benchmark started on {GraphicsDevice.Adapter.Description}, "
                + $"{GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height}, "
                + $"{GraphicsDevice.GraphicsProfile}; complete one warmup and ten measured laps.");
        }
    }

    protected override void Update(GameTime gameTime)
    {
        _benchmark?.RecordFrame(Stopwatch.GetTimestamp());
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
        if (_actions.ConsumePressed("TimeEarlier")) _timeOfDayHours -= 1f;
        if (_actions.ConsumePressed("TimeLater")) _timeOfDayHours += 1f;
        if (!_timePaused) _timeOfDayHours = (_timeOfDayHours + elapsedSeconds / 60f) % 24f;
        if (_timeOfDayHours < 0f) _timeOfDayHours += 24f;
        if (_actions.ConsumePressed(GameplayActionNames.Jump)) _player.RequestJump();

        _player.SetMoveInput(_camera.MoveDirection(input.ReadMovement()));
        if (_benchmark is { IsComplete: false } routeBenchmark)
            _player.SetMoveInput(routeBenchmark.GetMoveDirection(_player.Pose.Position));
        var result = _physicsStepper.Advance(elapsedSeconds, seconds => _physics.Step(seconds));
        var position = _physics.GetInterpolatedPose(_player.PhysicsBodyId, result.InterpolationAlpha).Position;
        _renderPlayerPosition = position;
        _camera.Follow(_physics, position);
        if (_benchmark is { IsMeasuring: true } benchmark && !benchmark.IsComplete)
        {
            var benchmarkElapsedSeconds = benchmark.MeasuredElapsedSeconds;
            var workingSetBytes = 0L;
            if (benchmark.ShouldSampleWorkingSet(benchmarkElapsedSeconds))
            {
                using var process = Process.GetCurrentProcess();
                workingSetBytes = process.WorkingSet64;
            }
            var trackedResources = 2 + _terrain.CachedChunkCount + 4
                + (_foliageInstancer?.OwnedGraphicsResourceCount ?? 0);
            benchmark.ObserveRuntime(_world.GetExteriorCoordinate(position), _cellStreamer.ActiveCellCount,
                _terrain.CachedChunkCount, trackedResources, workingSetBytes, benchmarkElapsedSeconds);
        }
        if (_benchmark is { IsComplete: true } && !_benchmarkReportWritten)
        {
            _benchmarkReportWritten = true;
            var report = _benchmark.BuildReport(_cellStreamer.LongestActivationMilliseconds,
                _cellStreamer.ActivationAttemptCount, _foliageInstancer is not null,
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
                GraphicsDevice.Adapter.Description);
            var reportPath = Path.Combine(Path.GetTempPath(), "ember-rpgslice-outdoor-benchmark.txt");
            File.WriteAllText(reportPath, report, Encoding.UTF8);
            Console.WriteLine(report);
            Console.WriteLine($"RpgSlice benchmark report saved to {reportPath}");
            Exit();
        }
        if (_benchmark is { IsComplete: false } runningBenchmark)
        {
            Window.Title = $"RPG Slice — {runningBenchmark.ProgressLabel}, leg {runningBenchmark.CurrentLeg}/{runningBenchmark.LegCount}, "
                + $"target ({runningBenchmark.CurrentTarget.X:0}, {runningBenchmark.CurrentTarget.Y:0})";
        }
        else
        {
            Window.Title = _player.IsMovementWaitingForCell
                ? _collisionGate.LastMovementResult.State == ExteriorCellCollisionState.MissingCell
                    ? "RPG Slice — No exterior cell at boundary"
                    : "RPG Slice — Waiting for cell collision"
                : GameWindowTitle;
        }
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
        var environment = OutdoorEnvironmentProfile.Evaluate(_timeOfDayHours,
            fogStart: _world.ExteriorCellWidth * 1.25f,
            fogEnd: _world.ExteriorCellWidth * 3f);
        GraphicsDevice.Clear(environment.SkyColor);
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        LitEffect.FogEnabled = true;
        LitEffect.FogColor = environment.FogColor.ToVector3();
        LitEffect.FogStart = environment.FogStart;
        LitEffect.FogEnd = environment.FogEnd;
        LitEffect.AmbientLightColor = environment.AmbientLightColor;
        LitEffect.DirectionalLight0.Direction = environment.LightDirection;
        LitEffect.DirectionalLight0.DiffuseColor = environment.DirectionalLightColor;
        LitEffect.DirectionalLight0.SpecularColor = environment.DirectionalLightColor * 0.2f;

        _renderer.Begin(LitEffect, _camera.View, _camera.Projection, _camera.Position,
            _camera.Yaw, StoneTextures.StonePalette.Sandstone, _lights);
        _terrain.Draw(_camera.View, _camera.Projection, _camera.Position, environment);
        _foliageInstances.Clear();
        foreach (var activeCell in _cellStreamer.ActiveCells)
        {
            foreach (var sceneObject in activeCell.Scene.Objects)
            {
                if (!sceneObject.Enabled || sceneObject.Name == "Ground") continue;
                var colour = sceneObject.Name switch
                {
                    "Foliage" => new Color(60, 111, 71),
                    "Trunk" => new Color(117, 82, 54),
                    _ => new Color(137, 125, 108)
                };
                var world = activeCell.Scene.GetWorldMatrix(sceneObject.Id) * activeCell.WorldTransform;
                if (sceneObject.Name == "Foliage" && _foliageInstancer is not null)
                    _foliageInstances.Add(new StaticMeshInstance(world, colour));
                else
                    _renderer.DrawCube(world, colour);
            }
        }

        if (_foliageInstancer is not null && _foliageInstances.Count > 0)
        {
            _foliageInstancer.Draw(_foliageInstances, _camera.View * _camera.Projection,
                _camera.Position, environment.LightDirection, environment.DirectionalLightColor,
                environment.AmbientLightColor, environment.FogColor.ToVector3(),
                environment.FogStart, environment.FogEnd);
            _benchmark?.ObserveInstancing(_foliageInstancer.LastInstanceCount,
                _foliageInstancer.LastDrawCallCount);
        }

        var playerPosition = _renderPlayerPosition;
        _renderer.DrawCube(playerPosition, new Vector3(0.7f, 1.7f, 0.7f), new Color(65, 112, 178), 0f);
        _water.Draw(_camera.View, _camera.Projection, _camera.Position, environment, waterLevel: 0.4f);
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
        _foliageInstancer?.Dispose();
        _water?.Dispose();
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

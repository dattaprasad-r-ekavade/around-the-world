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
    private readonly bool _travelSmokeRequested;
    private readonly bool _persistenceSmokeRequested;
    private int _travelSmokeApproachFrames;
    private readonly bool _benchmarkRequested;
    private readonly bool _timePaused;
    private readonly string _worldSavePath;
    private readonly bool _deleteSmokeSaveOnExit;
    private readonly InputActionMap _actions = new();
    private readonly PhysicsFixedStepper _physicsStepper = new();
    private readonly List<PointLight> _lights = new();
    private readonly List<string> _faults = new();
    private readonly List<StaticMeshInstance> _foliageInstances = new(32);
    private SceneRenderer _renderer = null!;
    private InstancedStaticMeshRenderer? _foliageInstancer;
    private WorldManifest _world = null!;
    private WorldPersistenceSession _worldPersistence = null!;
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
    private string? _saveFeedback;
    private float _saveFeedbackSeconds;
    private TravelSmokePhase _travelSmokePhase;
    private bool _insideInterior;
    private Guid _currentCellId;
    private Quaternion _playerFacing = Quaternion.Identity;
    private WorldCellLoadOperation<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>? _interiorOperation;
    private WorldCellLoadOperation<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>? _travelDestination;
    private WorldCellTravelTransaction<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>? _travel;

    public RpgSliceGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: GameWindowTitle)
    {
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _worldManifestPath = ParseOption(args, "--world")
            ?? Path.Combine(AppContext.BaseDirectory, "Content", "World", WorldManifest.DefaultFileName);
        _smokeControls = HasArgument(args, "--smoke-controls");
        _streamingSmokeRequested = HasArgument(args, "--streaming-smoke");
        _travelSmokeRequested = HasArgument(args, "--travel-smoke");
        _persistenceSmokeRequested = HasArgument(args, "--persistence-smoke");
        _benchmarkRequested = HasArgument(args, "--benchmark");
        _timePaused = HasArgument(args, "--time-paused");
        if ((_smokeControls ? 1 : 0) + (_streamingSmokeRequested ? 1 : 0)
            + (_travelSmokeRequested ? 1 : 0) + (_persistenceSmokeRequested ? 1 : 0)
            + (_benchmarkRequested ? 1 : 0) > 1)
            throw new ArgumentException("Choose one RpgSlice smoke or benchmark mode.", nameof(args));
        var configuredSavePath = ParseOption(args, "--save");
        _deleteSmokeSaveOnExit = _persistenceSmokeRequested && configuredSavePath is null;
        _worldSavePath = configuredSavePath
            ?? (_persistenceSmokeRequested
                ? Path.Combine(Path.GetTempPath(), $"ember-rpgslice-persistence-{Guid.NewGuid():N}.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ember", "RpgSlice", "world-save.json"));
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
        _actions.Bind("Interact", Keys.E);
        _actions.Bind("Save", Keys.F5);
    }

    protected override void LoadContent()
    {
        _world = WorldManifest.Load(_worldManifestPath);
        var originCell = _world.GetExteriorCoordinate(Vector3.Zero);
        if (!_world.TryGetExterior(originCell, out var originDefinition) || originDefinition is null)
            throw new InvalidDataException($"World manifest needs an exterior cell at coordinate ({originCell.X}, {originCell.Z}).");
        var loadedSave = File.Exists(_worldSavePath)
            ? WorldSaveFile.Load(_worldSavePath, _world)
            : null;
        _worldPersistence = new WorldPersistenceSession(_world, loadedSave);
        var initialLocation = _worldPersistence.RestoredPlayerLocation
            ?? new WorldPlayerLocation(originDefinition.Id, new Vector3(16f, 1.1f, 23f), Quaternion.Identity);
        var initialCell = _world.FindCell(initialLocation.CellId)
            ?? throw new InvalidDataException($"Player save points to unknown cell {initialLocation.CellId}.");
        _currentCellId = initialLocation.CellId;
        _playerFacing = initialLocation.Facing;

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
            _world, _physics, _collisionGate, _terrainSource, _terrainSettings, _worldPersistence);
        _collisionGate.CollisionRequired += _cellStreamer.Request;
        if (initialCell.Kind == WorldCellKind.Exterior)
        {
            if (initialCell.ExteriorCoordinate != _world.GetExteriorCoordinate(initialLocation.Position))
                throw new InvalidDataException("Saved player position does not belong to its saved exterior cell.");
            _cellStreamer.Start(initialLocation.Position);
        }
        else
        {
            _cellStreamer.Start(Vector3.Zero);
            _interiorOperation = _cellStreamer.LoadCell(initialCell.Id);
            _insideInterior = true;
        }
        _player = new PhysicsCharacterController(_physics, initialLocation.Position);
        _renderPlayerPosition = _player.Pose.Position;
        _player.SetHorizontalMovementGate(initialCell.Kind == WorldCellKind.Exterior
            ? (current, proposed, clearance) => _collisionGate.Evaluate(current, proposed, clearance).CanMove
            : null);
        _camera = new ThirdPersonFollowCamera { TargetOffset = new Vector3(0f, 0.2f, 0f) };
        _camera.Reset(_player.Pose.Position, distance: 9f, CameraYaw(_playerFacing), pitch: -0.18f);
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
        else if (_travelSmokeRequested)
        {
            _travelSmokePhase = TravelSmokePhase.ApproachExteriorDoor;
            Console.WriteLine("RpgSlice: door travel smoke started; walking to House A and using E at both doors.");
        }
        else if (_persistenceSmokeRequested)
        {
            if (loadedSave is not null)
                throw new InvalidOperationException("Persistence smoke requires a fresh save path.");
            RunPersistenceSmoke();
            _smokeRan = true;
            Exit();
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
                if (!_insideInterior)
                    _cellStreamer.Update(_player.Pose.Position);
                if (_travel is not null)
                    AdvanceDoorTravel();
                var keyboard = _travelSmokeRequested
                    ? FindNearbyDoor() is not null ? new KeyboardState(Keys.E) : new KeyboardState()
                    : Keyboard.GetState();
                UpdateMovement(keyboard, RealSeconds(gameTime), IsActive);
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
        if (_actions.ConsumePressed("Interact") && _travel is null
            && FindNearbyDoor() is { } nearbyDoor)
            BeginDoorTravel(nearbyDoor);
        if (_actions.ConsumePressed("Save"))
        {
            _worldPersistence.RequestSave(_worldSavePath, CapturePlayerLocation);
            _saveFeedback = "Save queued";
            _saveFeedbackSeconds = 2f;
        }
        if (_actions.ConsumePressed(GameplayActionNames.Jump) && _travel is null)
            _player.RequestJump();

        var moveDirection = _camera.MoveDirection(input.ReadMovement());
        if (_travelSmokeRequested && _travel is null
            && _travelSmokePhase is TravelSmokePhase.ApproachExteriorDoor or TravelSmokePhase.ApproachInteriorDoor)
        {
            var targetDoor = FindSmokeTargetDoor()
                ?? throw new InvalidOperationException("Travel smoke lost its target door.");
            var targetTransform = GetCurrentActiveCell().Scene.GetWorldMatrix(targetDoor.Id)
                * GetCurrentActiveCell().WorldTransform;
            moveDirection = new Vector3(targetTransform.M41 - _player.Pose.Position.X, 0f,
                targetTransform.M43 - _player.Pose.Position.Z);
            if (moveDirection.LengthSquared() > 1e-6f) moveDirection.Normalize();
        }
        _player.SetMoveInput(_travel is null ? moveDirection : Vector3.Zero);
        if (_travelSmokeRequested && _travel is null
            && _travelSmokePhase is TravelSmokePhase.ApproachExteriorDoor or TravelSmokePhase.ApproachInteriorDoor
            && ++_travelSmokeApproachFrames > 1200)
            throw new TimeoutException("Travel smoke could not reach the authored door within 20 seconds.");
        if (_benchmark is { IsComplete: false } routeBenchmark)
            _player.SetMoveInput(routeBenchmark.GetMoveDirection(_player.Pose.Position));
        var result = _physicsStepper.Advance(elapsedSeconds, seconds => _physics.Step(seconds));
        var position = _physics.GetInterpolatedPose(_player.PhysicsBodyId, result.InterpolationAlpha).Position;
        _renderPlayerPosition = position;
        _camera.Follow(_physics, position);
        if (!_insideInterior)
        {
            var coordinate = _world.GetExteriorCoordinate(position);
            if (_world.TryGetExterior(coordinate, out var currentDefinition) && currentDefinition is not null)
                _currentCellId = currentDefinition.Id;
        }
        _saveFeedbackSeconds = MathF.Max(0f, _saveFeedbackSeconds - elapsedSeconds);
        if (_saveFeedbackSeconds <= 0f) _saveFeedback = null;
        _worldPersistence.ProcessStableBoundary(_travel is not null);
        while (_worldPersistence.TryDequeueSaveResult(out var saveResult))
        {
            _saveFeedback = saveResult.Failure is null ? "World saved" : $"Save failed: {saveResult.Failure.Message}";
            _saveFeedbackSeconds = 3f;
            Console.WriteLine(saveResult.Failure is null
                ? $"RpgSlice: world saved to {saveResult.Path}"
                : $"RpgSlice: world save failed: {saveResult.Failure}");
        }
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
        else if (_travel is { } travel)
        {
            Window.Title = travel.State == WorldCellTravelState.DestinationReady
                ? "RPG Slice — destination ready; committing travel"
                : "RPG Slice — preparing destination";
        }
        else if (_saveFeedback is { } saveFeedback)
        {
            Window.Title = $"RPG Slice — {saveFeedback}";
        }
        else if (FindNearbyDoor() is { } promptDoor)
        {
            Window.Title = $"RPG Slice — Press E to use {promptDoor.Name}";
        }
        else
        {
            Window.Title = _cellStreamer.LoadingStatus is { } streamingStatus
                ? $"RPG Slice — {streamingStatus}"
                : _player.IsMovementWaitingForCell
                ? _collisionGate.LastMovementResult.State == ExteriorCellCollisionState.MissingCell
                    ? "RPG Slice — No exterior cell at boundary"
                    : "RPG Slice — Waiting for cell collision"
                : GameWindowTitle;
        }
    }

    private void BeginDoorTravel(SceneObject doorObject)
    {
        var door = doorObject.Door
            ?? throw new ArgumentException("The selected scene object is not a door.", nameof(doorObject));
        if (_travel is not null)
            throw new InvalidOperationException("A world travel transaction is already active.");
        if (door.DestinationCellId == _currentCellId)
            throw new InvalidOperationException("A world door cannot travel to its current cell.");

        WorldCellLoadOperation<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell> source;
        if (_insideInterior)
        {
            source = _interiorOperation
                ?? throw new InvalidOperationException("The current interior has no active cell operation.");
        }
        else if (!_cellStreamer.TryGetActiveCell(_currentCellId, out var exteriorOperation, out _)
            || exteriorOperation is null)
        {
            throw new InvalidOperationException($"Exterior cell {_currentCellId} is not active for travel.");
        }
        else
        {
            source = exteriorOperation;
        }

        var targetScene = SceneFile.Load(_world.ResolveScenePath(door.DestinationCellId));
        var targetScenes = new Dictionary<Guid, SceneGraph> { [door.DestinationCellId] = targetScene };
        var destinationSpawn = WorldTravelValidator.ResolveDestination(_world, targetScenes, door);
        if (_world.FindCell(door.DestinationCellId)?.Kind == WorldCellKind.Exterior)
            _cellStreamer.UnloadAndForgetCell(door.DestinationCellId);
        _travelDestination = new WorldCellLoadOperation<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>();
        _travel = new WorldCellTravelTransaction<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>(
            _currentCellId,
            source,
            _travelDestination,
            destinationSpawn,
            _cellStreamer.PrepareCellAsync,
            _cellStreamer.ActivatePreparedCell,
            PlacePlayerAtSpawn);
        if (_travelSmokeRequested)
        {
            _travelSmokePhase = _insideInterior
                ? TravelSmokePhase.Returning
                : TravelSmokePhase.Entering;
            _travelSmokeApproachFrames = 0;
        }
    }

    private void AdvanceDoorTravel()
    {
        if (_travel is not { } travel) return;
        travel.Tick();
        if (travel.State == WorldCellTravelState.Completed)
        {
            CompleteDoorTravel(travel);
            return;
        }
        if (travel.State != WorldCellTravelState.Failed) return;

        var failure = travel.Failure ?? new InvalidOperationException("World travel failed without an error.");
        if (_travelDestination?.State is CellLifecycleState.Ready or CellLifecycleState.Failed)
            _travelDestination!.Discard();
        _travel = null;
        _travelDestination = null;
        Console.WriteLine($"RpgSlice: door travel failed: {failure}");
        if (_travelSmokeRequested)
            throw new InvalidOperationException("Door travel smoke failed.", failure);
    }

    private void CompleteDoorTravel(
        WorldCellTravelTransaction<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell> travel)
    {
        var sourceCellId = travel.SourceCellId;
        var destinationCellId = travel.DestinationCellId;
        var destinationOperation = _travelDestination
            ?? throw new InvalidOperationException("Completed travel has no destination operation.");
        var destination = _world.FindCell(destinationCellId)
            ?? throw new InvalidOperationException($"Travel destination {destinationCellId} is absent from the world manifest.");

        if (destination.Kind == WorldCellKind.Interior)
        {
            if (_world.FindCell(sourceCellId)?.Kind == WorldCellKind.Exterior
                && !_cellStreamer.ForgetUnloadedCell(sourceCellId))
                throw new InvalidOperationException($"Unloaded exterior cell {sourceCellId} was not tracked by its streamer.");
            if (_interiorOperation is { State: CellLifecycleState.Failed } failedInterior)
                failedInterior.Discard();
            _interiorOperation = destinationOperation;
            _insideInterior = true;
        }
        else
        {
            if (_world.FindCell(sourceCellId)?.Kind == WorldCellKind.Exterior
                && !_cellStreamer.ForgetUnloadedCell(sourceCellId))
                throw new InvalidOperationException($"Unloaded exterior source cell {sourceCellId} was not tracked by its streamer.");
            if (_interiorOperation is { State: CellLifecycleState.Failed } failedInterior)
                failedInterior.Discard();
            _cellStreamer.AdoptActiveCell(destinationCellId, destinationOperation);
            _interiorOperation = null;
            _insideInterior = false;
            _cellStreamer.Update(travel.DestinationSpawn.Position);
        }

        if (travel.Failure is { } cleanupFailure)
            Console.WriteLine($"RpgSlice: travel committed, but source-cell cleanup reported: {cleanupFailure}");

        _currentCellId = destinationCellId;
        _travel = null;
        _travelDestination = null;

        if (!_travelSmokeRequested) return;
        if (_travelSmokePhase == TravelSmokePhase.Entering)
        {
            if (!_insideInterior || Vector3.Distance(_player.Pose.Position, travel.DestinationSpawn.Position) > 0.01f)
                throw new InvalidOperationException("Door travel smoke did not place the player in House A.");
            _travelSmokePhase = TravelSmokePhase.ApproachInteriorDoor;
            _travelSmokeApproachFrames = 0;
        }
        else if (_travelSmokePhase == TravelSmokePhase.Returning)
        {
            if (_insideInterior
                || _world.FindCell(travel.DestinationCellId)?.ExteriorCoordinate != new ExteriorCellCoordinate(0, 0)
                || Vector3.Distance(_player.Pose.Position, travel.DestinationSpawn.Position) > 0.01f)
                throw new InvalidOperationException("Door travel smoke did not return the player to the starting exterior cell.");
            Console.WriteLine("RpgSlice: exterior to House A to exterior travel smoke passed.");
            _travelSmokePhase = TravelSmokePhase.Complete;
            _smokeRan = true;
            Exit();
        }
    }

    private void PlacePlayerAtSpawn(WorldSpawnLocation spawn)
    {
        var nextPlayer = new PhysicsCharacterController(_physics, spawn.Position);
        _player.Dispose();
        _player = nextPlayer;
        _playerFacing = spawn.Facing;
        var targetIsExterior = _world.FindCell(spawn.CellId)?.Kind == WorldCellKind.Exterior;
        _player.SetHorizontalMovementGate(targetIsExterior
            ? (current, proposed, clearance) => _collisionGate.Evaluate(current, proposed, clearance).CanMove
            : null);
        _physicsStepper.Reset();
        _renderPlayerPosition = spawn.Position;
        var forward = Vector3.Transform(Vector3.Forward, spawn.Facing);
        var yaw = MathF.Atan2(-forward.X, -forward.Z);
        _camera.Reset(spawn.Position, distance: 9f, yaw, pitch: -0.18f);
        _camera.Follow(_physics, spawn.Position);
    }

    private SceneObject? FindNearbyDoor()
    {
        var active = TryGetCurrentActiveCell();
        if (active is null) return null;
        var playerPosition = _player.Pose.Position;
        const float reach = 2.6f;
        SceneObject? nearest = null;
        var nearestDistanceSquared = reach * reach;
        foreach (var sceneObject in active.Scene.Objects)
        {
            if (!sceneObject.Enabled || sceneObject.Door is null) continue;
            var transform = active.Scene.GetWorldMatrix(sceneObject.Id) * active.WorldTransform;
            var position = new Vector3(transform.M41, transform.M42, transform.M43);
            var offset = playerPosition - position;
            offset.Y = 0f;
            var distanceSquared = offset.LengthSquared();
            if (distanceSquared > nearestDistanceSquared) continue;
            nearestDistanceSquared = distanceSquared;
            nearest = sceneObject;
        }
        return nearest;
    }

    private SceneObject? FindSmokeTargetDoor()
    {
        var active = TryGetCurrentActiveCell();
        if (active is null) return null;
        return _insideInterior
            ? active.Scene.Objects.FirstOrDefault(item => item.Door is not null)
            : active.Scene.Objects.FirstOrDefault(item => item.Door is not null && item.Name == "Door to House A");
    }

    private RpgSliceCellStreamer.ActiveCell? TryGetCurrentActiveCell()
    {
        if (_insideInterior)
            return _interiorOperation?.ActiveResources;
        return _cellStreamer.TryGetActiveCell(_currentCellId, out _, out var active)
            ? active
            : null;
    }

    private RpgSliceCellStreamer.ActiveCell GetCurrentActiveCell() =>
        TryGetCurrentActiveCell()
        ?? throw new InvalidOperationException($"Current world cell {_currentCellId} is not active.");

    private void RunPersistenceSmoke()
    {
        if (File.Exists(_worldSavePath))
            throw new InvalidOperationException("Persistence smoke requires a fresh save path.");

        try
        {
            var exterior = GetCurrentActiveCell();
            var marker = exterior.Scene.Objects.FirstOrDefault(item => item.Name == "Exterior Return Spawn")
                ?? throw new InvalidDataException("Persistence smoke needs the authored exterior return spawn.");
            _worldPersistence.SetTransform(_currentCellId, marker, new Transform
            {
                Position = new Vector3(18f, 1.1f, 23f),
                Rotation = marker.Transform.Rotation,
                Scale = marker.Transform.Scale
            });
            _worldPersistence.SetEnabled(_currentCellId, marker, true);
            var runtime = _worldPersistence.Spawn(_currentCellId, exterior.Scene,
                new SceneObject(Guid.NewGuid(), "Persistence Smoke Gem")
                {
                    Transform = new Transform { Position = new Vector3(19f, 1f, 20f) }
                });

            var entryDoor = exterior.Scene.Objects.FirstOrDefault(item => item.Name == "Door to House A")
                ?? throw new InvalidDataException("Persistence smoke needs the authored House A door.");
            var interiorScene = SceneFile.Load(_world.ResolveScenePath(entryDoor.Door!.DestinationCellId));
            var destinationSpawn = WorldTravelValidator.ResolveDestination(_world,
                new Dictionary<Guid, SceneGraph> { [entryDoor.Door.DestinationCellId] = interiorScene }, entryDoor.Door);
            _worldPersistence.RequestSave(_worldSavePath,
                () => new WorldPlayerLocation(destinationSpawn.CellId, destinationSpawn.Position, destinationSpawn.Facing));
            if (!_worldPersistence.ProcessStableBoundary(travelInProgress: false))
                throw new InvalidOperationException("Persistence smoke save was not processed at the stable boundary.");
            if (!_worldPersistence.TryDequeueSaveResult(out var writeResult))
                throw new InvalidOperationException("Persistence smoke did not receive a save result.");
            if (writeResult.Failure is not null)
                throw new InvalidOperationException($"Persistence smoke could not save: {writeResult.Failure}");

            var snapshot = WorldSaveFile.Load(_worldSavePath, _world);
            if (snapshot.PlayerLocation.CellId != destinationSpawn.CellId)
                throw new InvalidOperationException("Queued persistence smoke save did not capture the committed interior location.");
            var restarted = new WorldPersistenceSession(_world, snapshot);
            using var verificationPhysics = new PhysicsWorld();
            var verificationGate = new ExteriorCellCollisionGate(_world);
            using var verificationStreamer = new RpgSliceCellStreamer(_world, verificationPhysics,
                verificationGate, _terrainSource, _terrainSettings, restarted);
            verificationGate.CollisionRequired += verificationStreamer.Request;
            verificationStreamer.Start(Vector3.Zero);

            var exteriorCell = _world.FindCell(_currentCellId)!;
            if (!verificationStreamer.TryGetActiveCell(exteriorCell.Id, out _, out var restoredExterior)
                || restoredExterior is null)
                throw new InvalidOperationException("Persistence smoke did not reload the exterior cell.");
            var restoredMarker = restoredExterior.Scene.Find(marker.Id)
                ?? throw new InvalidOperationException("Saved authored marker disappeared after restart.");
            if (!restoredMarker.Enabled || restoredMarker.Transform.Position != marker.Transform.Position)
                throw new InvalidOperationException("Authored transform/enabled changes did not survive restart.");
            var restoredRuntime = restoredExterior.Scene.Objects
                .Where(item => item.Name == "Persistence Smoke Gem").ToArray();
            if (restoredRuntime.Length != 1
                || !restarted.Identities.TryGet(exteriorCell.Id, runtime.SceneObjectId, out var restoredRuntimeId)
                || restoredRuntimeId != runtime.InstanceId
                || restoredRuntimeId == restarted.Identities.GetOrCreate(exteriorCell.Id, restoredMarker))
                throw new InvalidOperationException("Runtime object identity did not survive restart exactly once.");

            var restoredInterior = verificationStreamer.LoadCell(destinationSpawn.CellId);
            try
            {
                var restoredInteriorScene = restoredInterior.ActiveResources?.Scene;
                if (restarted.RestoredPlayerLocation != snapshot.PlayerLocation
                    || restoredInteriorScene is null
                    || !restoredInteriorScene.Objects.Any(item => item.SpawnPoint?.Id == destinationSpawn.SpawnId))
                    throw new InvalidOperationException("Saved interior location or scene did not restore after restart.");
                using var restoredPlayer = new PhysicsCharacterController(verificationPhysics, snapshot.PlayerLocation.Position);
                if (restoredPlayer.Pose.Position != snapshot.PlayerLocation.Position)
                    throw new InvalidOperationException("Saved player position did not initialize in the restored interior.");
            }
            finally
            {
                restoredInterior.Unload();
            }

            Console.WriteLine("RpgSlice: world persistence smoke passed (authored changes, runtime identity, interior player location, restart reload).");
        }
        finally
        {
            if (_deleteSmokeSaveOnExit && File.Exists(_worldSavePath))
                File.Delete(_worldSavePath);
        }
    }

    private WorldPlayerLocation CapturePlayerLocation() =>
        new(_currentCellId, _player.Pose.Position, _playerFacing);

    private static float CameraYaw(Quaternion facing)
    {
        var forward = Vector3.Transform(Vector3.Forward, facing);
        return MathF.Atan2(-forward.X, -forward.Z);
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

        LitEffect.FogEnabled = !_insideInterior;
        LitEffect.FogColor = environment.FogColor.ToVector3();
        LitEffect.FogStart = environment.FogStart;
        LitEffect.FogEnd = environment.FogEnd;
        LitEffect.AmbientLightColor = environment.AmbientLightColor;
        LitEffect.DirectionalLight0.Direction = environment.LightDirection;
        LitEffect.DirectionalLight0.DiffuseColor = environment.DirectionalLightColor;
        LitEffect.DirectionalLight0.SpecularColor = environment.DirectionalLightColor * 0.2f;

        _renderer.Begin(LitEffect, _camera.View, _camera.Projection, _camera.Position,
            _camera.Yaw, StoneTextures.StonePalette.Sandstone, _lights);
        if (!_insideInterior)
            _terrain.Draw(_camera.View, _camera.Projection, _camera.Position, environment);
        _foliageInstances.Clear();
        var visibleCells = _insideInterior
            ? new[] { _interiorOperation?.ActiveResources }
                .Where(item => item is not null).Cast<RpgSliceCellStreamer.ActiveCell>()
            : _cellStreamer.ActiveCells;
        foreach (var activeCell in visibleCells)
        {
            foreach (var sceneObject in activeCell.Scene.Objects)
            {
                if (!sceneObject.Enabled || (!_insideInterior && sceneObject.Name == "Ground")) continue;
                var colour = sceneObject.Name switch
                {
                    "Foliage" => new Color(60, 111, 71),
                    "Trunk" => new Color(117, 82, 54),
                    _ when sceneObject.Door is not null => new Color(139, 84, 49),
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
        if (!_insideInterior)
            _water.Draw(_camera.View, _camera.Projection, _camera.Position, environment, waterLevel: 0.4f);
        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera?.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        if (_travel is { State: WorldCellTravelState.PreparingDestination or WorldCellTravelState.DestinationReady } travel)
        {
            travel.Cancel();
            travel.PreparationTask.GetAwaiter().GetResult();
            _travelDestination?.PumpCompletions();
        }
        if (_travelDestination is { State: CellLifecycleState.Failed } failedDestination)
            failedDestination.Discard();
        if (_interiorOperation?.State == CellLifecycleState.Active)
            _interiorOperation.Unload();
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

    private enum TravelSmokePhase
    {
        ApproachExteriorDoor,
        Entering,
        ApproachInteriorDoor,
        Returning,
        Complete
    }
}

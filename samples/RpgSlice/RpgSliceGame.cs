using Ember;
using Ember.Authoring;
using Ember.Input;
using Ember.Physics;
using Ember.Render;
using Ember.Rpg;
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
    private readonly bool _transitionSoakRequested;
    private readonly int _transitionSoakTarget;
    private RpgSliceTransitionSoak? _transitionSoak;
    private bool _transitionSoakReportWritten;
    private readonly bool _lifecycleCheckRequested;
    private readonly int _lifecycleCheckTarget;
    private RpgSliceLifecycleCheck? _lifecycleCheck;
    private bool _lifecycleFailureArmed;
    private bool _lifecycleFailureRecorded;
    private bool _lifecycleReportWritten;
    private Guid[] _lifecycleInteriorCellIds = Array.Empty<Guid>();
    private int _lifecycleNextInteriorIndex;
    private Stopwatch? _doorTravelTimer;
    private float _transitionSoakSeconds;
    private int _soakDoorCooldownFrames;
    private readonly bool _rpgIntegrationSmokeRequested;
    private readonly bool _questSmokeRequested;
    private readonly bool _settlementSmokeRequested;
    private readonly bool _settlementBenchmarkRequested;
    private readonly string _rpgContentPath;
    private RpgContentSet _rpgContent = null!;
    private float _travelSmokeApproachSeconds;
    private readonly bool _benchmarkRequested;
    private readonly bool _timePaused;
    private readonly string _worldSavePath;
    private readonly string _rpgSavePath;
    private readonly bool _deleteSmokeSaveOnExit;
    private readonly InputActionMap _actions = new();
    private readonly PhysicsFixedStepper _physicsStepper = new();
    private readonly List<PointLight> _lights = new();
    private readonly List<string> _faults = new();
    private readonly List<StaticMeshInstance> _foliageInstances = new(32);
    private SceneRenderer _renderer = null!;
    private InstancedStaticMeshRenderer? _foliageInstancer;
    private WorldManifest _world = null!;
    private RpgSliceStaticAssets _staticAssets = null!;
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
    private RpgSliceGameplayIntegration? _rpgGameplay;
    private WorldClock _worldClock = new();
    private float _initialTimeOfDayHours = 12f;
    private bool _timeHoursSpecified;
    private int _rpgIntegrationSmokeFrames;
    private float _rpgIntegrationSmokeSeconds;
    private int _questSmokeFrames;
    private float _questSmokeSeconds;
    private RpgSliceQuestSmoke? _questSmoke;

    public RpgSliceGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: GameWindowTitle)
    {
        _rpgContentPath = Path.Combine(AppContext.BaseDirectory, "Content", "RpgContent.json");
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _rpgIntegrationSmokeRequested = HasArgument(args, "--rpg-integration-smoke");
        _settlementSmokeRequested = HasArgument(args, "--settlement-smoke");
        _settlementBenchmarkRequested = HasArgument(args, "--settlement-benchmark");
        _questSmokeRequested = HasArgument(args, "--quest-smoke");
        _worldManifestPath = ParseOption(args, "--world")
            ?? Path.Combine(AppContext.BaseDirectory, "Content", "World",
                _settlementSmokeRequested || _settlementBenchmarkRequested || _questSmokeRequested
                || _rpgIntegrationSmokeRequested
                    ? "settlement.json" : WorldManifest.DefaultFileName);
        _smokeControls = HasArgument(args, "--smoke-controls");
        _streamingSmokeRequested = HasArgument(args, "--streaming-smoke");
        _travelSmokeRequested = HasArgument(args, "--travel-smoke");
        _persistenceSmokeRequested = HasArgument(args, "--persistence-smoke");
        _transitionSoakRequested = HasArgument(args, "--transition-soak");
        _transitionSoakTarget = int.TryParse(ParseOption(args, "--transitions"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetTransitions)
            ? targetTransitions : RpgSliceTransitionSoak.DefaultTargetTransitions;
        _lifecycleCheckRequested = HasArgument(args, "--lifecycle-check");
        _lifecycleCheckTarget = int.TryParse(ParseOption(args, "--lifecycle-transitions"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lifecycleTransitions)
            ? lifecycleTransitions : RpgSliceLifecycleCheck.MinimumTransitions;
        _benchmarkRequested = HasArgument(args, "--benchmark");
        _timePaused = HasArgument(args, "--time-paused");
        if ((_smokeControls ? 1 : 0) + (_streamingSmokeRequested ? 1 : 0)
            + (_travelSmokeRequested ? 1 : 0) + (_persistenceSmokeRequested ? 1 : 0)
            + (_rpgIntegrationSmokeRequested ? 1 : 0) + (_questSmokeRequested ? 1 : 0)
            + (_settlementSmokeRequested ? 1 : 0) + (_benchmarkRequested ? 1 : 0)
            + (_settlementBenchmarkRequested ? 1 : 0) + (_transitionSoakRequested ? 1 : 0)
            + (_lifecycleCheckRequested ? 1 : 0) > 1)
            throw new ArgumentException("Choose one RpgSlice smoke or benchmark mode.", nameof(args));
        var configuredSavePath = ParseOption(args, "--save");
        var smokeRequested = _persistenceSmokeRequested || _rpgIntegrationSmokeRequested || _questSmokeRequested
            || _settlementSmokeRequested;
        _deleteSmokeSaveOnExit = smokeRequested
            && configuredSavePath is null;
        _worldSavePath = configuredSavePath
            ?? (smokeRequested
                ? Path.Combine(Path.GetTempPath(), $"ember-rpgslice-smoke-{Guid.NewGuid():N}.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ember", "RpgSlice", "world-save.json"));
        _rpgSavePath = _worldSavePath + ".rpg.json";
        if (_benchmarkRequested || _settlementBenchmarkRequested || _transitionSoakRequested || _lifecycleCheckRequested)
            _graphics.SynchronizeWithVerticalRetrace = false;
        if (ParseOption(args, "--time-hours") is { } timeText)
        {
            if (!float.TryParse(timeText, NumberStyles.Float, CultureInfo.InvariantCulture, out _initialTimeOfDayHours)
                || !float.IsFinite(_initialTimeOfDayHours))
                throw new ArgumentException("--time-hours must be a finite number.", nameof(args));
            _timeHoursSpecified = true;
        }
        _actions.Bind("Exit", Keys.Escape);
        _actions.Bind("TimeEarlier", Keys.PageDown);
        _actions.Bind("TimeLater", Keys.PageUp);
        _actions.Bind("Interact", Keys.E);
        _actions.Bind("TradeBuy", Keys.T);
        _actions.Bind("TradeSell", Keys.Y);
        _actions.Bind("Attack", Keys.F);
        _actions.Bind("Save", Keys.F5);
        _actions.Bind("DialogueChoice1", Keys.D1, Keys.NumPad1);
        _actions.Bind("DialogueChoice2", Keys.D2, Keys.NumPad2);
    }

    protected override void LoadContent()
    {
        AttachCanvas();
        _world = WorldManifest.Load(_worldManifestPath);
        _rpgContent = RpgContentJson.Load(_rpgContentPath);
        if (_settlementSmokeRequested) ValidateSettlementProject();
        _staticAssets = RpgSliceStaticAssets.Load(GraphicsDevice, _world);
        var originCell = _world.GetExteriorCoordinate(Vector3.Zero);
        if (!_world.TryGetExterior(originCell, out var originDefinition) || originDefinition is null)
            throw new InvalidDataException($"World manifest needs an exterior cell at coordinate ({originCell.X}, {originCell.Z}).");
        var loadedSave = File.Exists(_worldSavePath)
            ? WorldSaveFile.Load(_worldSavePath, _world)
            : null;
        var initialWorldTime = Math.Max(0d, _initialTimeOfDayHours) * 3600d;
        var rpgSave = File.Exists(_rpgSavePath)
            ? SaveState.Read(_rpgSavePath)
            : RpgSliceGameplayIntegration.CreateInitialSave(initialWorldTime, _rpgContent);
        if (_rpgIntegrationSmokeRequested)
        {
            if (loadedSave is not null)
                throw new InvalidOperationException("RPG integration smoke requires a fresh world save path.");
            rpgSave = RpgSliceGameplayIntegration.CreateInitialSave((6 * 60 * 60) - 2, _rpgContent);
        }
        else if (_timeHoursSpecified)
        {
            rpgSave = rpgSave with { WorldTimeSeconds = initialWorldTime };
        }
        _worldClock = new WorldClock(rpgSave.WorldTimeSeconds);
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
            _world, _physics, _collisionGate, _terrainSource, _terrainSettings, _worldPersistence,
            _staticAssets);
        _rpgGameplay = new RpgSliceGameplayIntegration(_world, _cellStreamer,
            _worldPersistence, _physics, rpgSave, _worldClock, _rpgSavePath,
            _rpgContent, _rpgIntegrationSmokeRequested);
        if (_questSmokeRequested) _questSmoke = new RpgSliceQuestSmoke();
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
        var characterSettings = _transitionSoakRequested || _lifecycleCheckRequested
            ? new PhysicsCharacterSettings { MoveSpeed = 15f }
            : null;
        _player = new PhysicsCharacterController(_physics, initialLocation.Position, characterSettings);
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
        else if (_benchmarkRequested || _settlementBenchmarkRequested)
        {
            _benchmark = new RpgSliceOutdoorBenchmark(_player.Pose.Position, _settlementBenchmarkRequested);
            Console.WriteLine($"RpgSlice: {(_settlementBenchmarkRequested ? "settlement" : "outdoor")} benchmark started on {GraphicsDevice.Adapter.Description}, "
                + $"{GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height}, "
                + $"{GraphicsDevice.GraphicsProfile}; complete one warmup and ten measured laps.");
        }
        else if (_travelSmokeRequested)
        {
            _travelSmokePhase = TravelSmokePhase.ApproachExteriorDoor;
            Console.WriteLine("RpgSlice: door travel smoke started; walking to House A and using E at both doors.");
        }
        else if (_transitionSoakRequested)
        {
            _transitionSoak = new RpgSliceTransitionSoak(_transitionSoakTarget);
            Console.WriteLine($"RpgSlice: transition soak started; targeting {_transitionSoakTarget} interior/exterior transitions.");
        }
        else if (_lifecycleCheckRequested)
        {
            _lifecycleInteriorCellIds = _world.Cells
                .Where(cell => cell.Kind == WorldCellKind.Interior)
                .OrderBy(cell => cell.ScenePath, StringComparer.OrdinalIgnoreCase)
                .Select(cell => cell.Id)
                .Take(2)
                .ToArray();
            if (_lifecycleInteriorCellIds.Length < 2)
                throw new InvalidDataException("Live lifecycle check requires at least two interior cells in the world manifest.");
            _lifecycleCheck = new RpgSliceLifecycleCheck(_lifecycleCheckTarget);
            Console.WriteLine($"RpgSlice: live lifecycle check started; targeting {_lifecycleCheckTarget} transitions across {_lifecycleInteriorCellIds.Length} interiors and exterior boundaries.");
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
                {
                    using (_benchmark.MeasurePhase(BenchmarkPhase.CellActivation))
                    {
                        _cellStreamer.Update(_player.Pose.Position);
                    }
                }
                if (_travel is not null)
                {
                    if (_transitionSoakRequested || _lifecycleCheckRequested)
                    {
                        try
                        {
                            AdvanceDoorTravel();
                        }
                        catch (Exception error)
                        {
                            if (_lifecycleCheckRequested)
                            {
                                _lifecycleCheck?.RecordUnexpectedError("transition execution", error);
                                FinishLifecycleCheck();
                            }
                            else
                            {
                                _transitionSoak?.RecordError("transition execution", error);
                                FinishTransitionSoak();
                            }
                            base.Update(gameTime);
                            return;
                        }
                    }
                    else
                    {
                        AdvanceDoorTravel();
                    }
                }
                if (_smokeRan)
                {
                    base.Update(gameTime);
                    return;
                }
                if (_soakDoorCooldownFrames > 0)
                    _soakDoorCooldownFrames--;
                var keyboard = (_travelSmokeRequested || _transitionSoakRequested || _lifecycleCheckRequested)
                    ? FindNearbyDoor() is not null && _soakDoorCooldownFrames == 0 ? new KeyboardState(Keys.E) : new KeyboardState()
                    : _rpgIntegrationSmokeRequested ? new KeyboardState()
                    : _questSmokeRequested
                        ? _questSmoke!.CreateKeyboard(_player.Pose.Position, _camera, _rpgGameplay!)
                        : Keyboard.GetState();
                var dt = RealSeconds(gameTime);
                UpdateMovement(keyboard, dt, IsActive || _questSmokeRequested || _transitionSoakRequested || _lifecycleCheckRequested);
                if (_questSmokeRequested)
                {
                    _questSmokeSeconds += dt;
                    _questSmokeFrames++;
                }
                if (_rpgIntegrationSmokeRequested)
                {
                    _rpgIntegrationSmokeSeconds += dt;
                    _rpgIntegrationSmokeFrames++;
                }
                if (_transitionSoakRequested || _lifecycleCheckRequested)
                {
                    _transitionSoakSeconds += dt;
                }
                if (_rpgIntegrationSmokeRequested && _rpgGameplay?.SmokeCompleted == true)
                {
                    _smokeRan = true;
                    Exit();
                }
                else if (_questSmokeRequested && _rpgGameplay is not null
                    && _questSmoke!.SaveKeyIssued && _worldPersistence.PendingSaveCount == 0
                    && File.Exists(_worldSavePath) && File.Exists(_rpgSavePath))
                {
                    VerifyQuestSmokeAfterRestart();
                    _smokeRan = true;
                    Exit();
                }
                else if (_settlementSmokeRequested && _rpgGameplay?.IsInitialized == true)
                {
                    RunSettlementRuntimeSmoke();
                    _smokeRan = true;
                    Exit();
                }
                else if (_rpgIntegrationSmokeRequested && _rpgIntegrationSmokeSeconds > 90f)
                {
                    throw new TimeoutException("RPG integration smoke did not complete both scheduled worker trips within 90 seconds.");
                }
                else if (_questSmokeRequested && _questSmokeSeconds > 90f)
                {
                    throw new TimeoutException($"Quest smoke did not complete the Lost Delivery using normal player inputs within 90 seconds (elapsed={_questSmokeSeconds:F1}s, frames={_questSmokeFrames}).");
                }
                else if (_transitionSoakRequested && _transitionSoakSeconds > 300f)
                {
                    var timeout = $"Transition soak did not complete {_transitionSoakTarget} transitions within 300 seconds (completed={_transitionSoak?.CompletedTransitions ?? 0}, elapsed={_transitionSoakSeconds:F1}s).";
                    _transitionSoak?.RecordTimeout(timeout);
                    FinishTransitionSoak();
                    base.Update(gameTime);
                    return;
                }
                else if (_lifecycleCheckRequested && _transitionSoakSeconds > 300f)
                {
                    _lifecycleCheck?.RecordTimeout($"Live lifecycle check did not complete {_lifecycleCheckTarget} transitions within 300 seconds (completed={_lifecycleCheck?.CompletedTransitions ?? 0}, elapsed={_transitionSoakSeconds:F1}s).");
                    FinishLifecycleCheck();
                    base.Update(gameTime);
                    return;
                }
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
        if (_actions.ConsumePressed("TimeEarlier"))
            _worldClock = new WorldClock(Math.Max(0d, _worldClock.TotalSeconds - 3600d));
        if (_actions.ConsumePressed("TimeLater")) _worldClock = _worldClock.Advance(3600d);
        if (!_timePaused) _worldClock = _worldClock.Advance(elapsedSeconds * 60d);
        if (_actions.ConsumePressed("Save"))
        {
            _rpgGameplay?.WriteSave(_worldClock);
            _worldPersistence.RequestSave(_worldSavePath, CapturePlayerLocation);
            _saveFeedback = "Save queued";
            _saveFeedbackSeconds = 2f;
        }
        if (_travel is null && _rpgGameplay is not null)
        {
            if (_rpgGameplay.HasOpenDialogue)
            {
                if (_actions.ConsumePressed("DialogueChoice1")
                    && _rpgGameplay.TrySelectDialogueOption(0, out var firstChoice))
                    SetGameplayFeedback(firstChoice);
                else if (_actions.ConsumePressed("DialogueChoice2")
                    && _rpgGameplay.TrySelectDialogueOption(1, out var secondChoice))
                    SetGameplayFeedback(secondChoice);
                else if (_actions.ConsumePressed("Interact")
                    && _rpgGameplay.TrySelectDialogueOption(0, out var defaultChoice))
                    SetGameplayFeedback(defaultChoice);
            }
            else if (_actions.ConsumePressed("Interact"))
            {
                if (FindNearbyDoor() is { } nearbyDoor)
                    BeginDoorTravel(nearbyDoor);
                else if (_rpgGameplay.TryInteractAt(_currentCellId, _player.Pose.Position, out var interactionMessage))
                    SetGameplayFeedback(interactionMessage);
            }

            if (!_rpgGameplay.HasOpenDialogue && _actions.ConsumePressed("TradeBuy")
                && _rpgGameplay.TryTradeAt(_currentCellId, _player.Pose.Position,
                    sell: false, out var buyMessage))
                SetGameplayFeedback(buyMessage);
            if (!_rpgGameplay.HasOpenDialogue && _actions.ConsumePressed("TradeSell")
                && _rpgGameplay.TryTradeAt(_currentCellId, _player.Pose.Position,
                    sell: true, out var sellMessage))
                SetGameplayFeedback(sellMessage);
            if (!_rpgGameplay.HasOpenDialogue && _actions.ConsumePressed("Attack")
                && _rpgGameplay.TryPlayerAttack(_currentCellId, _player.Pose.Position,
                    out var attackMessage))
                SetGameplayFeedback(attackMessage);
        }
        if (_actions.ConsumePressed(GameplayActionNames.Jump) && _travel is null)
            _player.RequestJump();

        var moveDirection = _camera.MoveDirection(input.ReadMovement());
        if ((_travelSmokeRequested || _transitionSoakRequested || _lifecycleCheckRequested) && _travel is null
            && (_travelSmokePhase is TravelSmokePhase.ApproachExteriorDoor or TravelSmokePhase.ApproachInteriorDoor
                || _transitionSoakRequested || _lifecycleCheckRequested))
        {
            var targetDoor = FindSmokeTargetDoor()
                ?? throw new InvalidOperationException("Target door not found for smoke, soak, or lifecycle check.");
            var targetTransform = GetCurrentActiveCell().Scene.GetWorldMatrix(targetDoor.Id)
                * GetCurrentActiveCell().WorldTransform;
            if (_lifecycleCheckRequested && !_insideInterior && _lifecycleNextInteriorIndex == 1)
            {
                // Go around the market stall and central boulder before approaching House B.
                // A straight line from the return spawn hits the stall at (20, 24), while
                // moving east first also runs directly into it.
                var playerPosition = _player.Pose.Position;
                var waypoint = playerPosition.Z > 11f
                    ? new Vector3(16f, playerPosition.Y, 10f)
                    : playerPosition.X < 25f
                        ? new Vector3(26f, playerPosition.Y, 10f)
                        : new Vector3(targetTransform.M41, playerPosition.Y, targetTransform.M43);
                moveDirection = waypoint - playerPosition;
            }
            else
            {
                moveDirection = new Vector3(targetTransform.M41 - _player.Pose.Position.X, 0f,
                    targetTransform.M43 - _player.Pose.Position.Z);
            }
            if (moveDirection.LengthSquared() > 1e-6f) moveDirection.Normalize();
        }
        _player.SetMoveInput(_travel is null && _rpgGameplay?.HasOpenDialogue != true
            ? moveDirection : Vector3.Zero);
        if (_travelSmokeRequested && _travel is null
            && _travelSmokePhase is TravelSmokePhase.ApproachExteriorDoor or TravelSmokePhase.ApproachInteriorDoor
            && (_travelSmokeApproachSeconds += elapsedSeconds) > 20f)
            throw new TimeoutException("Travel smoke could not reach the authored door within 20 seconds.");
        if (_benchmark is { IsComplete: false } routeBenchmark)
            _player.SetMoveInput(routeBenchmark.GetMoveDirection(_player.Pose.Position));
        if (_rpgGameplay is not null)
            _worldClock = _rpgGameplay.Update(elapsedSeconds, _worldClock,
                _currentCellId, _player.Pose.Position);
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
                + (_foliageInstancer?.OwnedGraphicsResourceCount ?? 0)
                + _staticAssets.OwnedGraphicsResourceCount;
            benchmark.ObserveRuntime(_world.GetExteriorCoordinate(position), _cellStreamer.ActiveCellCount,
                _terrain.CachedChunkCount, trackedResources, workingSetBytes, benchmarkElapsedSeconds);
        }
        if (_benchmark is { IsComplete: true } && !_benchmarkReportWritten)
        {
            _benchmarkReportWritten = true;
            var report = _benchmark.BuildReport(_cellStreamer.LongestActivationMilliseconds,
                _cellStreamer.ActivationAttemptCount, _foliageInstancer is not null,
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
                GraphicsDevice.Adapter.Description,
                _settlementBenchmarkRequested ? BuildSettlementContentSummary() : null);
            var reportFileName = _settlementBenchmarkRequested
                ? "ember-rpgslice-settlement-benchmark.txt"
                : "ember-rpgslice-outdoor-benchmark.txt";
            var reportPath = Path.Combine(Path.GetTempPath(), reportFileName);
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
        else if (_transitionSoak is { IsComplete: false } runningSoak)
        {
            Window.Title = $"RPG Slice — Transition soak {runningSoak.CompletedTransitions}/{runningSoak.TargetTransitions}, elapsed {runningSoak.TotalElapsedSeconds:F1}s";
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
        else if (_rpgGameplay?.GetPrompt(_currentCellId, _player.Pose.Position) is { } gameplayPrompt)
        {
            Window.Title = $"RPG Slice — {gameplayPrompt}";
        }
        else if (_rpgGameplay?.LastStatus is { } gameplayStatus)
        {
            Window.Title = $"RPG Slice — {gameplayStatus}";
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
        if (_lifecycleCheckRequested && !_lifecycleFailureArmed)
        {
            _cellStreamer.DelayAndFailNextPreparation(TimeSpan.FromMilliseconds(150));
            _lifecycleFailureArmed = true;
        }
        _travelDestination = new WorldCellLoadOperation<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>();
        _travel = new WorldCellTravelTransaction<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>(
            _currentCellId,
            source,
            _travelDestination,
            destinationSpawn,
            _cellStreamer.PrepareCellAsync,
            _cellStreamer.ActivatePreparedCell,
            PlacePlayerAtSpawn);
        _doorTravelTimer = Stopwatch.StartNew();
        if (_travelSmokeRequested)
        {
            _travelSmokePhase = _insideInterior
                ? TravelSmokePhase.Returning
                : TravelSmokePhase.Entering;
            _travelSmokeApproachSeconds = 0f;
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
        if (_lifecycleCheckRequested)
        {
            if (!_lifecycleFailureRecorded && _cellStreamer.InjectedPreparationFailureCount == 1
                && failure is IOException)
            {
                _lifecycleCheck?.RecordExpectedPreparationFailure(failure);
                _lifecycleFailureRecorded = true;
                _soakDoorCooldownFrames = 4;
                Console.WriteLine("RpgSlice: source remains active; retrying the injected failed destination load.");
                return;
            }

            _lifecycleCheck?.RecordUnexpectedError("door travel", failure);
            FinishLifecycleCheck();
            return;
        }
        if (_transitionSoakRequested)
        {
            _transitionSoak?.RecordError("door travel", failure);
            FinishTransitionSoak();
            return;
        }
        if (_travelSmokeRequested)
            throw new InvalidOperationException("Door travel failed during smoke or soak.", failure);
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
        {
            _transitionSoak?.RecordError("committed-travel cleanup", cleanupFailure);
            _lifecycleCheck?.RecordUnexpectedError("committed-travel cleanup", cleanupFailure);
            Console.WriteLine($"RpgSlice: travel committed, but source-cell cleanup reported: {cleanupFailure}");
        }

        _currentCellId = destinationCellId;
        _travel = null;
        _travelDestination = null;

        if (_lifecycleCheckRequested && _lifecycleCheck is not null)
        {
            var activeDestination = destinationOperation.ActiveResources
                ?? throw new InvalidOperationException("Lifecycle transition completed without active destination resources.");
            _lifecycleCheck.RecordCellIdentities(destinationCellId, activeDestination.Scene, _worldPersistence.Identities);
            var fromName = DescribeLifecycleCell(sourceCellId);
            var toName = DescribeLifecycleCell(destinationCellId);
            var activeCells = destination.Kind == WorldCellKind.Interior ? 1 : _cellStreamer.ActiveCellCount;
            var terrainChunks = destination.Kind == WorldCellKind.Interior ? 0 : _terrain.CachedChunkCount;
            var trackedResources = 2 + terrainChunks + 4
                + (_foliageInstancer?.OwnedGraphicsResourceCount ?? 0)
                + _staticAssets.OwnedGraphicsResourceCount;
            long workingSet;
            using (var process = Process.GetCurrentProcess())
                workingSet = process.WorkingSet64;
            _lifecycleCheck.RecordTransition(fromName, toName, activeCells,
                terrainChunks, trackedResources, workingSet);

            if (destination.Kind == WorldCellKind.Exterior
                && _world.FindCell(sourceCellId)?.Kind == WorldCellKind.Interior)
                _lifecycleNextInteriorIndex = (_lifecycleNextInteriorIndex + 1) % _lifecycleInteriorCellIds.Length;

            Console.WriteLine($"RpgSlice: lifecycle transition {_lifecycleCheck.CompletedTransitions}/{_lifecycleCheck.TargetTransitions}: {fromName} -> {toName}; identities={_worldPersistence.Identities.Count}; resources={trackedResources}; WS={workingSet / (1024d * 1024d):F1} MiB.");
            if (_lifecycleCheck.IsComplete)
            {
                FinishLifecycleCheck();
                return;
            }
        }

        if (_transitionSoakRequested && _transitionSoak is not null)
        {
            _doorTravelTimer?.Stop();
            var duration = _doorTravelTimer?.Elapsed.TotalSeconds ?? 0d;
            var fromCellName = destination.Kind == WorldCellKind.Interior ? "Exterior (0, 0)" : "House A Interior";
            var toCellName = destination.Kind == WorldCellKind.Interior ? "House A Interior" : "Exterior (0, 0)";
            var activeCells = _insideInterior ? 1 : _cellStreamer.ActiveCellCount;
            var terrainChunks = _insideInterior ? 0 : _terrain.CachedChunkCount;
            var trackedResources = 2 + terrainChunks + 4
                + (_foliageInstancer?.OwnedGraphicsResourceCount ?? 0)
                + _staticAssets.OwnedGraphicsResourceCount;
            long workingSet;
            using (var process = Process.GetCurrentProcess())
                workingSet = process.WorkingSet64;

            _transitionSoak.RecordTransition(fromCellName, toCellName, duration,
                activeCells, terrainChunks, trackedResources, workingSet);

            _soakDoorCooldownFrames = 4;

            Console.WriteLine($"RpgSlice: transition {_transitionSoak.CompletedTransitions}/{_transitionSoak.TargetTransitions} ({fromCellName} -> {toCellName}) completed in {duration * 1000d:F1}ms (WS: {workingSet / (1024d * 1024d):F1} MiB, Active: {activeCells}, Chunks: {terrainChunks})");

            if (_transitionSoak.IsComplete)
            {
                FinishTransitionSoak();
                return;
            }
        }

        if (!_travelSmokeRequested) return;
        if (_travelSmokePhase == TravelSmokePhase.Entering)
        {
            if (!_insideInterior || Vector3.Distance(_player.Pose.Position, travel.DestinationSpawn.Position) > 0.01f)
                throw new InvalidOperationException("Door travel smoke did not place the player in House A.");
            _travelSmokePhase = TravelSmokePhase.ApproachInteriorDoor;
            _travelSmokeApproachSeconds = 0f;
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

    private void FinishTransitionSoak()
    {
        if (_transitionSoak is null || _transitionSoakReportWritten) return;
        _transitionSoakReportWritten = true;

        string? markdownPath = null;
        string? jsonPath = null;
        try
        {
            var reportDirectory = Path.Combine(Path.GetTempPath(), "Ember", "RpgSlice", "TransitionSoaks");
            Directory.CreateDirectory(reportDirectory);
            var fileStem = $"transition-soak-{_transitionSoak.RunId:N}";
            markdownPath = Path.Combine(reportDirectory, fileStem + ".md");
            jsonPath = Path.Combine(reportDirectory, fileStem + ".json");

            var adapter = GraphicsDevice.Adapter.Description;
            var width = GraphicsDevice.Viewport.Width;
            var height = GraphicsDevice.Viewport.Height;
            File.WriteAllText(jsonPath, _transitionSoak.BuildRawData(adapter, width, height), Encoding.UTF8);
            File.WriteAllText(markdownPath, _transitionSoak.BuildReport(adapter, width, height), Encoding.UTF8);
        }
        catch (Exception error)
        {
            _transitionSoak.RecordError("report export", error);
            Console.Error.WriteLine($"RpgSlice: could not export the transition soak report: {error}");
        }

        var passed = _transitionSoak.Passed;
        Environment.ExitCode = _transitionSoak.ExitCode;
        _smokeRan = true;
        Console.WriteLine(_transitionSoak.BuildReport(GraphicsDevice.Adapter.Description,
            GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height));
        if (markdownPath is not null && jsonPath is not null)
        {
            Console.WriteLine($"RpgSlice: transition soak report saved to {markdownPath}");
            Console.WriteLine($"RpgSlice: full transition soak samples and run metadata saved to {jsonPath}");
        }
        Console.WriteLine($"RpgSlice: transition soak verdict {(passed ? "PASS" : "FAIL")}; process exit code {Environment.ExitCode}.");
        Exit();
    }

    private string DescribeLifecycleCell(Guid cellId)
    {
        var cell = _world.FindCell(cellId)
            ?? throw new InvalidOperationException($"Lifecycle route refers to missing cell {cellId}.");
        return cell.Kind == WorldCellKind.Exterior
            ? $"Exterior ({cell.ExteriorCoordinate?.X ?? 0}, {cell.ExteriorCoordinate?.Z ?? 0})"
            : Path.GetFileNameWithoutExtension(cell.ScenePath);
    }

    private void FinishLifecycleCheck()
    {
        if (_lifecycleCheck is null || _lifecycleReportWritten) return;
        _lifecycleReportWritten = true;

        var artifactDirectory = Path.Combine(Path.GetTempPath(), "Ember", "RpgSlice", "LifecycleChecks");
        Directory.CreateDirectory(artifactDirectory);
        var fileStem = $"lifecycle-check-{_lifecycleCheck.RunId:N}";
        var markdownPath = Path.Combine(artifactDirectory, fileStem + ".md");
        var jsonPath = Path.Combine(artifactDirectory, fileStem + ".json");
        var savePath = Path.Combine(artifactDirectory, fileStem + ".world.json");
        try
        {
            _worldPersistence.RequestSave(savePath, CapturePlayerLocation);
            if (!_worldPersistence.ProcessStableBoundary(travelInProgress: false))
                throw new IOException("Lifecycle world save did not process at the stable boundary.");
            if (!_worldPersistence.TryDequeueSaveResult(out var saveResult))
                throw new IOException("Lifecycle world save did not produce a completion result.");
            if (saveResult.Failure is not null) throw saveResult.Failure;

            var restoredWorld = WorldSaveFile.Load(savePath, _world);
            var restartedSession = new WorldPersistenceSession(_world, restoredWorld);
            var currentLocation = CapturePlayerLocation();
            if (restartedSession.RestoredPlayerLocation != currentLocation)
                throw new InvalidDataException("Save/restart did not restore the live player cell and transform.");

            var originalIdentities = _worldPersistence.Identities.ExportSnapshot();
            var restoredIdentities = restartedSession.Identities.ExportSnapshot();
            if (!originalIdentities.SequenceEqual(restoredIdentities)
                || restoredIdentities.Select(entry => entry.InstanceId.Value).Distinct().Count() != restoredIdentities.Count)
                throw new InvalidDataException("Save/restart changed or duplicated world instance identities.");

            var currentScene = SceneFile.Load(_world.ResolveScenePath(_currentCellId));
            restartedSession.PrepareCell(_currentCellId, currentScene);
            _lifecycleCheck.RecordSaveRestart(true,
                $"player restored in {DescribeLifecycleCell(_currentCellId)}; {restoredIdentities.Count} unique identity mappings restored from {Path.GetFileName(savePath)}");
        }
        catch (Exception error)
        {
            _lifecycleCheck.RecordUnexpectedError("save/restart", error);
            _lifecycleCheck.RecordSaveRestart(false, error.Message);
        }

        try
        {
            var adapter = GraphicsDevice.Adapter.Description;
            var width = GraphicsDevice.Viewport.Width;
            var height = GraphicsDevice.Viewport.Height;
            File.WriteAllText(jsonPath, _lifecycleCheck.BuildRawData(adapter, width, height), Encoding.UTF8);
            File.WriteAllText(markdownPath, _lifecycleCheck.BuildReport(adapter, width, height), Encoding.UTF8);
        }
        catch (Exception error)
        {
            _lifecycleCheck.RecordUnexpectedError("report export", error);
            Console.Error.WriteLine($"RpgSlice: could not export the live lifecycle report: {error}");
        }

        Environment.ExitCode = _lifecycleCheck.ExitCode;
        _smokeRan = true;
        Console.WriteLine(_lifecycleCheck.BuildReport(GraphicsDevice.Adapter.Description,
            GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height));
        Console.WriteLine($"RpgSlice: lifecycle report {markdownPath}");
        Console.WriteLine($"RpgSlice: lifecycle samples {jsonPath}");
        Console.WriteLine($"RpgSlice: lifecycle world save {savePath}");
        Console.WriteLine($"RpgSlice: lifecycle verdict {(_lifecycleCheck.Passed ? "PASS" : "FAIL")}; process exit code {Environment.ExitCode}.");
        Exit();
    }

    private void PlacePlayerAtSpawn(WorldSpawnLocation spawn)
    {
        var characterSettings = _transitionSoakRequested
            ? new PhysicsCharacterSettings { MoveSpeed = 15f }
            : null;
        var nextPlayer = new PhysicsCharacterController(_physics, spawn.Position, characterSettings);
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
        if (_lifecycleCheckRequested && !_insideInterior && _lifecycleInteriorCellIds.Length > 0)
        {
            var targetCellId = _lifecycleInteriorCellIds[_lifecycleNextInteriorIndex % _lifecycleInteriorCellIds.Length];
            return active.Scene.Objects.FirstOrDefault(item => item.Door?.DestinationCellId == targetCellId);
        }
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

            // Action 1: Drop item
            var appleId = new ContentId<ItemContentKind>("item.rpgslice.apple");
            var appleDef = new ItemDef(appleId, "Apple", null, stackable: true);
            var itemCatalogue = new ItemCatalogue();
            itemCatalogue.Add(appleDef);
            var playerBag = new Bag();
            playerBag.Add(appleDef, 3);
            var worldItems = new WorldItemStore();

            var dropObject = new SceneObject(Guid.NewGuid(), "Persistence Smoke Dropped Apple")
            {
                Transform = new Transform { Position = new Vector3(19f, 0.8f, 20f) }
            };
            var droppedItem = _worldPersistence.Spawn(_currentCellId, exterior.Scene, dropObject);
            if (!InventoryTransfer.TryDrop(droppedItem.InstanceId.Value, playerBag, worldItems, itemCatalogue, appleId, 1, out playerBag, out worldItems))
                throw new InvalidOperationException("Persistence smoke could not drop item.");

            // Action 2: Loot container
            var containerObject = new SceneObject(Guid.NewGuid(), "Persistence Smoke Container")
            {
                Transform = new Transform { Position = new Vector3(20f, 0.8f, 20f) }
            };
            var containerItem = _worldPersistence.Spawn(_currentCellId, exterior.Scene, containerObject);
            var initialContainerBag = new Bag();
            initialContainerBag.Add(appleDef, 2);
            var containers = new ContainerInventoryStore().SetContents(containerItem.InstanceId.Value, initialContainerBag);
            if (!InventoryTransfer.TryMove(containers.GetContents(containerItem.InstanceId.Value)!, playerBag, itemCatalogue, appleId, 2, out var emptiedContainerBag, out playerBag))
                throw new InvalidOperationException("Persistence smoke could not loot container.");
            containers = containers.SetContents(containerItem.InstanceId.Value, emptiedContainerBag);

            // Action 3: Kill enemy
            var enemyId = new ContentId<ActorContentKind>("actor.rpgslice.raider");
            var enemyObject = new SceneObject(Guid.NewGuid(), "Persistence Smoke Enemy")
            {
                Transform = new Transform { Position = new Vector3(21f, 1f, 21f) }
            };
            var enemyItem = _worldPersistence.Spawn(_currentCellId, exterior.Scene, enemyObject);
            var spawnedEnemy = exterior.Scene.Find(enemyItem.SceneObjectId)!;
            var actorStates = new ActorRuntimeStore().Set(new ActorRuntimeState(enemyItem.InstanceId.Value, enemyId, 0));
            _worldPersistence.SetEnabled(_currentCellId, spawnedEnemy, false);

            // Action 4: Move follower across cells
            var neighborCell = _world.Cells.FirstOrDefault(c => c.Kind == WorldCellKind.Exterior && c.Id != _currentCellId)
                ?? throw new InvalidOperationException("Persistence smoke requires an adjacent exterior cell.");
            var neighborScene = SceneFile.Load(_world.ResolveScenePath(neighborCell.Id));
            var followerObject = new SceneObject(Guid.NewGuid(), "Persistence Smoke Follower")
            {
                Transform = new Transform { Position = new Vector3(17f, 1f, 17f) }
            };
            var follower = _worldPersistence.Spawn(_currentCellId, exterior.Scene, followerObject);
            var followerDestination = new Transform { Position = new Vector3(8f, 1f, 12f) };
            _worldPersistence.Transfer(_currentCellId, neighborCell.Id, follower.InstanceId, exterior.Scene, neighborScene, followerDestination);

            // Action 5: Enter interior
            var entryDoor = exterior.Scene.Objects.FirstOrDefault(item => item.Name == "Door to House A")
                ?? throw new InvalidDataException("Persistence smoke needs the authored House A door.");
            var interiorScene = SceneFile.Load(_world.ResolveScenePath(entryDoor.Door!.DestinationCellId));
            var destinationSpawn = WorldTravelValidator.ResolveDestination(_world,
                new Dictionary<Guid, SceneGraph> { [entryDoor.Door.DestinationCellId] = interiorScene }, entryDoor.Door);
            var playerLocation = new WorldPlayerLocation(destinationSpawn.CellId, destinationSpawn.Position, destinationSpawn.Facing);

            // Action 6: Save and Restart
            _worldPersistence.RequestSave(_worldSavePath, () => playerLocation);
            if (!_worldPersistence.ProcessStableBoundary(travelInProgress: false))
                throw new InvalidOperationException("Persistence smoke save was not processed at the stable boundary.");
            if (!_worldPersistence.TryDequeueSaveResult(out var writeResult))
                throw new InvalidOperationException("Persistence smoke did not receive a save result.");
            if (writeResult.Failure is not null)
                throw new InvalidOperationException($"Persistence smoke could not save: {writeResult.Failure}");

            var rpgSave = new SaveState
            {
                Player = new PlayerRecord { Bag = playerBag },
                ContainerInventories = containers,
                WorldItems = worldItems,
                ActorStates = actorStates,
                ItemDefs = itemCatalogue
            };
            File.WriteAllText(_rpgSavePath, rpgSave.ToJson());

            // Restart verification
            var snapshot = WorldSaveFile.Load(_worldSavePath, _world);
            if (snapshot.PlayerLocation.CellId != destinationSpawn.CellId)
                throw new InvalidOperationException("Queued persistence smoke save did not capture the committed interior location.");
            var restartedRpg = SaveState.FromJson(File.ReadAllText(_rpgSavePath));
            var restarted = new WorldPersistenceSession(_world, snapshot);

            using var verificationPhysics = new PhysicsWorld();
            var verificationGate = new ExteriorCellCollisionGate(_world);
            using var verificationStreamer = new RpgSliceCellStreamer(_world, verificationPhysics,
                verificationGate, _terrainSource, _terrainSettings, restarted, _staticAssets);
            verificationGate.CollisionRequired += verificationStreamer.Request;
            verificationStreamer.Start(Vector3.Zero);

            var exteriorCell = _world.FindCell(_currentCellId)!;
            if (!verificationStreamer.TryGetActiveCell(exteriorCell.Id, out _, out var restoredExterior)
                || restoredExterior is null)
                throw new InvalidOperationException("Persistence smoke did not reload the exterior cell.");

            // Verify Action 1: Dropped item
            var restoredDrop = restoredExterior.Scene.Find(droppedItem.SceneObjectId);
            if (restoredDrop is null)
                throw new InvalidOperationException("Dropped item disappeared after restart.");
            if (!restartedRpg.WorldItems.TryGet(droppedItem.InstanceId.Value, out var restoredDropStack) || restoredDropStack.Count != 1)
                throw new InvalidOperationException("Dropped item was not restored in RPG WorldItems.");
            if (restartedRpg.Player.Bag.Count(appleId) != 4) // 3 starting - 1 dropped + 2 looted = 4
                throw new InvalidOperationException("Player bag does not reflect dropped and looted items.");

            // Verify Action 2: Looted container
            var restoredContainer = restoredExterior.Scene.Find(containerItem.SceneObjectId);
            if (restoredContainer is null)
                throw new InvalidOperationException("Container disappeared after restart.");
            var restoredContainerBag = restartedRpg.ContainerInventories.GetContents(containerItem.InstanceId.Value);
            if (restoredContainerBag is null || restoredContainerBag.Count(appleId) != 0)
                throw new InvalidOperationException("Looted container was not emptied after restart.");

            // Verify Action 3: Killed enemy
            var restoredEnemy = restoredExterior.Scene.Find(enemyItem.SceneObjectId);
            if (restoredEnemy is null || restoredEnemy.Enabled)
                throw new InvalidOperationException("Killed enemy was not restored as disabled.");
            if (!restartedRpg.ActorStates.TryGet(enemyItem.InstanceId.Value, out var restoredEnemyState) || !restoredEnemyState.IsDead || restoredEnemyState.CurrentHealth != 0)
                throw new InvalidOperationException("Killed enemy state was not restored as dead.");

            // Verify Action 4: Moved follower across cells
            if (restoredExterior.Scene.Find(follower.SceneObjectId) is not null)
                throw new InvalidOperationException("Moved follower still exists in source cell.");
            var reloadedNeighbor = verificationStreamer.LoadCell(neighborCell.Id);
            try
            {
                var reloadedNeighborScene = reloadedNeighbor.ActiveResources?.Scene
                    ?? throw new InvalidOperationException("Could not load neighbor cell scene.");
                var restoredFollower = reloadedNeighborScene.Find(follower.SceneObjectId);
                if (restoredFollower is null)
                    throw new InvalidOperationException("Moved follower was not found in destination cell.");
                if (Vector3.Distance(restoredFollower.Transform.Position, followerDestination.Position) > 0.01f)
                    throw new InvalidOperationException("Moved follower transform position did not match destination.");
            }
            finally
            {
                reloadedNeighbor.Unload();
            }

            // Verify Action 5: Enter interior
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

            // Verify exactly-once persistence
            var exportedRuntimes = restarted.RuntimeObjects.ExportSnapshot();
            if (exportedRuntimes.Count(r => r.InstanceId == follower.InstanceId) != 1)
                throw new InvalidOperationException("Follower does not exist exactly once in runtime store.");
            if (exportedRuntimes.Count(r => r.InstanceId == droppedItem.InstanceId) != 1)
                throw new InvalidOperationException("Dropped item does not exist exactly once in runtime store.");
            if (exportedRuntimes.Count(r => r.InstanceId == containerItem.InstanceId) != 1)
                throw new InvalidOperationException("Container does not exist exactly once in runtime store.");
            if (exportedRuntimes.Count(r => r.InstanceId == enemyItem.InstanceId) != 1)
                throw new InvalidOperationException("Enemy does not exist exactly once in runtime store.");

            Console.WriteLine("RpgSlice: world persistence smoke passed (repeatable scenario: drop item, loot container, kill enemy, move follower across cells, enter interior, save/restart, verified exactly-once).");
        }
        finally
        {
            if (_deleteSmokeSaveOnExit && File.Exists(_worldSavePath))
                File.Delete(_worldSavePath);
            if (_deleteSmokeSaveOnExit && File.Exists(_rpgSavePath))
                File.Delete(_rpgSavePath);
        }
    }

    private void VerifyQuestSmokeAfterRestart()
    {
        const string questId = "quest.rpgslice.lost_delivery";
        const string appleId = "item.rpgslice.apple";
        var satchelInstanceId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a13");
        var raiderInstanceId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a12");
        var satchelSceneObjectId = Guid.Parse("d71f9da0-e2e9-4aae-bf2f-d70a06b07a07");
        try
        {
            if (!File.Exists(_worldSavePath) || !File.Exists(_rpgSavePath))
                throw new InvalidOperationException("Quest smoke did not write both world and RPG saves through F5.");

            var savedRpg = SaveState.Read(_rpgSavePath);
            var quest = _rpgContent.Quests.Get(questId)
                ?? throw new InvalidDataException($"Quest smoke cannot find '{questId}' in loaded content.");
            var apple = new ContentId<ItemContentKind>(appleId);
            if (quest.StatusIn(savedRpg.Flags) != QuestStatus.Complete
                || quest.Stages.Any(stage => !stage.IsDone(savedRpg.Flags, quest.Id)))
                throw new InvalidOperationException("The Lost Delivery quest did not remain complete after RPG save/load.");
            if (!savedRpg.ActorStates.TryGet(raiderInstanceId, out var raider) || !raider.IsDead)
                throw new InvalidOperationException("The defeated raider did not remain dead after RPG save/load.");
            if (savedRpg.Player.Bag.Count(apple) != 2
                || savedRpg.WorldItems.TryGet(satchelInstanceId, out _))
                throw new InvalidOperationException("The recovered apples were lost or duplicated after RPG save/load.");
            if (savedRpg.Dialogue.Tree is not null)
                throw new InvalidOperationException("The completed quest save unexpectedly reopened its dialogue.");
            var saveDiagnostics = _rpgContent.ValidateSaveReferences(savedRpg);
            if (saveDiagnostics.Count > 0)
                throw new InvalidDataException("Quest save references did not validate: "
                    + string.Join(" ", saveDiagnostics));

            var worldSnapshot = WorldSaveFile.Load(_worldSavePath, _world);
            if (!worldSnapshot.Changes.Any(change => change.InstanceId.Value == satchelInstanceId && change.Deleted))
                throw new InvalidOperationException("The collected satchel has no persistent world deletion tombstone.");
            var restarted = new WorldPersistenceSession(_world, worldSnapshot);
            var home = _world.TryGetExterior(new ExteriorCellCoordinate(0, 0), out var homeCell)
                ? homeCell : null;
            if (home is null) throw new InvalidDataException("Quest smoke cannot resolve the settlement market cell.");
            var restoredScene = SceneFile.Load(_world.ResolveScenePath(home.Id));
            var identities = restarted.PrepareCell(home.Id, restoredScene);
            if (restoredScene.Find(satchelSceneObjectId) is not null
                || !identities.TryGetValue(satchelSceneObjectId, out var satchelIdentity)
                || satchelIdentity.Value != satchelInstanceId)
                throw new InvalidOperationException("The collected satchel reappeared or changed identity after world restart.");

            Console.WriteLine("RpgSlice: PASS Lost Delivery completed through E/1/F/F5; quest flags, dead raider, apple inventory, and satchel tombstone survived restart.");
        }
        finally
        {
            if (_deleteSmokeSaveOnExit && File.Exists(_worldSavePath)) File.Delete(_worldSavePath);
            if (_deleteSmokeSaveOnExit && File.Exists(_rpgSavePath)) File.Delete(_rpgSavePath);
        }
    }

    private void ValidateSettlementProject()
    {
        var validation = AuthoredProjectValidator.Validate(_worldManifestPath, _rpgContentPath);
        if (!validation.IsValid)
            throw new InvalidDataException("Settlement authored content failed validation: "
                + string.Join(Environment.NewLine, validation.Diagnostics));

        var exteriorCells = _world.Cells.Where(cell => cell.Kind == WorldCellKind.Exterior).ToArray();
        var interiorCells = _world.Cells.Where(cell => cell.Kind == WorldCellKind.Interior).ToArray();
        if (exteriorCells.Length < 3 || interiorCells.Length != 2)
            throw new InvalidDataException("Settlement requires at least three exterior cells and exactly two interiors.");
        if (!_world.TryGetExterior(new ExteriorCellCoordinate(0, 0), out var home) || home is null)
            throw new InvalidDataException("Settlement is missing its market cell at (0, 0).");

        var homeScene = SceneFile.Load(_world.ResolveScenePath(home.Id));
        var interiorIds = interiorCells.Select(cell => cell.Id).ToHashSet();
        var entryDestinations = homeScene.Objects
            .Where(item => item.Door is not null && interiorIds.Contains(item.Door.DestinationCellId))
            .Select(item => item.Door!.DestinationCellId)
            .ToHashSet();
        if (entryDestinations.Count != interiorIds.Count)
            throw new InvalidDataException("Both settlement interiors need a connected door from the market cell.");
        foreach (var interior in interiorCells)
        {
            var scene = SceneFile.Load(_world.ResolveScenePath(interior.Id));
            if (!scene.Objects.Any(item => item.Door?.DestinationCellId == home.Id))
                throw new InvalidDataException($"Interior {interior.Id} needs a return door to the market cell.");
        }
        Console.WriteLine($"RpgSlice: settlement world validated ({exteriorCells.Length} exterior cells, {interiorCells.Length} interiors, "
            + $"{_rpgContent.Actors.Count} actors, {_rpgContent.Items.Count} items).");
    }

    private string BuildSettlementContentSummary()
    {
        var totalObjects = 0;
        var enabledObjects = 0;
        var actorPlacements = 0;
        var itemPlacements = 0;
        var glbInstances = 0;
        foreach (var cell in _world.Cells)
        {
            var scene = SceneFile.Load(_world.ResolveScenePath(cell.Id));
            totalObjects += scene.Objects.Count;
            enabledObjects += scene.Objects.Count(item => item.Enabled);
            actorPlacements += scene.Objects.Count(item => item.WorldEntity?.Kind == WorldEntityKind.Actor);
            itemPlacements += scene.Objects.Count(item => item.WorldEntity?.Kind == WorldEntityKind.Item);
            glbInstances += scene.Objects.Count(item => item.GltfAsset is not null);
        }
        return $"cells={_world.Cells.Count}, scene-objects={totalObjects}, enabled-objects={enabledObjects}, "
            + $"actor-placements={actorPlacements}, item-placements={itemPlacements}, GLB-instances={glbInstances}, "
            + $"static-assets={_staticAssets.AssetCount}, GLB-primitives={_staticAssets.PrimitiveCount}, "
            + $"RPG-actors={_rpgContent.Actors.Count}, RPG-items={_rpgContent.Items.Count}";
    }

    private void RunSettlementRuntimeSmoke()
    {
        if (_staticAssets.AssetCount != 1 || _staticAssets.PrimitiveCount < 1)
            throw new InvalidOperationException("Settlement did not load its manifest-referenced static GLB.");
        var home = _world.FindCell(_world.TryGetExterior(new ExteriorCellCoordinate(0, 0), out var cell)
                ? cell!.Id : Guid.Empty)
            ?? throw new InvalidDataException("Settlement market cell was not found after validation.");
        if (!_cellStreamer.TryGetActiveCell(home.Id, out _, out var active) || active is null)
            throw new InvalidOperationException("Settlement market cell did not activate.");
        var glbInstances = active.Scene.Objects.Where(item => item.Enabled && item.GltfAsset is not null).ToArray();
        var expectedMeshColliders = glbInstances.Sum(item => _staticAssets.GetCollisionPrimitiveCount(item.GltfAsset!.AssetId));
        if (expectedMeshColliders == 0 || active.StaticMeshColliderCount < expectedMeshColliders)
            throw new InvalidOperationException("Settlement static GLB geometry was not added to the physics world.");

        var roles = _rpgGameplay?.LiveActorInstances
            ?? throw new InvalidOperationException("Settlement RPG runtime did not initialize.");
        if (roles.Count != 3 || roles.Select(role => role.InstanceId).Distinct().Count() != roles.Count)
            throw new InvalidOperationException("Settlement worker, merchant, and hostile must have unique world identities.");
        if (!_world.TryGetExterior(new ExteriorCellCoordinate(1, 0), out var work) || work is null
            || roles[1].CellId != home.Id || roles[2].CellId != home.Id
            || (roles[0].CellId != home.Id && roles[0].CellId != work.Id))
            throw new InvalidOperationException("Settlement merchant, hostile, or scheduled worker is assigned to the wrong cell.");
        foreach (var role in roles)
        {
            if (!_cellStreamer.TryGetActiveCell(role.CellId, out _, out var roleCell) || roleCell is null
                || roleCell.Scene.Find(role.SceneObjectId) is null)
                throw new InvalidOperationException($"Settlement actor instance {role.InstanceId} is not present in its active cell.");
        }
        var identities = _worldPersistence.Identities.ExportSnapshot();
        if (identities.Select(entry => entry.InstanceId.Value).Distinct().Count() != identities.Count)
            throw new InvalidOperationException("Settlement loaded cells contain duplicate world instance identities.");
        Console.WriteLine($"RpgSlice: settlement smoke passed (GLB primitives={expectedMeshColliders}, "
            + $"static mesh colliders={active.StaticMeshColliderCount}, live NPC roles={roles.Count}, "
            + $"unique loaded instance identities={identities.Count}).");
    }

    private WorldPlayerLocation CapturePlayerLocation() =>
        new(_currentCellId, _player.Pose.Position, _playerFacing);

    private void SetGameplayFeedback(string message)
    {
        _saveFeedback = message;
        _saveFeedbackSeconds = 2f;
    }

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
        var environment = OutdoorEnvironmentProfile.Evaluate((float)(_worldClock.TimeOfDaySeconds / 3600d),
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
        {
            using (_benchmark.MeasurePhase(BenchmarkPhase.TerrainWork))
            {
                _terrain.Draw(_camera.View, _camera.Projection, _camera.Position, environment);
            }
        }
        using (_benchmark.MeasurePhase(BenchmarkPhase.SceneSubmission))
        {
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
                    "Scheduled Worker" => new Color(212, 174, 90),
                    "RpgSlice Merchant" => new Color(77, 166, 128),
                    "RpgSlice Road Raider" => new Color(174, 67, 58),
                    _ when sceneObject.Door is not null => new Color(139, 84, 49),
                    _ => new Color(137, 125, 108)
                };
                if (sceneObject.GltfAsset is not null)
                {
                    _staticAssets.Draw(activeCell.Scene, sceneObject, activeCell.WorldTransform,
                        _camera.View, _camera.Projection, environment, !_insideInterior);
                    continue;
                }
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
        DrawRpgQuestUi();
        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
        }
    }

    private void DrawRpgQuestUi()
    {
        if (_rpgGameplay is null || _benchmark is not null) return;

        _ui.Begin();
        _ui.Panel(new Rectangle(24, 24, 610, 78), new Color(7, 13, 18, 220), new Color(117, 143, 131));
        _ui.Text("THE LOST DELIVERY", new Vector2(42, 36), 16, new Color(237, 208, 139));
        _ui.TextFit(_rpgGameplay.LostDeliveryJournal, new Vector2(42, 66), 574f, 15f, Color.White);

        if (_rpgGameplay.CurrentDialogueNode is { } dialogueNode)
        {
            _ui.Panel(new Rectangle(24, 510, 900, 180), new Color(7, 13, 18, 232), new Color(156, 125, 78));
            _ui.Text($"{dialogueNode.Speaker}", new Vector2(44, 526), 17, new Color(237, 208, 139));
            _ui.TextWrapped(dialogueNode.Text, new Vector2(44, 556), 860f, 16f, Color.White);
            var options = _rpgGameplay.CurrentDialogueOptions;
            for (var i = 0; i < options.Count && i < 2; i++)
                _ui.TextFit($"[{i + 1}] {options[i].Label}", new Vector2(44, 620 + (i * 26)), 850f, 15f,
                    new Color(198, 211, 206));
        }
        else
        {
            var prompt = FindNearbyDoor() is { } door
                ? $"E use {door.Name}"
                : _rpgGameplay.GetPrompt(_currentCellId, _player.Pose.Position);
            if (prompt is not null)
            {
                _ui.Panel(new Rectangle(24, 630, 700, 52), new Color(7, 13, 18, 210), new Color(117, 143, 131));
                _ui.TextFit(prompt, new Vector2(42, 647), 664f, 15f, Color.White);
            }
        }
        _ui.End();
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
        _staticAssets?.Dispose();
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

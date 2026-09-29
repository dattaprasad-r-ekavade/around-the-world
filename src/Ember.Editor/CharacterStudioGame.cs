using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ember.Authoring;
using Ember.Assets;
using Ember.Audio;
using Ember;
using Ember.Input;
using Ember.Project;
using Ember.Physics;
using Ember.Scene;
using Ember.Sequence;
using Ember.Render;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace Ember.Editor;

/// <summary>
/// The first external consumer of the scene foundation: shared-asset scene instances, independent
/// character playback, an orbit camera, and screenshot/open/save paths. It deliberately has no game rules.
/// </summary>
public sealed partial class CharacterStudioGame : EngineHost
{
    private sealed record ViewportTransformDrag(TransformGizmoMode Mode, Guid ObjectId,
        Transform Before, Vector3 StartWorldPosition, TransformGizmoHandle AxisHandle,
        TransformGizmoRing? RotationRing, Vector2 PointerStart, Vector2 LastPointer,
        float AccumulatedAngle, Matrix ParentWorld, bool HasParent);

    private sealed record LifecycleCycleSample(int Cycle, bool PlayStarted, bool PlayStopped,
        bool SceneReloaded, int SceneObjectCount, int OwnedPreviewGraphicsResources,
        long WorkingSetBytes, long ManagedHeapBytes);

    private enum LifecycleSmokeStage { StartPlaySession, StopPlaySession, ReloadScene, Complete }

    private sealed class LifecycleSmokeState
    {
        public required Guid RunId { get; init; }
        public required DateTimeOffset StartedUtc { get; init; }
        public required string ScenePath { get; init; }
        public required string VerifyPath { get; init; }
        public required string MarkdownPath { get; init; }
        public required string JsonPath { get; init; }
        public required string AuthoredSnapshot { get; init; }
        public required int InitialResourceCount { get; init; }
        public required Guid[] InitialSceneIds { get; init; }
        public List<LifecycleCycleSample> Samples { get; } = new();
        public List<string> Errors { get; } = new();
        public int CompletedCycles { get; set; }
        public LifecycleSmokeStage Stage { get; set; } = LifecycleSmokeStage.StartPlaySession;
    }

    private sealed class PendingAssetPlacementPreview : IDisposable
    {
        public PendingAssetPlacementPreview(GltfAssetReference reference, EngineProjectAssetImport? import,
            PreviewResources resources, SceneGraph scene, SceneObject item)
        {
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
            Import = import;
            Resources = resources;
            Scene = scene;
            Item = item;
        }

        public GltfAssetReference Reference { get; }
        public EngineProjectAssetImport? Import { get; }
        public PreviewResources Resources { get; }
        public SceneGraph Scene { get; }
        public SceneObject Item { get; }

        public void Dispose()
        {
            try { Resources.Dispose(); }
            finally { Import?.Dispose(); }
        }
    }

    private static readonly Guid PreviewInstanceId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly GltfAssetReference DefaultAsset = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"), "Assets/TextureCoordinateTest.glb");
    private static readonly GltfAssetReference FoxAsset = new(
        Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"), "Assets/Fox.glb");
    private static readonly Guid PairPreviewInstanceId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543211");
    private static readonly Guid HandPreviewAttachmentId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543212");
    private static readonly Guid WideCameraTrackId = Guid.Parse("6b9c2e11-954d-4a55-9ad2-7ddfd02c0001");
    private static readonly Guid CloseCameraTrackId = Guid.Parse("6b9c2e11-954d-4a55-9ad2-7ddfd02c0002");

    private SceneGraph _sceneData;
    private readonly EditorProjectSession _projectSession = new();
    private EngineProjectFile? _project
    {
        get => _projectSession.Project;
        set => _projectSession.SetProject(value);
    }
    private string _sceneAssetRoot = AppContext.BaseDirectory;
    private readonly OrbitCamera _camera = new() { MaxDistance = 500f };
    private SceneCommandHistory _editorHistory = new();
    private readonly SceneLighting _sceneLighting = new();
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private readonly bool _lifecycleSmokeRequested;
    private readonly int _lifecycleSmokeCycles;
    private readonly int _referenceSessionMinutes;
    private string? _sceneSavePath;
    private readonly string? _openSequencePath;
    private readonly string? _saveSequencePath;
    private readonly string? _startupSequenceExportDirectory;
    private readonly float? _startupSequenceExportStartTime;
    private readonly float? _startupSequenceExportEndTime;
    private readonly int _startupSequenceExportFrameRate;
    private readonly int _startupSequenceExportWidth;
    private readonly int _startupSequenceExportHeight;
    private readonly PlaybackOptions _playbackOptions;
    private SceneResourceScope? _sceneResources;
    private ReloadableAsset<PreviewResources>? _preview;
    private PendingAssetPlacementPreview? _pendingAssetPlacementPreview;
    private CharacterStudioEditorUi? _editorUi;
    private DirectionalShadowMap? _shadowMap;
    private Effect? _shadowEffect;
    private string? _blockedSaveReason;
    private string _reimportStatus = "P: play on clone | R: reimport GLBs | S: save scene";
    private readonly CharacterStudioPlayController _playController;
    private ScenePlaySession? _playSession
    {
        get => _playController.Session;
        set => _playController.SetSession(value);
    }
    private InputActionMap _playInputMap => _playController.InputMap;
    private PhysicsCharacterController? _playCharacterController
    {
        get => _playController.CharacterController;
        set => _playController.CharacterController = value;
    }
    private Guid? _playCharacterObjectId
    {
        get => _playController.CharacterObjectId;
        set => _playController.CharacterObjectId = value;
    }
    private SceneCommandHistory _playHistory
    {
        get => _playController.History;
        set => _playController.SetHistory(value);
    }
    private ImportedAudioClip? _playAudioClip
    {
        get => _playController.InteractionClip;
        set => _playController.InteractionClip = value;
    }
    private float _interactionVolume => _playController.InteractionVolume;
    private SceneSequence? _sequence;
    private SceneSequencePlayer? _sequencePlayer;
    private bool _sequencePreviewEnabled;
    private string? _activeSequenceCameraName;
    private SequenceFrameExportJob? _sequenceExportJob;
    private SequenceFrameRenderTarget? _sequenceExportTarget;
    private string _lastSequenceExportStatus = "Ready";
    private string? _lastSequenceExportDirectory;
    private string? _lastSequenceExportError;
    private float _sequenceExportRestoreTime;
    private bool _sequenceExportRestoreWasPlaying;
    private bool _sequenceExportRestorePreviewEnabled;
    private LifecycleSmokeState? _lifecycleSmoke;
    private CharacterStudioReferenceSession? _referenceSession;
    private readonly Stopwatch _referenceFrameClock = new();
    private bool _referenceFirstFrame = true;
    private bool _showDiagnostics;
    private bool _allowExitAfterConfirmation;
    private double _referenceNextModeSwitchSeconds = 60d;
    private double _referenceNextMemorySampleSeconds = 1d;
    private string? _referenceMarkdownPath;
    private string? _referenceJsonPath;
    private Vector3 _sequenceExportRestoreCameraPosition;
    private Quaternion _sequenceExportRestoreCameraRotation;
    private float _sequenceExportRestoreFieldOfView;
    private AttachmentBoxRenderer? _attachmentRenderer;
    private BasicEffect? _moveGizmoEffect;
    private ViewportTransformDrag? _viewportTransformDrag;
    private readonly Dictionary<Guid, bool> _farLodByObjectId = new();
    private Dictionary<Guid, CharacterStudioPlayController.PathPreviewAgent> _pathPreviewAgents =>
        _playController.PathFollowers;
    private PhysicsWorld? _pathPreviewWorld
    {
        get => _playController.PhysicsWorld;
        set => _playController.PhysicsWorld = value;
    }
    private SceneStaticColliderSet? _playSceneColliders
    {
        get => _playController.SceneColliders;
        set => _playController.SceneColliders = value;
    }
    private PhysicsFixedStepper? _pathPreviewStepper
    {
        get => _playController.PhysicsStepper;
        set => _playController.PhysicsStepper = value;
    }
    private MouseState _lastMouse;
    private Vector2 _viewportClickOrigin;
    private bool _viewportPointerWasDown;
    private bool _viewportClickPending;
    private bool _hasMouse;
    private int _sceneDrawCalls;
    private int _shadowDrawCalls;
    private int _culledStaticDrawCalls;
    private int _skinnedDrawCalls;
    private double _frameMilliseconds;

    private SceneGraph CurrentScene => _playSession?.RuntimeScene ?? _sceneData;

    public CharacterStudioGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Ember Character Studio")
    {
        _playController = new CharacterStudioPlayController(this);
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _savePath = ParseOption(args, "--save");
        _lifecycleSmokeRequested = HasArgument(args, "--lifecycle-smoke");
        _lifecycleSmokeCycles = ParseIntOption(args, "--lifecycle-cycles", 10);
        _referenceSessionMinutes = ParseIntOption(args, "--reference-session-minutes", 0);
        if (_lifecycleSmokeCycles < 1)
            throw new ArgumentOutOfRangeException(nameof(args), "--lifecycle-cycles must be positive.");
        if (!_lifecycleSmokeRequested && ParseOption(args, "--lifecycle-cycles") is not null)
            throw new ArgumentException("--lifecycle-cycles requires --lifecycle-smoke.", nameof(args));
        if (_referenceSessionMinutes < 0
            || (_referenceSessionMinutes == 0 && ParseOption(args, "--reference-session-minutes") is not null))
            throw new ArgumentOutOfRangeException(nameof(args), "--reference-session-minutes must be positive when supplied.");
        if (_lifecycleSmokeRequested && _referenceSessionMinutes > 0)
            throw new ArgumentException("--lifecycle-smoke and --reference-session-minutes cannot be combined.", nameof(args));
        var openPath = ParseOption(args, "--open");
        var projectPath = ParseOption(args, "--project");
        if (projectPath is not null && openPath is not null)
            throw new ArgumentException("Use either --project <ember.project.json> or --open <scene.json>, not both.");
        if (projectPath is not null)
        {
            _project = EngineProjectFile.Load(projectPath);
            RecordRecentProject(_project.FilePath);
            openPath = EngineProjectWorkspace.ResolveInitialScenePath(_project);
        }
        _openSequencePath = ParseOption(args, "--open-sequence");
        _saveSequencePath = ParseOption(args, "--save-sequence");
        _startupSequenceExportDirectory = ParseOption(args, "--export-sequence");
        if (_lifecycleSmokeRequested && (_openSequencePath is not null || _saveSequencePath is not null
            || _startupSequenceExportDirectory is not null))
            throw new ArgumentException("--lifecycle-smoke cannot be combined with sequence open, save, or export options.", nameof(args));
        if (_referenceSessionMinutes > 0 && (_openSequencePath is not null || _saveSequencePath is not null
            || _startupSequenceExportDirectory is not null))
            throw new ArgumentException("--reference-session-minutes cannot be combined with sequence open, save, or export options.", nameof(args));
        var hasStartupExportOptions = HasArgument(args, "--export-start")
            || HasArgument(args, "--export-end") || HasArgument(args, "--export-fps")
            || HasArgument(args, "--export-width") || HasArgument(args, "--export-height");
        if (_startupSequenceExportDirectory is null && hasStartupExportOptions)
            throw new ArgumentException("Sequence export settings require --export-sequence <directory>.");
        _startupSequenceExportStartTime = ParseFiniteFloatOption(args, "--export-start");
        _startupSequenceExportEndTime = ParseFiniteFloatOption(args, "--export-end");
        _startupSequenceExportFrameRate = ParseIntOption(args, "--export-fps", 30);
        _startupSequenceExportWidth = ParseIntOption(args, "--export-width", 1280);
        _startupSequenceExportHeight = ParseIntOption(args, "--export-height", 720);
        var requestedAnimation = ParseOption(args, "--clip");
        var requestedAnimationTime = ParseFiniteFloatOption(args, "--time");
        var requestedAnimationSpeed = ParseFiniteFloatOption(args, "--speed") ?? 1f;
        var pauseAnimation = HasArgument(args, "--pause");
        var startPlayback = HasArgument(args, "--play");
        var forceLoop = HasArgument(args, "--loop");
        var forceNoLoop = HasArgument(args, "--no-loop");
        var pair = HasArgument(args, "--pair");
        var secondClip = ParseOption(args, "--second-clip");
        var secondTime = ParseFiniteFloatOption(args, "--second-time");
        var secondSpeed = ParseFiniteFloatOption(args, "--second-speed") ?? 1f;
        var pauseSecond = HasArgument(args, "--pause-second");
        var secondNoLoop = HasArgument(args, "--second-no-loop");
        var crossfadeClip = ParseOption(args, "--crossfade");
        var blendAmount = ParseFiniteFloatOption(args, "--blend") ?? 0.5f;
        var attachHand = HasArgument(args, "--attach-hand");

        if (pauseAnimation && startPlayback)
            throw new ArgumentException("Use either --play or --pause with --clip, not both.");
        if (forceLoop && forceNoLoop)
            throw new ArgumentException("Use either --loop or --no-loop with --clip, not both.");
        var hasAnimationOptions = requestedAnimationTime is not null
            || ParseOption(args, "--speed") is not null
            || pauseAnimation || startPlayback || forceLoop || forceNoLoop;
        if (requestedAnimation is null && hasAnimationOptions)
            throw new ArgumentException("Animation playback options require --clip <name>.");
        var hasSecondOptions = secondClip is not null || secondTime is not null
            || ParseOption(args, "--second-speed") is not null || pauseSecond || secondNoLoop;
        if (hasSecondOptions && !pair)
            throw new ArgumentException("Second-character options require --pair.");
        if (hasSecondOptions && requestedAnimation is null)
            throw new ArgumentException("Second-character playback options require --clip <name> for the first character.");
        if (crossfadeClip is not null && requestedAnimation is null)
            throw new ArgumentException("--crossfade requires --clip <name>.");
        if (ParseOption(args, "--blend") is not null && crossfadeClip is null)
            throw new ArgumentException("--blend requires --crossfade <name>.");
        if (!float.IsFinite(blendAmount) || blendAmount < 0f || blendAmount > 1f)
            throw new ArgumentOutOfRangeException(nameof(blendAmount), "--blend must be between zero and one.");

        _playbackOptions = new PlaybackOptions(requestedAnimation, requestedAnimationTime,
            requestedAnimationSpeed, pauseAnimation, !forceNoLoop, pair, secondClip,
            secondTime, secondSpeed, pauseSecond, !secondNoLoop, crossfadeClip,
            blendAmount, attachHand, hasSecondOptions);

        var openedExistingScene = false;
        if (openPath is null)
        {
            _sceneData = CreateDefaultScene(ResolveDefaultAsset(args), pair);
        }
        else
        {
            try
            {
                _sceneData = SceneFile.Load(openPath);
                if (pair) AddPairIfNeeded(_sceneData);
                openedExistingScene = true;
            }
            catch (Exception exception)
            {
                _sceneData = CreateDefaultScene(ResolveDefaultAsset(args), pair);
                _faults.Add($"open {openPath}: {exception.Message}");
                _reimportStatus = "Open failed; recovery scene is unsaved unless you use --save to another path.";
            }
        }
        _sceneAssetRoot = _project?.RootDirectory ?? AppContext.BaseDirectory;

        // Never let S overwrite a malformed or unsupported source with the fallback scene.
        // An explicit --save path remains available for recovery/Save As.
        var saveTargetsFailedSource = !openedExistingScene && openPath is not null && _savePath is not null
            && PathsReferToSameFile(openPath, _savePath);
        _sceneSavePath = saveTargetsFailedSource
            ? null
            : _savePath ?? (openedExistingScene ? openPath : null);
        if (saveTargetsFailedSource)
            _blockedSaveReason = "Invalid source preserved; choose a different --save path.";

        Exiting += OnGameExiting;

    }

    protected override void LoadContent()
    {
        _sceneResources = new SceneResourceScope();
        try
        {
            IsMouseVisible = true;
            Window.TextInput += HandleTextInput;
            AttachCanvas();
            _editorUi = new CharacterStudioEditorUi(GraphicsDevice, LogicalWidth, LogicalHeight,
                _editorHistory, AfterSceneStructureChange,
                GetCharacterEditorInfo, SelectCharacterClip, SeekCharacter, CommitCharacterTimeEdit,
                SetCharacterPlaying, _sceneLighting,
                () => _playSession is not null, StartPlaySession, StopPlaySession, TriggerInteraction,
                () => _interactionVolume, SetInteractionVolume, GetSequenceEditorInfo,
                SetSequencePlaying, SeekSequence, SetSequencePreviewEnabled,
                GetSequenceExportEditorInfo, StartSequenceExport, CancelSequenceExport,
                SaveSceneAs, OpenWorldCell, OnWorldCellRenamed, () => _sceneSavePath,
                ApplyRecoveredProject,
                StartPathFollow, GetPathFollowStatus, StopPathFollow,
                CreateProjectForEditor, path => OpenProjectForEditor(path), ImportGlbForEditor,
                GetPendingAssetPreviewName, AcceptPendingAssetPreview, CancelPendingAssetPreview,
                () => _project?.FilePath,
                GetProjectAssetReferencesForEditor, InvalidateProjectAssetCatalog,
                PreviewProjectAssetForEditor, ReloadProjectAssetForEditor,
                LoadRecentProjectPaths, visible => _showDiagnostics = visible,
                ConfirmExitAfterSaveOrDiscard);
            _moveGizmoEffect = _sceneResources.Own(new BasicEffect(GraphicsDevice)
            {
                LightingEnabled = false,
                VertexColorEnabled = true
            });
            _shadowEffect = Content.Load<Effect>("Effects/SceneShadow");
            _sceneLighting.Apply(_shadowEffect);
            _shadowMap = _sceneResources.Own(new DirectionalShadowMap(GraphicsDevice,
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height));

            _preview = new ReloadableAsset<PreviewResources>(PreviewResources.Load(
                GraphicsDevice, ResolveSceneAssets(), _sceneData));
            ApplyCommandLineSettings();
            _preview.Current.RebuildCharacterInstances(_sceneData);
            if (_preview.Current.HasSkinnedCharacters)
            {
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    _preview.Current.MaximumJointCount);
                if (_preview.Current.AttachmentsByInstanceId.Count > 0)
                    _attachmentRenderer = _sceneResources.Own(new AttachmentBoxRenderer(GraphicsDevice));
            }

            _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);
            var sceneBounds = GetSceneBounds();
            if (sceneBounds is { } bounds)
            {
                _camera.Reset(bounds.Center, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);
                _camera.Frame(bounds);
            }
            else
                _camera.Reset(Vector3.Zero, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);
            BuildSequencePreview();

            if (_openSequencePath is { } sequencePath)
            {
                _sequence = SequenceFile.Load(sequencePath, _sceneData,
                    BuildSequenceClipCatalog(_preview.Current));
                _sequencePlayer = new SceneSequencePlayer(_sequence);
                _sequencePreviewEnabled = false;
                Console.WriteLine($"Opened sequence '{_sequence.Name}' from {Path.GetFullPath(sequencePath)}");
            }

            if (_saveSequencePath is { } saveSequencePath)
            {
                var sequence = _sequence
                    ?? throw new InvalidOperationException("There is no character sequence to save.");
                SequenceFile.SaveAtomic(sequence, _sceneData, saveSequencePath);
                Console.WriteLine($"Saved sequence '{sequence.Name}' to {Path.GetFullPath(saveSequencePath)}");
            }

            if (_startupSequenceExportDirectory is { } exportDirectory)
            {
                if (_sequence is null)
                {
                    Console.Error.WriteLine("CharacterStudio export failed: --export-sequence requires a scene with a skinned character.");
                    Environment.ExitCode = 1;
                    Exit();
                }
                else
                {
                    StartSequenceExport(new SequenceExportEditorRequest(exportDirectory,
                        _startupSequenceExportStartTime ?? 0f,
                        _startupSequenceExportEndTime ?? _sequence.Duration,
                        _startupSequenceExportFrameRate, _startupSequenceExportWidth, _startupSequenceExportHeight));
                    if (_sequenceExportJob?.IsRunning != true)
                    {
                        Console.Error.WriteLine($"CharacterStudio export failed: {_lastSequenceExportError}");
                        Environment.ExitCode = 1;
                        Exit();
                    }
                }
            }

            if (_savePath is not null) SaveScene();
            foreach (var fault in _faults) Console.WriteLine($"character studio: {fault}");
            if (_lifecycleSmokeRequested) RunLifecycleSmoke();
            if (_referenceSessionMinutes > 0) RunReferenceSession();
        }
        catch
        {
            Window.TextInput -= HandleTextInput;
            _editorUi?.Dispose();
            _editorUi = null;
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
        if (_referenceSession is not null)
            RecordReferenceFrameInterval();
        _input.Sample();
        var mouse = _input.CurrentMouse;
        _editorUi?.Update((float)gameTime.ElapsedGameTime.TotalSeconds,
            _input.CurrentKeyboard, mouse, LogicalMouse(mouse), CurrentScene,
            suppressMouseInput: _viewportTransformDrag is not null);
        if (_referenceSession is not null)
        {
            AdvanceReferenceSession();
            _sequencePlayer?.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
            if (_preview?.Current is { } activePreview)
                foreach (var state in activePreview.CharacterInstances.Values)
                    state.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
            _input.Commit();
            base.Update(gameTime);
            return;
        }
        if (_lifecycleSmoke is not null)
        {
            AdvanceLifecycleSmoke();
            _input.Commit();
            base.Update(gameTime);
            return;
        }

        var uiCapturesMouse = _editorUi?.WantsMouse ?? false;
        var uiCapturesKeyboard = _editorUi?.WantsKeyboard ?? false;
        ProcessViewportSelection(mouse, LogicalMouse(mouse), uiCapturesMouse);

        var sequenceExportRunning = _sequenceExportJob?.IsRunning == true;
        var playInput = _playInputMap.Sample(_input.CurrentKeyboard, IsActive,
            _playSession is null || uiCapturesKeyboard || sequenceExportRunning);
        if (_playSession is not null && _playCharacterController is { } playCharacter)
        {
            playCharacter.SetMoveInput(ToOrbitCameraMovement(playInput.ReadMovement(), _camera.Yaw));
            if (_playInputMap.ConsumePressed(GameplayActionNames.Jump)) playCharacter.RequestJump();
        }
        if (!sequenceExportRunning && _viewportTransformDrag is null && !uiCapturesKeyboard
            && _input.Pressed(_input.CurrentKeyboard, Keys.Escape)) Exit();
        if (!sequenceExportRunning && _viewportTransformDrag is null && !uiCapturesKeyboard
            && _input.Pressed(_input.CurrentKeyboard, Keys.P))
        {
            if (_playSession is null) StartPlaySession();
            else StopPlaySession();
        }
        if (!sequenceExportRunning && _viewportTransformDrag is null && !uiCapturesKeyboard && _playSession is not null
            && _input.Pressed(_input.CurrentKeyboard, Keys.E)) TriggerInteraction();
        if (!sequenceExportRunning && _viewportTransformDrag is null && !uiCapturesKeyboard
            && _playSession is null && _input.Pressed(_input.CurrentKeyboard, Keys.R)) ReimportAsset();
        if (!sequenceExportRunning && _viewportTransformDrag is null && !uiCapturesKeyboard
            && _playSession is null && _input.Pressed(_input.CurrentKeyboard, Keys.S)) SaveScene();
        if (!_hasMouse)
        {
            _lastMouse = mouse;
            _hasMouse = true;
        }

        if (!sequenceExportRunning && !_sequencePreviewEnabled && _viewportTransformDrag is null
            && !uiCapturesMouse && mouse.LeftButton == ButtonState.Pressed)
        {
            _camera.Orbit(new Vector2(mouse.X - _lastMouse.X, mouse.Y - _lastMouse.Y),
                _playSession?.RuntimeScene.PlaySettings.CameraOrbitSensitivity);
        }

        if (!sequenceExportRunning && !_sequencePreviewEnabled && _viewportTransformDrag is null && !uiCapturesMouse)
            _camera.Zoom(mouse.ScrollWheelValue - _lastMouse.ScrollWheelValue);
        _lastMouse = mouse;
        _input.Commit();
        var preview = _preview?.Current;
        if (preview is not null)
        {
            if (!sequenceExportRunning)
            {
                UpdatePathFollowers(RealSeconds(gameTime));
                _sequencePlayer?.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
                foreach (var state in preview.CharacterInstances.Values)
                    state.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
                if (_pendingAssetPlacementPreview is { } pending)
                    foreach (var state in pending.Resources.CharacterInstances.Values)
                        state.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
                if (_sequencePreviewEnabled) ApplySequenceAtCurrentTime();
            }
        }
        base.Update(gameTime);
    }

}

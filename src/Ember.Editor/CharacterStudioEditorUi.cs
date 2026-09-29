using Ember.Scene;
using Ember.Render;
using Ember.Rpg;
using Ember.Authoring;
using Ember.Project;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Ember.Editor;

internal sealed record CharacterEditorInfo(
    IReadOnlyList<string> ClipNames, string? ClipName, float Time, float Duration, bool IsPlaying);
internal sealed record SequenceEditorInfo(
    string Name, float Time, float Duration, bool IsPlaying, bool PreviewEnabled, string? CameraName);
internal sealed record SequenceExportEditorInfo(
    bool IsRunning, int CompletedFrames, int TotalFrames, string Status, string? OutputDirectory, string? Error);
internal sealed record SequenceExportEditorRequest(
    string OutputDirectory, float StartTime, float EndTime, int FrameRate, int Width, int Height);
internal sealed record RpgPlacementOption(WorldEntityKind Kind, string Id, string Name);
internal delegate string RecoveryCaptureAction(SceneGraph scene, string worldManifestPath, string rpgContentPath,
    string? currentScenePath, RpgContentSet? content);
internal delegate string RecoveryApplyAction(AuthoredProjectRecoveryStaging staging);

/// <summary>Immediate-mode scene hierarchy and transform panel for CharacterStudio.</summary>
internal sealed partial class CharacterStudioEditorUi : IDisposable
{
    private sealed record RecoveryReviewResult(AuthoredProjectRecoveryStaging Staging,
        AuthoredProjectValidationResult? Validation, Exception? ValidationError);

    private enum TransformTool
    {
        Move,
        Turn,
        Size
    }

    private readonly IntPtr _context;
    private readonly IntPtr _windowHandle;
    private readonly ImGuiIOPtr _io;
    private readonly ImGuiMonoGameRenderer _renderer;
    private SceneCommandHistory _history;
    private readonly Action _afterStructureChange;
    private readonly Func<Guid, CharacterEditorInfo?> _getCharacterInfo;
    private readonly Action<Guid, string> _selectCharacterClip;
    private readonly Action<Guid, float> _seekCharacter;
    private readonly Action<Guid, float, float> _commitCharacterTimeEdit;
    private readonly Action<Guid, bool> _setCharacterPlaying;
    private readonly SceneLighting _lighting;
    private readonly Func<bool> _isPlaying;
    private readonly Action _startPlay;
    private readonly Action _stopPlay;
    private readonly Action _interact;
    private readonly Func<float> _getInteractionVolume;
    private readonly Action<float> _setInteractionVolume;
    private readonly Func<SequenceEditorInfo?> _getSequenceInfo;
    private readonly Action<bool> _setSequencePlaying;
    private readonly Action<float> _seekSequence;
    private readonly Action<bool> _setSequencePreviewEnabled;
    private readonly Func<SequenceExportEditorInfo> _getSequenceExportInfo;
    private readonly Action<SequenceExportEditorRequest> _startSequenceExport;
    private readonly Action _cancelSequenceExport;
    private readonly Action<string> _saveSceneAs;
    private readonly Func<string, string, string?> _openWorldCell;
    private readonly Action<string, string, Guid> _worldCellRenamed;
    private readonly Func<string?> _getCurrentScenePath;
    private readonly RecoveryCaptureAction _captureRecovery;
    private readonly RecoveryApplyAction _applyRecovery;
    private readonly Func<Guid, CellPathGraph, CellPathRoute, string> _startPathFollow;
    private readonly Func<Guid, string?> _getPathFollowStatus;
    private readonly Action<Guid> _stopPathFollow;
    private readonly Func<string, bool, string> _createProject;
    private readonly Func<string, string> _openProject;
    private readonly Func<string, string> _importGlb;
    private readonly Func<string?> _getPendingAssetPreviewName;
    private readonly Func<string> _acceptPendingAssetPreview;
    private readonly Action _cancelPendingAssetPreview;
    private readonly Func<string?> _getCurrentProjectPath;
    private readonly Func<IReadOnlyList<GltfAssetReference>> _getProjectAssetReferences;
    private readonly Action _refreshProjectAssetReferences;
    private readonly Func<GltfAssetReference, string> _previewProjectAsset;
    private readonly Func<GltfAssetReference, string> _reloadProjectAsset;
    private readonly Func<IReadOnlyList<string>> _getRecentProjectPaths;
    private readonly Action<bool> _setDiagnosticsVisible;
    private FirstCreationLesson? _firstCreationLesson;
    private string? _lessonProjectOpenCheck;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private string _sequenceExportDirectory = Path.Combine(Environment.CurrentDirectory, "SequenceFrames");
    private string _worldManifestPath = Path.Combine(Environment.CurrentDirectory, WorldManifest.DefaultFileName);
    private string _worldStatus = "Open or create a world manifest.";
    private string _cellName = "New Cell";
    private string _renameCellName = string.Empty;
    private string _sceneSaveAsPath = Path.Combine(Environment.CurrentDirectory, "Scenes", "Untitled.json");
    private string _rpgContentPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RpgPlacementDefinitions.json");
    private string _rpgPlacementStatus = "Load RPG placement definitions.";
    private RpgContentSet? _rpgContent;
    private string? _selectedRpgDefinitionKey;
    private NumericsVector3 _rpgPlacementPosition = NumericsVector3.Zero;
    private NumericsVector3 _spawnMarkerPosition = NumericsVector3.Zero;
    private string _travelStatus = "Open a world manifest to author travel links.";
    private Guid? _selectedTravelCellId;
    private Guid? _selectedTravelSpawnId;
    private readonly Dictionary<Guid, string> _doorLinkStatuses = new();
    private readonly Dictionary<Guid, (string Path, DateTime LastWriteUtc, SceneGraph Scene)> _travelSceneCache = new();
    private WorldManifest? _worldManifest;
    private AuthoredProjectRecoveryStaging? _recoveryStaging;
    private string _recoveryStatus = "No recovery snapshot reviewed.";
    private string _recoveryReport = string.Empty;
    private bool _recoveryCanApply;
    private Task<RecoveryReviewResult>? _recoveryReviewTask;
    private CancellationTokenSource? _recoveryReviewCancellation;
    private AuthoredProjectRecoveryProgress? _recoveryReviewProgress;
    private string? _recoveryReviewActivity;
    private string? _recoveryReviewProjectRoot;
    private string? _recoveryReviewDirectory;
    private float _recoveryAutosaveElapsedSeconds;
    private Guid? _selectedWorldCellId;
    private int _exteriorCellX;
    private int _exteriorCellZ;
    private float _exteriorCellWidth = WorldManifest.Version1DefaultExteriorCellWidth;
    private int _sequenceExportWidth = 1280;
    private int _sequenceExportHeight = 720;
    private int _sequenceExportFrameRate = 30;
    private float _sequenceExportStartTime;
    private float _sequenceExportEndTime;
    private bool _sequenceExportEndTimeInitialized;
    private Guid? _selectedObjectId;
    private Guid? _selectedAssetId;
    private string _assetSearchQuery = string.Empty;
    private Guid? _activeTransformObjectId;
    private Transform? _activeTransformStart;
    private Guid? _activeBoxColliderObjectId;
    private SceneBoxColliderComponent? _activeBoxColliderStart;
    private Guid? _activeCharacterTimeObjectId;
    private float _activeCharacterTimeStart;
    private float _activeCharacterTimeCurrent;
    private TransformTool _transformTool = TransformTool.Move;
    private bool _initialSelectionSet;
    private bool _showHome;
    private bool _snapMoveToGrid;
    private float _moveGridStep = 10f;
    private bool _snapTurnToStep;
    private float _turnSnapDegrees = 15f;
    private bool _snapSizeToStep;
    private float _sizeSnapStep = 0.1f;
    private bool _showAddLibrary = true;
    private bool _showToolMenu;
    private bool _showSequenceTools;
    private bool _showWorldTools;
    private bool _showRpgTools;
    private bool _showSceneTemplateTools;
    private bool _showDiagnostics;
    private bool _showFirstCreationLesson;
    private bool _showLessonWhy;
    private int _lessonHintLevel;
    private FirstCreationLessonStep? _lastObservedLessonStep;
    private bool _guideNewProject;
    private bool _startLessonAfterProjectCreate;
    private bool _wantsMouse;
    private bool _wantsKeyboard;
    private int _lastWheel;
    private int _letterboxOffsetX;
    private int _letterboxOffsetY;
    private bool _disposed;

    private static readonly (Keys Source, ImGuiKey Target)[] SpecialKeys =
    [
        (Keys.Tab, ImGuiKey.Tab),
        (Keys.Left, ImGuiKey.LeftArrow), (Keys.Right, ImGuiKey.RightArrow),
        (Keys.Up, ImGuiKey.UpArrow), (Keys.Down, ImGuiKey.DownArrow),
        (Keys.PageUp, ImGuiKey.PageUp), (Keys.PageDown, ImGuiKey.PageDown),
        (Keys.Home, ImGuiKey.Home), (Keys.End, ImGuiKey.End),
        (Keys.Insert, ImGuiKey.Insert), (Keys.Delete, ImGuiKey.Delete),
        (Keys.Back, ImGuiKey.Backspace), (Keys.Space, ImGuiKey.Space),
        (Keys.Enter, ImGuiKey.Enter), (Keys.Escape, ImGuiKey.Escape),
        (Keys.OemComma, ImGuiKey.Comma), (Keys.OemPeriod, ImGuiKey.Period),
        (Keys.OemMinus, ImGuiKey.Minus), (Keys.OemPlus, ImGuiKey.Equal),
        (Keys.OemQuestion, ImGuiKey.Slash), (Keys.OemSemicolon, ImGuiKey.Semicolon),
        (Keys.OemOpenBrackets, ImGuiKey.LeftBracket), (Keys.OemCloseBrackets, ImGuiKey.RightBracket),
        (Keys.OemPipe, ImGuiKey.Backslash), (Keys.OemTilde, ImGuiKey.GraveAccent)
    ];

    private const float RecoveryAutosaveIntervalSeconds = 60f;

    public CharacterStudioEditorUi(GraphicsDevice device, int logicalWidth, int logicalHeight,
        SceneCommandHistory history, Action afterStructureChange,
        Func<Guid, CharacterEditorInfo?> getCharacterInfo, Action<Guid, string> selectCharacterClip,
        Action<Guid, float> seekCharacter, Action<Guid, float, float> commitCharacterTimeEdit,
        Action<Guid, bool> setCharacterPlaying, SceneLighting lighting,
        Func<bool> isPlaying, Action startPlay, Action stopPlay, Action interact,
        Func<float> getInteractionVolume, Action<float> setInteractionVolume,
        Func<SequenceEditorInfo?> getSequenceInfo, Action<bool> setSequencePlaying,
        Action<float> seekSequence, Action<bool> setSequencePreviewEnabled,
        Func<SequenceExportEditorInfo> getSequenceExportInfo,
        Action<SequenceExportEditorRequest> startSequenceExport, Action cancelSequenceExport,
        Action<string> saveSceneAs, Func<string, string, string?> openWorldCell,
        Action<string, string, Guid> worldCellRenamed, Func<string?> getCurrentScenePath,
        RecoveryCaptureAction captureRecovery, RecoveryApplyAction applyRecovery,
        Func<Guid, CellPathGraph, CellPathRoute, string> startPathFollow,
        Func<Guid, string?> getPathFollowStatus, Action<Guid> stopPathFollow,
        Func<string, bool, string> createProject, Func<string, string> openProject,
        Func<string, string> importGlb, Func<string?> getPendingAssetPreviewName,
        Func<string> acceptPendingAssetPreview, Action cancelPendingAssetPreview,
        Func<string?> getCurrentProjectPath,
        Func<IReadOnlyList<GltfAssetReference>> getProjectAssetReferences,
        Action refreshProjectAssetReferences,
        Func<GltfAssetReference, string> previewProjectAsset,
        Func<GltfAssetReference, string> reloadProjectAsset,
        Func<IReadOnlyList<string>> getRecentProjectPaths, Action<bool> setDiagnosticsVisible,
        Action confirmExit)
    {
        _logicalWidth = Math.Max(1, logicalWidth);
        _logicalHeight = Math.Max(1, logicalHeight);
        _windowHandle = device.PresentationParameters.DeviceWindowHandle;
        _homePanel = new HomePanel(this);
        _workspacePanel = new WorkspacePanel(this);
        _scenePanel = new ScenePanel(this);
        _inspectorPanel = new InspectorPanel(this);
        _worldPanel = new WorldPanel(this);
        _dialoguePanel = new DialoguePanel(this);
        _questPanel = new QuestPanel(this);
        _pathPanel = new PathPanel(this);
        _placementTemplatePanel = new PlacementTemplatePanel(this);
        _playSettingsPanel = new PlaySettingsPanel(this);
        _unsavedChangesController = new UnsavedChangesController(this);
        _projectValidationPanel = new ProjectValidationPanel(this);
        _sceneTemplatePanel = new SceneTemplatePanel(this);
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _afterStructureChange = afterStructureChange ?? throw new ArgumentNullException(nameof(afterStructureChange));
        _getCharacterInfo = getCharacterInfo ?? throw new ArgumentNullException(nameof(getCharacterInfo));
        _selectCharacterClip = selectCharacterClip ?? throw new ArgumentNullException(nameof(selectCharacterClip));
        _seekCharacter = seekCharacter ?? throw new ArgumentNullException(nameof(seekCharacter));
        _commitCharacterTimeEdit = commitCharacterTimeEdit ?? throw new ArgumentNullException(nameof(commitCharacterTimeEdit));
        _setCharacterPlaying = setCharacterPlaying ?? throw new ArgumentNullException(nameof(setCharacterPlaying));
        _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _isPlaying = isPlaying ?? throw new ArgumentNullException(nameof(isPlaying));
        _startPlay = startPlay ?? throw new ArgumentNullException(nameof(startPlay));
        _stopPlay = stopPlay ?? throw new ArgumentNullException(nameof(stopPlay));
        _interact = interact ?? throw new ArgumentNullException(nameof(interact));
        _getInteractionVolume = getInteractionVolume ?? throw new ArgumentNullException(nameof(getInteractionVolume));
        _setInteractionVolume = setInteractionVolume ?? throw new ArgumentNullException(nameof(setInteractionVolume));
        _getSequenceInfo = getSequenceInfo ?? throw new ArgumentNullException(nameof(getSequenceInfo));
        _setSequencePlaying = setSequencePlaying ?? throw new ArgumentNullException(nameof(setSequencePlaying));
        _seekSequence = seekSequence ?? throw new ArgumentNullException(nameof(seekSequence));
        _setSequencePreviewEnabled = setSequencePreviewEnabled ?? throw new ArgumentNullException(nameof(setSequencePreviewEnabled));
        _getSequenceExportInfo = getSequenceExportInfo ?? throw new ArgumentNullException(nameof(getSequenceExportInfo));
        _startSequenceExport = startSequenceExport ?? throw new ArgumentNullException(nameof(startSequenceExport));
        _cancelSequenceExport = cancelSequenceExport ?? throw new ArgumentNullException(nameof(cancelSequenceExport));
        _saveSceneAs = saveSceneAs ?? throw new ArgumentNullException(nameof(saveSceneAs));
        _openWorldCell = openWorldCell ?? throw new ArgumentNullException(nameof(openWorldCell));
        _worldCellRenamed = worldCellRenamed ?? throw new ArgumentNullException(nameof(worldCellRenamed));
        _getCurrentScenePath = getCurrentScenePath ?? throw new ArgumentNullException(nameof(getCurrentScenePath));
        _captureRecovery = captureRecovery ?? throw new ArgumentNullException(nameof(captureRecovery));
        _applyRecovery = applyRecovery ?? throw new ArgumentNullException(nameof(applyRecovery));
        _startPathFollow = startPathFollow ?? throw new ArgumentNullException(nameof(startPathFollow));
        _getPathFollowStatus = getPathFollowStatus ?? throw new ArgumentNullException(nameof(getPathFollowStatus));
        _stopPathFollow = stopPathFollow ?? throw new ArgumentNullException(nameof(stopPathFollow));
        _createProject = createProject ?? throw new ArgumentNullException(nameof(createProject));
        _openProject = openProject ?? throw new ArgumentNullException(nameof(openProject));
        _importGlb = importGlb ?? throw new ArgumentNullException(nameof(importGlb));
        _getPendingAssetPreviewName = getPendingAssetPreviewName
            ?? throw new ArgumentNullException(nameof(getPendingAssetPreviewName));
        _acceptPendingAssetPreview = acceptPendingAssetPreview
            ?? throw new ArgumentNullException(nameof(acceptPendingAssetPreview));
        _cancelPendingAssetPreview = cancelPendingAssetPreview
            ?? throw new ArgumentNullException(nameof(cancelPendingAssetPreview));
        _getCurrentProjectPath = getCurrentProjectPath ?? throw new ArgumentNullException(nameof(getCurrentProjectPath));
        _getProjectAssetReferences = getProjectAssetReferences
            ?? throw new ArgumentNullException(nameof(getProjectAssetReferences));
        _refreshProjectAssetReferences = refreshProjectAssetReferences
            ?? throw new ArgumentNullException(nameof(refreshProjectAssetReferences));
        _previewProjectAsset = previewProjectAsset
            ?? throw new ArgumentNullException(nameof(previewProjectAsset));
        _reloadProjectAsset = reloadProjectAsset
            ?? throw new ArgumentNullException(nameof(reloadProjectAsset));
        _getRecentProjectPaths = getRecentProjectPaths ?? throw new ArgumentNullException(nameof(getRecentProjectPaths));
        _setDiagnosticsVisible = setDiagnosticsVisible ?? throw new ArgumentNullException(nameof(setDiagnosticsVisible));
        _confirmExit = confirmExit ?? throw new ArgumentNullException(nameof(confirmExit));
        _showHome = _getCurrentProjectPath() is null && _getCurrentScenePath() is null;
        _projectWorkspaceStatus = GetWorkspaceReadyMessage();
        LoadRpgPlacementContent();
        _context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(_context);
            ImGui.StyleColorsDark();
            _io = ImGui.GetIO();
            unsafe { _io.NativePtr->IniFilename = null; }
            _io.DisplaySize = new NumericsVector2(_logicalWidth, _logicalHeight);
            _renderer = new ImGuiMonoGameRenderer(device);
        }
        catch
        {
            ImGui.DestroyContext(_context);
            throw;
        }
    }

    public bool WantsMouse => _wantsMouse;
    public bool WantsKeyboard => _wantsKeyboard;
    public Guid? SelectedObjectId => _selectedObjectId;
    public bool IsMoveToolSelected => _transformTool == TransformTool.Move;
    public bool IsTurnToolSelected => _transformTool == TransformTool.Turn;
    public bool IsSizeToolSelected => _transformTool == TransformTool.Size;
    public bool IsHomeVisible => _showHome;
    public bool SnapMoveToGrid => _snapMoveToGrid;
    public float MoveGridStep => _moveGridStep;
    public bool SnapTurnToStep => _snapTurnToStep;
    public float TurnSnapDegrees => _turnSnapDegrees;
    public bool SnapSizeToStep => _snapSizeToStep;
    public float SizeSnapStep => _sizeSnapStep;

    public void SetHistory(SceneCommandHistory history) =>
        _history = history ?? throw new ArgumentNullException(nameof(history));

    public void CompletePendingEdit(SceneGraph scene)
    {
        CommitActiveTransformEdit(scene);
        CommitActivePlaySettingsEdit(scene);
    }

    public void CommitViewportTransform(SceneGraph scene, Guid objectId,
        Transform before, Transform after)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (scene.Find(objectId) is not { } item) return;
        var beforeCopy = SceneTransformCopy(before);
        var afterCopy = SceneTransformCopy(after);
        if (TransformsEqual(beforeCopy, afterCopy)) return;
        item.Transform = SceneTransformCopy(beforeCopy);
        _history.Execute(scene, new TransformEditCommand(objectId, beforeCopy, afterCopy));
        _projectWorkspaceStatus = $"Updated {item.Name} with the scene view. Undo is available.";
    }

    public void SelectObject(Guid objectId, Guid? assetId = null)
    {
        _selectedObjectId = objectId;
        if (assetId is not null) _selectedAssetId = assetId;
        _initialSelectionSet = true;
    }

    public void ClearObjectSelection()
    {
        _selectedObjectId = null;
        _initialSelectionSet = true;
    }

    public bool IsSceneViewportPoint(Vector2 logicalPosition)
    {
        if (_showHome || logicalPosition.X < 0f || logicalPosition.X >= _logicalWidth
            || logicalPosition.Y < 48f || logicalPosition.Y >= _logicalHeight)
            return false;

        if (_showAddLibrary && _logicalWidth >= 1_050 && logicalPosition.X < 220f)
            return false;
        var inspectorWidth = Math.Min(280f, _logicalWidth * 0.24f);
        return logicalPosition.X < _logicalWidth - inspectorWidth;
    }

    public void ResetSceneSelection()
    {
        _selectedObjectId = null;
        _selectedAssetId = null;
        _activeTransformObjectId = null;
        _activeTransformStart = null;
        _activeBoxColliderObjectId = null;
        _activeBoxColliderStart = null;
        _initialSelectionSet = false;
    }

    public void OnProjectOpened(string projectRoot, string? worldManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        CancelRecoveryReview();
        _projectWorkspaceStatus = GetWorkspaceReadyMessage();
        _worldManifest = null;
        _worldManifestPath = worldManifestPath is null
            ? Path.Combine(projectRoot, WorldManifest.DefaultFileName)
            : Path.GetFullPath(worldManifestPath);
        _worldStatus = "Open or create a world manifest.";
        if (worldManifestPath is not null) LoadWorldManifest();
        _travelSceneCache.Clear();
        _doorLinkStatuses.Clear();
        _recoveryStaging = null;
        _recoveryStatus = "No recovery snapshot reviewed.";
        _recoveryReport = string.Empty;
        _recoveryCanApply = false;
        _recoveryAutosaveElapsedSeconds = 0f;
        _selectedWorldCellId = null;
        _selectedTravelCellId = null;
        _selectedTravelSpawnId = null;
        _rpgContentPath = Path.Combine(projectRoot, "RpgContent.json");
        LoadRpgPlacementContent();
        ResetSceneSelection();
    }

    public void AddTextInput(char character)
    {
        if (_disposed || char.IsControl(character)) return;
        _io.AddInputCharacterUTF16(character);
    }

    public void Update(float elapsedSeconds, KeyboardState keyboard, MouseState mouse,
        Vector2 logicalMouse, SceneGraph scene, bool suppressMouseInput = false)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CharacterStudioEditorUi));
        PollRecoveryReview();
        var viewport = _renderer.Viewport;
        _io.DisplaySize = new NumericsVector2(_logicalWidth, _logicalHeight);
        var framebufferScale = MathF.Min(
            viewport.Width / (float)_logicalWidth,
            viewport.Height / (float)_logicalHeight);
        _letterboxOffsetX = (int)MathF.Floor((viewport.Width - _logicalWidth * framebufferScale) * 0.5f);
        _letterboxOffsetY = (int)MathF.Floor((viewport.Height - _logicalHeight * framebufferScale) * 0.5f);
        _io.DisplayFramebufferScale = new NumericsVector2(framebufferScale, framebufferScale);
        _io.DeltaTime = Math.Max(1f / 1000f, elapsedSeconds);
        _io.AddMousePosEvent(suppressMouseInput ? -float.MaxValue : logicalMouse.X,
            suppressMouseInput ? -float.MaxValue : logicalMouse.Y);
        _io.AddMouseButtonEvent(0, !suppressMouseInput && mouse.LeftButton == ButtonState.Pressed);
        _io.AddMouseButtonEvent(1, !suppressMouseInput && mouse.RightButton == ButtonState.Pressed);
        _io.AddMouseButtonEvent(2, !suppressMouseInput && mouse.MiddleButton == ButtonState.Pressed);
        var wheel = (mouse.ScrollWheelValue - _lastWheel) / 120f;
        if (!suppressMouseInput && wheel != 0f) _io.AddMouseWheelEvent(0f, wheel);
        _lastWheel = mouse.ScrollWheelValue;
        UpdateKeyboard(keyboard);
        MaybeAutosaveAuthoredProject(scene, elapsedSeconds);

        if (_startLessonAfterProjectCreate && !_showHome
            && _getCurrentProjectPath() is { } createdProjectPath)
        {
            BeginFirstCreationLesson(scene, createdProjectPath);
            _startLessonAfterProjectCreate = false;
        }

        if (_lessonProjectOpenCheck is { } reopenedProjectPath)
        {
            _lessonProjectOpenCheck = null;
            if (_firstCreationLesson is { } lesson)
            {
                var projectPath = _getCurrentProjectPath();
                var scenePath = _getCurrentScenePath();
                if (lesson.ObserveReopened(projectPath ?? string.Empty,
                    scenePath ?? string.Empty, scene))
                {
                    _showFirstCreationLesson = true;
                    try
                    {
                        var project = EngineProjectFile.Load(projectPath!);
                        ProjectLearningProgressStore.RecordCompletion(
                            project, "first-creation", scenePath!);
                        _projectWorkspaceStatus = "First creation completed. This project now remembers the lesson.";
                    }
                    catch (Exception exception)
                    {
                        _projectWorkspaceStatus = $"Guide completed, but lesson progress could not be saved: {exception.Message}";
                    }
                }
            }
        }

        if (_firstCreationLesson is { } activeLesson
            && IsSamePath(activeLesson.ProjectFilePath, _getCurrentProjectPath()))
        {
            activeLesson.ObserveScene(scene);
            activeLesson.ObservePlayback(_isPlaying());
        }
        RefreshLessonHelpForCurrentStep();

        ImGui.NewFrame();
        if (_showHome)
        {
            DrawHomeWorkspace();
        }
        else
        {
            DrawMainToolbar(scene);
            if (!_showHome)
            {
                DrawAddLibrary(scene);
                DrawPanel(scene);
                var toolMenuVisible = DrawMoreToolsMenu();
                if (!toolMenuVisible)
                {
                    if (_showSequenceTools) DrawSequencePanel();
                    if (_showWorldTools) DrawWorldCellPanel(scene);
                    if (_showRpgTools) DrawRpgAuthoringPanel(scene);
                    if (_showSceneTemplateTools) DrawSceneTemplatePanel(scene);
                }
            }
        }
        DrawUnsavedChangesDialog(scene);
        ImGui.Render();
        _wantsMouse = _io.WantCaptureMouse;
        _wantsKeyboard = _io.WantCaptureKeyboard;
    }

}

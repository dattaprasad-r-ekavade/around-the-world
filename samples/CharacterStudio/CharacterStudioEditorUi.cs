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
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

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
    private readonly Action _beforeStructureChange;
    private readonly Action _afterStructureChange;
    private readonly Func<Guid, CharacterEditorInfo?> _getCharacterInfo;
    private readonly Action<Guid, string> _selectCharacterClip;
    private readonly Action<Guid, float> _seekCharacter;
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
    private Guid? _activeTransformObjectId;
    private Transform? _activeTransformStart;
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
        SceneCommandHistory history, Action beforeStructureChange, Action afterStructureChange,
        Func<Guid, CharacterEditorInfo?> getCharacterInfo, Action<Guid, string> selectCharacterClip,
        Action<Guid, float> seekCharacter, Action<Guid, bool> setCharacterPlaying, SceneLighting lighting,
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
        Func<IReadOnlyList<string>> getRecentProjectPaths, Action<bool> setDiagnosticsVisible)
    {
        _logicalWidth = Math.Max(1, logicalWidth);
        _logicalHeight = Math.Max(1, logicalHeight);
        _windowHandle = device.PresentationParameters.DeviceWindowHandle;
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _beforeStructureChange = beforeStructureChange ?? throw new ArgumentNullException(nameof(beforeStructureChange));
        _afterStructureChange = afterStructureChange ?? throw new ArgumentNullException(nameof(afterStructureChange));
        _getCharacterInfo = getCharacterInfo ?? throw new ArgumentNullException(nameof(getCharacterInfo));
        _selectCharacterClip = selectCharacterClip ?? throw new ArgumentNullException(nameof(selectCharacterClip));
        _seekCharacter = seekCharacter ?? throw new ArgumentNullException(nameof(seekCharacter));
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

    public void CompletePendingEdit(SceneGraph scene) => CommitActiveTransformEdit(scene);

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
        _initialSelectionSet = false;
    }

    public void OnProjectOpened(string projectRoot, string? worldManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
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
                }
            }
        }
        ImGui.Render();
        _wantsMouse = _io.WantCaptureMouse;
        _wantsKeyboard = _io.WantCaptureKeyboard;
    }

    private void DrawHomeWorkspace()
    {
        ImGui.SetNextWindowPos(NumericsVector2.Zero);
        ImGui.SetNextWindowSize(_io.DisplaySize);
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        if (!ImGui.Begin("Ember Home", flags))
        {
            ImGui.End();
            return;
        }

        var background = ImGui.GetWindowDrawList();
        var windowPosition = ImGui.GetWindowPos();
        background.AddRectFilled(windowPosition, windowPosition + ImGui.GetWindowSize(),
            ImGui.GetColorU32(new NumericsVector4(0.025f, 0.035f, 0.05f, 1f)));

        var panelWidth = Math.Min(800f, Math.Max(360f, _logicalWidth - 48f));
        var panelHeight = Math.Min(510f, Math.Max(360f, _logicalHeight - 48f));
        ImGui.SetCursorPos(new NumericsVector2(
            Math.Max(24f, (_logicalWidth - panelWidth) * 0.5f),
            Math.Max(24f, (_logicalHeight - panelHeight) * 0.5f)));
        var showHomeChoices = ImGui.BeginChild("Home choices",
            new NumericsVector2(panelWidth, panelHeight), ImGuiChildFlags.Borders);
        if (showHomeChoices)
        {
            ImGui.Dummy(new NumericsVector2(0f, 12f));
            ImGui.Text("EMBER · MAKE SOMETHING REAL");
            ImGui.TextWrapped("Choose a starting point. You can change direction whenever you like.");
            ImGui.Separator();

            ImGui.Text("Name your project");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##homeProjectName", "Project name", ref _projectName, 128);
            ImGui.TextWrapped($"Save in: {_projectParentDirectory}");
            if (ImGui.Button("Choose a folder…"))
            {
                var selectedFolder = CharacterStudioFilePickers.PickProjectParent(
                    _projectParentDirectory, _windowHandle);
                if (selectedFolder is not null) _projectParentDirectory = selectedFolder;
            }
            ImGui.Dummy(new NumericsVector2(0f, 6f));
            ImGui.Checkbox("Guide me through my first creation", ref _guideNewProject);
            ImGui.TextDisabled("Optional: free creation always stays available.");
            ImGui.Dummy(new NumericsVector2(0f, 4f));

            var twoStarterColumns = panelWidth >= 620f;
            var cardWidth = twoStarterColumns
                ? Math.Max(140f, (panelWidth - 48f) * 0.5f)
                : Math.Max(140f, panelWidth - 24f);
            DrawStarterThumbnail(false, cardWidth, 72f);
            if (twoStarterColumns) ImGui.SameLine();
            DrawStarterThumbnail(true, cardWidth, 72f);
            if (ImGui.Button("Make a game\nCourtyard + two characters", new NumericsVector2(cardWidth, 72f)))
                CreateProjectFromHome(isFilm: false);
            if (twoStarterColumns) ImGui.SameLine();
            if (ImGui.Button("Make a film\nCourtyard + one animated character", new NumericsVector2(cardWidth, 72f)))
                CreateProjectFromHome(isFilm: true);

            ImGui.Dummy(new NumericsVector2(0f, 4f));
            if (ImGui.Button("Open a project…", new NumericsVector2(-1f, 38f)))
            {
                var projectFile = CharacterStudioFilePickers.PickProjectFile(
                    _projectParentDirectory, _windowHandle);
                if (projectFile is not null) OpenProjectFromHome(projectFile);
            }

            var recentProjects = _getRecentProjectPaths();
            if (recentProjects.Count > 0)
            {
                var recentLabel = recentProjects.Select(path => Path.GetFileName(Path.GetDirectoryName(path)))
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Recent projects";
                if (ImGui.BeginCombo("Recent projects", recentLabel))
                {
                    foreach (var projectPath in recentProjects)
                    {
                        var name = Path.GetFileName(Path.GetDirectoryName(projectPath));
                        if (ImGui.Selectable($"{name}##{projectPath}"))
                            OpenProjectFromHome(projectPath);
                    }
                    ImGui.EndCombo();
                }
            }

            if (_getCurrentProjectPath() is not null || _getCurrentScenePath() is not null)
            {
                if (ImGui.Button("Continue editing", new NumericsVector2(-1f, 38f))) _showHome = false;
            }
            if (!string.IsNullOrWhiteSpace(_projectWorkspaceStatus))
                ImGui.TextWrapped(_projectWorkspaceStatus);
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private static void DrawStarterThumbnail(bool isFilm, float width, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        static uint Color(float red, float green, float blue) =>
            ImGui.GetColorU32(new NumericsVector4(red, green, blue, 1f));

        var right = origin.X + width;
        var bottom = origin.Y + height;
        drawList.AddRectFilled(origin, new NumericsVector2(right, bottom), Color(0.08f, 0.12f, 0.16f));
        drawList.AddRectFilled(new NumericsVector2(origin.X + 8f, origin.Y + height * 0.62f),
            new NumericsVector2(right - 8f, bottom - 7f), Color(0.11f, 0.24f, 0.22f));
        drawList.AddRectFilled(new NumericsVector2(origin.X + 18f, origin.Y + 14f),
            new NumericsVector2(origin.X + width * 0.38f, origin.Y + height * 0.66f), Color(0.37f, 0.25f, 0.18f));
        drawList.AddRectFilled(new NumericsVector2(right - width * 0.38f, origin.Y + 20f),
            new NumericsVector2(right - 18f, origin.Y + height * 0.66f), Color(0.42f, 0.29f, 0.20f));

        var characterCount = isFilm ? 1 : 2;
        for (var index = 0; index < characterCount; index++)
        {
            var centerX = origin.X + width * (isFilm ? 0.52f : index == 0 ? 0.50f : 0.72f);
            var centerY = origin.Y + height * 0.55f;
            var foxColor = Color(0.88f, 0.48f, 0.13f);
            drawList.AddRectFilled(new NumericsVector2(centerX - 13f, centerY - 4f),
                new NumericsVector2(centerX + 12f, centerY + 8f), foxColor);
            drawList.AddCircleFilled(new NumericsVector2(centerX + 11f, centerY - 8f), 7f, foxColor, 12);
            drawList.AddRectFilled(new NumericsVector2(centerX + 12f, centerY - 17f),
                new NumericsVector2(centerX + 16f, centerY - 10f), foxColor);
            drawList.AddLine(new NumericsVector2(centerX - 7f, centerY + 7f),
                new NumericsVector2(centerX - 9f, centerY + 16f), foxColor, 3f);
            drawList.AddLine(new NumericsVector2(centerX + 5f, centerY + 7f),
                new NumericsVector2(centerX + 7f, centerY + 16f), foxColor, 3f);
        }

        if (isFilm)
        {
            var frameColor = Color(0.82f, 0.83f, 0.72f);
            drawList.AddLine(new NumericsVector2(origin.X + 28f, origin.Y + 8f),
                new NumericsVector2(right - 28f, origin.Y + 8f), frameColor, 2f);
            drawList.AddLine(new NumericsVector2(origin.X + 28f, origin.Y + 8f),
                new NumericsVector2(origin.X + 28f, bottom - 15f), frameColor, 2f);
            drawList.AddLine(new NumericsVector2(right - 28f, origin.Y + 8f),
                new NumericsVector2(right - 28f, bottom - 15f), frameColor, 2f);
            drawList.AddRectFilled(new NumericsVector2(origin.X + 8f, bottom - 5f),
                new NumericsVector2(origin.X + width * 0.62f, bottom - 2f), Color(0.75f, 0.31f, 0.20f));
        }

        ImGui.Dummy(new NumericsVector2(width, height));
    }

    private void CreateProjectFromHome(bool isFilm)
    {
        try
        {
            var projectName = _projectName.Trim();
            if (string.IsNullOrWhiteSpace(projectName) || projectName is "." or ".."
                || projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || projectName.EndsWith(".", StringComparison.Ordinal)
                || projectName.EndsWith(" ", StringComparison.Ordinal))
                throw new ArgumentException("Choose a project name that can be used as a folder name.");

            var projectDirectory = Path.Combine(_projectParentDirectory, projectName);
            _projectWorkspaceStatus = _createProject(projectDirectory, isFilm);
            _projectName = projectName;
            _showSequenceTools = false;
            _startLessonAfterProjectCreate = _guideNewProject;
            _showHome = false;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not create the project: {exception.Message}";
        }
    }

    private void OpenProjectFromHome(string path)
    {
        try
        {
            _projectWorkspaceStatus = _openProject(path);
            _lessonProjectOpenCheck = path;
            _showHome = false;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not open the project: {exception.Message}";
        }
    }

    private string GetWorkspaceReadyMessage()
    {
        var projectPath = _getCurrentProjectPath();
        if (projectPath is not null)
        {
            var readyMessage = $"Ready to edit {Path.GetFileName(Path.GetDirectoryName(projectPath))}. Select an object to change it, or use Add to place something new.";
            try
            {
                var completion = ProjectLearningProgressStore.Load(EngineProjectFile.Load(projectPath))
                    .FirstOrDefault(item => string.Equals(item.LessonId, "first-creation", StringComparison.Ordinal));
                if (completion is not null)
                    return $"{readyMessage} First creation completed {completion.CompletionCount} time(s); choose First creation to replay it.";
            }
            catch (Exception exception)
            {
                return $"{readyMessage} Saved lesson progress could not be read: {exception.Message}";
            }
            return readyMessage;
        }
        if (_getCurrentScenePath() is not null)
            return "This scene is open on its own. Create or open a project to add models and keep your work together.";
        return "Choose Make a game or Make a film to create a project.";
    }

    private void BeginFirstCreationLesson(SceneGraph scene, string projectFilePath)
    {
        _firstCreationLesson = new FirstCreationLesson(scene, projectFilePath);
        _showFirstCreationLesson = true;
        _showLessonWhy = false;
        _lessonHintLevel = 0;
        _lastObservedLessonStep = null;
        _showAddLibrary = true;
        _showHome = false;
    }

    private void RefreshLessonHelpForCurrentStep()
    {
        if (_firstCreationLesson is not { } lesson || _lastObservedLessonStep == lesson.Step) return;
        _lastObservedLessonStep = lesson.Step;
        _showLessonWhy = false;
        _lessonHintLevel = 0;
    }

    private static bool IsSamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DrawMainToolbar(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(NumericsVector2.Zero);
        ImGui.SetNextWindowSize(new NumericsVector2(_logicalWidth, 48f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        if (!ImGui.Begin("Main toolbar", flags))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Home"))
        {
            CommitActiveTransformEdit(scene);
            if (_getPendingAssetPreviewName() is not null)
            {
                _cancelPendingAssetPreview();
                _projectWorkspaceStatus = "Model preview canceled; the asset was not added to the scene.";
            }
            _showHome = true;
        }
        ImGui.SameLine();
        var scenePath = _getCurrentScenePath();
        var canSave = scenePath is not null && !_isPlaying();
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button("Save") && scenePath is not null)
        {
            try
            {
                _saveSceneAs(scenePath);
                if (_firstCreationLesson is { } lesson
                    && IsSamePath(lesson.ProjectFilePath, _getCurrentProjectPath()))
                    lesson.ObserveSaved(_getCurrentProjectPath()!, scenePath, scene);
                _projectWorkspaceStatus = $"Saved {Path.GetFileName(scenePath)}.";
            }
            catch (Exception exception)
            {
                _projectWorkspaceStatus = $"Could not save the scene: {exception.Message}";
            }
        }
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_history.CanUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) RunHistoryAction(scene, undo: true);
        if (!_history.CanUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_history.CanRedo) ImGui.BeginDisabled();
        if (ImGui.Button("Redo")) RunHistoryAction(scene, undo: false);
        if (!_history.CanRedo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(_isPlaying() ? "Stop" : "Play"))
        {
            if (_isPlaying()) _stopPlay();
            else _startPlay();
        }
        ImGui.SameLine();
        var guideProjectPath = _getCurrentProjectPath();
        var currentLesson = _firstCreationLesson;
        var lessonMatchesProject = currentLesson is not null
            && IsSamePath(currentLesson.ProjectFilePath, guideProjectPath);
        var guideLabel = lessonMatchesProject && currentLesson is not null
            ? currentLesson.Step == FirstCreationLessonStep.Complete
                ? "Replay guide"
                : _showFirstCreationLesson ? "Hide guide" : "Show guide"
            : "First creation";
        if (guideProjectPath is null) ImGui.BeginDisabled();
        if (ImGui.Button(guideLabel) && guideProjectPath is not null)
        {
            if (lessonMatchesProject && currentLesson is not null
                && currentLesson.Step != FirstCreationLessonStep.Complete)
            {
                _showFirstCreationLesson = !_showFirstCreationLesson;
                if (_showFirstCreationLesson) _showAddLibrary = true;
            }
            else
            {
                BeginFirstCreationLesson(scene, guideProjectPath);
            }
        }
        if (guideProjectPath is null) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Animate"))
        {
            _showToolMenu = false;
            _showSequenceTools = true;
            _showWorldTools = false;
            _showRpgTools = false;
            if (_getSequenceInfo() is null)
                _projectWorkspaceStatus = "Add an animated character to this scene to create a film sequence.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Finish"))
        {
            _showToolMenu = false;
            _showSequenceTools = true;
            _showWorldTools = false;
            _showRpgTools = false;
            if (_getSequenceInfo() is null)
                _projectWorkspaceStatus = "A film sequence is needed before frames can be exported.";
        }
        ImGui.SameLine();
        if (ImGui.Button(_showAddLibrary ? "Hide Add" : "Add")) _showAddLibrary = !_showAddLibrary;
        ImGui.SameLine();
        if (ImGui.Button(_showToolMenu ? "Close tools" : "More tools"))
        {
            _showToolMenu = !_showToolMenu;
            if (_showToolMenu)
                _showSequenceTools = _showWorldTools = _showRpgTools = false;
        }

        var currentProject = _getCurrentProjectPath();
        if (currentProject is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Path.GetFileName(Path.GetDirectoryName(currentProject)));
        }
        ImGui.End();
    }

    private bool DrawMoreToolsMenu()
    {
        if (!_showToolMenu) return false;
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(220f, _logicalWidth), 196f));
        if (!ImGui.Begin("More tools", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return true;
        }

        var showSequenceTools = _showSequenceTools;
        if (ImGui.Checkbox("Animate and Finish", ref showSequenceTools))
        {
            _showSequenceTools = showSequenceTools;
            if (showSequenceTools) _showWorldTools = _showRpgTools = false;
            _showToolMenu = false;
        }
        var showWorldTools = _showWorldTools;
        if (ImGui.Checkbox("World Cells", ref showWorldTools))
        {
            _showWorldTools = showWorldTools;
            if (showWorldTools) _showSequenceTools = _showRpgTools = false;
            _showToolMenu = false;
        }
        var showRpgTools = _showRpgTools;
        if (ImGui.Checkbox("RPG authoring", ref showRpgTools))
        {
            _showRpgTools = showRpgTools;
            if (showRpgTools) _showSequenceTools = _showWorldTools = false;
            _showToolMenu = false;
        }
        if (ImGui.Checkbox("Performance details", ref _showDiagnostics))
            _setDiagnosticsVisible(_showDiagnostics);
        ImGui.Separator();
        if (ImGui.Button("Reset workspace layout"))
        {
            _showAddLibrary = true;
            _showToolMenu = false;
            _showSequenceTools = false;
            _showWorldTools = false;
            _showRpgTools = false;
            _showDiagnostics = false;
            _setDiagnosticsVisible(false);
        }
        ImGui.End();
        return true;
    }

    private void DrawAddLibrary(SceneGraph scene)
    {
        if (!_showAddLibrary || _logicalWidth < 1_050) return;
        const float libraryWidth = 220f;
        ImGui.SetNextWindowPos(new NumericsVector2(0f, 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(libraryWidth, Math.Max(160f, _logicalHeight - 48f)));
        if (!ImGui.Begin("Add to scene", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Add empty object", new NumericsVector2(-1f, 34f))) CreateEmpty(scene);
        ImGui.Separator();
        ImGui.Text("Add a model");
        var canImport = _getCurrentProjectPath() is not null;
        if (!canImport) ImGui.BeginDisabled();
        var pendingPreviewName = _getPendingAssetPreviewName();
        if (pendingPreviewName is not null)
        {
            ImGui.TextWrapped($"Previewing {pendingPreviewName}. It is not in the scene yet.");
            if (ImGui.Button("Add to scene", new NumericsVector2(-1f, 34f)))
            {
                try { _projectWorkspaceStatus = _acceptPendingAssetPreview(); }
                catch (Exception exception) { _projectWorkspaceStatus = $"Could not add the model: {exception.Message}"; }
            }
            if (ImGui.Button("Cancel preview", new NumericsVector2(-1f, 30f)))
            {
                try
                {
                    _cancelPendingAssetPreview();
                    _projectWorkspaceStatus = "Model preview canceled; the asset was not added to the scene.";
                }
                catch (Exception exception) { _projectWorkspaceStatus = $"Could not cancel the preview: {exception.Message}"; }
            }
        }
        if (ImGui.Button("Browse for a model…", new NumericsVector2(-1f, 34f))) BrowseForModel();
        if (!canImport) ImGui.EndDisabled();
        if (!canImport) ImGui.TextWrapped("Create or open a project before adding a model.");

        ImGui.Separator();
        ImGui.Text("Models in this project");
        if (ImGui.SmallButton("Refresh models")) _refreshProjectAssetReferences();

        var availableAssets = GetAvailableAssets(scene);
        if (_selectedAssetId is null || availableAssets.All(asset => asset.AssetId != _selectedAssetId))
            _selectedAssetId = availableAssets.FirstOrDefault()?.AssetId;
        var lessonVisibleForProject = _showFirstCreationLesson
            && _firstCreationLesson is { } activeLesson
            && IsSamePath(activeLesson.ProjectFilePath, _getCurrentProjectPath());
        var modelListHeight = Math.Max(64f, _logicalHeight * (lessonVisibleForProject ? 0.15f : 0.2f));
        ImGui.BeginChild("Scene assets", new NumericsVector2(0f, modelListHeight), ImGuiChildFlags.Borders);
        if (availableAssets.Length == 0)
        {
            if (pendingPreviewName is not null)
                ImGui.TextWrapped($"Previewing {pendingPreviewName}. Add it above to place it in the scene.");
            else
                ImGui.TextWrapped("No models are in this project yet. Browse above to import one.");
        }
        var sceneAssetPaths = GetSceneAssets(scene)
            .Select(asset => asset.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in availableAssets)
        {
            var usage = sceneAssetPaths.Contains(asset.SourcePath) ? " · In scene" : " · In project";
            var label = $"{Path.GetFileName(asset.SourcePath)}{usage}##asset-{asset.AssetId:N}";
            if (ImGui.Selectable(label, _selectedAssetId == asset.AssetId))
                _selectedAssetId = asset.AssetId;
        }
        ImGui.EndChild();
        var selectedAsset = availableAssets.FirstOrDefault(asset => asset.AssetId == _selectedAssetId);
        var canPreviewSelectedAsset = selectedAsset is not null && _getCurrentProjectPath() is not null;
        if (!canPreviewSelectedAsset) ImGui.BeginDisabled();
        if (ImGui.Button("Preview selected model", new NumericsVector2(-1f, 34f))
            && canPreviewSelectedAsset && selectedAsset is not null)
        {
            try { _projectWorkspaceStatus = _previewProjectAsset(selectedAsset); }
            catch (Exception exception) { _projectWorkspaceStatus = $"Could not preview the model: {exception.Message}"; }
        }
        if (!canPreviewSelectedAsset) ImGui.EndDisabled();
        if (selectedAsset is not null && !canPreviewSelectedAsset)
            ImGui.TextWrapped("Create or open a project to preview and place a model.");
        var canReloadSelectedAsset = selectedAsset is not null && _getCurrentProjectPath() is not null
            && pendingPreviewName is null;
        if (!canReloadSelectedAsset) ImGui.BeginDisabled();
        if (ImGui.Button("Reload selected model", new NumericsVector2(-1f, 30f))
            && canReloadSelectedAsset && selectedAsset is not null)
        {
            try { _projectWorkspaceStatus = _reloadProjectAsset(selectedAsset); }
            catch (Exception exception) { _projectWorkspaceStatus = $"Could not reload the model: {exception.Message}"; }
        }
        if (!canReloadSelectedAsset) ImGui.EndDisabled();
        if (pendingPreviewName is not null)
            ImGui.TextWrapped("Add or cancel the current preview before reloading a project model.");
        else if (canReloadSelectedAsset)
            ImGui.TextWrapped("Reloads from project files. A failed reload keeps the current scene preview.");
        if (!string.IsNullOrWhiteSpace(_projectWorkspaceStatus)
            && !_projectWorkspaceStatus.StartsWith("Could not", StringComparison.OrdinalIgnoreCase))
            ImGui.TextWrapped(_projectWorkspaceStatus);
        DrawFirstCreationLesson(scene);
        ImGui.End();
    }

    private void DrawFirstCreationLesson(SceneGraph scene)
    {
        if (!_showFirstCreationLesson || _firstCreationLesson is not { } lesson
            || !IsSamePath(lesson.ProjectFilePath, _getCurrentProjectPath())) return;

        ImGui.Separator();
        ImGui.TextDisabled($"FIRST CREATION · {lesson.StepNumber}/{FirstCreationLesson.TotalSteps}");
        ImGui.Text(lesson.Title);
        ImGui.TextWrapped(lesson.Explanation);
        if (!string.IsNullOrWhiteSpace(lesson.Feedback))
            ImGui.TextWrapped(lesson.Feedback);

        switch (lesson.Step)
        {
            case FirstCreationLessonStep.PredictPlay:
                if (ImGui.Button("The saved scene changes", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayPrediction(savedSceneChanges: true);
                if (ImGui.Button("Play uses a temporary copy", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayPrediction(savedSceneChanges: false);
                break;
            case FirstCreationLessonStep.Reflect:
                if (ImGui.Button("My authored scene stayed the same", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayReflection(authoredSceneStayedUnchanged: true);
                if (ImGui.Button("Play edits were saved", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayReflection(authoredSceneStayedUnchanged: false);
                break;
            case FirstCreationLessonStep.Reopen:
                if (ImGui.Button("Reopen this project", new NumericsVector2(-1f, 30f)))
                    ReopenFirstCreationProject(lesson);
                break;
            case FirstCreationLessonStep.Complete:
                if (ImGui.Button("Replay guide", new NumericsVector2(-1f, 28f)))
                    BeginFirstCreationLesson(scene, lesson.ProjectFilePath);
                break;
        }

        if (lesson.Step != FirstCreationLessonStep.Complete)
        {
            if (ImGui.SmallButton(_showLessonWhy ? "Hide Why?" : "Why?"))
                _showLessonWhy = !_showLessonWhy;
            ImGui.SameLine();
            var hintButton = _lessonHintLevel switch
            {
                0 => "Show a hint",
                1 => "More help",
                _ => "Hide hints"
            };
            if (ImGui.SmallButton(hintButton))
                _lessonHintLevel = _lessonHintLevel switch { 0 => 1, 1 => 2, _ => 0 };
            ImGui.SameLine();
            if (ImGui.SmallButton("Skip guide")) _showFirstCreationLesson = false;
            if (_showLessonWhy) ImGui.TextWrapped(lesson.Why);
            if (_lessonHintLevel > 0) ImGui.TextWrapped(lesson.Hint);
            if (_lessonHintLevel > 1) ImGui.TextWrapped(lesson.MoreSpecificHint);
        }
        else if (ImGui.SmallButton("Hide guide"))
        {
            _showFirstCreationLesson = false;
        }
    }

    private void ReopenFirstCreationProject(FirstCreationLesson lesson)
    {
        try
        {
            _projectWorkspaceStatus = _openProject(lesson.ProjectFilePath);
            _lessonProjectOpenCheck = lesson.ProjectFilePath;
            _showHome = false;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not reopen the project: {exception.Message}";
        }
    }

    private GltfAssetReference[] GetAvailableAssets(SceneGraph scene)
    {
        var byPath = new Dictionary<string, GltfAssetReference>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var asset in _getProjectAssetReferences())
                byPath[asset.SourcePath] = asset;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not list project models: {exception.Message}";
        }

        foreach (var asset in GetSceneAssets(scene))
            byPath[asset.SourcePath] = asset;
        return byPath.Values.OrderBy(asset => asset.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static GltfAssetReference[] GetSceneAssets(SceneGraph scene) => scene.Objects
        .SelectMany(GetAssetReferences)
        .GroupBy(asset => asset.SourcePath, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();

    private static IEnumerable<GltfAssetReference> GetAssetReferences(SceneObject item)
    {
        if (item.GltfAsset is { } asset) yield return asset;
        if (item.StaticMeshLod is { } lod)
        {
            yield return lod.NearAsset;
            yield return lod.FarAsset;
        }
    }

    private void BrowseForModel()
    {
        var projectPath = _getCurrentProjectPath();
        if (projectPath is null)
        {
            _projectWorkspaceStatus = "Create or open a project before adding a model.";
            return;
        }

        var modelPath = CharacterStudioFilePickers.PickGltfFile(
            Path.GetDirectoryName(projectPath), _windowHandle);
        if (modelPath is null) return;

        try
        {
            _projectWorkspaceStatus = _importGlb(modelPath);
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not add the model: {exception.Message}";
        }
    }

    private void DrawRpgAuthoringPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _logicalWidth), Math.Max(180f, _logicalHeight - 64f)));
        if (!ImGui.Begin("RPG Authoring", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("RPG authoring tabs"))
        {
            if (ImGui.BeginTabItem("Placement"))
            {
                DrawRpgPlacementTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Templates"))
            {
                DrawPlacementTemplatesTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Travel"))
            {
                DrawWorldTravelTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Paths"))
            {
                DrawPathAuthoringTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Dialogue"))
            {
                DrawDialogueAuthoringTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Quests"))
            {
                DrawQuestAuthoringTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void DrawRpgPlacementTab(SceneGraph scene)
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##rpgContentPath", "Path to registered RPG definitions", ref _rpgContentPath, 1024);
        if (ImGui.Button("Load definitions")) LoadRpgPlacementContent();
        ImGui.SameLine();
        ImGui.TextDisabled(_rpgContent is null ? "No definitions loaded" :
            $"{_rpgContent.Actors.Count} actors · {_rpgContent.Items.Count} items");

        if (_rpgContent is not null)
        {
            var options = GetRpgPlacementOptions();
            ImGui.BeginChild("RPG definition list", new NumericsVector2(0f, 112f), ImGuiChildFlags.Borders);
            foreach (var option in options)
            {
                var key = RpgPlacementKey(option);
                if (ImGui.Selectable($"{option.Name} · {option.Kind}##{key}",
                    string.Equals(_selectedRpgDefinitionKey, key, StringComparison.Ordinal)))
                    _selectedRpgDefinitionKey = key;
            }
            ImGui.EndChild();

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputFloat3("Position", ref _rpgPlacementPosition);
            var selected = options.FirstOrDefault(option =>
                string.Equals(RpgPlacementKey(option), _selectedRpgDefinitionKey, StringComparison.Ordinal));
            var canPlace = selected is not null && !_isPlaying();
            if (!canPlace) ImGui.BeginDisabled();
            if (ImGui.Button("Place selected definition")) PlaceRpgDefinition(scene, selected!);
            if (!canPlace) ImGui.EndDisabled();
        }

        ImGui.TextWrapped(_rpgPlacementStatus);
    }

    private IReadOnlyList<RpgPlacementOption> GetRpgPlacementOptions()
    {
        if (_rpgContent is null) return Array.Empty<RpgPlacementOption>();
        return _rpgContent.Actors.All.Values
            .Select(actor => new RpgPlacementOption(WorldEntityKind.Actor, actor.Id.Value, actor.Name))
            .Concat(_rpgContent.Items.All.Values
                .Select(item => new RpgPlacementOption(WorldEntityKind.Item, item.Id.Value, item.Name)))
            .OrderBy(option => option.Kind)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string RpgPlacementKey(RpgPlacementOption option) => $"{option.Kind}:{option.Id}";

    private void LoadRpgPlacementContent()
    {
        _rpgContent = null;
        _selectedRpgDefinitionKey = null;
        try
        {
            var parsed = RpgContentJson.ParseForValidation(File.ReadAllText(_rpgContentPath));
            _rpgContent = parsed.Content;
            _rpgPlacementStatus = parsed.Diagnostics.Count == 0
                ? $"Loaded definitions from {Path.GetFileName(_rpgContentPath)}."
                : $"Loaded draft from {Path.GetFileName(_rpgContentPath)} with {parsed.Diagnostics.Count} validation issue(s).";
        }
        catch (Exception exception)
        {
            _rpgPlacementStatus = $"Could not load definitions: {exception.Message}";
        }
    }

    private void PlaceRpgDefinition(SceneGraph scene, RpgPlacementOption option)
    {
        try
        {
            var placement = SceneObjectFactory.CreateWorldEntityPlacement(scene, option.Kind, option.Id,
                option.Name, new Vector3(_rpgPlacementPosition.X, _rpgPlacementPosition.Y, _rpgPlacementPosition.Z));
            CommitActiveTransformEdit(scene);
            RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(placement)));
            _selectedObjectId = placement.Id;
            _rpgPlacementStatus = $"Placed {option.Kind.ToString().ToLowerInvariant()} '{option.Name}' " +
                $"with instance ID {placement.WorldEntity!.InstanceId}.";
        }
        catch (Exception exception)
        {
            _rpgPlacementStatus = $"Could not place definition: {exception.Message}";
        }
    }

    private void DrawWorldTravelTab(SceneGraph scene)
    {
        var activeCell = FindCurrentWorldCell();
        if (_worldManifest is null)
        {
            ImGui.TextWrapped("Open a world manifest in World Cells before adding spawn markers or door links.");
            ImGui.TextWrapped(_travelStatus);
            return;
        }

        if (_selectedTravelCellId is null || _worldManifest.FindCell(_selectedTravelCellId.Value) is null)
            _selectedTravelCellId = activeCell?.Id;
        ImGui.TextDisabled(activeCell is null
            ? "Save/open a scene that belongs to this world to edit its links."
            : $"Active cell: {WorldCellWorkspace.GetCellName(activeCell)}");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputFloat3("Spawn position", ref _spawnMarkerPosition);
        var canAddSpawn = activeCell is not null && !_isPlaying();
        if (!canAddSpawn) ImGui.BeginDisabled();
        if (ImGui.Button("Add spawn marker to active cell")) AddSpawnMarker(scene);
        if (!canAddSpawn) ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.Text("Destination cell");
        ImGui.BeginChild("Travel destination cells", new NumericsVector2(0f, 70f), ImGuiChildFlags.Borders);
        foreach (var cell in _worldManifest.Cells)
        {
            var location = cell.ExteriorCoordinate is { } coordinate
                ? $"Exterior ({coordinate.X}, {coordinate.Z})"
                : "Interior";
            if (ImGui.Selectable($"{WorldCellWorkspace.GetCellName(cell)} · {location}##travel-{cell.Id:N}",
                _selectedTravelCellId == cell.Id))
            {
                _selectedTravelCellId = cell.Id;
                _selectedTravelSpawnId = null;
                _travelStatus = $"Selected {WorldCellWorkspace.GetCellName(cell)}.";
            }
        }
        ImGui.EndChild();

        var targetCell = _selectedTravelCellId is { } targetId ? _worldManifest.FindCell(targetId) : null;
        SceneGraph? targetScene = null;
        if (targetCell is not null)
        {
            try { targetScene = LoadTravelScene(targetCell, scene, activeCell); }
            catch (Exception exception) { _travelStatus = $"Could not open destination scene: {exception.Message}"; }
        }

        ImGui.Text("Destination spawn");
        ImGui.BeginChild("Travel destination spawns", new NumericsVector2(0f, 62f), ImGuiChildFlags.Borders);
        if (targetScene is not null)
        {
            foreach (var marker in targetScene.Objects.Where(item => item.SpawnPoint is not null)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable($"{marker.Name} · {marker.SpawnPoint!.Id:N}##spawn-{marker.Id:N}",
                    _selectedTravelSpawnId == marker.SpawnPoint.Id))
                    _selectedTravelSpawnId = marker.SpawnPoint.Id;
            }
        }
        ImGui.EndChild();

        var selectedObject = _selectedObjectId is { } objectId ? scene.Find(objectId) : null;
        var selectedSpawn = targetScene?.Objects.FirstOrDefault(item => item.SpawnPoint?.Id == _selectedTravelSpawnId);
        var canLink = !_isPlaying() && activeCell is not null && selectedObject is not null && targetCell is not null
            && selectedSpawn?.SpawnPoint is not null;
        if (!canLink) ImGui.BeginDisabled();
        if (ImGui.Button(selectedObject?.Door is null ? "Link selected object to spawn" : "Update selected door link"))
            ApplyDoorLink(scene, selectedObject!, targetCell!, targetScene!, selectedSpawn!);
        if (!canLink) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Check door links")) CheckDoorLinks(scene, activeCell);

        ImGui.BeginChild("Door link statuses", new NumericsVector2(0f, 52f), ImGuiChildFlags.Borders);
        foreach (var door in scene.Objects.Where(item => item.Door is not null).OrderBy(item => item.Name,
            StringComparer.OrdinalIgnoreCase))
        {
            var status = _doorLinkStatuses.GetValueOrDefault(door.Id, "Not checked");
            var color = status.StartsWith("Broken:", StringComparison.Ordinal)
                ? new NumericsVector4(1f, 0.35f, 0.3f, 1f)
                : status == "Not checked" ? new NumericsVector4(0.75f, 0.75f, 0.75f, 1f)
                : new NumericsVector4(0.4f, 0.9f, 0.5f, 1f);
            ImGui.TextColored(color, $"{door.Name}: {status}");
        }
        ImGui.EndChild();

        ImGui.TextWrapped(_travelStatus);
    }

    private void AddSpawnMarker(SceneGraph scene)
    {
        try
        {
            var marker = SceneObjectFactory.CreateSpawnMarker(scene, "Spawn",
                new Vector3(_spawnMarkerPosition.X, _spawnMarkerPosition.Y, _spawnMarkerPosition.Z));
            CommitActiveTransformEdit(scene);
            RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(marker)));
            _selectedObjectId = marker.Id;
            _selectedTravelSpawnId = marker.SpawnPoint!.Id;
            _travelStatus = $"Added spawn '{marker.Name}' with stable ID {marker.SpawnPoint.Id}.";
        }
        catch (Exception exception)
        {
            _travelStatus = $"Could not add spawn marker: {exception.Message}";
        }
    }

    private void ApplyDoorLink(SceneGraph scene, SceneObject doorObject, WorldCellDefinition targetCell,
        SceneGraph targetScene, SceneObject spawnObject)
    {
        try
        {
            var replacement = new WorldDoorComponent(targetCell.Id, spawnObject.SpawnPoint!.Id,
                doorObject.Door?.Facing ?? Quaternion.Identity);
            var destination = WorldTravelValidator.ResolveDestination(_worldManifest!,
                new Dictionary<Guid, SceneGraph> { [targetCell.Id] = targetScene }, replacement);
            CommitActiveTransformEdit(scene);
            RunStructureChange(scene, () => _history.Execute(scene,
                new WorldDoorEditCommand(doorObject.Id, replacement)));
            _doorLinkStatuses[doorObject.Id] = $"Valid → {destination.Position.X:0.##}, {destination.Position.Y:0.##}, {destination.Position.Z:0.##}";
            _travelStatus = $"Linked '{doorObject.Name}' to {WorldCellWorkspace.GetCellName(targetCell)} / {spawnObject.Name}.";
        }
        catch (Exception exception)
        {
            _travelStatus = $"Could not create door link: {exception.Message}";
        }
    }

    private void CheckDoorLinks(SceneGraph scene, WorldCellDefinition? activeCell)
    {
        _doorLinkStatuses.Clear();
        if (_worldManifest is null) return;
        foreach (var item in scene.Objects.Where(item => item.Door is not null))
        {
            try
            {
                var door = item.Door!;
                var targetCell = _worldManifest.FindCell(door.DestinationCellId)
                    ?? throw new InvalidDataException($"Unknown cell {door.DestinationCellId}.");
                var targetScene = LoadTravelScene(targetCell, scene, activeCell);
                var destination = WorldTravelValidator.ResolveDestination(_worldManifest,
                    new Dictionary<Guid, SceneGraph> { [targetCell.Id] = targetScene }, door);
                _doorLinkStatuses[item.Id] = $"Valid → {destination.Position.X:0.##}, {destination.Position.Y:0.##}, {destination.Position.Z:0.##}";
            }
            catch (Exception exception)
            {
                _doorLinkStatuses[item.Id] = $"Broken: {exception.Message}";
            }
        }
        _travelStatus = $"Checked {_doorLinkStatuses.Count} door link(s).";
    }

    private WorldCellDefinition? FindCurrentWorldCell()
    {
        if (_worldManifest is null || string.IsNullOrWhiteSpace(_getCurrentScenePath())) return null;
        string currentPath;
        try { currentPath = Path.GetFullPath(_getCurrentScenePath()!); }
        catch { return null; }
        foreach (var cell in _worldManifest.Cells)
        {
            var scenePath = _worldManifest.ResolveScenePath(cell.Id);
            if (string.Equals(currentPath, Path.GetFullPath(scenePath), StringComparison.OrdinalIgnoreCase))
                return cell;
        }
        return null;
    }

    private SceneGraph LoadTravelScene(WorldCellDefinition cell, SceneGraph currentScene,
        WorldCellDefinition? activeCell)
    {
        if (activeCell?.Id == cell.Id) return currentScene;
        var path = _worldManifest!.ResolveScenePath(cell.Id);
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (_travelSceneCache.TryGetValue(cell.Id, out var cached)
            && string.Equals(cached.Path, path, StringComparison.OrdinalIgnoreCase)
            && cached.LastWriteUtc == lastWriteUtc)
            return cached.Scene;
        var loaded = SceneFile.Load(path);
        _travelSceneCache[cell.Id] = (path, lastWriteUtc, loaded);
        return loaded;
    }

    private void DrawWorldCellPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(380f, _logicalWidth), Math.Max(180f, _logicalHeight - 64f)));
        if (!ImGui.Begin("World Cells", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##worldManifestPath", "Path to world.json", ref _worldManifestPath, 1024);
        if (ImGui.Button("Open manifest")) LoadWorldManifest();
        ImGui.SameLine();
        if (ImGui.Button("Create world")) CreateWorldManifest();
        if (ImGui.Button("Validate project")) ValidateAuthoredProject();
        DrawProjectValidationReport();
        ImGui.Separator();
        ImGui.Text("Authored recovery");
        if (ImGui.Button("Autosave now"))
        {
            try
            {
                _recoveryStatus = _captureRecovery(scene, _worldManifestPath, _rpgContentPath,
                    _getCurrentScenePath(), _rpgContent);
            }
            catch (Exception exception)
            {
                _recoveryStatus = $"Autosave failed: {exception.Message}";
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Review recovery")) ReviewRecovery();
        ImGui.TextWrapped(_recoveryStatus);
        if (_recoveryStaging is not null)
        {
            ImGui.TextWrapped($"Reviewed snapshot {_recoveryStaging.SnapshotId:N} in staging.");
            ImGui.BeginDisabled(!_recoveryCanApply);
            if (ImGui.Button("Apply reviewed recovery"))
            {
                try
                {
                    _recoveryStatus = _applyRecovery(_recoveryStaging);
                    _recoveryStaging = null;
                    _recoveryReport = string.Empty;
                    _recoveryCanApply = false;
                }
                catch (Exception exception)
                {
                    _recoveryStatus = $"Recovery apply failed: {exception.Message}";
                }
            }
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(_recoveryReport))
            {
                ImGui.BeginChild("Recovery validation report", new NumericsVector2(0f, 110f), ImGuiChildFlags.Borders);
                foreach (var line in _recoveryReport.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    ImGui.TextWrapped(line);
                ImGui.EndChild();
            }
        }
        ImGui.Text("Exterior cell width (used for new worlds)");
        if (_worldManifest is not null) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputFloat("##exteriorCellWidth", ref _exteriorCellWidth, 1f, 8f, "%.1f");
        if (_worldManifest is not null) ImGui.EndDisabled();

        if (_worldManifest is not null)
        {
            ImGui.TextDisabled($"{_worldManifest.Cells.Count} cells · {Path.GetFileName(_worldManifest.FilePath)}");
            ImGui.BeginChild("World cell list", new NumericsVector2(0f, 145f), ImGuiChildFlags.Borders);
            foreach (var cell in _worldManifest.Cells)
            {
                var location = cell.ExteriorCoordinate is { } coordinate
                    ? $"({coordinate.X}, {coordinate.Z})"
                    : "interior";
                var label = $"{WorldCellWorkspace.GetCellName(cell)} · {location}##{cell.Id:N}";
                if (ImGui.Selectable(label, _selectedWorldCellId == cell.Id))
                {
                    _selectedWorldCellId = cell.Id;
                    _renameCellName = WorldCellWorkspace.GetCellName(cell);
                }
            }
            ImGui.EndChild();

            var selectedCell = _selectedWorldCellId is { } selectedId
                ? _worldManifest.FindCell(selectedId)
                : null;
            if (selectedCell is null) ImGui.BeginDisabled();
            if (ImGui.Button("Open selected cell"))
            {
                var error = _openWorldCell(_worldManifest.ResolveScenePath(selectedCell!.Id),
                    _worldManifest.RootDirectory);
                _worldStatus = error ?? $"Opened {WorldCellWorkspace.GetCellName(selectedCell)}.";
            }
            if (selectedCell is null) ImGui.EndDisabled();

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##renameCellName", "New cell name", ref _renameCellName, 128);
            if (selectedCell is null) ImGui.BeginDisabled();
            if (ImGui.Button("Rename selected")) RenameSelectedCell(selectedCell!);
            if (selectedCell is null) ImGui.EndDisabled();
        }
        else
        {
            ImGui.TextDisabled("Open or create a manifest to edit cells.");
        }

        ImGui.Separator();
        ImGui.Text("Create cell");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##newCellName", "Cell name", ref _cellName, 128);
        ImGui.Text("Exterior coordinate");
        ImGui.SetNextItemWidth(150f);
        ImGui.InputInt("X##exteriorCellX", ref _exteriorCellX);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        ImGui.InputInt("Z##exteriorCellZ", ref _exteriorCellZ);
        var canCreate = _worldManifest is not null;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create exterior"))
            CreateCell(WorldCellKind.Exterior, new ExteriorCellCoordinate(_exteriorCellX, _exteriorCellZ));
        ImGui.SameLine();
        if (ImGui.Button("Create interior")) CreateCell(WorldCellKind.Interior, null);
        if (!canCreate) ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextWrapped("Cell switching saves the current scene first. Save As is available for a new scene.");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##sceneSaveAsPath", "Save current scene as...", ref _sceneSaveAsPath, 1024);
        if (ImGui.Button("Save current scene as..."))
            _worldStatus = RunSceneSaveAs(_sceneSaveAsPath);
        ImGui.TextWrapped(_worldStatus);
        ImGui.End();
    }

    private void LoadWorldManifest()
    {
        try
        {
            _worldManifest = WorldManifest.Load(_worldManifestPath);
            _exteriorCellWidth = _worldManifest.ExteriorCellWidth;
            _selectedWorldCellId = null;
            _recoveryAutosaveElapsedSeconds = RecoveryAutosaveIntervalSeconds;
            _worldStatus = $"Loaded {Path.GetFileName(_worldManifest.FilePath)}.";
        }
        catch (Exception exception)
        {
            _worldStatus = $"Could not open world: {exception.Message}";
        }
    }

    private void ReviewRecovery()
    {
        try
        {
            var recoveryDirectory = AuthoredProjectRecoveryService.GetDefaultRecoveryDirectory(
                _worldManifestPath, _rpgContentPath);
            _recoveryStaging = AuthoredProjectRecoveryService.RestoreLatestToStaging(
                _worldManifestPath, _rpgContentPath, recoveryDirectory);
            var validation = AuthoredProjectValidator.Validate(
                _recoveryStaging.WorldManifestPath, _recoveryStaging.RpgContentPath);
            _recoveryCanApply = validation.IsValid;
            _recoveryReport = validation.IsValid
                ? "Validation passed. This snapshot is ready to apply."
                : string.Join(Environment.NewLine, validation.Diagnostics.Select(value => value.ToString()));
            _recoveryStatus = validation.IsValid
                ? "Recovery staged and validated."
                : $"Recovery staged with {validation.Diagnostics.Count} validation issue(s); apply is disabled.";
        }
        catch (Exception exception)
        {
            _recoveryStaging = null;
            _recoveryCanApply = false;
            _recoveryReport = string.Empty;
            _recoveryStatus = $"Could not review recovery: {exception.Message}";
        }
    }

    private void MaybeAutosaveAuthoredProject(SceneGraph scene, float elapsedSeconds)
    {
        if (_worldManifest is null || _rpgContent is null || _isPlaying()
            || _getSequenceExportInfo().IsRunning)
        {
            _recoveryAutosaveElapsedSeconds = 0f;
            return;
        }

        var currentScenePath = _getCurrentScenePath();
        if (currentScenePath is null || !_worldManifest.Cells.Any(cell =>
                string.Equals(Path.GetFullPath(_worldManifest.ResolveScenePath(cell.Id)),
                    Path.GetFullPath(currentScenePath), StringComparison.OrdinalIgnoreCase)))
            return;

        _recoveryAutosaveElapsedSeconds += Math.Max(0f, elapsedSeconds);
        if (_recoveryAutosaveElapsedSeconds < RecoveryAutosaveIntervalSeconds) return;
        _recoveryAutosaveElapsedSeconds = 0f;
        try
        {
            _recoveryStatus = _captureRecovery(scene, _worldManifestPath, _rpgContentPath,
                currentScenePath, _rpgContent);
        }
        catch (Exception exception)
        {
            _recoveryStatus = $"Autosave failed: {exception.Message}";
        }
    }

    public void RefreshAfterRecovery()
    {
        _travelSceneCache.Clear();
        _doorLinkStatuses.Clear();
        _recoveryStaging = null;
        _recoveryCanApply = false;
        _recoveryReport = string.Empty;
        LoadWorldManifest();
        LoadRpgPlacementContent();
    }

    private void CreateWorldManifest()
    {
        try
        {
            _worldManifest = WorldCellWorkspace.CreateWorld(_worldManifestPath, _exteriorCellWidth);
            _selectedWorldCellId = null;
            _worldStatus = $"Created {Path.GetFileName(_worldManifest.FilePath)}.";
        }
        catch (Exception exception)
        {
            _worldStatus = $"Could not create world: {exception.Message}";
        }
    }

    private void CreateCell(WorldCellKind kind, ExteriorCellCoordinate? coordinate)
    {
        try
        {
            var cell = WorldCellWorkspace.CreateCell(_worldManifest!.FilePath, kind, _cellName, coordinate);
            _worldManifest = WorldManifest.Load(_worldManifest.FilePath);
            _selectedWorldCellId = cell.Id;
            _cellName = WorldCellWorkspace.GetCellName(cell);
            _worldStatus = $"Created {kind.ToString().ToLowerInvariant()} cell '{_cellName}'.";
        }
        catch (Exception exception)
        {
            _worldStatus = $"Could not create cell: {exception.Message}";
        }
    }

    private void RenameSelectedCell(WorldCellDefinition selectedCell)
    {
        try
        {
            var oldScenePath = _worldManifest!.ResolveScenePath(selectedCell.Id);
            var renamed = WorldCellWorkspace.RenameCell(_worldManifest.FilePath, selectedCell.Id, _renameCellName);
            _worldManifest = WorldManifest.Load(_worldManifest.FilePath);
            var newScenePath = _worldManifest.ResolveScenePath(renamed.Id);
            _worldCellRenamed(oldScenePath, newScenePath, renamed.Id);
            _selectedWorldCellId = renamed.Id;
            _renameCellName = WorldCellWorkspace.GetCellName(renamed);
            _cellName = _renameCellName;
            _worldStatus = $"Renamed cell to '{_renameCellName}'.";
        }
        catch (Exception exception)
        {
            _worldStatus = $"Could not rename cell: {exception.Message}";
        }
    }

    private string RunSceneSaveAs(string path)
    {
        try
        {
            _saveSceneAs(path);
            return $"Saved current scene to {Path.GetFileName(path)}.";
        }
        catch (Exception exception)
        {
            return $"Could not save scene: {exception.Message}";
        }
    }

    public void Render()
    {
        _renderer.Draw(ImGui.GetDrawData(), _letterboxOffsetX, _letterboxOffsetY);
    }

    private void UpdateKeyboard(KeyboardState keyboard)
    {
        foreach (var (source, target) in SpecialKeys)
            _io.AddKeyEvent(target, keyboard.IsKeyDown(source));

        for (var index = 0; index < 26; index++)
        {
            _io.AddKeyEvent((ImGuiKey)((int)ImGuiKey.A + index),
                keyboard.IsKeyDown((Keys)((int)Keys.A + index)));
        }

        for (var index = 0; index < 10; index++)
        {
            _io.AddKeyEvent((ImGuiKey)((int)ImGuiKey._0 + index),
                keyboard.IsKeyDown((Keys)((int)Keys.D0 + index)));
        }

        var leftControl = keyboard.IsKeyDown(Keys.LeftControl);
        var rightControl = keyboard.IsKeyDown(Keys.RightControl);
        var leftShift = keyboard.IsKeyDown(Keys.LeftShift);
        var rightShift = keyboard.IsKeyDown(Keys.RightShift);
        var leftAlt = keyboard.IsKeyDown(Keys.LeftAlt);
        var rightAlt = keyboard.IsKeyDown(Keys.RightAlt);
        _io.AddKeyEvent(ImGuiKey.LeftCtrl, leftControl);
        _io.AddKeyEvent(ImGuiKey.RightCtrl, rightControl);
        _io.AddKeyEvent(ImGuiKey.LeftShift, leftShift);
        _io.AddKeyEvent(ImGuiKey.RightShift, rightShift);
        _io.AddKeyEvent(ImGuiKey.LeftAlt, leftAlt);
        _io.AddKeyEvent(ImGuiKey.RightAlt, rightAlt);
        _io.AddKeyEvent(ImGuiKey.ModCtrl, leftControl || rightControl);
        _io.AddKeyEvent(ImGuiKey.ModShift, leftShift || rightShift);
        _io.AddKeyEvent(ImGuiKey.ModAlt, leftAlt || rightAlt);
    }

    private void DrawPanel(SceneGraph scene)
    {
        if (!_initialSelectionSet)
        {
            _selectedObjectId = scene.Objects.FirstOrDefault(item => item.CharacterSettings is not null)?.Id
                ?? scene.Objects.FirstOrDefault()?.Id;
            _initialSelectionSet = true;
        }
        else if (_selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _selectedObjectId = null;

        var inspectorWidth = Math.Min(280f, _logicalWidth * 0.24f);
        ImGui.SetNextWindowPos(new NumericsVector2(Math.Max(0f, _logicalWidth - inspectorWidth), 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(inspectorWidth, Math.Max(160f, _logicalHeight - 48f)));
        if (!ImGui.Begin("Inspector", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        DrawWorkspaceFailure();

        if (_isPlaying())
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.72f, 0.2f, 1f), "PLAYING ON CLONE");
            ImGui.TextWrapped("Changes made during Play are temporary. Stop to return to the scene you were editing.");
            if (ImGui.Button("Interact")) _interact();
            var volume = _getInteractionVolume();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Interaction volume", ref volume, 0f, 1f, "%.2f"))
                _setInteractionVolume(volume);
        }
        ImGui.Text("Objects");
        ImGui.BeginChild("Scene hierarchy", new NumericsVector2(0f, Math.Min(132f, _logicalHeight * 0.2f)), ImGuiChildFlags.Borders);
        DrawSceneHierarchy(scene);
        ImGui.EndChild();

        if (_activeTransformObjectId is not null && _selectedObjectId != _activeTransformObjectId)
            CommitActiveTransformEdit(scene);

        if (_selectedObjectId is not { } objectId || scene.Find(objectId) is not { } selected)
        {
            var pendingPreviewName = _getPendingAssetPreviewName();
            if (pendingPreviewName is not null)
            {
                ImGui.TextWrapped($"Previewing {pendingPreviewName}. It is not in the scene yet; use the Add panel to place it or cancel the preview.");
                if (!_showAddLibrary && ImGui.Button("Show model actions", new NumericsVector2(-1f, 34f)))
                    _showAddLibrary = true;
            }
            else if (scene.Objects.Count == 0)
            {
                ImGui.TextWrapped("This scene is empty. Add a simple object or a model to give yourself something to build with.");
                if (ImGui.Button("Add an empty object", new NumericsVector2(-1f, 34f)))
                    CreateEmpty(scene);

                if (_getCurrentProjectPath() is null)
                {
                    if (ImGui.Button("Make or open a project…", new NumericsVector2(-1f, 34f)))
                        _showHome = true;
                    ImGui.TextWrapped("Create or open a project to add a model.");
                }
                else if (ImGui.Button("Browse for a model…", new NumericsVector2(-1f, 34f)))
                {
                    BrowseForModel();
                }
            }
            else
            {
                ImGui.TextWrapped("Nothing is selected. Choose an object in the list above to edit it.");
            }
            ImGui.End();
            return;
        }

        DrawParentControl(scene, selected);
        ImGui.Separator();
        ImGui.Text($"Selected: {selected.Name}");
        if (ImGui.Button("Duplicate")) Duplicate(scene, selected);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) Delete(scene, selected.Id);

        ImGui.Separator();
        ImGui.Text("Change this object");
        if (ImGui.RadioButton("Move", _transformTool == TransformTool.Move))
            _transformTool = TransformTool.Move;
        ImGui.SameLine();
        if (ImGui.RadioButton("Turn", _transformTool == TransformTool.Turn))
            _transformTool = TransformTool.Turn;
        ImGui.SameLine();
        if (ImGui.RadioButton("Size", _transformTool == TransformTool.Size))
            _transformTool = TransformTool.Size;
        DrawTransformToolActions(scene, selected);
        if (ImGui.TreeNode("Why?"))
        {
            ImGui.TextWrapped("A transform combines position, rotation and scale to place an object in the scene. It changes this scene object, not the source model, so several objects can reuse one model with different placements.");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("More details"))
        {
            var transform = selected.Transform;
            var position = new NumericsVector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
            ImGui.Text("Position");
            ImGui.SetNextItemWidth(-1f);
            var positionChanged = ImGui.InputFloat3("##position", ref position);
            TrackTransformInput(scene, selected.Id, transform, positionChanged, () =>
            {
                if (IsFinite(position))
                    transform.Position = new Microsoft.Xna.Framework.Vector3(position.X, position.Y, position.Z);
            });

            var euler = ToEulerDegrees(transform.Rotation);
            ImGui.Text("Rotation XYZ (degrees)");
            ImGui.SetNextItemWidth(-1f);
            var rotationChanged = ImGui.InputFloat3("##rotation", ref euler);
            TrackTransformInput(scene, selected.Id, transform, rotationChanged, () =>
            {
                if (IsFinite(euler))
                {
                    var radians = MathF.PI / 180f;
                    transform.Rotation = Microsoft.Xna.Framework.Quaternion.Normalize(
                        Microsoft.Xna.Framework.Quaternion.CreateFromYawPitchRoll(
                            euler.Y * radians, euler.X * radians, euler.Z * radians));
                }
            });

            var scale = new NumericsVector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z);
            ImGui.Text("Scale");
            ImGui.SetNextItemWidth(-1f);
            var scaleChanged = ImGui.InputFloat3("##scale", ref scale);
            TrackTransformInput(scene, selected.Id, transform, scaleChanged, () =>
            {
                if (IsFinite(scale))
                    transform.Scale = new Microsoft.Xna.Framework.Vector3(scale.X, scale.Y, scale.Z);
            });
            ImGui.TreePop();
        }

        DrawCharacterControls(selected);
        DrawLightingControls();
        ImGui.End();
    }

    private void DrawSceneHierarchy(SceneGraph scene)
    {
        var childrenByParent = scene.Objects
            .Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id).ToArray());
        foreach (var root in scene.Objects.Where(item => item.ParentId is null)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id))
            DrawSceneHierarchyNode(root, childrenByParent, isRoot: true);
    }

    private void DrawSceneHierarchyNode(SceneObject item,
        IReadOnlyDictionary<Guid, SceneObject[]> childrenByParent, bool isRoot)
    {
        var hasChildren = childrenByParent.TryGetValue(item.Id, out var children) && children.Length > 0;
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow;
        if (_selectedObjectId == item.Id) flags |= ImGuiTreeNodeFlags.Selected;
        if (hasChildren && isRoot) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        var isOpen = ImGui.TreeNodeEx($"{item.Name}##{item.Id:N}", flags);
        if (ImGui.IsItemClicked()) _selectedObjectId = item.Id;
        if (!hasChildren || !isOpen) return;
        foreach (var child in children!) DrawSceneHierarchyNode(child, childrenByParent, isRoot: false);
        ImGui.TreePop();
    }

    private void DrawParentControl(SceneGraph scene, SceneObject selected)
    {
        var currentParentName = selected.ParentId is { } parentId
            ? scene.Find(parentId)?.Name ?? "Missing parent"
            : "No parent";
        ImGui.Text("Parent");
        if (ImGui.BeginCombo("##scene-parent", currentParentName))
        {
            if (ImGui.Selectable("No parent", selected.ParentId is null)
                && selected.ParentId is not null)
                ReparentObject(scene, selected, null);

            foreach (var candidate in scene.Objects
                         .Where(item => item.Id != selected.Id
                             && !IsHierarchyDescendant(scene, item.Id, selected.Id))
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Id))
            {
                var label = $"{candidate.Name}##parent-{candidate.Id:N}";
                if (ImGui.Selectable(label, selected.ParentId == candidate.Id)
                    && selected.ParentId != candidate.Id)
                    ReparentObject(scene, selected, candidate.Id);
            }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped("Changing parent keeps this object's world placement when it can be represented without changing its shape.");
    }

    private void ReparentObject(SceneGraph scene, SceneObject selected, Guid? parentId)
    {
        try
        {
            _beforeStructureChange();
            _history.Execute(scene, new ReparentSceneObjectCommand(selected.Id, parentId));
            _afterStructureChange();
            _projectWorkspaceStatus = "Parent changed. Undo restores the previous relationship and placement.";
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not change parent: {exception.Message}";
        }
    }

    private static bool IsHierarchyDescendant(SceneGraph scene, Guid possibleDescendantId, Guid ancestorId)
    {
        var cursor = scene.Find(possibleDescendantId);
        while (cursor?.ParentId is { } parentId)
        {
            if (parentId == ancestorId) return true;
            cursor = scene.Find(parentId);
        }
        return false;
    }

    private void DrawWorkspaceFailure()
    {
        var status = _projectWorkspaceStatus;
        if (!status.StartsWith("Could not", StringComparison.OrdinalIgnoreCase)) return;

        ImGui.Separator();
        ImGui.Text("Action needs attention");
        ImGui.TextWrapped(status);

        var nextStep = status.Contains("save", StringComparison.OrdinalIgnoreCase)
            ? "Check that the project folder is writable, then try Save again."
            : status.Contains("reload", StringComparison.OrdinalIgnoreCase)
                ? "Repair or replace the selected GLB in the project, then reload the model again."
            : status.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                ? "Try canceling the preview again, or add the previewed model from the Add panel."
                : status.Contains("open", StringComparison.OrdinalIgnoreCase)
                    ? "Choose another project file, or return Home and try Open again."
                    : status.Contains("create", StringComparison.OrdinalIgnoreCase)
                        ? "Choose a valid project name and a folder you can write to."
                        : "Choose another model from Browse, or review the supported model features.";

        ImGui.TextWrapped(nextStep);
        if (ImGui.Button("Dismiss message"))
            _projectWorkspaceStatus = string.Empty;
        ImGui.Separator();
    }

    private void DrawTransformToolActions(SceneGraph scene, SceneObject selected)
    {
        switch (_transformTool)
        {
            case TransformTool.Move:
                ImGui.TextWrapped("Drag a colored axis or move by 25 scene units.");
                DrawTransformActionPair("X -", "X +",
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(-25f, 0f, 0f), "X"),
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(25f, 0f, 0f), "X"));
                DrawTransformActionPair("Y -", "Y +",
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, -25f, 0f), "Y"),
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, 25f, 0f), "Y"));
                DrawTransformActionPair("Z -", "Z +",
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, 0f, -25f), "Z"),
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, 0f, 25f), "Z"));
                DrawMoveSnapControls();
                break;
            case TransformTool.Turn:
                ImGui.TextWrapped("Drag a ring or turn 15 degrees around a local axis.");
                DrawTransformActionPair("X -", "X +",
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitX, -15f, "X"),
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitX, 15f, "X"));
                DrawTransformActionPair("Y -", "Y +",
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitY, -15f, "Y"),
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitY, 15f, "Y"));
                DrawTransformActionPair("Z -", "Z +",
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitZ, -15f, "Z"),
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitZ, 15f, "Z"));
                DrawTurnSnapControls();
                break;
            case TransformTool.Size:
                ImGui.TextWrapped("Drag a colored axis or change size by 10%.");
                DrawTransformActionPair("Smaller", "Larger",
                    () => Resize(scene, selected, 0.9f),
                    () => Resize(scene, selected, 1.1f));
                DrawSizeSnapControls();
                break;
        }
    }

    private void DrawMoveSnapControls()
    {
        ImGui.Separator();
        ImGui.Checkbox("Snap Move to grid", ref _snapMoveToGrid);
        if (_snapMoveToGrid)
        {
            var gridStep = _moveGridStep;
            ImGui.SetNextItemWidth(110f);
            if (ImGui.InputFloat("Grid step", ref gridStep, 0.5f, 5f, "%.2f"))
                _moveGridStep = Math.Clamp(float.IsFinite(gridStep) ? gridStep : _moveGridStep,
                    0.1f, 1_000f);
            ImGui.TextDisabled("World units");
        }
    }

    private void DrawTurnSnapControls()
    {
        ImGui.Separator();
        ImGui.Checkbox("Snap Turn", ref _snapTurnToStep);
        if (!_snapTurnToStep) return;
        var step = _turnSnapDegrees;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.InputFloat("Angle step", ref step, 1f, 5f, "%.1f"))
            _turnSnapDegrees = Math.Clamp(float.IsFinite(step) ? step : _turnSnapDegrees, 0.1f, 180f);
        ImGui.TextDisabled("Degrees");
    }

    private void DrawSizeSnapControls()
    {
        ImGui.Separator();
        ImGui.Checkbox("Snap Size", ref _snapSizeToStep);
        if (!_snapSizeToStep) return;
        var step = _sizeSnapStep;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.InputFloat("Scale step", ref step, 0.05f, 0.25f, "%.2f"))
            _sizeSnapStep = Math.Clamp(float.IsFinite(step) ? step : _sizeSnapStep, 0.01f, 10f);
        ImGui.TextDisabled("Scale multiplier");
    }

    private static void DrawTransformActionPair(string firstLabel, string secondLabel,
        Action firstAction, Action secondAction)
    {
        if (ImGui.Button(firstLabel, new NumericsVector2(92f, 30f))) firstAction();
        ImGui.SameLine();
        if (ImGui.Button(secondLabel, new NumericsVector2(92f, 30f))) secondAction();
    }

    private void NudgePosition(SceneGraph scene, SceneObject selected,
        Microsoft.Xna.Framework.Vector3 offset, string axis)
    {
        ApplyTransformEdit(scene, selected, transform => transform.Position += offset,
            $"Moved {selected.Name} along {axis}.");
    }

    private void Turn(SceneGraph scene, SceneObject selected,
        Microsoft.Xna.Framework.Vector3 axis, float degrees, string axisName)
    {
        var radians = degrees * (MathF.PI / 180f);
        ApplyTransformEdit(scene, selected, transform =>
        {
            var delta = Microsoft.Xna.Framework.Quaternion.CreateFromAxisAngle(axis, radians);
            transform.Rotation = Microsoft.Xna.Framework.Quaternion.Normalize(transform.Rotation * delta);
        }, $"Turned {selected.Name} {degrees:+#;-#;0} degrees around {axisName}.");
    }

    private void Resize(SceneGraph scene, SceneObject selected, float factor)
    {
        ApplyTransformEdit(scene, selected, transform =>
        {
            transform.Scale = new Microsoft.Xna.Framework.Vector3(
                ResizeAxis(transform.Scale.X, factor),
                ResizeAxis(transform.Scale.Y, factor),
                ResizeAxis(transform.Scale.Z, factor));
        }, factor < 1f ? $"Made {selected.Name} 10% smaller." : $"Made {selected.Name} 10% larger.");
    }

    private static float ResizeAxis(float value, float factor)
    {
        if (!float.IsFinite(value)) return value;
        var magnitude = Math.Clamp(MathF.Abs(value) * factor, 0.05f, 1_000f);
        return MathF.CopySign(magnitude, value == 0f ? 1f : value);
    }

    private void ApplyTransformEdit(SceneGraph scene, SceneObject selected,
        Action<Transform> applyChange, string status)
    {
        CommitActiveTransformEdit(scene);
        var before = SceneTransformCopy(selected.Transform);
        applyChange(selected.Transform);
        var after = SceneTransformCopy(selected.Transform);
        if (TransformsEqual(before, after)) return;

        selected.Transform = SceneTransformCopy(before);
        _history.Execute(scene, new TransformEditCommand(selected.Id, before, after));
        _projectWorkspaceStatus = status;
    }

    private void DrawSequencePanel()
    {
        var sequence = _getSequenceInfo();
        if (sequence is null) return;

        ImGui.SetNextWindowPos(new NumericsVector2(232f, 60f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _logicalWidth * 0.5f), 150f));
        if (!ImGui.Begin("Animate and Finish", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.Text(sequence.Name);
        if (ImGui.Button(sequence.IsPlaying ? "Pause" : "Play"))
            _setSequencePlaying(!sequence.IsPlaying);
        ImGui.SameLine();
        var previewEnabled = sequence.PreviewEnabled;
        if (ImGui.Checkbox("Preview sequence", ref previewEnabled))
            _setSequencePreviewEnabled(previewEnabled);

        var time = Math.Clamp(sequence.Time, 0f, sequence.Duration);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("Time (seconds)", ref time, 0f, sequence.Duration, "%.2f"))
            _seekSequence(time);
        ImGui.TextDisabled(sequence.CameraName is null
            ? "No camera cut at this time"
            : $"Camera cut: {sequence.CameraName}");
        ImGui.End();

        DrawSequenceExportPanel(sequence);
    }

    private void DrawSequenceExportPanel(SequenceEditorInfo sequence)
    {
        var export = _getSequenceExportInfo();
        if (!_sequenceExportEndTimeInitialized)
        {
            _sequenceExportEndTime = sequence.Duration;
            _sequenceExportEndTimeInitialized = true;
        }

        ImGui.SetNextWindowPos(new NumericsVector2(232f, 218f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _logicalWidth * 0.5f), 330f));
        if (!ImGui.Begin("Finish film", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (export.IsRunning)
        {
            ImGui.Text($"Exporting: {export.CompletedFrames} / {export.TotalFrames} frames");
            var progress = export.TotalFrames > 0
                ? Math.Clamp((float)export.CompletedFrames / export.TotalFrames, 0f, 1f)
                : 0f;
            ImGui.ProgressBar(progress, new NumericsVector2(-1f, 0f));
            if (ImGui.Button("Cancel export")) _cancelSequenceExport();
        }
        else
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("Output folder", "Choose an empty folder", ref _sequenceExportDirectory, 1024);
            ImGui.InputInt("Width", ref _sequenceExportWidth);
            ImGui.InputInt("Height", ref _sequenceExportHeight);
            ImGui.InputInt("Frames per second", ref _sequenceExportFrameRate);
            ImGui.InputFloat("Start time", ref _sequenceExportStartTime, 0f, 0f, "%.2f");
            ImGui.InputFloat("End time", ref _sequenceExportEndTime, 0f, 0f, "%.2f");
            if (ImGui.Button("Export PNG frames"))
            {
                _startSequenceExport(new SequenceExportEditorRequest(_sequenceExportDirectory,
                    _sequenceExportStartTime, _sequenceExportEndTime,
                    _sequenceExportFrameRate, _sequenceExportWidth, _sequenceExportHeight));
            }
        }

        ImGui.Text($"Status: {export.Status}");
        if (!string.IsNullOrWhiteSpace(export.OutputDirectory))
            ImGui.TextWrapped(export.OutputDirectory);
        if (!string.IsNullOrWhiteSpace(export.Error))
            ImGui.TextWrapped(export.Error);
        ImGui.End();
    }

    private void DrawCharacterControls(SceneObject selected)
    {
        var character = _getCharacterInfo(selected.Id);
        ImGui.Separator();
        ImGui.Text("Character animation");
        if (character is null || character.ClipNames.Count == 0)
        {
            ImGui.TextDisabled("Select a character with imported clips.");
            return;
        }

        var currentClip = character.ClipName ?? "Select clip";
        if (ImGui.BeginCombo("Clip", currentClip))
        {
            foreach (var clipName in character.ClipNames)
            {
                var isSelected = string.Equals(character.ClipName, clipName, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(clipName, isSelected)) _selectCharacterClip(selected.Id, clipName);
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (character.Duration > 0f)
        {
            var time = Math.Clamp(character.Time, 0f, character.Duration);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Time (seconds)", ref time, 0f, character.Duration, "%.2f"))
                _seekCharacter(selected.Id, time);
        }

        var playing = character.IsPlaying;
        if (ImGui.Checkbox("Playing", ref playing)) _setCharacterPlaying(selected.Id, playing);
    }

    private void DrawLightingControls()
    {
        ImGui.Separator();
        if (!ImGui.TreeNode("Scene lighting")) return;

        var ambient = new NumericsVector3(_lighting.AmbientColor.X, _lighting.AmbientColor.Y, _lighting.AmbientColor.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Ambient RGB", ref ambient, 0f, 1.5f))
            _lighting.AmbientColor = new Microsoft.Xna.Framework.Vector3(ambient.X, ambient.Y, ambient.Z);

        var direction = new NumericsVector3(
            _lighting.DirectionalDirection.X, _lighting.DirectionalDirection.Y, _lighting.DirectionalDirection.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Direction", ref direction, -1f, 1f)
            && direction.LengthSquared() > 0.0001f)
            _lighting.DirectionalDirection = new Microsoft.Xna.Framework.Vector3(
                direction.X, direction.Y, direction.Z);

        var directional = new NumericsVector3(
            _lighting.DirectionalColor.X, _lighting.DirectionalColor.Y, _lighting.DirectionalColor.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Sun RGB", ref directional, 0f, 1.5f))
            _lighting.DirectionalColor = new Microsoft.Xna.Framework.Vector3(
                directional.X, directional.Y, directional.Z);
        ImGui.TreePop();
    }

    private void TrackTransformInput(SceneGraph scene, Guid objectId, Transform transform,
        bool changed, Action applyChange)
    {
        if (ImGui.IsItemActivated())
        {
            _activeTransformObjectId = objectId;
            _activeTransformStart = SceneTransformCopy(transform);
        }

        if (changed) applyChange();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitActiveTransformEdit(scene);
    }

    private void CommitActiveTransformEdit(SceneGraph scene)
    {
        if (_activeTransformObjectId is not { } objectId || _activeTransformStart is not { } before)
        {
            _activeTransformObjectId = null;
            _activeTransformStart = null;
            return;
        }

        _activeTransformObjectId = null;
        _activeTransformStart = null;
        if (scene.Find(objectId) is not { } item) return;
        var after = SceneTransformCopy(item.Transform);
        if (TransformsEqual(before, after)) return;
        item.Transform = SceneTransformCopy(before);
        _history.Execute(scene, new TransformEditCommand(objectId, before, after));
    }

    private void RunHistoryAction(SceneGraph scene, bool undo)
    {
        CommitActiveTransformEdit(scene);
        _beforeStructureChange();
        var changed = undo ? _history.Undo(scene) : _history.Redo(scene);
        if (undo && changed && _firstCreationLesson is { } lesson
            && IsSamePath(lesson.ProjectFilePath, _getCurrentProjectPath()))
            lesson.ObserveUndo(scene, undoSucceeded: true);
        _afterStructureChange();
        _projectWorkspaceStatus = undo ? "Undid the last change." : "Redid the last change.";
        if (_selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    private void CreateEmpty(SceneGraph scene)
    {
        CommitActiveTransformEdit(scene);
        var item = new SceneObject(Guid.NewGuid(), UniqueName("New Object", scene.Objects.Select(value => value.Name)));
        RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(item)));
        _selectedObjectId = item.Id;
        _projectWorkspaceStatus = $"Added {item.Name}. Select it to change its position, rotation or size in the Inspector.";
    }

    private void Duplicate(SceneGraph scene, SceneObject selected)
    {
        CommitActiveTransformEdit(scene);
        _beforeStructureChange();
        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, selected.Id);
        _history.Execute(scene, new CreateSceneObjectCommand(duplicate));
        _afterStructureChange();
        _selectedObjectId = duplicate.Id;
    }

    private void Delete(SceneGraph scene, Guid objectId)
    {
        CommitActiveTransformEdit(scene);
        RunStructureChange(scene, () => _history.Execute(scene, new DeleteSceneObjectCommand(objectId)));
        _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    private void RunStructureChange(SceneGraph scene, Action action)
    {
        _beforeStructureChange();
        action();
        _afterStructureChange();
    }

    private static string UniqueName(string basis, IEnumerable<string> existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(basis)) return basis;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{basis} {suffix}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private static Transform SceneTransformCopy(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    private static bool TransformsEqual(Transform first, Transform second) =>
        first.Position == second.Position && first.Rotation == second.Rotation && first.Scale == second.Scale;

    private static NumericsVector3 ToEulerDegrees(Microsoft.Xna.Framework.Quaternion rotation)
    {
        var sinPitch = 2f * (rotation.W * rotation.X - rotation.Y * rotation.Z);
        var pitch = MathF.Abs(sinPitch) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinPitch)
            : MathF.Asin(sinPitch);
        var yaw = MathF.Atan2(
            2f * (rotation.W * rotation.Y + rotation.X * rotation.Z),
            1f - 2f * (rotation.X * rotation.X + rotation.Y * rotation.Y));
        var roll = MathF.Atan2(
            2f * (rotation.W * rotation.Z + rotation.X * rotation.Y),
            1f - 2f * (rotation.X * rotation.X + rotation.Z * rotation.Z));
        var degrees = 180f / MathF.PI;
        return new NumericsVector3(pitch * degrees, yaw * degrees, roll * degrees);
    }

    private static bool IsFinite(NumericsVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
        ImGui.DestroyContext(_context);
    }
}

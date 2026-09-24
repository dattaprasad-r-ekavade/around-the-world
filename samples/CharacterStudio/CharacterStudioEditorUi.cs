using Ember.Scene;
using Ember.Render;
using Ember.Rpg;
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

/// <summary>Immediate-mode scene hierarchy and transform panel for CharacterStudio.</summary>
internal sealed class CharacterStudioEditorUi : IDisposable
{
    private readonly IntPtr _context;
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
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private string _textEntry = string.Empty;
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
    private bool _initialSelectionSet;
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
        Action<string, string, Guid> worldCellRenamed, Func<string?> getCurrentScenePath)
    {
        _logicalWidth = Math.Max(1, logicalWidth);
        _logicalHeight = Math.Max(1, logicalHeight);
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

    public void SetHistory(SceneCommandHistory history) =>
        _history = history ?? throw new ArgumentNullException(nameof(history));

    public void CompletePendingEdit(SceneGraph scene) => CommitActiveTransformEdit(scene);

    public void ResetSceneSelection()
    {
        _selectedObjectId = null;
        _selectedAssetId = null;
        _activeTransformObjectId = null;
        _activeTransformStart = null;
        _initialSelectionSet = false;
    }

    public void AddTextInput(char character)
    {
        if (_disposed || char.IsControl(character)) return;
        _io.AddInputCharacterUTF16(character);
    }

    public void Update(float elapsedSeconds, KeyboardState keyboard, MouseState mouse,
        Vector2 logicalMouse, SceneGraph scene)
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
        _io.AddMousePosEvent(logicalMouse.X, logicalMouse.Y);
        _io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        _io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        _io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        var wheel = (mouse.ScrollWheelValue - _lastWheel) / 120f;
        if (wheel != 0f) _io.AddMouseWheelEvent(0f, wheel);
        _lastWheel = mouse.ScrollWheelValue;
        UpdateKeyboard(keyboard);

        ImGui.NewFrame();
        DrawPanel(scene);
        DrawSequencePanel();
        DrawWorldCellPanel();
        DrawRpgPlacementPanel(scene);
        DrawWorldTravelPanel(scene);
        ImGui.Render();
        _wantsMouse = _io.WantCaptureMouse;
        _wantsKeyboard = _io.WantCaptureKeyboard;
    }

    private void DrawRpgPlacementPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(800f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(450f, 286f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("RPG Placement", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

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
        ImGui.End();
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
            _rpgContent = RpgContentJson.Load(_rpgContentPath);
            _rpgPlacementStatus = $"Loaded definitions from {Path.GetFileName(_rpgContentPath)}.";
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

    private void DrawWorldTravelPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(800f, 310f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(450f, 400f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("World Travel", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        var activeCell = FindCurrentWorldCell();
        if (_worldManifest is null)
        {
            ImGui.TextWrapped("Open a world manifest in World Cells before adding spawn markers or door links.");
            ImGui.TextWrapped(_travelStatus);
            ImGui.End();
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
        ImGui.End();
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

    private void DrawWorldCellPanel()
    {
        ImGui.SetNextWindowPos(new NumericsVector2(400f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(380f, 530f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("World Cells", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##worldManifestPath", "Path to world.json", ref _worldManifestPath, 1024);
        if (ImGui.Button("Open manifest")) LoadWorldManifest();
        ImGui.SameLine();
        if (ImGui.Button("Create world")) CreateWorldManifest();
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
            _worldStatus = $"Loaded {Path.GetFileName(_worldManifest.FilePath)}.";
        }
        catch (Exception exception)
        {
            _worldStatus = $"Could not open world: {exception.Message}";
        }
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
            _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
            _initialSelectionSet = true;
        }
        else if (_selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _selectedObjectId = null;

        ImGui.SetNextWindowPos(new NumericsVector2(16f, 116f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(370f, 570f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Character Studio", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Scene tools");
        if (_isPlaying())
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.72f, 0.2f, 1f), "PLAYING ON CLONE");
            ImGui.SameLine();
            if (ImGui.Button("Stop and restore"))
            {
                _stopPlay();
                ImGui.End();
                return;
            }
            ImGui.SameLine();
            if (ImGui.Button("Interact")) _interact();
            var volume = _getInteractionVolume();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Interaction volume", ref volume, 0f, 1f, "%.2f"))
                _setInteractionVolume(volume);
        }
        else if (ImGui.Button("Play on clone"))
        {
            _startPlay();
            ImGui.End();
            return;
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##textEntry", "Click here and type", ref _textEntry, 128);
        ImGui.TextDisabled("Orbit pauses while a tool window is active.");
        if (!_history.CanUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) RunHistoryAction(scene, undo: true);
        if (!_history.CanUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_history.CanRedo) ImGui.BeginDisabled();
        if (ImGui.Button("Redo")) RunHistoryAction(scene, undo: false);
        if (!_history.CanRedo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Create Empty")) CreateEmpty(scene);
        ImGui.Separator();
        ImGui.Text("Hierarchy");
        ImGui.BeginChild("Scene hierarchy", new NumericsVector2(0f, 145f), ImGuiChildFlags.Borders);
        foreach (var item in scene.Objects)
        {
            var label = $"{item.Name}##{item.Id:N}";
            if (ImGui.Selectable(label, _selectedObjectId == item.Id))
                _selectedObjectId = item.Id;
        }
        ImGui.EndChild();

        if (_activeTransformObjectId is not null && _selectedObjectId != _activeTransformObjectId)
            CommitActiveTransformEdit(scene);

        var availableAssets = scene.Objects
            .Where(item => item.GltfAsset is not null)
            .Select(item => item.GltfAsset!)
            .GroupBy(asset => asset.AssetId)
            .Select(group => group.First())
            .ToArray();
        if (_selectedAssetId is null || availableAssets.All(asset => asset.AssetId != _selectedAssetId))
            _selectedAssetId = availableAssets.FirstOrDefault()?.AssetId;
        ImGui.Separator();
        ImGui.Text("Assets");
        ImGui.BeginChild("Scene assets", new NumericsVector2(0f, 80f), ImGuiChildFlags.Borders);
        foreach (var asset in availableAssets)
        {
            var label = $"{Path.GetFileName(asset.SourcePath)}##asset-{asset.AssetId:N}";
            if (ImGui.Selectable(label, _selectedAssetId == asset.AssetId))
                _selectedAssetId = asset.AssetId;
        }
        ImGui.EndChild();
        var selectedAsset = availableAssets.FirstOrDefault(asset => asset.AssetId == _selectedAssetId);
        if (selectedAsset is null) ImGui.BeginDisabled();
        if (ImGui.Button("Place instance")) PlaceAsset(scene, selectedAsset!);
        if (selectedAsset is null) ImGui.EndDisabled();

        if (_selectedObjectId is not { } objectId || scene.Find(objectId) is not { } selected)
        {
            ImGui.TextDisabled("Select an object to edit its local transform.");
            ImGui.End();
            return;
        }

        ImGui.Separator();
        ImGui.Text($"Selected: {selected.Name}");
        if (ImGui.Button("Duplicate")) Duplicate(scene, selected);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) Delete(scene, selected.Id);
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

        DrawCharacterControls(selected);
        DrawLightingControls();
        ImGui.End();
    }

    private void DrawSequencePanel()
    {
        var sequence = _getSequenceInfo();
        if (sequence is null) return;

        ImGui.SetNextWindowPos(new NumericsVector2(800f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(450f, 150f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Sequence preview", ImGuiWindowFlags.NoCollapse))
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

        ImGui.SetNextWindowPos(new NumericsVector2(800f, 182f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(450f, 330f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Sequence frame export", ImGuiWindowFlags.NoCollapse))
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
        if (undo) _history.Undo(scene);
        else _history.Redo(scene);
        _afterStructureChange();
        if (_selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    private void CreateEmpty(SceneGraph scene)
    {
        CommitActiveTransformEdit(scene);
        var item = new SceneObject(Guid.NewGuid(), UniqueName("New Object", scene.Objects.Select(value => value.Name)));
        RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(item)));
        _selectedObjectId = item.Id;
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

    private void PlaceAsset(SceneGraph scene, GltfAssetReference asset)
    {
        CommitActiveTransformEdit(scene);
        var existing = scene.Objects.Where(item => item.GltfAsset?.AssetId == asset.AssetId).ToArray();
        var positionX = existing.Length == 0 ? 0f : existing.Max(item => item.Transform.Position.X) + 100f;
        var item = SceneObjectFactory.CreateAssetInstance(scene, asset,
            new Microsoft.Xna.Framework.Vector3(positionX, 0f, 0f));
        RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(item)));
        _selectedObjectId = item.Id;
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

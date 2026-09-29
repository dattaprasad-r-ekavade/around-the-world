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

internal sealed partial class CharacterStudioEditorUi
{
    private sealed class WorldPanel
    {
        private readonly CharacterStudioEditorUi _owner;

        public WorldPanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    public void DrawRpgAuthoringPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _owner._logicalWidth), Math.Max(180f, _owner._logicalHeight - 64f)));
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
                _owner.DrawPlacementTemplatesTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Travel"))
            {
                DrawWorldTravelTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Paths"))
            {
                _owner.DrawPathAuthoringTab(scene);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Dialogue"))
            {
                _owner.DrawDialogueAuthoringTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Quests"))
            {
                _owner.DrawQuestAuthoringTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void DrawRpgPlacementTab(SceneGraph scene)
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##rpgContentPath", "Path to registered RPG definitions", ref _owner._rpgContentPath, 1024);
        if (ImGui.Button("Load definitions")) LoadRpgPlacementContent();
        ImGui.SameLine();
        ImGui.TextDisabled(_owner._rpgContent is null ? "No definitions loaded" :
            $"{_owner._rpgContent.Actors.Count} actors · {_owner._rpgContent.Items.Count} items");

        if (_owner._rpgContent is not null)
        {
            var options = GetRpgPlacementOptions();
            ImGui.BeginChild("RPG definition list", new NumericsVector2(0f, 112f), ImGuiChildFlags.Borders);
            foreach (var option in options)
            {
                var key = RpgPlacementKey(option);
                if (ImGui.Selectable($"{option.Name} · {option.Kind}##{key}",
                    string.Equals(_owner._selectedRpgDefinitionKey, key, StringComparison.Ordinal)))
                    _owner._selectedRpgDefinitionKey = key;
            }
            ImGui.EndChild();

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputFloat3("Position", ref _owner._rpgPlacementPosition);
            var selected = options.FirstOrDefault(option =>
                string.Equals(RpgPlacementKey(option), _owner._selectedRpgDefinitionKey, StringComparison.Ordinal));
            var canPlace = selected is not null && !_owner._isPlaying();
            if (!canPlace) ImGui.BeginDisabled();
            if (ImGui.Button("Place selected definition")) PlaceRpgDefinition(scene, selected!);
            if (!canPlace) ImGui.EndDisabled();
        }

        ImGui.TextWrapped(_owner._rpgPlacementStatus);
    }

    private IReadOnlyList<RpgPlacementOption> GetRpgPlacementOptions()
    {
        if (_owner._rpgContent is null) return Array.Empty<RpgPlacementOption>();
        return _owner._rpgContent.Actors.All.Values
            .Select(actor => new RpgPlacementOption(WorldEntityKind.Actor, actor.Id.Value, actor.Name))
            .Concat(_owner._rpgContent.Items.All.Values
                .Select(item => new RpgPlacementOption(WorldEntityKind.Item, item.Id.Value, item.Name)))
            .OrderBy(option => option.Kind)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string RpgPlacementKey(RpgPlacementOption option) => $"{option.Kind}:{option.Id}";

    public void LoadRpgPlacementContent()
    {
        _owner._rpgContent = null;
        _owner._selectedRpgDefinitionKey = null;
        try
        {
            var parsed = RpgContentJson.ParseForValidation(File.ReadAllText(_owner._rpgContentPath));
            _owner._rpgContent = parsed.Content;
            _owner._rpgPlacementStatus = parsed.Diagnostics.Count == 0
                ? $"Loaded definitions from {Path.GetFileName(_owner._rpgContentPath)}."
                : $"Loaded draft from {Path.GetFileName(_owner._rpgContentPath)} with {parsed.Diagnostics.Count} validation issue(s).";
        }
        catch (Exception exception)
        {
            _owner._rpgPlacementStatus = $"Could not load definitions: {exception.Message}";
        }
    }

    private void PlaceRpgDefinition(SceneGraph scene, RpgPlacementOption option)
    {
        try
        {
            var placement = SceneObjectFactory.CreateWorldEntityPlacement(scene, option.Kind, option.Id,
                option.Name, new Vector3(_owner._rpgPlacementPosition.X, _owner._rpgPlacementPosition.Y, _owner._rpgPlacementPosition.Z));
            _owner.CommitActiveTransformEdit(scene);
            _owner.RunStructureChange(scene, () => _owner._history.Execute(scene, new CreateSceneObjectCommand(placement)));
            _owner._selectedObjectId = placement.Id;
            _owner._rpgPlacementStatus = $"Placed {option.Kind.ToString().ToLowerInvariant()} '{option.Name}' " +
                $"with instance ID {placement.WorldEntity!.InstanceId}.";
        }
        catch (Exception exception)
        {
            _owner._rpgPlacementStatus = $"Could not place definition: {exception.Message}";
        }
    }

    private void DrawWorldTravelTab(SceneGraph scene)
    {
        var activeCell = FindCurrentWorldCell();
        if (_owner._worldManifest is null)
        {
            ImGui.TextWrapped("Open a world manifest in World Cells before adding spawn markers or door links.");
            ImGui.TextWrapped(_owner._travelStatus);
            return;
        }

        if (_owner._selectedTravelCellId is null || _owner._worldManifest.FindCell(_owner._selectedTravelCellId.Value) is null)
            _owner._selectedTravelCellId = activeCell?.Id;
        ImGui.TextDisabled(activeCell is null
            ? "Save/open a scene that belongs to this world to edit its links."
            : $"Active cell: {WorldCellWorkspace.GetCellName(activeCell)}");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputFloat3("Spawn position", ref _owner._spawnMarkerPosition);
        var canAddSpawn = activeCell is not null && !_owner._isPlaying();
        if (!canAddSpawn) ImGui.BeginDisabled();
        if (ImGui.Button("Add spawn marker to active cell")) AddSpawnMarker(scene);
        if (!canAddSpawn) ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.Text("Destination cell");
        ImGui.BeginChild("Travel destination cells", new NumericsVector2(0f, 70f), ImGuiChildFlags.Borders);
        foreach (var cell in _owner._worldManifest.Cells)
        {
            var location = cell.ExteriorCoordinate is { } coordinate
                ? $"Exterior ({coordinate.X}, {coordinate.Z})"
                : "Interior";
            if (ImGui.Selectable($"{WorldCellWorkspace.GetCellName(cell)} · {location}##travel-{cell.Id:N}",
                _owner._selectedTravelCellId == cell.Id))
            {
                _owner._selectedTravelCellId = cell.Id;
                _owner._selectedTravelSpawnId = null;
                _owner._travelStatus = $"Selected {WorldCellWorkspace.GetCellName(cell)}.";
            }
        }
        ImGui.EndChild();

        var targetCell = _owner._selectedTravelCellId is { } targetId ? _owner._worldManifest.FindCell(targetId) : null;
        SceneGraph? targetScene = null;
        if (targetCell is not null)
        {
            try { targetScene = LoadTravelScene(targetCell, scene, activeCell); }
            catch (Exception exception) { _owner._travelStatus = $"Could not open destination scene: {exception.Message}"; }
        }

        ImGui.Text("Destination spawn");
        ImGui.BeginChild("Travel destination spawns", new NumericsVector2(0f, 62f), ImGuiChildFlags.Borders);
        if (targetScene is not null)
        {
            foreach (var marker in targetScene.Objects.Where(item => item.SpawnPoint is not null)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable($"{marker.Name} · {marker.SpawnPoint!.Id:N}##spawn-{marker.Id:N}",
                    _owner._selectedTravelSpawnId == marker.SpawnPoint.Id))
                    _owner._selectedTravelSpawnId = marker.SpawnPoint.Id;
            }
        }
        ImGui.EndChild();

        var selectedObject = _owner._selectedObjectId is { } objectId ? scene.Find(objectId) : null;
        var selectedSpawn = targetScene?.Objects.FirstOrDefault(item => item.SpawnPoint?.Id == _owner._selectedTravelSpawnId);
        var canLink = !_owner._isPlaying() && activeCell is not null && selectedObject is not null && targetCell is not null
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
            var status = _owner._doorLinkStatuses.GetValueOrDefault(door.Id, "Not checked");
            var color = status.StartsWith("Broken:", StringComparison.Ordinal)
                ? new NumericsVector4(1f, 0.35f, 0.3f, 1f)
                : status == "Not checked" ? new NumericsVector4(0.75f, 0.75f, 0.75f, 1f)
                : new NumericsVector4(0.4f, 0.9f, 0.5f, 1f);
            ImGui.TextColored(color, $"{door.Name}: {status}");
        }
        ImGui.EndChild();

        ImGui.TextWrapped(_owner._travelStatus);
    }

    private void AddSpawnMarker(SceneGraph scene)
    {
        try
        {
            var marker = SceneObjectFactory.CreateSpawnMarker(scene, "Spawn",
                new Vector3(_owner._spawnMarkerPosition.X, _owner._spawnMarkerPosition.Y, _owner._spawnMarkerPosition.Z));
            _owner.CommitActiveTransformEdit(scene);
            _owner.RunStructureChange(scene, () => _owner._history.Execute(scene, new CreateSceneObjectCommand(marker)));
            _owner._selectedObjectId = marker.Id;
            _owner._selectedTravelSpawnId = marker.SpawnPoint!.Id;
            _owner._travelStatus = $"Added spawn '{marker.Name}' with stable ID {marker.SpawnPoint.Id}.";
        }
        catch (Exception exception)
        {
            _owner._travelStatus = $"Could not add spawn marker: {exception.Message}";
        }
    }

    private void ApplyDoorLink(SceneGraph scene, SceneObject doorObject, WorldCellDefinition targetCell,
        SceneGraph targetScene, SceneObject spawnObject)
    {
        try
        {
            var replacement = new WorldDoorComponent(targetCell.Id, spawnObject.SpawnPoint!.Id,
                doorObject.Door?.Facing ?? Quaternion.Identity);
            var destination = WorldTravelValidator.ResolveDestination(_owner._worldManifest!,
                new Dictionary<Guid, SceneGraph> { [targetCell.Id] = targetScene }, replacement);
            _owner.CommitActiveTransformEdit(scene);
            _owner.RunStructureChange(scene, () => _owner._history.Execute(scene,
                new WorldDoorEditCommand(doorObject.Id, replacement)));
            _owner._doorLinkStatuses[doorObject.Id] = $"Valid → {destination.Position.X:0.##}, {destination.Position.Y:0.##}, {destination.Position.Z:0.##}";
            _owner._travelStatus = $"Linked '{doorObject.Name}' to {WorldCellWorkspace.GetCellName(targetCell)} / {spawnObject.Name}.";
        }
        catch (Exception exception)
        {
            _owner._travelStatus = $"Could not create door link: {exception.Message}";
        }
    }

    private void CheckDoorLinks(SceneGraph scene, WorldCellDefinition? activeCell)
    {
        _owner._doorLinkStatuses.Clear();
        if (_owner._worldManifest is null) return;
        foreach (var item in scene.Objects.Where(item => item.Door is not null))
        {
            try
            {
                var door = item.Door!;
                var targetCell = _owner._worldManifest.FindCell(door.DestinationCellId)
                    ?? throw new InvalidDataException($"Unknown cell {door.DestinationCellId}.");
                var targetScene = LoadTravelScene(targetCell, scene, activeCell);
                var destination = WorldTravelValidator.ResolveDestination(_owner._worldManifest,
                    new Dictionary<Guid, SceneGraph> { [targetCell.Id] = targetScene }, door);
                _owner._doorLinkStatuses[item.Id] = $"Valid → {destination.Position.X:0.##}, {destination.Position.Y:0.##}, {destination.Position.Z:0.##}";
            }
            catch (Exception exception)
            {
                _owner._doorLinkStatuses[item.Id] = $"Broken: {exception.Message}";
            }
        }
        _owner._travelStatus = $"Checked {_owner._doorLinkStatuses.Count} door link(s).";
    }

    public WorldCellDefinition? FindCurrentWorldCell()
    {
        if (_owner._worldManifest is null || string.IsNullOrWhiteSpace(_owner._getCurrentScenePath())) return null;
        string currentPath;
        try { currentPath = Path.GetFullPath(_owner._getCurrentScenePath()!); }
        catch { return null; }
        foreach (var cell in _owner._worldManifest.Cells)
        {
            var scenePath = _owner._worldManifest.ResolveScenePath(cell.Id);
            if (string.Equals(currentPath, Path.GetFullPath(scenePath), StringComparison.OrdinalIgnoreCase))
                return cell;
        }
        return null;
    }

    private SceneGraph LoadTravelScene(WorldCellDefinition cell, SceneGraph currentScene,
        WorldCellDefinition? activeCell)
    {
        if (activeCell?.Id == cell.Id) return currentScene;
        var path = _owner._worldManifest!.ResolveScenePath(cell.Id);
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (_owner._travelSceneCache.TryGetValue(cell.Id, out var cached)
            && string.Equals(cached.Path, path, StringComparison.OrdinalIgnoreCase)
            && cached.LastWriteUtc == lastWriteUtc)
            return cached.Scene;
        var loaded = SceneFile.Load(path);
        _owner._travelSceneCache[cell.Id] = (path, lastWriteUtc, loaded);
        return loaded;
    }

    public void DrawWorldCellPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(380f, _owner._logicalWidth), Math.Max(180f, _owner._logicalHeight - 64f)));
        if (!ImGui.Begin("World Cells", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        var recoveryReviewRunning = _owner._recoveryReviewTask is not null;
        if (recoveryReviewRunning) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##worldManifestPath", "Path to world.json", ref _owner._worldManifestPath, 1024);
        if (ImGui.Button("Open manifest")) LoadWorldManifest();
        ImGui.SameLine();
        if (ImGui.Button("Create world")) CreateWorldManifest();
        if (ImGui.Button("Validate project")) _owner.ValidateAuthoredProject();
        if (recoveryReviewRunning) ImGui.EndDisabled();
        _owner.DrawProjectValidationReport();
        ImGui.Separator();
        ImGui.Text("Authored recovery");
        ImGui.BeginDisabled(recoveryReviewRunning);
        if (ImGui.Button("Autosave now"))
        {
            try
            {
                _owner._recoveryStatus = _owner._captureRecovery(scene, _owner._worldManifestPath, _owner._rpgContentPath,
                    _owner._getCurrentScenePath(), _owner._rpgContent);
            }
            catch (Exception exception)
            {
                _owner._recoveryStatus = $"Autosave failed: {exception.Message}";
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (!recoveryReviewRunning)
        {
            if (ImGui.Button("Review recovery")) StartRecoveryReview();
        }
        else
        {
            var cancelRequested = _owner._recoveryReviewCancellation?.IsCancellationRequested == true;
            ImGui.BeginDisabled(cancelRequested);
            if (ImGui.Button(cancelRequested ? "Canceling recovery…" : "Cancel recovery"))
                CancelRecoveryReview();
            ImGui.EndDisabled();
            ImGui.TextDisabled(Volatile.Read(ref _owner._recoveryReviewActivity) ?? "Restoring authored files…");
            if (Volatile.Read(ref _owner._recoveryReviewProgress) is { TotalFiles: > 0 } progress)
            {
                var fraction = Math.Clamp(progress.CompletedFiles / (float)progress.TotalFiles, 0f, 1f);
                ImGui.ProgressBar(fraction, new NumericsVector2(-1f, 0f));
                ImGui.TextDisabled($"Restoring {progress.CompletedFiles} of {progress.TotalFiles}: {Path.GetFileName(progress.CurrentRelativePath)}");
            }
        }
        ImGui.TextWrapped(_owner._recoveryStatus);
        if (_owner._recoveryStaging is not null)
        {
            ImGui.TextWrapped($"Reviewed snapshot {_owner._recoveryStaging.SnapshotId:N} in staging.");
            ImGui.BeginDisabled(!_owner._recoveryCanApply || recoveryReviewRunning);
            if (ImGui.Button("Apply reviewed recovery"))
            {
                try
                {
                    _owner._recoveryStatus = _owner._applyRecovery(_owner._recoveryStaging);
                    _owner._recoveryStaging = null;
                    _owner._recoveryReport = string.Empty;
                    _owner._recoveryCanApply = false;
                }
                catch (Exception exception)
                {
                    _owner._recoveryStatus = $"Recovery apply failed: {exception.Message}";
                }
            }
            ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(_owner._recoveryReport))
            {
                ImGui.BeginChild("Recovery validation report", new NumericsVector2(0f, 110f), ImGuiChildFlags.Borders);
                foreach (var line in _owner._recoveryReport.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    ImGui.TextWrapped(line);
                ImGui.EndChild();
            }
        }
        ImGui.Text("Exterior cell width (used for new worlds)");
        if (_owner._worldManifest is not null) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputFloat("##exteriorCellWidth", ref _owner._exteriorCellWidth, 1f, 8f, "%.1f");
        if (_owner._worldManifest is not null) ImGui.EndDisabled();

        if (_owner._worldManifest is not null)
        {
            ImGui.TextDisabled($"{_owner._worldManifest.Cells.Count} cells · {Path.GetFileName(_owner._worldManifest.FilePath)}");
            ImGui.BeginChild("World cell list", new NumericsVector2(0f, 145f), ImGuiChildFlags.Borders);
            foreach (var cell in _owner._worldManifest.Cells)
            {
                var location = cell.ExteriorCoordinate is { } coordinate
                    ? $"({coordinate.X}, {coordinate.Z})"
                    : "interior";
                var label = $"{WorldCellWorkspace.GetCellName(cell)} · {location}##{cell.Id:N}";
                if (ImGui.Selectable(label, _owner._selectedWorldCellId == cell.Id))
                {
                    _owner._selectedWorldCellId = cell.Id;
                    _owner._renameCellName = WorldCellWorkspace.GetCellName(cell);
                }
            }
            ImGui.EndChild();

            var selectedCell = _owner._selectedWorldCellId is { } selectedId
                ? _owner._worldManifest.FindCell(selectedId)
                : null;
            if (selectedCell is null) ImGui.BeginDisabled();
            if (ImGui.Button("Open selected cell"))
            {
                var error = _owner._openWorldCell(_owner._worldManifest.ResolveScenePath(selectedCell!.Id),
                    _owner._worldManifest.RootDirectory);
                _owner._worldStatus = error ?? $"Opened {WorldCellWorkspace.GetCellName(selectedCell)}.";
            }
            if (selectedCell is null) ImGui.EndDisabled();

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##renameCellName", "New cell name", ref _owner._renameCellName, 128);
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
        ImGui.InputTextWithHint("##newCellName", "Cell name", ref _owner._cellName, 128);
        ImGui.Text("Exterior coordinate");
        ImGui.SetNextItemWidth(150f);
        ImGui.InputInt("X##exteriorCellX", ref _owner._exteriorCellX);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150f);
        ImGui.InputInt("Z##exteriorCellZ", ref _owner._exteriorCellZ);
        var canCreate = _owner._worldManifest is not null;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create exterior"))
            CreateCell(WorldCellKind.Exterior, new ExteriorCellCoordinate(_owner._exteriorCellX, _owner._exteriorCellZ));
        ImGui.SameLine();
        if (ImGui.Button("Create interior")) CreateCell(WorldCellKind.Interior, null);
        if (!canCreate) ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextWrapped("Cell switching saves the current scene first. Save As is available for a new scene.");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##sceneSaveAsPath", "Save current scene as...", ref _owner._sceneSaveAsPath, 1024);
        if (ImGui.Button("Save current scene as..."))
            _owner._worldStatus = RunSceneSaveAs(_owner._sceneSaveAsPath);
        ImGui.TextWrapped(_owner._worldStatus);
        ImGui.End();
    }

    public void LoadWorldManifest()
    {
        try
        {
            _owner._worldManifest = WorldManifest.Load(_owner._worldManifestPath);
            _owner._exteriorCellWidth = _owner._worldManifest.ExteriorCellWidth;
            _owner._selectedWorldCellId = null;
            _owner._recoveryAutosaveElapsedSeconds = RecoveryAutosaveIntervalSeconds;
            _owner._worldStatus = $"Loaded {Path.GetFileName(_owner._worldManifest.FilePath)}.";
        }
        catch (Exception exception)
        {
            _owner._worldStatus = $"Could not open world: {exception.Message}";
        }
    }

    private void StartRecoveryReview()
    {
        if (_owner._recoveryReviewTask is not null) return;

        _owner._recoveryStaging = null;
        _owner._recoveryCanApply = false;
        _owner._recoveryReport = string.Empty;
        Interlocked.Exchange(ref _owner._recoveryReviewProgress, null);
        try
        {
            var manifestPath = Path.GetFullPath(_owner._worldManifestPath);
            var rpgContentPath = Path.GetFullPath(_owner._rpgContentPath);
            _owner._recoveryReviewProjectRoot = AuthoredProjectRecoveryService.GetProjectRoot(
                manifestPath, rpgContentPath);
            _owner._recoveryReviewDirectory = AuthoredProjectRecoveryService.GetDefaultRecoveryDirectory(
                manifestPath, rpgContentPath);
            var cancellation = new CancellationTokenSource();
            _owner._recoveryReviewCancellation = cancellation;
            Interlocked.Exchange(ref _owner._recoveryReviewActivity, "Restoring authored files…");
            var progress = new InlineRecoveryProgress(value =>
                Interlocked.Exchange(ref _owner._recoveryReviewProgress, value));
            _owner._recoveryStatus = "Preparing recovery review…";
            var recoveryDirectory = _owner._recoveryReviewDirectory
                ?? throw new InvalidOperationException("The recovery directory could not be resolved.");
            _owner._recoveryReviewTask = Task.Run(async () =>
            {
                var staging = await AuthoredProjectRecoveryService.RestoreLatestToStagingAsync(
                    manifestPath, rpgContentPath, recoveryDirectory, cancellation.Token, progress)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _owner._recoveryReviewActivity, "Validating restored project…");
                try
                {
                    var validation = AuthoredProjectValidator.Validate(
                        staging.WorldManifestPath, staging.RpgContentPath);
                    return new CharacterStudioEditorUi.RecoveryReviewResult(staging, validation, null);
                }
                catch (Exception exception)
                {
                    return new CharacterStudioEditorUi.RecoveryReviewResult(staging, null, exception);
                }
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _owner._recoveryReviewCancellation?.Dispose();
            _owner._recoveryReviewCancellation = null;
            _owner._recoveryReviewProjectRoot = null;
            _owner._recoveryReviewDirectory = null;
            Interlocked.Exchange(ref _owner._recoveryReviewActivity, null);
            _owner._recoveryStatus = $"Could not review recovery: {exception.Message}";
        }
    }

    public void CancelRecoveryReview()
    {
        try { _owner._recoveryReviewCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void PollRecoveryReview()
    {
        var task = _owner._recoveryReviewTask;
        if (task is null || !task.IsCompleted) return;

        AuthoredProjectRecoveryStaging? staging = null;
        try
        {
            var review = task.GetAwaiter().GetResult();
            staging = review.Staging;
            if (_owner._recoveryReviewCancellation?.IsCancellationRequested == true)
            {
                DeleteRecoveryStaging(staging, _owner._recoveryReviewDirectory);
                staging = null;
                _owner._recoveryStaging = null;
                _owner._recoveryCanApply = false;
                _owner._recoveryReport = string.Empty;
                _owner._recoveryStatus = "Recovery review canceled. Staged files were removed.";
                return;
            }

            if (review.ValidationError is not null)
                throw new InvalidDataException("The staged recovery could not be validated.", review.ValidationError);
            var validation = review.Validation
                ?? throw new InvalidDataException("The recovery review completed without a validation result.");
            var activeProjectRoot = AuthoredProjectRecoveryService.GetProjectRoot(
                _owner._worldManifestPath, _owner._rpgContentPath);
            if (_owner._recoveryReviewProjectRoot is not { } requestedProjectRoot
                || !SameDirectory(staging.ProjectRoot, requestedProjectRoot)
                || !SameDirectory(staging.ProjectRoot, activeProjectRoot))
            {
                DeleteRecoveryStaging(staging, _owner._recoveryReviewDirectory);
                staging = null;
                _owner._recoveryStaging = null;
                _owner._recoveryCanApply = false;
                _owner._recoveryReport = string.Empty;
                _owner._recoveryStatus = "The active project changed during recovery review; staged files were discarded.";
                return;
            }

            _owner._recoveryStaging = staging;
            _owner._recoveryCanApply = validation.IsValid;
            _owner._recoveryReport = validation.IsValid
                ? "Validation passed. This snapshot is ready to apply."
                : string.Join(Environment.NewLine, validation.Diagnostics.Select(value => value.ToString()));
            _owner._recoveryStatus = validation.IsValid
                ? "Recovery staged and validated."
                : $"Recovery staged with {validation.Diagnostics.Count} validation issue(s); apply is disabled.";
        }
        catch (OperationCanceledException)
        {
            _owner._recoveryStaging = null;
            _owner._recoveryCanApply = false;
            _owner._recoveryReport = string.Empty;
            _owner._recoveryStatus = "Recovery review canceled. Partial staging files were removed.";
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            if (staging is not null)
            {
                try { DeleteRecoveryStaging(staging, _owner._recoveryReviewDirectory); }
                catch (Exception cleanupError) { message += $" Staging cleanup failed: {cleanupError.Message}"; }
            }
            _owner._recoveryStaging = null;
            _owner._recoveryCanApply = false;
            _owner._recoveryReport = string.Empty;
            _owner._recoveryStatus = $"Could not review recovery: {message}";
        }
        finally
        {
            _owner._recoveryReviewTask = null;
            _owner._recoveryReviewCancellation?.Dispose();
            _owner._recoveryReviewCancellation = null;
            _owner._recoveryReviewProjectRoot = null;
            _owner._recoveryReviewDirectory = null;
            Interlocked.Exchange(ref _owner._recoveryReviewActivity, null);
            Interlocked.Exchange(ref _owner._recoveryReviewProgress, null);
        }
    }

    private static bool SameDirectory(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    public static void DeleteRecoveryStaging(AuthoredProjectRecoveryStaging staging, string? recoveryDirectory)
    {
        if (string.IsNullOrWhiteSpace(recoveryDirectory))
            throw new InvalidOperationException("The recovery staging directory is unavailable for cleanup.");
        var stagingParent = Path.GetFullPath(Path.Combine(recoveryDirectory, "staging"));
        var stagingRoot = Path.GetFullPath(staging.StagingRoot);
        var relative = Path.GetRelativePath(stagingParent, stagingRoot);
        if (Path.IsPathRooted(relative) || relative is "." or ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery staging cleanup path escapes its staging directory.");
        if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
    }

    private sealed class InlineRecoveryProgress(Action<AuthoredProjectRecoveryProgress> report)
        : IProgress<AuthoredProjectRecoveryProgress>
    {
        public void Report(AuthoredProjectRecoveryProgress value) => report(value);
    }

    public void MaybeAutosaveAuthoredProject(SceneGraph scene, float elapsedSeconds)
    {
        if (_owner._worldManifest is null || _owner._rpgContent is null || _owner._isPlaying()
            || _owner._getSequenceExportInfo().IsRunning)
        {
            _owner._recoveryAutosaveElapsedSeconds = 0f;
            return;
        }

        var currentScenePath = _owner._getCurrentScenePath();
        if (currentScenePath is null || !_owner._worldManifest.Cells.Any(cell =>
                string.Equals(Path.GetFullPath(_owner._worldManifest.ResolveScenePath(cell.Id)),
                    Path.GetFullPath(currentScenePath), StringComparison.OrdinalIgnoreCase)))
            return;

        _owner._recoveryAutosaveElapsedSeconds += Math.Max(0f, elapsedSeconds);
        if (_owner._recoveryAutosaveElapsedSeconds < RecoveryAutosaveIntervalSeconds) return;
        _owner._recoveryAutosaveElapsedSeconds = 0f;
        try
        {
            _owner._recoveryStatus = _owner._captureRecovery(scene, _owner._worldManifestPath, _owner._rpgContentPath,
                currentScenePath, _owner._rpgContent);
        }
        catch (Exception exception)
        {
            _owner._recoveryStatus = $"Autosave failed: {exception.Message}";
        }
    }

    public void RefreshAfterRecovery()
    {
        _owner._travelSceneCache.Clear();
        _owner._doorLinkStatuses.Clear();
        _owner._recoveryStaging = null;
        _owner._recoveryCanApply = false;
        _owner._recoveryReport = string.Empty;
        LoadWorldManifest();
        LoadRpgPlacementContent();
    }

    private void CreateWorldManifest()
    {
        try
        {
            _owner._worldManifest = WorldCellWorkspace.CreateWorld(_owner._worldManifestPath, _owner._exteriorCellWidth);
            _owner._selectedWorldCellId = null;
            _owner._worldStatus = $"Created {Path.GetFileName(_owner._worldManifest.FilePath)}.";
        }
        catch (Exception exception)
        {
            _owner._worldStatus = $"Could not create world: {exception.Message}";
        }
    }

    private void CreateCell(WorldCellKind kind, ExteriorCellCoordinate? coordinate)
    {
        try
        {
            var cell = WorldCellWorkspace.CreateCell(_owner._worldManifest!.FilePath, kind, _owner._cellName, coordinate);
            _owner._worldManifest = WorldManifest.Load(_owner._worldManifest.FilePath);
            _owner._selectedWorldCellId = cell.Id;
            _owner._cellName = WorldCellWorkspace.GetCellName(cell);
            _owner._worldStatus = $"Created {kind.ToString().ToLowerInvariant()} cell '{_owner._cellName}'.";
        }
        catch (Exception exception)
        {
            _owner._worldStatus = $"Could not create cell: {exception.Message}";
        }
    }

    private void RenameSelectedCell(WorldCellDefinition selectedCell)
    {
        try
        {
            var oldScenePath = _owner._worldManifest!.ResolveScenePath(selectedCell.Id);
            var renamed = WorldCellWorkspace.RenameCell(_owner._worldManifest.FilePath, selectedCell.Id, _owner._renameCellName);
            _owner._worldManifest = WorldManifest.Load(_owner._worldManifest.FilePath);
            var newScenePath = _owner._worldManifest.ResolveScenePath(renamed.Id);
            _owner._worldCellRenamed(oldScenePath, newScenePath, renamed.Id);
            _owner._selectedWorldCellId = renamed.Id;
            _owner._renameCellName = WorldCellWorkspace.GetCellName(renamed);
            _owner._cellName = _owner._renameCellName;
            _owner._worldStatus = $"Renamed cell to '{_owner._renameCellName}'.";
        }
        catch (Exception exception)
        {
            _owner._worldStatus = $"Could not rename cell: {exception.Message}";
        }
    }

    private string RunSceneSaveAs(string path)
    {
        try
        {
            _owner._saveSceneAs(path);
            return $"Saved current scene to {Path.GetFileName(path)}.";
        }
        catch (Exception exception)
        {
            return $"Could not save scene: {exception.Message}";
        }
    }


    }
}

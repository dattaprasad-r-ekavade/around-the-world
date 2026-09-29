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

        var recoveryReviewRunning = _recoveryReviewTask is not null;
        if (recoveryReviewRunning) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##worldManifestPath", "Path to world.json", ref _worldManifestPath, 1024);
        if (ImGui.Button("Open manifest")) LoadWorldManifest();
        ImGui.SameLine();
        if (ImGui.Button("Create world")) CreateWorldManifest();
        if (ImGui.Button("Validate project")) ValidateAuthoredProject();
        if (recoveryReviewRunning) ImGui.EndDisabled();
        DrawProjectValidationReport();
        ImGui.Separator();
        ImGui.Text("Authored recovery");
        ImGui.BeginDisabled(recoveryReviewRunning);
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
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (!recoveryReviewRunning)
        {
            if (ImGui.Button("Review recovery")) StartRecoveryReview();
        }
        else
        {
            var cancelRequested = _recoveryReviewCancellation?.IsCancellationRequested == true;
            ImGui.BeginDisabled(cancelRequested);
            if (ImGui.Button(cancelRequested ? "Canceling recovery…" : "Cancel recovery"))
                CancelRecoveryReview();
            ImGui.EndDisabled();
            ImGui.TextDisabled(Volatile.Read(ref _recoveryReviewActivity) ?? "Restoring authored files…");
            if (Volatile.Read(ref _recoveryReviewProgress) is { TotalFiles: > 0 } progress)
            {
                var fraction = Math.Clamp(progress.CompletedFiles / (float)progress.TotalFiles, 0f, 1f);
                ImGui.ProgressBar(fraction, new NumericsVector2(-1f, 0f));
                ImGui.TextDisabled($"Restoring {progress.CompletedFiles} of {progress.TotalFiles}: {Path.GetFileName(progress.CurrentRelativePath)}");
            }
        }
        ImGui.TextWrapped(_recoveryStatus);
        if (_recoveryStaging is not null)
        {
            ImGui.TextWrapped($"Reviewed snapshot {_recoveryStaging.SnapshotId:N} in staging.");
            ImGui.BeginDisabled(!_recoveryCanApply || recoveryReviewRunning);
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

    private void StartRecoveryReview()
    {
        if (_recoveryReviewTask is not null) return;

        _recoveryStaging = null;
        _recoveryCanApply = false;
        _recoveryReport = string.Empty;
        Interlocked.Exchange(ref _recoveryReviewProgress, null);
        try
        {
            var manifestPath = Path.GetFullPath(_worldManifestPath);
            var rpgContentPath = Path.GetFullPath(_rpgContentPath);
            _recoveryReviewProjectRoot = AuthoredProjectRecoveryService.GetProjectRoot(
                manifestPath, rpgContentPath);
            _recoveryReviewDirectory = AuthoredProjectRecoveryService.GetDefaultRecoveryDirectory(
                manifestPath, rpgContentPath);
            var cancellation = new CancellationTokenSource();
            _recoveryReviewCancellation = cancellation;
            Interlocked.Exchange(ref _recoveryReviewActivity, "Restoring authored files…");
            var progress = new InlineRecoveryProgress(value =>
                Interlocked.Exchange(ref _recoveryReviewProgress, value));
            _recoveryStatus = "Preparing recovery review…";
            var recoveryDirectory = _recoveryReviewDirectory
                ?? throw new InvalidOperationException("The recovery directory could not be resolved.");
            _recoveryReviewTask = Task.Run(async () =>
            {
                var staging = await AuthoredProjectRecoveryService.RestoreLatestToStagingAsync(
                    manifestPath, rpgContentPath, recoveryDirectory, cancellation.Token, progress)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _recoveryReviewActivity, "Validating restored project…");
                try
                {
                    var validation = AuthoredProjectValidator.Validate(
                        staging.WorldManifestPath, staging.RpgContentPath);
                    return new RecoveryReviewResult(staging, validation, null);
                }
                catch (Exception exception)
                {
                    return new RecoveryReviewResult(staging, null, exception);
                }
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _recoveryReviewCancellation?.Dispose();
            _recoveryReviewCancellation = null;
            _recoveryReviewProjectRoot = null;
            _recoveryReviewDirectory = null;
            Interlocked.Exchange(ref _recoveryReviewActivity, null);
            _recoveryStatus = $"Could not review recovery: {exception.Message}";
        }
    }

    private void CancelRecoveryReview()
    {
        try { _recoveryReviewCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void PollRecoveryReview()
    {
        var task = _recoveryReviewTask;
        if (task is null || !task.IsCompleted) return;

        AuthoredProjectRecoveryStaging? staging = null;
        try
        {
            var review = task.GetAwaiter().GetResult();
            staging = review.Staging;
            if (_recoveryReviewCancellation?.IsCancellationRequested == true)
            {
                DeleteRecoveryStaging(staging, _recoveryReviewDirectory);
                staging = null;
                _recoveryStaging = null;
                _recoveryCanApply = false;
                _recoveryReport = string.Empty;
                _recoveryStatus = "Recovery review canceled. Staged files were removed.";
                return;
            }

            if (review.ValidationError is not null)
                throw new InvalidDataException("The staged recovery could not be validated.", review.ValidationError);
            var validation = review.Validation
                ?? throw new InvalidDataException("The recovery review completed without a validation result.");
            var activeProjectRoot = AuthoredProjectRecoveryService.GetProjectRoot(
                _worldManifestPath, _rpgContentPath);
            if (_recoveryReviewProjectRoot is not { } requestedProjectRoot
                || !SameDirectory(staging.ProjectRoot, requestedProjectRoot)
                || !SameDirectory(staging.ProjectRoot, activeProjectRoot))
            {
                DeleteRecoveryStaging(staging, _recoveryReviewDirectory);
                staging = null;
                _recoveryStaging = null;
                _recoveryCanApply = false;
                _recoveryReport = string.Empty;
                _recoveryStatus = "The active project changed during recovery review; staged files were discarded.";
                return;
            }

            _recoveryStaging = staging;
            _recoveryCanApply = validation.IsValid;
            _recoveryReport = validation.IsValid
                ? "Validation passed. This snapshot is ready to apply."
                : string.Join(Environment.NewLine, validation.Diagnostics.Select(value => value.ToString()));
            _recoveryStatus = validation.IsValid
                ? "Recovery staged and validated."
                : $"Recovery staged with {validation.Diagnostics.Count} validation issue(s); apply is disabled.";
        }
        catch (OperationCanceledException)
        {
            _recoveryStaging = null;
            _recoveryCanApply = false;
            _recoveryReport = string.Empty;
            _recoveryStatus = "Recovery review canceled. Partial staging files were removed.";
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            if (staging is not null)
            {
                try { DeleteRecoveryStaging(staging, _recoveryReviewDirectory); }
                catch (Exception cleanupError) { message += $" Staging cleanup failed: {cleanupError.Message}"; }
            }
            _recoveryStaging = null;
            _recoveryCanApply = false;
            _recoveryReport = string.Empty;
            _recoveryStatus = $"Could not review recovery: {message}";
        }
        finally
        {
            _recoveryReviewTask = null;
            _recoveryReviewCancellation?.Dispose();
            _recoveryReviewCancellation = null;
            _recoveryReviewProjectRoot = null;
            _recoveryReviewDirectory = null;
            Interlocked.Exchange(ref _recoveryReviewActivity, null);
            Interlocked.Exchange(ref _recoveryReviewProgress, null);
        }
    }

    private static bool SameDirectory(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static void DeleteRecoveryStaging(AuthoredProjectRecoveryStaging staging, string? recoveryDirectory)
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

}

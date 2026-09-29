using Ember.Scene;
using Ember.Render;
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
    public void DrawWorldAuthoringPanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _owner._logicalWidth), Math.Max(180f, _owner._logicalHeight - 64f)));
        if (!ImGui.Begin("World authoring", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("RPG authoring tabs"))
        {
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
            ImGui.EndTabBar();
        }
        ImGui.End();
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

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##worldManifestPath", "Path to world.json", ref _owner._worldManifestPath, 1024);
        if (ImGui.Button("Open manifest")) LoadWorldManifest();
        ImGui.SameLine();
        if (ImGui.Button("Create world")) CreateWorldManifest();
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
            _owner._worldStatus = $"Loaded {Path.GetFileName(_owner._worldManifest.FilePath)}.";
        }
        catch (Exception exception)
        {
            _owner._worldStatus = $"Could not open world: {exception.Message}";
        }
    }

    public void RefreshAfterRecovery()
    {
        _owner._travelSceneCache.Clear();
        _owner._doorLinkStatuses.Clear();
        LoadWorldManifest();
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

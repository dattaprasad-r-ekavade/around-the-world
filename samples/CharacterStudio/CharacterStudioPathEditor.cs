using Ember.Scene;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private Guid? _pathGraphCellId;
    private CellPathGraph? _pathGraph;
    private Guid? _selectedPathNodeId;
    private int? _selectedPathEdgeIndex;
    private Guid? _routeStartNodeId;
    private Guid? _routeTargetNodeId;
    private NumericsVector3 _pathNodePosition = NumericsVector3.Zero;
    private float _pathEdgeClearance = 1f;
    private float _routeRequiredClearance = 0.45f;
    private bool _pathEdgeBidirectional = true;
    private string _pathStatus = "Open a world cell to author its navigation graph.";

    private void DrawPathAuthoringTab(SceneGraph scene)
    {
        var activeCell = FindCurrentWorldCell();
        if (_worldManifest is null || activeCell is null)
        {
            ImGui.TextWrapped("Open a scene that belongs to the current world to edit its path graph.");
            ImGui.TextWrapped(_pathStatus);
            return;
        }
        if (_pathGraphCellId != activeCell.Id) LoadPathGraph(activeCell);
        if (_pathGraph is not { } graph)
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.4f, 0.3f, 1f), _pathStatus);
            if (ImGui.Button("Replace with an empty path graph"))
            {
                _pathGraph = new CellPathGraph { CellId = activeCell.Id, Kind = activeCell.Kind };
                _pathGraphCellId = activeCell.Id;
                _selectedPathNodeId = null;
                _routeStartNodeId = null;
                _routeTargetNodeId = null;
                SavePathGraph();
            }
            return;
        }

        ImGui.TextDisabled($"{graph.Nodes.Count} nodes · {graph.Edges.Count} edges · cell {activeCell.Id:N}");
        ImGui.BeginChild("Path node list", new NumericsVector2(0f, 184f), ImGuiChildFlags.Borders);
        foreach (var node in graph.Nodes.OrderBy(node => node.Id))
        {
            var marker = node.Id == _routeStartNodeId ? " [Start]"
                : node.Id == _routeTargetNodeId ? " [Target]" : string.Empty;
            if (ImGui.Selectable($"{node.Id:N} · ({node.Position.X:0.##}, {node.Position.Y:0.##}, {node.Position.Z:0.##}){marker}##path-{node.Id:N}",
                _selectedPathNodeId == node.Id))
            {
                _selectedPathNodeId = node.Id;
                _pathNodePosition = new NumericsVector3(node.Position.X, node.Position.Y, node.Position.Z);
            }
        }
        ImGui.EndChild();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputFloat3("Node position", ref _pathNodePosition);
        if (ImGui.Button("Add node")) AddPathNode();
        ImGui.SameLine();
        var selectedNode = _selectedPathNodeId is { } selectedId
            ? graph.Nodes.FirstOrDefault(node => node.Id == selectedId)
            : null;
        if (selectedNode is null) ImGui.BeginDisabled();
        if (ImGui.Button("Update selected node")) UpdatePathNode(selectedNode!.Id);
        if (selectedNode is null) ImGui.EndDisabled();
        ImGui.SameLine();
        if (selectedNode is null) ImGui.BeginDisabled();
        if (ImGui.Button("Set route start"))
        {
            _routeStartNodeId = selectedNode!.Id;
            _pathStatus = "Selected route start node.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Set route target"))
        {
            _routeTargetNodeId = selectedNode!.Id;
            _pathStatus = "Selected route target node.";
        }
        if (selectedNode is null) ImGui.EndDisabled();

        ImGui.SetNextItemWidth(140f);
        ImGui.InputFloat("New edge clearance", ref _pathEdgeClearance, 0.1f, 0.5f, "%.2f");
        ImGui.SameLine();
        ImGui.Checkbox("Bidirectional", ref _pathEdgeBidirectional);
        ImGui.SetNextItemWidth(180f);
        ImGui.InputFloat("NPC body radius", ref _routeRequiredClearance, 0.05f, 0.25f, "%.2f");
        var canConnect = _routeStartNodeId is not null && _routeTargetNodeId is not null
            && _routeStartNodeId != _routeTargetNodeId;
        if (!canConnect) ImGui.BeginDisabled();
        if (ImGui.Button("Connect start to target")) AddPathEdge();
        if (!canConnect) ImGui.EndDisabled();

        ImGui.BeginChild("Path edge list", new NumericsVector2(0f, 88f), ImGuiChildFlags.Borders);
        for (var listedEdgeIndex = 0; listedEdgeIndex < graph.Edges.Count; listedEdgeIndex++)
        {
            var edge = graph.Edges[listedEdgeIndex];
            if (ImGui.Selectable($"{edge.FromNodeId:N} → {edge.ToNodeId:N} · {edge.ClearanceRadius:0.##} m · " +
                $"{(edge.Bidirectional ? "both ways" : "one way")}##edge-{listedEdgeIndex}",
                _selectedPathEdgeIndex == listedEdgeIndex))
            {
                _selectedPathEdgeIndex = listedEdgeIndex;
                _pathEdgeClearance = edge.ClearanceRadius;
                _pathEdgeBidirectional = edge.Bidirectional;
            }
        }
        ImGui.EndChild();
        var selectedEdgeExists = _selectedPathEdgeIndex is { } edgeIndex
            && (uint)edgeIndex < (uint)graph.Edges.Count;
        if (!selectedEdgeExists) ImGui.BeginDisabled();
        if (ImGui.Button("Update selected edge")) UpdatePathEdge(_selectedPathEdgeIndex!.Value);
        ImGui.SameLine();
        if (ImGui.Button("Delete selected edge")) RemovePathEdge(_selectedPathEdgeIndex!.Value);
        if (!selectedEdgeExists) ImGui.EndDisabled();
        ImGui.SameLine();
        if (selectedNode is null) ImGui.BeginDisabled();
        if (ImGui.Button("Delete selected node")) RemovePathNode(selectedNode!.Id);
        if (selectedNode is null) ImGui.EndDisabled();

        var route = FindAuthoredRoute(graph);
        DrawPathReachabilityMap(graph, route);
        ImGui.Separator();
        if (route is null)
            ImGui.TextColored(new NumericsVector4(1f, 0.68f, 0.25f, 1f), "Unreachable with the selected clearance.");
        else
        {
            ImGui.TextColored(new NumericsVector4(0.4f, 0.9f, 0.5f, 1f),
                $"Reachable · {route.NodeIds.Count} nodes · {route.Distance:0.##} m");
            var previewObject = _selectedObjectId is { } actorId ? scene.Find(actorId) : null;
            var canFollow = _isPlaying() && previewObject is not null;
            if (!canFollow) ImGui.BeginDisabled();
            if (ImGui.Button("Follow route with selected object in play mode"))
                _pathStatus = _startPathFollow(previewObject!.Id, graph, route);
            if (!canFollow) ImGui.EndDisabled();
            if (previewObject is not null && _getPathFollowStatus(previewObject.Id) is { } followStatus)
            {
                ImGui.SameLine();
                if (ImGui.Button("Stop")) _stopPathFollow(previewObject.Id);
                ImGui.TextDisabled($"Follow state: {followStatus}");
            }
        }

        if (ImGui.Button("Reload graph")) LoadPathGraph(activeCell);
        ImGui.SameLine();
        if (ImGui.Button("Save graph")) SavePathGraph();
        ImGui.TextWrapped(_pathStatus);
    }

    private void LoadPathGraph(WorldCellDefinition cell)
    {
        var path = PathGraphPath(cell);
        try
        {
            _pathGraph = File.Exists(path)
                ? CellPathGraphFile.Load(path)
                : new CellPathGraph { CellId = cell.Id, Kind = cell.Kind };
            if (_pathGraph.CellId != cell.Id || _pathGraph.Kind != cell.Kind)
                throw new InvalidDataException($"Path graph '{path}' belongs to another cell or cell kind.");
            _pathGraphCellId = cell.Id;
            _selectedPathNodeId = _pathGraph.Nodes.FirstOrDefault()?.Id;
            _routeStartNodeId = _pathGraph.Nodes.FirstOrDefault()?.Id;
            _routeTargetNodeId = _pathGraph.Nodes.LastOrDefault()?.Id;
            _pathStatus = File.Exists(path) ? $"Loaded {Path.GetFileName(path)}." : "No graph saved yet; add nodes and save.";
        }
        catch (Exception exception)
        {
            _pathGraph = null;
            _pathGraphCellId = cell.Id;
            _pathStatus = $"Could not load path graph: {exception.Message}";
        }
    }

    private string PathGraphPath(WorldCellDefinition cell) =>
        Path.Combine(_worldManifest!.RootDirectory, "Navigation", $"{cell.Id:N}.paths.json");

    private void AddPathNode()
    {
        if (_pathGraph is not { } graph) return;
        var node = new CellPathNode(Guid.NewGuid(), new NavigationPoint(
            _pathNodePosition.X, _pathNodePosition.Y, _pathNodePosition.Z));
        SavePathGraph(graph with { Nodes = graph.Nodes.Append(node).ToArray() },
            () =>
            {
                _selectedPathNodeId = node.Id;
                _routeStartNodeId ??= node.Id;
                if (_routeTargetNodeId is null || _routeTargetNodeId == _routeStartNodeId)
                    _routeTargetNodeId = node.Id;
            }, "Added path node.");
    }

    private void AddPathEdge()
    {
        if (_pathGraph is not { } graph || _routeStartNodeId is not { } start
            || _routeTargetNodeId is not { } target) return;
        var edge = new CellPathEdge(start, target, _pathEdgeClearance, _pathEdgeBidirectional);
        SavePathGraph(graph with { Edges = graph.Edges.Append(edge).ToArray() }, null,
            $"Connected {start:N} to {target:N}.");
    }

    private void UpdatePathNode(Guid id)
    {
        if (_pathGraph is not { } graph) return;
        var updated = graph with
        {
            Nodes = graph.Nodes.Select(node => node.Id == id
                ? node with { Position = new NavigationPoint(_pathNodePosition.X, _pathNodePosition.Y, _pathNodePosition.Z) }
                : node).ToArray()
        };
        SavePathGraph(updated, null, "Updated path node position.");
    }

    private void UpdatePathEdge(int index)
    {
        if (_pathGraph is not { } graph || (uint)index >= (uint)graph.Edges.Count) return;
        var updated = graph with
        {
            Edges = graph.Edges.Select((edge, edgeIndex) => edgeIndex == index
                ? edge with { ClearanceRadius = _pathEdgeClearance, Bidirectional = _pathEdgeBidirectional }
                : edge).ToArray()
        };
        SavePathGraph(updated, null, "Updated edge clearance and direction.");
    }

    private void RemovePathEdge(int index)
    {
        if (_pathGraph is not { } graph || (uint)index >= (uint)graph.Edges.Count) return;
        SavePathGraph(graph with
        {
            Edges = graph.Edges.Where((_, edgeIndex) => edgeIndex != index).ToArray()
        }, () => _selectedPathEdgeIndex = null, "Deleted path edge.");
    }

    private void DrawPathReachabilityMap(CellPathGraph graph, CellPathRoute? route)
    {
        ImGui.BeginChild("Path reachability map", new NumericsVector2(0f, 148f), ImGuiChildFlags.Borders);
        var nodes = graph.Nodes;
        if (nodes.Count == 0)
        {
            ImGui.TextDisabled("Add path nodes to see the route map.");
            ImGui.EndChild();
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var mapTopLeft = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail();
        var mapWidth = Math.Max(1f, available.X - 8f);
        var mapHeight = Math.Max(1f, available.Y - 8f);
        var minX = nodes.Min(node => node.Position.X);
        var maxX = nodes.Max(node => node.Position.X);
        var minZ = nodes.Min(node => node.Position.Z);
        var maxZ = nodes.Max(node => node.Position.Z);
        var spanX = MathF.Max(1f, maxX - minX);
        var spanZ = MathF.Max(1f, maxZ - minZ);
        var scale = MathF.Min((mapWidth - 24f) / spanX, (mapHeight - 24f) / spanZ);
        var drawnWidth = spanX * scale;
        var drawnHeight = spanZ * scale;
        var offsetX = (mapWidth - drawnWidth) * 0.5f;
        var offsetY = (mapHeight - drawnHeight) * 0.5f;
        NumericsVector2 Screen(CellPathNode node) => new(
            mapTopLeft.X + offsetX + (node.Position.X - minX) * scale,
            mapTopLeft.Y + offsetY + (maxZ - node.Position.Z) * scale);
        bool RouteUses(CellPathEdge edge)
        {
            if (route is null) return false;
            for (var i = 0; i + 1 < route.NodeIds.Count; i++)
                if (route.NodeIds[i] == edge.FromNodeId && route.NodeIds[i + 1] == edge.ToNodeId
                    || edge.Bidirectional && route.NodeIds[i] == edge.ToNodeId && route.NodeIds[i + 1] == edge.FromNodeId)
                    return true;
            return false;
        }

        foreach (var edge in graph.Edges)
        {
            var from = nodes.First(node => node.Id == edge.FromNodeId);
            var to = nodes.First(node => node.Id == edge.ToNodeId);
            drawList.AddLine(Screen(from), Screen(to), ImGui.GetColorU32(RouteUses(edge)
                ? new NumericsVector4(0.2f, 0.9f, 0.4f, 1f)
                : new NumericsVector4(0.45f, 0.5f, 0.6f, 1f)), RouteUses(edge) ? 3f : 1.5f);
        }
        foreach (var node in nodes)
        {
            var color = node.Id == _selectedPathNodeId ? new NumericsVector4(1f, 0.85f, 0.25f, 1f)
                : node.Id == _routeStartNodeId ? new NumericsVector4(0.25f, 0.75f, 1f, 1f)
                : node.Id == _routeTargetNodeId ? new NumericsVector4(1f, 0.45f, 0.35f, 1f)
                : new NumericsVector4(0.8f, 0.83f, 0.88f, 1f);
            var point = Screen(node);
            drawList.AddCircleFilled(point, 5f, ImGui.GetColorU32(color), 12);
            drawList.AddText(new NumericsVector2(point.X + 7f, point.Y + 4f),
                ImGui.GetColorU32(new NumericsVector4(0.9f, 0.9f, 0.9f, 1f)), node.Id.ToString("N")[..4]);
        }
        ImGui.EndChild();
    }

    private void RemovePathNode(Guid id)
    {
        if (_pathGraph is not { } graph) return;
        var updated = graph with
        {
            Nodes = graph.Nodes.Where(node => node.Id != id).ToArray(),
            Edges = graph.Edges.Where(edge => edge.FromNodeId != id && edge.ToNodeId != id).ToArray()
        };
        SavePathGraph(updated, () =>
        {
            if (_selectedPathNodeId == id) _selectedPathNodeId = null;
            if (_routeStartNodeId == id) _routeStartNodeId = updated.Nodes.FirstOrDefault()?.Id;
            if (_routeTargetNodeId == id) _routeTargetNodeId = updated.Nodes.LastOrDefault()?.Id;
        }, "Deleted path node and its connected edges.");
    }

    private void SavePathGraph() => SavePathGraph(_pathGraph, null, "Saved path graph.");

    private void SavePathGraph(CellPathGraph? graph, Action? afterSave, string success)
    {
        if (graph is null || _worldManifest is null || _pathGraphCellId is not { } cellId) return;
        var cell = _worldManifest.FindCell(cellId);
        if (cell is null) return;
        try
        {
            CellPathGraphFile.SaveAtomic(PathGraphPath(cell), graph);
            _pathGraph = graph;
            afterSave?.Invoke();
            _pathStatus = success;
        }
        catch (Exception exception)
        {
            _pathStatus = $"Could not save path graph: {exception.Message}";
        }
    }

    private CellPathRoute? FindAuthoredRoute(CellPathGraph graph)
    {
        if (_routeStartNodeId is not { } start || _routeTargetNodeId is not { } target
            || graph.Nodes.All(node => node.Id != start) || graph.Nodes.All(node => node.Id != target))
            return null;
        try { return CellRouteSearch.FindShortestRoute(graph, start, target, _routeRequiredClearance); }
        catch (Exception exception)
        {
            _pathStatus = $"Path graph is invalid: {exception.Message}";
            return null;
        }
    }
}

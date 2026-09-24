using System;
using System.Collections.Generic;
using System.IO;

namespace Ember.World;

public enum WorldPathConnectionKind
{
    ExteriorBoundary,
    Door
}

/// <summary>A stable waypoint reference scoped to its authored cell.</summary>
public readonly record struct WorldPathNodeRef(Guid CellId, Guid NodeId);

/// <summary>A graph connection that transfers route planning between two cells.</summary>
public sealed record WorldPathConnection
{
    public Guid Id { get; init; }
    public WorldPathNodeRef From { get; init; }
    public WorldPathNodeRef To { get; init; }
    public WorldPathConnectionKind Kind { get; init; }
    public float ClearanceRadius { get; init; }
    public double TransitionCost { get; init; }
    public bool Bidirectional { get; init; } = true;
    public Guid? DoorInstanceId { get; init; }
}

/// <summary>The explicit cell change required between two consecutive local route legs.</summary>
public sealed record WorldPathTransition(Guid ConnectionId, WorldPathConnectionKind Kind,
    WorldPathNodeRef From, WorldPathNodeRef To, Guid? DoorInstanceId);

/// <summary>One locally traversable part of a world route and its optional exit connection.</summary>
public sealed record WorldPathLeg(Guid CellId, IReadOnlyList<Guid> NodeIds,
    WorldPathTransition? TransitionToNext);

/// <summary>A route keeps local movement separate from exterior-boundary and door transitions.</summary>
public sealed record WorldPathRoute(IReadOnlyList<WorldPathLeg> Legs, double Distance);

/// <summary>Validated set of per-cell graphs joined by explicit world connections.</summary>
public sealed record WorldPathNetwork
{
    public IReadOnlyList<CellPathGraph> Cells { get; init; } = Array.Empty<CellPathGraph>();
    public IReadOnlyList<WorldPathConnection> Connections { get; init; } = Array.Empty<WorldPathConnection>();

    public void Validate()
    {
        if (Cells is null || Connections is null)
            throw new InvalidDataException("World path network cells and connections are required.");
        var nodesByCell = new Dictionary<Guid, HashSet<Guid>>();
        var kindsByCell = new Dictionary<Guid, WorldCellKind?>();
        foreach (var graph in Cells)
        {
            if (graph is null) throw new InvalidDataException("World path network contains a null cell graph.");
            graph.Validate();
            if (!nodesByCell.TryAdd(graph.CellId, new HashSet<Guid>()))
                throw new InvalidDataException($"World path network repeats cell {graph.CellId}.");
            kindsByCell.Add(graph.CellId, graph.Kind);
            foreach (var node in graph.Nodes) nodesByCell[graph.CellId].Add(node.Id);
        }

        var connectionIds = new HashSet<Guid>();
        var arcs = new HashSet<(WorldPathNodeRef From, WorldPathNodeRef To)>();
        foreach (var connection in Connections)
        {
            if (connection is null || connection.Id == Guid.Empty || !Enum.IsDefined(connection.Kind)
                || connection.From.CellId == Guid.Empty || connection.From.NodeId == Guid.Empty
                || connection.To.CellId == Guid.Empty || connection.To.NodeId == Guid.Empty
                || connection.From.CellId == connection.To.CellId
                || !float.IsFinite(connection.ClearanceRadius) || connection.ClearanceRadius <= 0
                || !double.IsFinite(connection.TransitionCost) || connection.TransitionCost < 0)
                throw new InvalidDataException("World path network contains an invalid cross-cell connection.");
            if (!connectionIds.Add(connection.Id))
                throw new InvalidDataException($"World path network repeats connection {connection.Id}.");
            if (!HasNode(nodesByCell, connection.From) || !HasNode(nodesByCell, connection.To))
                throw new InvalidDataException(
                    $"World path connection {connection.Id} references a missing cell or path node.");
            var fromKind = kindsByCell[connection.From.CellId];
            var toKind = kindsByCell[connection.To.CellId];
            if (connection.Kind == WorldPathConnectionKind.ExteriorBoundary
                    && (fromKind != WorldCellKind.Exterior || toKind != WorldCellKind.Exterior)
                || connection.Kind == WorldPathConnectionKind.Door
                    && fromKind != WorldCellKind.Interior && toKind != WorldCellKind.Interior)
                throw new InvalidDataException(
                    $"World path connection {connection.Id} does not match its endpoint cell kinds.");
            var invalidDoorReference = connection.Kind == WorldPathConnectionKind.Door
                ? !connection.DoorInstanceId.HasValue || connection.DoorInstanceId.Value == Guid.Empty
                : connection.DoorInstanceId.HasValue;
            if (invalidDoorReference)
                throw new InvalidDataException(
                    $"World path connection {connection.Id} has an invalid door instance reference.");
            if (!arcs.Add((connection.From, connection.To))
                || connection.Bidirectional && !arcs.Add((connection.To, connection.From)))
                throw new InvalidDataException($"World path network repeats or overlaps connection {connection.Id}.");
        }
    }

    private static bool HasNode(IReadOnlyDictionary<Guid, HashSet<Guid>> nodesByCell,
        WorldPathNodeRef reference) => nodesByCell.TryGetValue(reference.CellId, out var nodes)
        && nodes.Contains(reference.NodeId);
}

/// <summary>Shortest-route search across local paths and explicit cell transitions.</summary>
public static class WorldRouteSearch
{
    public static WorldPathRoute? FindShortestRoute(WorldPathNetwork network,
        WorldPathNodeRef start, WorldPathNodeRef target, float requiredClearanceRadius = 0)
    {
        ArgumentNullException.ThrowIfNull(network);
        network.Validate();
        if (!float.IsFinite(requiredClearanceRadius) || requiredClearanceRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredClearanceRadius));

        var positions = new Dictionary<WorldPathNodeRef, NavigationPoint>();
        foreach (var graph in network.Cells)
            foreach (var node in graph.Nodes)
                positions.Add(new WorldPathNodeRef(graph.CellId, node.Id), node.Position);
        if (!positions.ContainsKey(start))
            throw new ArgumentException($"World route start {start} does not exist.", nameof(start));
        if (!positions.ContainsKey(target))
            throw new ArgumentException($"World route target {target} does not exist.", nameof(target));
        if (start == target)
            return new WorldPathRoute(new[] { new WorldPathLeg(start.CellId, new[] { start.NodeId }, null) }, 0);

        var adjacency = new Dictionary<WorldPathNodeRef, List<Arc>>(positions.Count);
        foreach (var node in positions.Keys) adjacency.Add(node, new List<Arc>());
        foreach (var graph in network.Cells)
        foreach (var edge in graph.Edges)
        {
            if (edge.ClearanceRadius < requiredClearanceRadius) continue;
            var from = new WorldPathNodeRef(graph.CellId, edge.FromNodeId);
            var to = new WorldPathNodeRef(graph.CellId, edge.ToNodeId);
            var distance = positions[from].DistanceTo(positions[to]);
            adjacency[from].Add(new Arc(to, distance, null));
            if (edge.Bidirectional) adjacency[to].Add(new Arc(from, distance, null));
        }
        foreach (var connection in network.Connections)
        {
            if (connection.ClearanceRadius < requiredClearanceRadius) continue;
            var forward = new WorldPathTransition(connection.Id, connection.Kind,
                connection.From, connection.To, connection.DoorInstanceId);
            adjacency[connection.From].Add(new Arc(connection.To, connection.TransitionCost, forward));
            if (connection.Bidirectional)
            {
                var reverse = new WorldPathTransition(connection.Id, connection.Kind,
                    connection.To, connection.From, connection.DoorInstanceId);
                adjacency[connection.To].Add(new Arc(connection.From, connection.TransitionCost, reverse));
            }
        }
        foreach (var arcs in adjacency.Values)
            arcs.Sort(static (left, right) => left.Target.CellId != right.Target.CellId
                ? left.Target.CellId.CompareTo(right.Target.CellId)
                : left.Target.NodeId.CompareTo(right.Target.NodeId));

        var distances = new Dictionary<WorldPathNodeRef, double> { [start] = 0 };
        var previous = new Dictionary<WorldPathNodeRef, Previous>();
        var pending = new PriorityQueue<WorldPathNodeRef, (double Cost, Guid CellId, Guid NodeId)>();
        pending.Enqueue(start, (0, start.CellId, start.NodeId));
        while (pending.TryDequeue(out var current, out var priority))
        {
            if (!distances.TryGetValue(current, out var known) || priority.Cost != known) continue;
            if (current == target) return BuildRoute(start, target, previous, known);
            foreach (var arc in adjacency[current])
            {
                var candidate = known + arc.Cost;
                if (distances.TryGetValue(arc.Target, out var old) && candidate >= old) continue;
                distances[arc.Target] = candidate;
                previous[arc.Target] = new Previous(current, arc.Transition);
                pending.Enqueue(arc.Target, (candidate, arc.Target.CellId, arc.Target.NodeId));
            }
        }
        return null;
    }

    private static WorldPathRoute BuildRoute(WorldPathNodeRef start, WorldPathNodeRef target,
        IReadOnlyDictionary<WorldPathNodeRef, Previous> previous, double distance)
    {
        var reversed = new List<WorldPathNodeRef> { target };
        var current = target;
        while (current != start)
        {
            if (!previous.TryGetValue(current, out var link))
                throw new InvalidOperationException("World route predecessor chain is incomplete.");
            current = link.Node;
            reversed.Add(current);
        }
        reversed.Reverse();

        var legs = new List<WorldPathLeg>();
        var cellId = reversed[0].CellId;
        var nodeIds = new List<Guid> { reversed[0].NodeId };
        for (var i = 1; i < reversed.Count; i++)
        {
            var next = reversed[i];
            if (next.CellId == cellId)
            {
                nodeIds.Add(next.NodeId);
                continue;
            }
            if (!previous.TryGetValue(next, out var previousLink) || previousLink.Transition is not { } transition)
                throw new InvalidOperationException("World route changes cells without an explicit transition.");
            legs.Add(new WorldPathLeg(cellId, nodeIds.ToArray(), transition));
            cellId = next.CellId;
            nodeIds = new List<Guid> { next.NodeId };
        }
        legs.Add(new WorldPathLeg(cellId, nodeIds.ToArray(), null));
        return new WorldPathRoute(legs, distance);
    }

    private readonly record struct Arc(WorldPathNodeRef Target, double Cost, WorldPathTransition? Transition);
    private readonly record struct Previous(WorldPathNodeRef Node, WorldPathTransition? Transition);
}

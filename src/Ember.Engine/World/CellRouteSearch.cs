using System;
using System.Collections.Generic;

namespace Ember.World;

/// <summary>A shortest path through one cell's authored graph.</summary>
public sealed record CellPathRoute(IReadOnlyList<Guid> NodeIds, double Distance);

/// <summary>Deterministic Dijkstra search over authored edges with sufficient clearance.</summary>
public static class CellRouteSearch
{
    public static CellPathRoute? FindShortestRoute(CellPathGraph graph, Guid startNodeId, Guid targetNodeId,
        float requiredClearanceRadius = 0)
    {
        ArgumentNullException.ThrowIfNull(graph);
        graph.Validate();
        if (!float.IsFinite(requiredClearanceRadius) || requiredClearanceRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredClearanceRadius));

        var positions = new Dictionary<Guid, NavigationPoint>(graph.Nodes.Count);
        foreach (var node in graph.Nodes) positions.Add(node.Id, node.Position);
        if (!positions.ContainsKey(startNodeId))
            throw new ArgumentException($"Navigation start node {startNodeId} does not exist.", nameof(startNodeId));
        if (!positions.ContainsKey(targetNodeId))
            throw new ArgumentException($"Navigation target node {targetNodeId} does not exist.", nameof(targetNodeId));
        if (startNodeId == targetNodeId) return new CellPathRoute(new[] { startNodeId }, 0);

        var adjacency = new Dictionary<Guid, List<(Guid Target, double Cost)>>(positions.Count);
        foreach (var nodeId in positions.Keys) adjacency.Add(nodeId, new List<(Guid, double)>());
        foreach (var edge in graph.Edges)
        {
            if (edge.ClearanceRadius < requiredClearanceRadius) continue;
            var cost = positions[edge.FromNodeId].DistanceTo(positions[edge.ToNodeId]);
            adjacency[edge.FromNodeId].Add((edge.ToNodeId, cost));
            if (edge.Bidirectional) adjacency[edge.ToNodeId].Add((edge.FromNodeId, cost));
        }
        foreach (var links in adjacency.Values)
            links.Sort(static (left, right) => left.Target.CompareTo(right.Target));

        var distances = new Dictionary<Guid, double> { [startNodeId] = 0 };
        var previous = new Dictionary<Guid, Guid>();
        var pending = new PriorityQueue<Guid, (double Cost, Guid NodeId)>();
        pending.Enqueue(startNodeId, (0, startNodeId));
        while (pending.TryDequeue(out var current, out var priority))
        {
            if (!distances.TryGetValue(current, out var knownDistance) || priority.Cost != knownDistance)
                continue;
            if (current == targetNodeId)
                return BuildRoute(startNodeId, targetNodeId, previous, knownDistance);

            foreach (var link in adjacency[current])
            {
                var candidate = knownDistance + link.Cost;
                if (distances.TryGetValue(link.Target, out var oldDistance) && candidate >= oldDistance)
                    continue;
                distances[link.Target] = candidate;
                previous[link.Target] = current;
                pending.Enqueue(link.Target, (candidate, link.Target));
            }
        }
        return null;
    }

    private static CellPathRoute BuildRoute(Guid start, Guid target,
        IReadOnlyDictionary<Guid, Guid> previous, double distance)
    {
        var reversed = new List<Guid> { target };
        var current = target;
        while (current != start)
        {
            if (!previous.TryGetValue(current, out var parent))
                throw new InvalidOperationException("Route predecessor chain is incomplete.");
            current = parent;
            reversed.Add(current);
        }
        reversed.Reverse();
        return new CellPathRoute(reversed, distance);
    }
}

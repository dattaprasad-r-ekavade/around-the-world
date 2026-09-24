using System;
using System.Collections.Generic;
using System.IO;

namespace Ember.World;

/// <summary>A finite 3D point stored with an authored path node.</summary>
public readonly record struct NavigationPoint(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public double DistanceTo(NavigationPoint other)
    {
        var x = (double)X - other.X;
        var y = (double)Y - other.Y;
        var z = (double)Z - other.Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}

/// <summary>One stable waypoint within a cell.</summary>
public sealed record CellPathNode(Guid Id, NavigationPoint Position);

/// <summary>
/// A traversable path segment. ClearanceRadius is the largest actor radius the authored
/// segment can safely accommodate; Bidirectional controls whether the reverse arc exists.
/// </summary>
public sealed record CellPathEdge(Guid FromNodeId, Guid ToNodeId, float ClearanceRadius,
    bool Bidirectional = true);

/// <summary>Authored navigation data local to one exterior or interior world cell.</summary>
public sealed record CellPathGraph
{
    public Guid CellId { get; init; }
    public WorldCellKind? Kind { get; init; }
    public IReadOnlyList<CellPathNode> Nodes { get; init; } = Array.Empty<CellPathNode>();
    public IReadOnlyList<CellPathEdge> Edges { get; init; } = Array.Empty<CellPathEdge>();

    public void Validate()
    {
        if (CellId == Guid.Empty) throw new InvalidDataException("Navigation graph cell ID cannot be empty.");
        if (Kind is { } kind && !Enum.IsDefined(kind))
            throw new InvalidDataException($"Navigation graph has unknown cell kind '{kind}'.");
        if (Nodes is null || Edges is null) throw new InvalidDataException("Navigation graph nodes and edges are required.");

        var nodes = new Dictionary<Guid, CellPathNode>();
        foreach (var node in Nodes)
        {
            if (node is null || node.Id == Guid.Empty || !node.Position.IsFinite)
                throw new InvalidDataException("Navigation graph contains a node with an empty ID or nonfinite position.");
            if (!nodes.TryAdd(node.Id, node))
                throw new InvalidDataException($"Navigation graph repeats node {node.Id}.");
        }

        var arcs = new HashSet<(Guid From, Guid To)>();
        foreach (var edge in Edges)
        {
            if (edge is null || edge.FromNodeId == Guid.Empty || edge.ToNodeId == Guid.Empty
                || edge.FromNodeId == edge.ToNodeId || !float.IsFinite(edge.ClearanceRadius)
                || edge.ClearanceRadius <= 0)
                throw new InvalidDataException("Navigation graph contains an invalid edge or clearance radius.");
            if (!nodes.ContainsKey(edge.FromNodeId) || !nodes.ContainsKey(edge.ToNodeId))
                throw new InvalidDataException(
                    $"Navigation edge {edge.FromNodeId} -> {edge.ToNodeId} references a missing node.");
            if (!arcs.Add((edge.FromNodeId, edge.ToNodeId))
                || edge.Bidirectional && !arcs.Add((edge.ToNodeId, edge.FromNodeId)))
                throw new InvalidDataException(
                    $"Navigation graph repeats or overlaps edge {edge.FromNodeId} -> {edge.ToNodeId}.");
        }
    }
}

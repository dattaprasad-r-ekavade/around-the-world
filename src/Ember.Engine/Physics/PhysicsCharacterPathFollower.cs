using System;
using System.Collections.Generic;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Physics;

public enum CharacterPathFollowState
{
    Following,
    Arrived,
    TimedOut
}

/// <summary>Feeds local graph waypoints to the collision-aware character controller.</summary>
public sealed class PhysicsCharacterPathFollower
{
    private readonly PhysicsCharacterController _controller;
    private readonly NavigationPoint[] _waypoints;
    private readonly float _waypointRadius;
    private readonly float _noProgressTimeoutSeconds;
    private int _waypointIndex;
    private double _previousDistance = double.NaN;
    private float _noProgressSeconds;

    public PhysicsCharacterPathFollower(PhysicsCharacterController controller, CellPathGraph graph,
        CellPathRoute route, float waypointRadius = 0.2f, float noProgressTimeoutSeconds = 2f)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(route);
        graph.Validate();
        if (!float.IsFinite(waypointRadius) || waypointRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(waypointRadius));
        if (!float.IsFinite(noProgressTimeoutSeconds) || noProgressTimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(noProgressTimeoutSeconds));
        if (route.NodeIds is null || route.NodeIds.Count == 0
            || !double.IsFinite(route.Distance) || route.Distance < 0)
            throw new ArgumentException("A character route needs at least one node and a finite nonnegative distance.", nameof(route));

        var nodes = new Dictionary<Guid, CellPathNode>(graph.Nodes.Count);
        foreach (var node in graph.Nodes) nodes.Add(node.Id, node);
        _waypoints = new NavigationPoint[route.NodeIds.Count];
        for (var i = 0; i < route.NodeIds.Count; i++)
        {
            if (!nodes.TryGetValue(route.NodeIds[i], out var node))
                throw new ArgumentException($"Character route references missing node {route.NodeIds[i]}.", nameof(route));
            if (i > 0 && !HasTraversableEdge(graph, route.NodeIds[i - 1], route.NodeIds[i]))
                throw new ArgumentException(
                    $"Character route has no directed path edge {route.NodeIds[i - 1]} -> {route.NodeIds[i]}.", nameof(route));
            _waypoints[i] = node.Position;
        }

        _controller = controller;
        _waypointRadius = waypointRadius;
        _noProgressTimeoutSeconds = noProgressTimeoutSeconds;
    }

    public CharacterPathFollowState State { get; private set; } = CharacterPathFollowState.Following;
    public int CurrentWaypointIndex => _waypointIndex;

    /// <summary>Call before the fixed physics step; blocked movement stops after a bounded timeout.</summary>
    public CharacterPathFollowState Advance(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (State != CharacterPathFollowState.Following)
        {
            _controller.SetMoveInput(Vector3.Zero);
            return State;
        }

        var position = _controller.Pose.Position;
        while (_waypointIndex < _waypoints.Length)
        {
            var distance = HorizontalDistance(position, _waypoints[_waypointIndex]);
            if (distance > _waypointRadius) break;
            if (_waypointIndex == _waypoints.Length - 1)
            {
                State = CharacterPathFollowState.Arrived;
                _controller.SetMoveInput(Vector3.Zero);
                return State;
            }
            _waypointIndex++;
            _previousDistance = double.NaN;
            _noProgressSeconds = 0;
        }

        var target = _waypoints[_waypointIndex];
        var delta = new Vector3(target.X - position.X, 0, target.Z - position.Z);
        var currentDistance = Math.Sqrt((double)delta.X * delta.X + (double)delta.Z * delta.Z);
        if (double.IsNaN(_previousDistance) || currentDistance + 0.001 < _previousDistance)
            _noProgressSeconds = 0;
        else
            _noProgressSeconds += elapsedSeconds;
        _previousDistance = currentDistance;

        if (_noProgressSeconds >= _noProgressTimeoutSeconds)
        {
            State = CharacterPathFollowState.TimedOut;
            _controller.SetMoveInput(Vector3.Zero);
            return State;
        }

        _controller.SetMoveInput(currentDistance <= float.Epsilon ? Vector3.Zero : delta / (float)currentDistance);
        return State;
    }

    private static double HorizontalDistance(Vector3 position, NavigationPoint target)
    {
        var x = (double)target.X - position.X;
        var z = (double)target.Z - position.Z;
        return Math.Sqrt((x * x) + (z * z));
    }

    private static bool HasTraversableEdge(CellPathGraph graph, Guid from, Guid to)
    {
        foreach (var edge in graph.Edges)
            if (edge.FromNodeId == from && edge.ToNodeId == to
                || edge.Bidirectional && edge.FromNodeId == to && edge.ToNodeId == from)
                return true;
        return false;
    }
}

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Ember.World;

public enum ExteriorCellCollisionState
{
    Ready,
    WaitingForCollision,
    MissingCell
}

/// <summary>Availability of every exterior cell touched by one proposed movement step.</summary>
public readonly record struct ExteriorCellMovementResult(
    ExteriorCellCollisionState State,
    ExteriorCellCoordinate? RequiredCoordinate)
{
    public bool CanMove => State == ExteriorCellCollisionState.Ready;
}

/// <summary>Stops a character before entering a cell whose collision has not been activated.</summary>
public sealed class ExteriorCellCollisionGate
{
    private readonly WorldManifest _world;
    private readonly HashSet<ExteriorCellCoordinate> _collisionReady = new();
    private readonly HashSet<ExteriorCellCoordinate> _requested = new();

    public ExteriorCellCollisionGate(WorldManifest world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    public event Action<ExteriorCellCoordinate>? CollisionRequired;
    public ExteriorCellMovementResult LastMovementResult { get; private set; } =
        new(ExteriorCellCollisionState.Ready, null);

    public void MarkCollisionReady(ExteriorCellCoordinate coordinate)
    {
        if (!_world.TryGetExterior(coordinate, out _))
            throw new KeyNotFoundException($"World manifest has no exterior cell at ({coordinate.X}, {coordinate.Z}).");
        _collisionReady.Add(coordinate);
        _requested.Remove(coordinate);
    }

    public bool MarkCollisionUnavailable(ExteriorCellCoordinate coordinate)
    {
        _requested.Remove(coordinate);
        return _collisionReady.Remove(coordinate);
    }

    /// <summary>
    /// Checks the capsule's horizontal footprint at the proposed endpoint. Missing authored cells
    /// and cells whose collision is still loading both block movement, but report distinct states.
    /// </summary>
    public ExteriorCellMovementResult Evaluate(Vector3 currentPosition, Vector3 proposedPosition,
        float horizontalClearance = 0f)
    {
        ValidateFinite(currentPosition, nameof(currentPosition));
        ValidateFinite(proposedPosition, nameof(proposedPosition));
        if (!float.IsFinite(horizontalClearance) || horizontalClearance < 0f)
            throw new ArgumentOutOfRangeException(nameof(horizontalClearance), "Movement clearance must be finite and nonnegative.");

        var current = _world.GetExteriorCoordinate(currentPosition);
        var minimum = _world.GetExteriorCoordinate(new Vector3(
            proposedPosition.X - horizontalClearance, proposedPosition.Y,
            proposedPosition.Z - horizontalClearance));
        var maximum = _world.GetExteriorCoordinate(new Vector3(
            proposedPosition.X + horizontalClearance, proposedPosition.Y,
            proposedPosition.Z + horizontalClearance));

        for (var x = (long)minimum.X; x <= maximum.X; x++)
        for (var z = (long)minimum.Z; z <= maximum.Z; z++)
        {
            var coordinate = new ExteriorCellCoordinate((int)x, (int)z);
            if (coordinate == current || _collisionReady.Contains(coordinate)) continue;
            if (!_world.TryGetExterior(coordinate, out _))
                return LastMovementResult = new ExteriorCellMovementResult(ExteriorCellCollisionState.MissingCell, coordinate);
            if (_requested.Add(coordinate)) CollisionRequired?.Invoke(coordinate);
            return LastMovementResult = new ExteriorCellMovementResult(ExteriorCellCollisionState.WaitingForCollision, coordinate);
        }

        return LastMovementResult = new ExteriorCellMovementResult(ExteriorCellCollisionState.Ready, null);
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Movement positions must be finite.");
    }
}

using System;
using Microsoft.Xna.Framework;

namespace Ember.World;

/// <summary>Integer exterior grid coordinate, with Z matching the world's Z axis.</summary>
public readonly record struct ExteriorCellCoordinate(int X, int Z);

/// <summary>Maps world-space positions to fixed-width exterior cells.</summary>
public static class ExteriorCellGrid
{
    public static ExteriorCellCoordinate FromWorldPosition(Vector3 worldPosition, float cellWidth)
    {
        if (!float.IsFinite(worldPosition.X) || !float.IsFinite(worldPosition.Y) || !float.IsFinite(worldPosition.Z))
            throw new ArgumentOutOfRangeException(nameof(worldPosition), "Position components must be finite.");
        if (!float.IsFinite(cellWidth) || cellWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellWidth), "Cell width must be finite and positive.");

        return new ExteriorCellCoordinate(
            ToCellCoordinate(worldPosition.X, cellWidth),
            ToCellCoordinate(worldPosition.Z, cellWidth));
    }

    private static int ToCellCoordinate(float position, float cellWidth)
    {
        var coordinate = Math.Floor((double)position / cellWidth);
        if (coordinate < int.MinValue || coordinate > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(position), "Position is outside the supported cell coordinate range.");
        return (int)coordinate;
    }
}

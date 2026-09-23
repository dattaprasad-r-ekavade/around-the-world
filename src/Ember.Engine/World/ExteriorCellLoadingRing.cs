using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ember.World;

/// <summary>Plans new exterior cell requests within a square radius around the player.</summary>
public sealed class ExteriorCellLoadingRing
{
    private readonly HashSet<ExteriorCellCoordinate> _requested = new();

    public ExteriorCellLoadingRing(int radiusInCells, float cellWidth)
    {
        if (radiusInCells < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusInCells), "Loading radius cannot be negative.");
        if (!float.IsFinite(cellWidth) || cellWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellWidth), "Cell width must be finite and positive.");

        RadiusInCells = radiusInCells;
        CellWidth = cellWidth;
    }

    public int RadiusInCells { get; }
    public float CellWidth { get; }

    /// <summary>Coordinates newly requested on this update, in stable X/Z order.</summary>
    public IReadOnlyList<ExteriorCellCoordinate> UpdatePlayerPosition(Vector3 worldPosition)
    {
        var centre = ExteriorCellGrid.FromWorldPosition(worldPosition, CellWidth);
        var newlyRequested = new List<ExteriorCellCoordinate>();
        var minimumX = (long)centre.X - RadiusInCells;
        var maximumX = (long)centre.X + RadiusInCells;
        var minimumZ = (long)centre.Z - RadiusInCells;
        var maximumZ = (long)centre.Z + RadiusInCells;
        if (minimumX < int.MinValue || maximumX > int.MaxValue
            || minimumZ < int.MinValue || maximumZ > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(worldPosition), "Loading ring exceeds the supported cell coordinate range.");

        for (var x = minimumX; x <= maximumX; x++)
        for (var z = minimumZ; z <= maximumZ; z++)
        {
            var coordinate = new ExteriorCellCoordinate((int)x, (int)z);
            if (_requested.Add(coordinate)) newlyRequested.Add(coordinate);
        }

        return newlyRequested.OrderBy(value => value.X).ThenBy(value => value.Z).ToArray();
    }

    /// <summary>Allows a cell to be requested again after its runtime has been unloaded.</summary>
    public bool Forget(ExteriorCellCoordinate coordinate) => _requested.Remove(coordinate);
}

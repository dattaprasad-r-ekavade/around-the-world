using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Ember.World;

/// <summary>New requests and cells outside the retention radius for one player-centre change.</summary>
public readonly record struct ExteriorCellLoadingUpdate(
    IReadOnlyList<ExteriorCellCoordinate> Entered,
    IReadOnlyList<ExteriorCellCoordinate> Left);

/// <summary>Plans exterior cell requests and unload candidates around the player.</summary>
public sealed class ExteriorCellLoadingRing
{
    private static readonly ExteriorCellLoadingUpdate EmptyUpdate =
        new(Array.Empty<ExteriorCellCoordinate>(), Array.Empty<ExteriorCellCoordinate>());

    private readonly HashSet<ExteriorCellCoordinate> _tracked = new();
    private ExteriorCellCoordinate? _lastCentre;

    public ExteriorCellLoadingRing(int radiusInCells, float cellWidth, int? retentionRadiusInCells = null)
    {
        if (radiusInCells < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusInCells), "Loading radius cannot be negative.");
        if (!float.IsFinite(cellWidth) || cellWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellWidth), "Cell width must be finite and positive.");

        var retention = retentionRadiusInCells ?? (radiusInCells == int.MaxValue ? int.MaxValue : radiusInCells + 1);
        if (retention < radiusInCells)
            throw new ArgumentOutOfRangeException(nameof(retentionRadiusInCells), "Retention radius cannot be smaller than the loading radius.");

        RadiusInCells = radiusInCells;
        RetentionRadiusInCells = retention;
        CellWidth = cellWidth;
    }

    public int RadiusInCells { get; }
    public int RetentionRadiusInCells { get; }
    public float CellWidth { get; }

    /// <summary>
    /// Computes newly entered requests and cells to unload. Calls within the same centre cell return
    /// a shared empty result without allocating. Requests are ordered nearest-first, then X/Z.
    /// </summary>
    public ExteriorCellLoadingUpdate UpdatePlayerPosition(Vector3 worldPosition)
    {
        var centre = ExteriorCellGrid.FromWorldPosition(worldPosition, CellWidth);
        if (_lastCentre == centre) return EmptyUpdate;

        ValidateBounds(centre, RadiusInCells, nameof(worldPosition));
        ValidateBounds(centre, RetentionRadiusInCells, nameof(worldPosition));
        _lastCentre = centre;

        var entered = new List<ExteriorCellCoordinate>();
        var minimumX = (long)centre.X - RadiusInCells;
        var maximumX = (long)centre.X + RadiusInCells;
        var minimumZ = (long)centre.Z - RadiusInCells;
        var maximumZ = (long)centre.Z + RadiusInCells;
        for (var x = minimumX; x <= maximumX; x++)
        for (var z = minimumZ; z <= maximumZ; z++)
        {
            var coordinate = new ExteriorCellCoordinate((int)x, (int)z);
            if (_tracked.Add(coordinate)) entered.Add(coordinate);
        }
        entered.Sort((left, right) => CompareByDistance(left, right, centre));

        var leftCandidates = new List<ExteriorCellCoordinate>();
        foreach (var coordinate in _tracked)
        {
            if (ChebyshevDistance(coordinate, centre) > RetentionRadiusInCells)
                leftCandidates.Add(coordinate);
        }
        leftCandidates.Sort(static (left, right) =>
        {
            var x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Z.CompareTo(right.Z);
        });
        foreach (var coordinate in leftCandidates) _tracked.Remove(coordinate);

        return entered.Count == 0 && leftCandidates.Count == 0
            ? EmptyUpdate
            : new ExteriorCellLoadingUpdate(entered.ToArray(), leftCandidates.ToArray());
    }

    /// <summary>Allows a cell to be requested again after its runtime has been unloaded.</summary>
    public bool Forget(ExteriorCellCoordinate coordinate)
    {
        var removed = _tracked.Remove(coordinate);
        if (removed) _lastCentre = null;
        return removed;
    }

    private static long ChebyshevDistance(ExteriorCellCoordinate left, ExteriorCellCoordinate right) =>
        Math.Max(Math.Abs((long)left.X - right.X), Math.Abs((long)left.Z - right.Z));

    private static int CompareByDistance(ExteriorCellCoordinate left, ExteriorCellCoordinate right,
        ExteriorCellCoordinate centre)
    {
        var distance = ChebyshevDistance(left, centre).CompareTo(ChebyshevDistance(right, centre));
        if (distance != 0) return distance;
        var x = left.X.CompareTo(right.X);
        return x != 0 ? x : left.Z.CompareTo(right.Z);
    }

    private static void ValidateBounds(ExteriorCellCoordinate centre, int radius, string parameterName)
    {
        var minimumX = (long)centre.X - radius;
        var maximumX = (long)centre.X + radius;
        var minimumZ = (long)centre.Z - radius;
        var maximumZ = (long)centre.Z + radius;
        if (minimumX < int.MinValue || maximumX > int.MaxValue
            || minimumZ < int.MinValue || maximumZ > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName,
                "Loading or retention ring exceeds the supported cell coordinate range.");
    }
}

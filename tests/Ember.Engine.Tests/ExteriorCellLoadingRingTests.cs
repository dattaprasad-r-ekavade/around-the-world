using System;
using System.Linq;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ExteriorCellLoadingRingTests
{
    [Fact]
    public void InitialUpdateRequestsEveryCellInConfiguredSquareRingOnce()
    {
        var ring = new ExteriorCellLoadingRing(radiusInCells: 1, cellWidth: 32f);

        var requested = ring.UpdatePlayerPosition(Vector3.Zero);

        Assert.Equal(9, requested.Count);
        Assert.Equal(new ExteriorCellCoordinate(-1, -1), requested[0]);
        Assert.Equal(new ExteriorCellCoordinate(0, 0), requested[4]);
        Assert.Equal(new ExteriorCellCoordinate(1, 1), requested[8]);
        Assert.Empty(ring.UpdatePlayerPosition(Vector3.Zero));
    }

    [Fact]
    public void CrossingCellBoundaryRequestsOnlyTheNewNeighborColumn()
    {
        var ring = new ExteriorCellLoadingRing(radiusInCells: 1, cellWidth: 32f);
        ring.UpdatePlayerPosition(new Vector3(31.99f, 0f, 5f));

        var requested = ring.UpdatePlayerPosition(new Vector3(32f, 0f, 5f));

        Assert.Equal(
        [
            new ExteriorCellCoordinate(2, -1),
            new ExteriorCellCoordinate(2, 0),
            new ExteriorCellCoordinate(2, 1)
        ], requested);
        Assert.Empty(ring.UpdatePlayerPosition(new Vector3(33f, 0f, 5f)));
    }

    [Fact]
    public void UnloadedCoordinateCanBeRequestedAgain()
    {
        var ring = new ExteriorCellLoadingRing(radiusInCells: 0, cellWidth: 32f);
        var coordinate = new ExteriorCellCoordinate(0, 0);
        ring.UpdatePlayerPosition(Vector3.Zero);

        Assert.True(ring.Forget(coordinate));
        Assert.False(ring.Forget(coordinate));
        Assert.Equal([coordinate], ring.UpdatePlayerPosition(Vector3.Zero));
    }

    [Fact]
    public void ConstructorRejectsNegativeRadiusAndInvalidCellWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExteriorCellLoadingRing(-1, 32f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExteriorCellLoadingRing(1, 0f));
    }
}

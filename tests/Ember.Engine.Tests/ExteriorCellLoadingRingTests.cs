using System;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ExteriorCellLoadingRingTests
{
    [Fact]
    public void InitialUpdateRequestsNearestCellsFirstAndUnchangedCentreAllocatesNoNewResult()
    {
        var ring = new ExteriorCellLoadingRing(radiusInCells: 1, cellWidth: 32f);

        var update = ring.UpdatePlayerPosition(Vector3.Zero);

        Assert.Equal(9, update.Entered.Count);
        Assert.Equal(new ExteriorCellCoordinate(0, 0), update.Entered[0]);
        Assert.Equal(new ExteriorCellCoordinate(-1, -1), update.Entered[1]);
        Assert.Empty(update.Left);
        var repeated = ring.UpdatePlayerPosition(new Vector3(1f, 0f, 1f));
        var repeatedAgain = ring.UpdatePlayerPosition(new Vector3(2f, 0f, 2f));
        Assert.Same(repeated.Entered, repeatedAgain.Entered);
        Assert.Same(repeated.Left, repeatedAgain.Left);
    }

    [Fact]
    public void CrossingBoundaryRequestsNewColumnAndRetentionRadiusDelaysUnload()
    {
        var ring = new ExteriorCellLoadingRing(radiusInCells: 1, cellWidth: 32f, retentionRadiusInCells: 2);
        ring.UpdatePlayerPosition(new Vector3(31.99f, 0f, 5f));

        var firstCrossing = ring.UpdatePlayerPosition(new Vector3(32f, 0f, 5f));

        Assert.Equal(
        [
            new ExteriorCellCoordinate(2, -1),
            new ExteriorCellCoordinate(2, 0),
            new ExteriorCellCoordinate(2, 1)
        ], firstCrossing.Entered);
        Assert.Empty(firstCrossing.Left);

        var secondCrossing = ring.UpdatePlayerPosition(new Vector3(64f, 0f, 5f));

        Assert.Equal(
        [
            new ExteriorCellCoordinate(3, -1),
            new ExteriorCellCoordinate(3, 0),
            new ExteriorCellCoordinate(3, 1)
        ], secondCrossing.Entered);
        Assert.Equal(
        [
            new ExteriorCellCoordinate(-1, -1),
            new ExteriorCellCoordinate(-1, 0),
            new ExteriorCellCoordinate(-1, 1)
        ], secondCrossing.Left);
    }

    [Fact]
    public void UnloadedCoordinateCanBeRequestedAgain()
    {
        var ring = new ExteriorCellLoadingRing(radiusInCells: 0, cellWidth: 32f);
        var coordinate = new ExteriorCellCoordinate(0, 0);
        ring.UpdatePlayerPosition(Vector3.Zero);

        Assert.True(ring.Forget(coordinate));
        Assert.False(ring.Forget(coordinate));
        Assert.Equal([coordinate], ring.UpdatePlayerPosition(Vector3.Zero).Entered);
    }

    [Fact]
    public void ConstructorRejectsInvalidRadiiAndCellWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExteriorCellLoadingRing(-1, 32f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExteriorCellLoadingRing(1, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExteriorCellLoadingRing(2, 32f, retentionRadiusInCells: 1));
    }
}

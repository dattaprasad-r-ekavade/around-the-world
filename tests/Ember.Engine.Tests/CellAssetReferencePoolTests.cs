using System.Collections.Generic;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class CellAssetReferencePoolTests
{
    [Fact]
    public void SharedAssetStaysAliveUntilTheFinalCellReleasesIt()
    {
        using var pool = new CellAssetReferencePool<string, AssetProbe>(System.StringComparer.OrdinalIgnoreCase);
        var createCount = 0;
        var first = pool.Acquire("Content/Stone.bin", () =>
        {
            createCount++;
            return new AssetProbe();
        });
        var sharedAsset = first.Asset;
        var second = pool.Acquire("content/stone.bin", () =>
        {
            createCount++;
            return new AssetProbe();
        });

        Assert.Same(sharedAsset, second.Asset);
        Assert.Equal(1, createCount);
        Assert.Equal(2, pool.GetReferenceCount("CONTENT/STONE.BIN"));
        Assert.Equal(1, pool.LiveAssetCount);

        first.Dispose();
        first.Dispose();
        Assert.Equal(0, sharedAsset.DisposeCount);
        Assert.Equal(1, pool.TotalReferenceCount);

        second.Dispose();
        Assert.Equal(1, sharedAsset.DisposeCount);
        Assert.Equal(0, pool.LiveAssetCount);
        Assert.Equal(0, pool.TotalReferenceCount);
    }

    [Fact]
    public void RepeatedCellCrossingsUnloadPastRetentionWithoutAccumulatingSharedAssets()
    {
        using var pool = new CellAssetReferencePool<string, AssetProbe>();
        var ring = new ExteriorCellLoadingRing(radiusInCells: 1, cellWidth: 1f, retentionRadiusInCells: 2);
        var cellAssets = new Dictionary<ExteriorCellCoordinate, CellAssetReference<string, AssetProbe>>();
        AssetProbe? sharedAsset = null;

        void Apply(Vector3 position)
        {
            var update = ring.UpdatePlayerPosition(position);
            foreach (var coordinate in update.Entered)
            {
                var lease = pool.Acquire("shared/stoney-ground", () => sharedAsset = new AssetProbe());
                sharedAsset ??= lease.Asset;
                cellAssets.Add(coordinate, lease);
            }
            foreach (var coordinate in update.Left)
            {
                cellAssets[coordinate].Dispose();
                cellAssets.Remove(coordinate);
            }
            Assert.Equal(cellAssets.Count, pool.TotalReferenceCount);
        }

        Apply(Vector3.Zero);
        for (var cellX = 1; cellX <= 20; cellX++)
        {
            Apply(new Vector3(cellX, 0f, 0f));
            Assert.InRange(cellAssets.Count, 9, 15);
            Assert.Equal(1, pool.LiveAssetCount);
            Assert.Equal(0, sharedAsset!.DisposeCount);
        }

        foreach (var lease in cellAssets.Values) lease.Dispose();
        cellAssets.Clear();
        Assert.Equal(0, pool.TotalReferenceCount);
        Assert.Equal(0, pool.LiveAssetCount);
        Assert.Equal(1, sharedAsset!.DisposeCount);
    }

    private sealed class AssetProbe : System.IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}

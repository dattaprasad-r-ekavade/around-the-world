using Ember.World;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RpgSlice;

/// <summary>Checks retained-cell unload events release shared assets only after their final user leaves.</summary>
internal static class RpgSliceRetentionSmoke
{
    public static void Run(WorldManifest world)
    {
        using var pool = new CellAssetReferencePool<string, SmokeSharedAsset>(StringComparer.OrdinalIgnoreCase);
        var ring = world.CreateLoadingRing(radiusInCells: 1, retentionRadiusInCells: 2);
        var leases = new Dictionary<ExteriorCellCoordinate, CellAssetReference<string, SmokeSharedAsset>>();
        SmokeSharedAsset? sharedAsset = null;

        void Apply(Vector3 position)
        {
            var update = ring.UpdatePlayerPosition(position);
            foreach (var coordinate in update.Entered)
            {
                var lease = pool.Acquire("Content/Shared/BlockoutTexture", () => sharedAsset = new SmokeSharedAsset());
                sharedAsset ??= lease.Asset;
                leases.Add(coordinate, lease);
            }
            foreach (var coordinate in update.Left)
            {
                leases[coordinate].Dispose();
                leases.Remove(coordinate);
            }

            if (pool.TotalReferenceCount != leases.Count)
                throw new InvalidOperationException("Cell asset references do not match the retained-cell set.");
        }

        Apply(Vector3.Zero);
        for (var cellX = 1; cellX <= 20; cellX++)
        {
            Apply(new Vector3(cellX * world.ExteriorCellWidth, 0f, 0f));
            if (leases.Count is < 9 or > 15 || pool.LiveAssetCount != 1 || sharedAsset?.DisposeCount != 0)
                throw new InvalidOperationException("Repeated cell crossings accumulated or prematurely released a shared asset.");
        }

        foreach (var lease in leases.Values) lease.Dispose();
        if (pool.LiveAssetCount != 0 || sharedAsset?.DisposeCount != 1)
            throw new InvalidOperationException("The final retained cell did not release its shared asset.");
        Console.WriteLine("RpgSlice: retention smoke passed 20 boundary crossings; shared assets were released at the final reference.");
    }

    private sealed class SmokeSharedAsset : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}

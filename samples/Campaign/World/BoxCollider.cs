using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

/// <summary>
/// Swept circle vs AABB in XZ. Floor and ceiling slabs are skipped so you walk on them
/// instead of into them. Spatial hash so a town of dozens of boxes stays cheap.
/// </summary>
public sealed class BoxCollider
{
    private const float Cell = 8f;
    private readonly List<WorldBox> _solids = new();
    private readonly Dictionary<(int, int), List<int>> _hash = new();

    public void Rebuild(IEnumerable<WorldBox> boxes)
    {
        _solids.Clear();
        _hash.Clear();

        foreach (var box in boxes)
        {
            var height = box.Max.Y - box.Min.Y;
            if (height < 0.55f) continue;

            var index = _solids.Count;
            _solids.Add(box);

            var minX = CellOf(box.Min.X);
            var maxX = CellOf(box.Max.X);
            var minZ = CellOf(box.Min.Z);
            var maxZ = CellOf(box.Max.Z);
            for (var z = minZ; z <= maxZ; z++)
            for (var x = minX; x <= maxX; x++)
            {
                var key = (x, z);
                if (!_hash.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    _hash[key] = list;
                }

                list.Add(index);
            }
        }
    }

    public Vector3 Resolve(Vector3 origin, Vector3 delta, float radius)
    {
        var x = origin.X + delta.X;
        var z = origin.Z;
        if (Hits(x, z, origin.Y, radius)) x = origin.X;

        z = origin.Z + delta.Z;
        if (Hits(x, z, origin.Y, radius)) z = origin.Z;

        return new Vector3(x, origin.Y, z);
    }

    private bool Hits(float x, float z, float eyeY, float radius)
    {
        var bodyMinY = eyeY - 1.6f;
        var bodyMaxY = eyeY + 0.3f;
        var cellX = CellOf(x);
        var cellZ = CellOf(z);

        for (var dz = -1; dz <= 1; dz++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (!_hash.TryGetValue((cellX + dx, cellZ + dz), out var list)) continue;

            foreach (var index in list)
            {
                var box = _solids[index];
                if (bodyMaxY < box.Min.Y || bodyMinY > box.Max.Y) continue;

                var nearestX = Math.Clamp(x, box.Min.X, box.Max.X);
                var nearestZ = Math.Clamp(z, box.Min.Z, box.Max.Z);
                var ox = x - nearestX;
                var oz = z - nearestZ;
                if (ox * ox + oz * oz < radius * radius) return true;
            }
        }

        return false;
    }

    private static int CellOf(float metres) => (int)MathF.Floor(metres / Cell);
}

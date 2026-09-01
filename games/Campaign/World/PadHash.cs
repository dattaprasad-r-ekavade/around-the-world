using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

/// <summary>Town and dungeon pads hashed so a continent of sites is not an O(n) walk.</summary>
public sealed class PadHash
{
    public const float Cell = 256f;

    private readonly Vector3[] _pads;
    private readonly Dictionary<(int, int), List<int>> _cells = new();

    public PadHash(Vector3[] pads)
    {
        _pads = pads;
        for (var i = 0; i < pads.Length; i++)
        {
            var key = CellOf(pads[i].X, pads[i].Z);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                _cells[key] = list;
            }

            list.Add(i);
        }
    }

    public Vector3[] Pads => _pads;

    public void Query(float x, float z, float radius, List<int> into)
    {
        into.Clear();
        var r2 = radius * radius;
        var minX = (int)MathF.Floor((x - radius) / Cell);
        var maxX = (int)MathF.Floor((x + radius) / Cell);
        var minZ = (int)MathF.Floor((z - radius) / Cell);
        var maxZ = (int)MathF.Floor((z + radius) / Cell);
        for (var cz = minZ; cz <= maxZ; cz++)
        for (var cx = minX; cx <= maxX; cx++)
        {
            if (!_cells.TryGetValue((cx, cz), out var list)) continue;
            foreach (var index in list)
            {
                var pad = _pads[index];
                var dx = pad.X - x;
                var dz = pad.Z - z;
                if (dx * dx + dz * dz <= r2)
                    into.Add(index);
            }
        }
    }

    public bool Any(float x, float z, float radius)
    {
        var r2 = radius * radius;
        var minX = (int)MathF.Floor((x - radius) / Cell);
        var maxX = (int)MathF.Floor((x + radius) / Cell);
        var minZ = (int)MathF.Floor((z - radius) / Cell);
        var maxZ = (int)MathF.Floor((z + radius) / Cell);
        for (var cz = minZ; cz <= maxZ; cz++)
        for (var cx = minX; cx <= maxX; cx++)
        {
            if (!_cells.TryGetValue((cx, cz), out var list)) continue;
            foreach (var index in list)
            {
                var pad = _pads[index];
                var dx = pad.X - x;
                var dz = pad.Z - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
        }

        return false;
    }

    private static (int, int) CellOf(float x, float z) =>
        ((int)MathF.Floor(x / Cell), (int)MathF.Floor(z / Cell));
}

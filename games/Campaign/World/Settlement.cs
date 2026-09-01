using Ember.Render;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

public sealed class Settlement
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Vector3 Pad { get; init; }
    public required BiomeKind Biome { get; init; }
    public Vector3 Gate { get; private set; }
    public List<WorldBox> Buildings { get; } = new();
    public List<BillboardProp> Props { get; } = new();

    public static Settlement Build(int id, string name, Vector3 pad, BiomeKind biome, int seed,
        bool cottage = false, bool city = false)
    {
        var town = new Settlement { Id = id, Name = name, Pad = pad, Biome = biome, Gate = pad };
        var rng = new Random(seed);
        var plaster = biome switch
        {
            BiomeKind.Desert => new Color(196, 168, 118),
            BiomeKind.Snow or BiomeKind.Mountain => new Color(150, 148, 152),
            BiomeKind.Forest => new Color(118, 92, 64),
            BiomeKind.Marsh => new Color(108, 118, 86),
            _ => new Color(168, 156, 138)
        };
        var timber = new Color(146, 108, 68);
        var stone = new Color(150, 142, 130);
        var y = pad.Y;

        if (cottage)
        {
            var cottages = 2 + rng.Next(2);
            for (var i = 0; i < cottages; i++)
                AddHouse(town, pad, y, 3.5f + (float)rng.NextDouble() * 6f,
                    (float)(rng.NextDouble() * MathF.Tau), 3.6f, 3.6f, plaster, timber, rng);
            AddPeople(town, pad, y, 2, rng);
            UnclipProps(town.Props, town.Buildings);
            return town;
        }

        var half = city ? 48f : 34f;
        var thick = city ? 2.6f : 2.2f;
        var wallH = city ? 12.4f : 9.8f;
        var gateW = city ? 5.2f : 4.4f;
        town.Gate = new Vector3(pad.X, y, pad.Z + half + 3.2f);

        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X - half, y - 0.08f, pad.Z - half),
            new Vector3(pad.X + half, y + 0.18f, pad.Z + half),
            stone, "stone"));

        AddCurtain(town, pad, y, half, thick, wallH, gateW, stone);

        var grid = city ? 4 : 3;
        var spacing = city ? 16f : 13.5f;
        for (var gz = -grid; gz <= grid; gz++)
        for (var gx = -grid; gx <= grid; gx++)
        {
            if (gx == 0 && gz == 0) continue;
            if (gx == 0 && gz > 0) continue;
            if (Math.Abs(gx) + Math.Abs(gz) > 5 && rng.NextDouble() < 0.3) continue;
            if (rng.NextDouble() < 0.1) continue;
            var cx = pad.X + gx * spacing + ((float)rng.NextDouble() - 0.5f) * 1.2f;
            var cz = pad.Z + gz * spacing + ((float)rng.NextDouble() - 0.5f) * 1.2f;
            if (MathF.Abs(cx - pad.X) > half - 10f) continue;
            if (cz < pad.Z - half + 10f || cz > pad.Z + half - 12f) continue;
            var w = 4.6f + (float)rng.NextDouble() * 2.6f;
            var d = 4.6f + (float)rng.NextDouble() * 2.6f;
            var h = 4.8f + (float)rng.NextDouble() * 3.6f;
            AddHouseAt(town, cx, y, cz, w, d, h, plaster, timber, rng);
        }

        if (biome == BiomeKind.Desert || id % 5 == 0)
            AddKeep(town, pad + new Vector3(0f, 0f, -12f), y, plaster, timber);

        var tree = biome == BiomeKind.Desert || biome == BiomeKind.Coast ? "palm" : "oak";
        var trees = 10 + rng.Next(6);
        for (var i = 0; i < trees; i++)
        {
            var angle = (float)(rng.NextDouble() * MathF.Tau);
            var dist = 10f + (float)rng.NextDouble() * 18f;
            var tx = pad.X + MathF.Cos(angle) * dist;
            var tz = pad.Z + MathF.Sin(angle) * dist;
            if (tz > pad.Z + half - 14f && MathF.Abs(tx - pad.X) < 8f) continue;
            var th = tree == "palm" ? 6.2f + (float)rng.NextDouble() * 2.4f
                : 4.4f + (float)rng.NextDouble() * 1.8f;
            TryTree(town, tx, y, tz, th, tree);
        }

        AddPeople(town, pad, y, 6 + rng.Next(5), rng);
        var watchX = pad.X + gateW + 5.6f;
        var watchZ = pad.Z + half - 6.8f;
        if (HitsObstacle(town.Buildings, watchX, y, watchZ, 1.05f, 1.85f))
            watchX = pad.X - gateW - 5.6f;
        if (!HitsObstacle(town.Buildings, watchX, y, watchZ, 1.05f, 1.85f))
        {
            town.Props.Add(new BillboardProp("watch",
                new Vector3(watchX, y, watchZ), 1.85f, MathF.PI, Color.White));
        }

        UnclipProps(town.Props, town.Buildings);
        return town;
    }

    private static void AddCurtain(Settlement town, Vector3 pad, float y,
        float half, float thick, float height, float gate, Color stone)
    {
        var x0 = pad.X - half;
        var x1 = pad.X + half;
        var z0 = pad.Z - half;
        var z1 = pad.Z + half;

        town.Buildings.Add(new WorldBox(
            new Vector3(x0, y, z0), new Vector3(x1, y + height, z0 + thick), stone, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(x0, y, z0), new Vector3(x0 + thick, y + height, z1), stone, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(x1 - thick, y, z0), new Vector3(x1, y + height, z1), stone, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(x0, y, z1 - thick), new Vector3(pad.X - gate, y + height, z1), stone, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X + gate, y, z1 - thick), new Vector3(x1, y + height, z1), stone, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X - gate, y + 5.6f, z1 - thick),
            new Vector3(pad.X + gate, y + height, z1), stone, "stone"));

        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X - gate - 4.2f, y, z1 - 4.2f),
            new Vector3(pad.X - gate, y + height + 3.6f, z1), stone, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X + gate, y, z1 - 4.2f),
            new Vector3(pad.X + gate + 4.2f, y + height + 3.6f, z1), stone, "stone"));

        ShutGate(town, pad.X - gate, pad.X + gate, y, z1 - thick, z1, 5.6f);

        CrenelRow(town, x0, x1, z0, z0 + thick, y + height, stone);
        CrenelRow(town, x0, x0 + thick, z0, z1, y + height, stone);
        CrenelRow(town, x1 - thick, x1, z0, z1, y + height, stone);
        CrenelRow(town, x0, pad.X - gate, z1 - thick, z1, y + height, stone);
        CrenelRow(town, pad.X + gate, x1, z1 - thick, z1, y + height, stone);
    }

    private static readonly Color GateOak = new(86, 56, 34);
    private static readonly Color GateIron = new(68, 66, 70);
    private static readonly Color GateLeaf = new(108, 74, 46);

    private static void ShutGate(Settlement town, float x0, float x1, float y,
        float zInner, float zOuter, float leafTop)
    {
        town.Buildings.Add(new WorldBox(
            new Vector3(x0, y, zInner), new Vector3(x1, y + leafTop, zOuter),
            GateOak, "timber"));
        var face = zInner - 0.12f;
        var mid = (x0 + x1) * 0.5f;
        town.Buildings.Add(new WorldBox(
            new Vector3(x0 + 0.12f, y + 0.06f, face),
            new Vector3(mid - 0.08f, y + leafTop - 0.12f, zInner),
            GateLeaf, "timber"));
        town.Buildings.Add(new WorldBox(
            new Vector3(mid + 0.08f, y + 0.06f, face),
            new Vector3(x1 - 0.12f, y + leafTop - 0.12f, zInner),
            GateLeaf, "timber"));
        town.Buildings.Add(new WorldBox(
            new Vector3(mid - 0.1f, y + 0.06f, face - 0.04f),
            new Vector3(mid + 0.1f, y + leafTop - 0.08f, zInner),
            GateIron, "stone"));
        for (var t = 0.8f; t < leafTop - 0.4f; t += 1.7f)
        {
            town.Buildings.Add(new WorldBox(
                new Vector3(x0 + 0.16f, y + t, face - 0.03f),
                new Vector3(x1 - 0.16f, y + t + 0.14f, zInner),
                GateIron, "stone"));
        }
    }

    private static void CrenelRow(Settlement town, float x0, float x1, float z0, float z1,
        float top, Color stone)
    {
        const float Step = 5.2f;
        const float Tooth = 1.35f;
        var alongX = x1 - x0 >= z1 - z0;
        if (alongX)
        {
            for (var x = x0 + 0.5f; x + Tooth < x1; x += Step)
                town.Buildings.Add(new WorldBox(
                    new Vector3(x, top, z0),
                    new Vector3(MathF.Min(x + Tooth, x1), top + 1.25f, z1),
                    stone, "stone"));
        }
        else
        {
            for (var z = z0 + 0.5f; z + Tooth < z1; z += Step)
                town.Buildings.Add(new WorldBox(
                    new Vector3(x0, top, z),
                    new Vector3(x1, top + 1.25f, MathF.Min(z + Tooth, z1)),
                    stone, "stone"));
        }
    }

    private static void TryTree(Settlement town, float x, float y, float z, float height,
        string sprite)
    {
        var radius = height * 0.45f + 0.8f;
        if (HitsWall(town.Buildings, x, y, z, radius, height)) return;
        town.Props.Add(new BillboardProp(sprite, new Vector3(x, y, z), height, 0f, Color.White));
    }

    public static bool HitsWall(IReadOnlyList<WorldBox> boxes, float x, float y, float z,
        float radius, float height) =>
        HitsVolume(boxes, x, y, z, radius, height, furniture: false);

    public static bool HitsObstacle(IReadOnlyList<WorldBox> boxes, float x, float y, float z,
        float radius, float height) =>
        HitsVolume(boxes, x, y, z, radius, height, furniture: true);

    public static void UnclipProps(List<BillboardProp> props, IReadOnlyList<WorldBox> boxes)
    {
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            if (!Stands(prop.Sprite)) continue;
            var feet = prop.Feet;
            var radius = MathF.Max(0.85f, prop.Height * 0.28f);
            if (!HitsObstacle(boxes, feet.X, feet.Y, feet.Z, radius, prop.Height)) continue;
            if (!TryClear(boxes, ref feet, radius, prop.Height)) continue;
            props[i] = prop with { Feet = feet };
        }
    }

    public static void UnclipBlock(MapBlock block)
    {
        for (var i = 0; i < block.Props.Count; i++)
        {
            var prop = block.Props[i];
            if (!Stands(prop.Sprite)) continue;
            var feet = prop.Feet;
            var radius = MathF.Max(0.85f, prop.Height * 0.28f);
            if (!HitsObstacle(block.Boxes, feet.X, feet.Y, feet.Z, radius, prop.Height)) continue;
            var old = feet;
            if (!TryClear(block.Boxes, ref feet, radius, prop.Height)) continue;
            block.Props[i] = prop with { Feet = feet };
            var delta = feet - old;
            for (var m = 0; m < block.Markers.Count; m++)
            {
                var marker = block.Markers[m];
                var dx = marker.Position.X - old.X;
                var dz = marker.Position.Z - old.Z;
                if (dx * dx + dz * dz > 2.6f * 2.6f) continue;
                block.Markers[m] = marker with { Position = marker.Position + delta };
            }
        }
    }

    private static bool Stands(string sprite) =>
        sprite is not ("pine" or "oak" or "palm" or "boulder" or "mouth" or "key"
            or "mappin" or "wagon" or "fire" or "sword");

    private static bool HitsVolume(IReadOnlyList<WorldBox> boxes, float x, float y, float z,
        float radius, float height, bool furniture)
    {
        var top = y + height;
        foreach (var box in boxes)
        {
            if (furniture)
            {
                if (box.Max.Y < y + 0.28f) continue;
            }
            else if (box.Max.Y - box.Min.Y < 0.55f)
            {
                continue;
            }

            if (top < box.Min.Y || y > box.Max.Y) continue;
            var nx = Math.Clamp(x, box.Min.X, box.Max.X);
            var nz = Math.Clamp(z, box.Min.Z, box.Max.Z);
            var dx = x - nx;
            var dz = z - nz;
            if (dx * dx + dz * dz < radius * radius) return true;
        }

        return false;
    }

    private static bool TryClear(IReadOnlyList<WorldBox> boxes, ref Vector3 feet,
        float radius, float height)
    {
        ReadOnlySpan<float> steps = [0.6f, 1.1f, 1.7f, 2.4f, 3.2f];
        for (var s = 0; s < steps.Length; s++)
        for (var dir = 0; dir < 8; dir++)
        {
            var a = dir * MathF.PI * 0.25f;
            var x = feet.X + MathF.Cos(a) * steps[s];
            var z = feet.Z + MathF.Sin(a) * steps[s];
            if (HitsObstacle(boxes, x, feet.Y, z, radius, height)) continue;
            feet = new Vector3(x, feet.Y, z);
            return true;
        }

        return false;
    }

    private static void AddHouse(Settlement town, Vector3 pad, float y, float dist, float angle,
        float minSize, float heightBase, Color plaster, Color timber, Random rng)
    {
        var cx = pad.X + MathF.Cos(angle) * dist;
        var cz = pad.Z + MathF.Sin(angle) * dist;
        var w = minSize + (float)rng.NextDouble() * 2.4f;
        var d = minSize + (float)rng.NextDouble() * 2.4f;
        var h = heightBase + (float)rng.NextDouble() * 2.2f;
        AddHouseAt(town, cx, y, cz, w, d, h, plaster, timber, rng);
    }

    private static void AddHouseAt(Settlement town, float cx, float y, float cz,
        float w, float d, float h, Color plaster, Color timber, Random rng)
    {
        var material = rng.NextDouble() > 0.4 ? "stone" : "timber";
        var colour = material == "timber" ? timber : plaster;
        town.Buildings.Add(new WorldBox(
            new Vector3(cx - w * 0.5f, y, cz - d * 0.5f),
            new Vector3(cx + w * 0.5f, y + h, cz + d * 0.5f),
            colour, material));
        town.Buildings.Add(new WorldBox(
            new Vector3(cx - w * 0.58f, y + h - 0.12f, cz - d * 0.58f),
            new Vector3(cx + w * 0.58f, y + h + 0.7f, cz + d * 0.58f),
            new Color(92, 58, 42), "timber"));
    }

    private static void AddKeep(Settlement town, Vector3 pad, float y, Color plaster, Color timber)
    {
        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X - 6.2f, y, pad.Z - 6.2f),
            new Vector3(pad.X + 6.2f, y + 7.2f, pad.Z + 6.2f),
            plaster, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X - 4.4f, y + 7.0f, pad.Z - 4.4f),
            new Vector3(pad.X + 4.4f, y + 10.8f, pad.Z + 4.4f),
            plaster, "stone"));
        town.Buildings.Add(new WorldBox(
            new Vector3(pad.X - 2.4f, y + 10.5f, pad.Z - 2.4f),
            new Vector3(pad.X + 2.4f, y + 13.6f, pad.Z + 2.4f),
            new Color(92, 58, 42), "timber"));
        _ = timber;
    }

    private static void AddPeople(Settlement town, Vector3 pad, float y, int count, Random rng)
    {
        var names = new[] { "traveler", "watch", "trader", "innkeep" };
        var placed = 0;
        for (var attempt = 0; attempt < count * 16 && placed < count; attempt++)
        {
            var angle = (float)(rng.NextDouble() * MathF.Tau);
            var dist = 3.4f + (float)rng.NextDouble() * 7.2f;
            var x = pad.X + MathF.Cos(angle) * dist;
            var z = pad.Z + MathF.Sin(angle) * dist;
            if (HitsObstacle(town.Buildings, x, y, z, 1.05f, 1.85f)) continue;
            town.Props.Add(new BillboardProp(names[rng.Next(names.Length)],
                new Vector3(x, y, z), 1.85f, (float)rng.NextDouble() * MathF.Tau, Color.White));
            placed++;
        }
    }
}

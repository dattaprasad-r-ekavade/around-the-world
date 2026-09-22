using Ember.Render;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

public static class TownGenerator
{
    private static readonly string[] Names = ["Hearthford", "Millcross", "Dunwick"];

    public static string NameOf(int index) => Names[Math.Clamp(index, 0, Names.Length - 1)];

    public static MapBlock Generate(int seed, string townName) =>
        TownLayout.Build(seed, townName);

    public static MapBlock[] Interiors() =>
    [
        BlockLibrary.ShopInterior(),
        BlockLibrary.InnInterior(),
        BlockLibrary.HouseInterior(),
        BlockLibrary.FightersHall(),
        BlockLibrary.MagesHall(),
        BlockLibrary.Temple()
    ];
}

/// <summary>Every load-in town: curtain walls, south gate, flagstone yard, named buildings.</summary>
public static class TownLayout
{
    public static MapBlock Build(int seed, string townName)
    {
        var rng = new Random(seed);
        var assembled = new MapBlock();
        BlockLibrary.TownCurtain(townName).AppendTo(assembled);
        BlockLibrary.Plaza().Translated(Vector3.Zero).AppendTo(assembled);

        BlockLibrary.HouseShell(0, "General Store", "north-west", DoorSide.PosZ)
            .Translated(new Vector3(-16f, 0f, -16f)).AppendTo(assembled);
        BlockLibrary.HouseShell(1, "The Resting Hound", "north-east", DoorSide.PosZ)
            .Translated(new Vector3(16f, 0f, -16f)).AppendTo(assembled);
        BlockLibrary.HouseShell(2, "A Private House", "west", DoorSide.PosX)
            .Translated(new Vector3(-16f, 0f, 0f)).AppendTo(assembled);
        BlockLibrary.HouseShell(3, "Fighters Guild", "east", DoorSide.NegX)
            .Translated(new Vector3(16f, 0f, 0f)).AppendTo(assembled);
        BlockLibrary.HouseShell(4, "Mages Guild", "south-west", DoorSide.NegZ)
            .Translated(new Vector3(-16f, 0f, 16f)).AppendTo(assembled);
        BlockLibrary.HouseShell(5, "Temple of Kynareth", "south-east", DoorSide.NegZ)
            .Translated(new Vector3(16f, 0f, 16f)).AppendTo(assembled);

        assembled.Props.Add(new BillboardProp("watch",
            new Vector3(8.0f, 0f, 30.6f), 1.85f, 3.5f, Color.White));
        assembled.Markers.Add(new Marker(MarkerKind.Talk,
            new Vector3(8.0f, 0f, 30.6f), 2.4f, "Talk to the watch", 2));
        var wander = (float)(rng.NextDouble() * 5.0 + 2.4);
        assembled.Props.Add(new BillboardProp("traveler",
            new Vector3(wander, 0f, 8.4f), 1.85f, 0.2f, Color.White));
        assembled.Markers.Add(new Marker(MarkerKind.Talk,
            new Vector3(wander, 0f, 8.4f), 2.4f, "Talk to a traveler", 3));
        assembled.Spawn = new Vector3(0f, WorldScale.EyeHeight, 30.0f);
        assembled.Lights.Add(new PointLight(new Vector3(0f, 4.2f, 8f),
            new Vector3(1f, 0.86f, 0.62f) * 2.2f, 22f));
        return assembled;
    }
}

public static class DungeonGenerator
{
    private static readonly string[] Names =
        ["Old Cellars", "Witherbarrow", "Salt Hollow", "Red Pit", "Grey Stair"];

    public static string NameOf(int index) => Names[Math.Clamp(index, 0, Names.Length - 1)];

    public static MapBlock Generate(int seed, int rooms)
    {
        rooms = Math.Clamp(rooms, WorldScale.DungeonRoomMin, WorldScale.DungeonRoomMax);
        var rng = new Random(seed);
        var assembled = new MapBlock();
        var z = 0f;
        const float Step = WorldScale.BlockMetres;

        BlockLibrary.DungeonEntrance().Translated(new Vector3(0f, 0f, z)).AppendTo(assembled);

        for (var i = 1; i < rooms - 1; i++)
        {
            z += Step;
            MapBlock tile = (i % 3) switch
            {
                1 => BlockLibrary.Corridor(),
                2 => rng.NextDouble() > 0.4 ? BlockLibrary.Chamber() : BlockLibrary.Junction(),
                _ => BlockLibrary.DeadEnd()
            };

            if (i == rooms / 2)
            {
                BlockLibrary.Chamber().Translated(new Vector3(0f, 0f, z)).AppendTo(assembled);
                z += Step;
                BlockLibrary.LockedGate().Translated(new Vector3(0f, 0f, z)).AppendTo(assembled);
                z += Step;
                tile = BlockLibrary.Vault();
            }
            tile.Translated(new Vector3(0f, 0f, z)).AppendTo(assembled);
        }

        z += Step;
        BlockLibrary.DungeonExit().Translated(new Vector3(0f, 0f, z)).AppendTo(assembled);
        assembled.Spawn = new Vector3(0f, WorldScale.EyeHeight, 3.6f);
        return assembled;
    }
}

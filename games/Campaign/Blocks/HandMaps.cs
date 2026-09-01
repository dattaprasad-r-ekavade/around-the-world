using Ember.Render;
using Microsoft.Xna.Framework;

namespace Campaign;

/// <summary>One hand-built town and one hand-built dungeon. Walkable before generation.</summary>
public static class HandMaps
{
    public const string TownName = "Hearthford";
    public const string DungeonName = "Old Cellars";

    public static MapBlock Town() => TownLayout.Build(0, TownName);

    public static MapBlock[] TownInteriors() =>
    [
        BlockLibrary.ShopInterior(),
        BlockLibrary.InnInterior(),
        BlockLibrary.HouseInterior(),
        BlockLibrary.FightersHall(),
        BlockLibrary.MagesHall(),
        BlockLibrary.Temple()
    ];

    public static MapBlock Dungeon()
    {
        var assembled = new MapBlock();
        const float Step = 16f;
        BlockLibrary.DungeonEntrance().Translated(new Vector3(0f, 0f, 0f)).AppendTo(assembled);
        BlockLibrary.Corridor().Translated(new Vector3(0f, 0f, Step)).AppendTo(assembled);
        BlockLibrary.Chamber().Translated(new Vector3(0f, 0f, Step * 2f)).AppendTo(assembled);
        BlockLibrary.LockedGate().Translated(new Vector3(0f, 0f, Step * 3f)).AppendTo(assembled);
        BlockLibrary.Vault().Translated(new Vector3(0f, 0f, Step * 4f)).AppendTo(assembled);
        BlockLibrary.Corridor().Translated(new Vector3(0f, 0f, Step * 5f)).AppendTo(assembled);
        BlockLibrary.DungeonExit().Translated(new Vector3(0f, 0f, Step * 6f)).AppendTo(assembled);
        assembled.Spawn = new Vector3(0f, WorldScale.EyeHeight, 3.6f);
        return assembled;
    }
}

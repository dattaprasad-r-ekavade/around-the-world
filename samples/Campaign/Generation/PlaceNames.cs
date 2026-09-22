using System;

namespace Campaign;

public static class PlaceNames
{
    private static readonly string[] Prefix =
    [
        "Hart", "Mill", "Dun", "Grey", "Red", "Salt", "Ash", "Frost", "Gold", "Thorn",
        "Wick", "Stan", "Elm", "Moss", "High", "Low", "East", "West", "Black", "White",
        "Oak", "Fen", "Ridge", "Storm", "Iron", "Copper", "Wolf", "Raven", "Hollow", "Bright"
    ];

    private static readonly string[] Suffix =
    [
        "ford", "ham", "bury", "wick", "mouth", "gate", "hold", "mere", "fell", "toft",
        "by", "kirk", "field", "haven", "stead", "bridge", "cross", "well", "barrow", "spire"
    ];

    private static readonly string[] Dungeon =
    [
        "Old Cellars", "Witherbarrow", "Salt Hollow", "Red Pit", "Grey Stair",
        "Ash Vault", "Fen Crypt", "Iron Deep", "Wolf Den", "Raven Pit",
        "Hollow Mine", "Bright Tomb", "Storm Cleft", "Copper Vein", "Moss Delve",
        "Black Well", "White Cairn", "Thorn Shaft", "Gold Cut", "Hart Barrow",
        "Mill Pit", "Dun Vault", "East Barrow", "West Delve", "High Crypt",
        "Low Mine", "Oak Hollow", "Ridge Cut", "Stan Well", "Elm Tomb",
        "Frost Delve", "Sand Crypt", "Marsh Shaft", "Hill Barrow", "Sea Cleft",
        "Moor Pit"
    ];

    public static string Town(int index, int seed, BiomeKind biome)
    {
        if (index == 0) return "Hearthford";
        var rng = new Random(seed * 7919 + index * 104729);
        var name = Prefix[rng.Next(Prefix.Length)] + Suffix[rng.Next(Suffix.Length)];
        return biome switch
        {
            BiomeKind.Desert => name.Contains("ford", StringComparison.Ordinal) ? "Sandspit" : name,
            BiomeKind.Snow => "Frost" + Suffix[rng.Next(Suffix.Length)],
            BiomeKind.Marsh => "Fen" + Suffix[rng.Next(Suffix.Length)],
            _ => name
        };
    }

    private static readonly string[] HoleKind =
        ["Pit", "Delve", "Vault", "Barrow", "Hollow", "Cleft", "Mine", "Crypt", "Well", "Shaft"];

    public static string Hole(int index, int seed)
    {
        if (index == 0) return "Old Cellars";
        if (index > 0 && index < Dungeon.Length) return Dungeon[index];
        var rng = new Random(seed * 13 + index * 97);
        return Prefix[rng.Next(Prefix.Length)] + " " + HoleKind[rng.Next(HoleKind.Length)];
    }
}

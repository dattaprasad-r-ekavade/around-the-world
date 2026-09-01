using Microsoft.Xna.Framework;

namespace Campaign;

public enum BiomeKind
{
    Ocean,
    Coast,
    Marsh,
    Grass,
    Forest,
    Desert,
    Hills,
    Mountain,
    Snow
}

public static class Biomes
{
    public static Color Ground(BiomeKind biome) => biome switch
    {
        BiomeKind.Ocean => new Color(42, 72, 96),
        BiomeKind.Coast => new Color(168, 150, 112),
        BiomeKind.Marsh => new Color(72, 92, 58),
        BiomeKind.Grass => new Color(92, 118, 62),
        BiomeKind.Forest => new Color(48, 78, 42),
        BiomeKind.Desert => new Color(186, 152, 92),
        BiomeKind.Hills => new Color(110, 108, 78),
        BiomeKind.Mountain => new Color(118, 118, 124),
        BiomeKind.Snow => new Color(214, 222, 228),
        _ => new Color(100, 96, 80)
    };

    public static Color Sky(BiomeKind biome) => biome switch
    {
        BiomeKind.Desert => new Color(168, 186, 214),
        BiomeKind.Snow or BiomeKind.Mountain => new Color(176, 188, 204),
        BiomeKind.Marsh => new Color(118, 148, 152),
        BiomeKind.Forest => new Color(112, 152, 186),
        BiomeKind.Ocean or BiomeKind.Coast => new Color(128, 168, 196),
        _ => new Color(132, 172, 210)
    };

    public static Color Horizon(BiomeKind biome) => biome switch
    {
        BiomeKind.Desert => new Color(210, 198, 176),
        BiomeKind.Snow or BiomeKind.Mountain => new Color(198, 206, 214),
        BiomeKind.Marsh => new Color(168, 178, 170),
        BiomeKind.Ocean or BiomeKind.Coast => new Color(186, 204, 214),
        _ => new Color(196, 206, 214)
    };

    public static string Label(BiomeKind biome) => biome switch
    {
        BiomeKind.Ocean => "sea",
        BiomeKind.Coast => "coast",
        BiomeKind.Marsh => "marsh",
        BiomeKind.Grass => "grassland",
        BiomeKind.Forest => "forest",
        BiomeKind.Desert => "desert",
        BiomeKind.Hills => "hills",
        BiomeKind.Mountain => "mountains",
        BiomeKind.Snow => "tundra",
        _ => "wilds"
    };
}

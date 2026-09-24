using Ember.World;
using Microsoft.Xna.Framework;

namespace Campaign;

/// <summary>Bridges Campaign's seeded biome/height rules to the game-independent terrain grid.</summary>
internal sealed class CampaignTerrainSurfaceSource : ITerrainHeightMaterialSource
{
    private readonly WorldHeights _heights;
    private readonly HeightNoise _noise;

    public CampaignTerrainSurfaceSource(WorldHeights heights, HeightNoise noise)
    {
        _heights = heights;
        _noise = noise;
    }

    public TerrainSurfaceSample Sample(float worldX, float worldZ)
    {
        var height = _heights.Sample(worldX, worldZ);
        var biome = _noise.BiomeAt(worldX, worldZ);
        var tint = _heights.PadNear(worldX, worldZ, 50f)
            ? new Color(168, 148, 116)
            : height < WorldScale.WaterLevel
                ? Color.Lerp(new Color(70, 96, 88), Biomes.Ground(BiomeKind.Ocean), 0.45f)
                : Color.Lerp(Color.White, Biomes.Ground(biome), 0.62f);
        return new TerrainSurfaceSample(height, tint);
    }
}

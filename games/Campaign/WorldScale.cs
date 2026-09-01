namespace Campaign;

public static class WorldScale
{
    /// <summary>
    /// Iliac Bay is cited at 161,600 square miles: about 647 km on a side if it were square.
    /// 655.36 km is that size without a stored height grid — only the streaming ring is meshed.
    /// </summary>
    public const float WorldMetres = 655360f;
    public const float ChunkMetres = 128f;
    public const float VertexSpacing = 8f;
    public const float BlockMetres = 16f;
    public const float FarPlane = 320f;
    public const float FogStart = 175f;
    public const float FogEnd = 305f;
    public const float WaterLevel = 4f;
    public const float EyeHeight = 1.7f;
    public const float HorseEye = 2.4f;
    public const float HorseWalk = 18f;
    public const float HorseSprint = 30f;
    public const float HorseRadius = 0.7f;
    public const float WagonWalk = 14f;
    public const float WagonSprint = 22f;
    public const float WagonEye = 2.15f;
    public const float WagonRadius = 1.05f;
    public const float SwimSpeed = 3.7f;
    public const float SwimSprint = 6.4f;

    /// <summary>Water deeper than this, in metres, is a swim rather than a wade.</summary>
    public const float WadeDepth = 1.05f;
    public const float TownPadRadius = 70f;
    public const int ChunkRing = 2;
    public const int TownCount = 8192;
    public const int DungeonMouthCount = 6144;
    public const int DungeonRoomMin = 8;
    public const int DungeonRoomMax = 12;
    public const int DefaultSeed = 1742;
    public const int MapSize = 512;
    public const int MapCityStride = 32;

    /// <summary>Rise over run. About 46 degrees; steeper ground is a wall.</summary>
    public const float MaxWalkSlope = 1.05f;

    /// <summary>A lip this high is a step, not a slope test.</summary>
    public const float StepHeight = 0.55f;

    /// <summary>How many game seconds pass per real second. A day is about twelve minutes.</summary>
    public const float GameSecondsPerReal = 120f;

    public const float FatigueMax = 100f;

    /// <summary>Map travel. The horse is assumed; a ship beats it on the coast.</summary>
    public const float RoadHorseKph = 15f;
    public const float RoadShipKph = 28f;
    public const float RoadHoursMin = 0.45f;
    public const float RoadHoursMax = 28f;
    public const float RoadHungerPerHour = 2.1f;
    public const float RoadFatiguePerHour = 1.9f;

    public const float HungerIdle = 1.05f;
    public const float HungerWalk = 2.15f;
    public const float HungerSprint = 3.6f;
    public const float HungerIndoor = 0.55f;

    public const float EncounterMetres = 240f;
    public const float EncounterDay = 0.055f;
    public const float EncounterNight = 0.13f;
    public const float EncounterFog = 0.05f;
    public const float RoadAmbushHours = 3.2f;
    public const float RoadAmbushChance = 0.26f;
    public const float CampWolfChance = 0.22f;

    public const float UiScaleMin = 0.75f;
    public const float UiScaleMax = 1.35f;
    public const float UiScaleStep = 0.08f;
}

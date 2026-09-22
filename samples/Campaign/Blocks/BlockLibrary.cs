using Ember.Render;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

public enum DoorSide
{
    PosZ,
    NegZ,
    PosX,
    NegX
}

/// <summary>Prefab tiles the generator stitches. Dungeon tiles are 16 m; interiors are their own size.</summary>
public static class BlockLibrary
{
    private static readonly Color Stone = new(150, 142, 130);
    private static readonly Color Timber = new(146, 108, 68);
    private static readonly Color Earth = new(120, 102, 78);
    private static readonly Color Plaster = new(168, 156, 138);
    private static readonly Color Wood = new(108, 74, 46);
    private static readonly Color Cloth = new(92, 58, 42);

    private const float Tile = 8f;
    private const float WallT = 0.4f;
    private const float Door = 1.9f;

    public static MapBlock Plaza()
    {
        var block = new MapBlock();
        Floor(block, -8f, -8f, 8f, 8f, Stone, "stone");
        block.Lights.Add(new PointLight(new Vector3(0f, 3.6f, 0f), new Vector3(1f, 0.86f, 0.62f) * 2f, 18f));
        block.Props.Add(new BillboardProp("trader", new Vector3(-5.1f, 0f, 0.2f), 1.85f, 0.6f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Bank,
            new Vector3(-5.1f, 0f, 0.2f), 2.2f, "Speak to the banker", 0));
        HangSign(block, "Bank of the Bay", "plaza",
            new Vector3(-3.15f, 2.48f, -1.6f), MathF.PI * 0.5f, 1.85f, 0.55f);
        Furniture(block, -0.12f, 0f, -0.12f, 0.12f, 3.2f, 0.12f, Wood);
        HangSign(block, "Shop · Inn", "north",
            new Vector3(0f, 2.88f, -0.28f), 0f, 1.55f, 0.48f);
        HangSign(block, "Gate · Temple", "south",
            new Vector3(0f, 2.88f, 0.28f), MathF.PI, 1.7f, 0.48f);
        HangSign(block, "Fighters Guild", "east",
            new Vector3(0.28f, 2.88f, 0f), MathF.PI * 0.5f, 1.7f, 0.48f);
        HangSign(block, "House", "west",
            new Vector3(-0.28f, 2.88f, 0f), -MathF.PI * 0.5f, 1.35f, 0.48f);
        return block;
    }

    /// <summary>
    /// Curtain walls around the load-in town: flagstone yard, high stone, a south gatehouse.
    /// </summary>
    public static MapBlock TownCurtain(string townName = "South Gate")
    {
        const float X0 = -27f;
        const float X1 = 27f;
        const float Z0 = -27f;
        const float Z1 = 36f;
        const float H = 9.6f;
        const float T = 1.9f;
        const float Gate = 2.8f;
        var title = string.IsNullOrWhiteSpace(townName) ? "South Gate" : townName;
        var block = new MapBlock();
        Floor(block, X0, Z0, X1, Z1, Stone, "stone");

        Wall(block, X0, Z0, X1, Z0 + T, H, Stone, "stone");
        Wall(block, X0, Z0, X0 + T, Z1, H, Stone, "stone");
        Wall(block, X1 - T, Z0, X1, Z1, H, Stone, "stone");
        Wall(block, X0, Z1 - T, -Gate, Z1, H, Stone, "stone");
        Wall(block, Gate, Z1 - T, X1, Z1, H, Stone, "stone");
        Slab(block, -Gate, 5.4f, Z1 - T, Gate, H, Z1, Stone, "stone");

        Wall(block, -Gate - 3.6f, Z1 - 3.6f, -Gate, Z1, H + 3.4f, Stone, "stone");
        Wall(block, Gate, Z1 - 3.6f, Gate + 3.6f, Z1, H + 3.4f, Stone, "stone");
        Battlements(block, X0, X1, Z0, Z0 + T, H);
        Battlements(block, X0, X0 + T, Z0, Z1, H);
        Battlements(block, X1 - T, X1, Z0, Z1, H);
        Battlements(block, X0, -Gate, Z1 - T, Z1, H);
        Battlements(block, Gate, X1, Z1 - T, Z1, H);

        ShutGate(block, -Gate, Gate, Z1 - T, Z1, 5.4f);

        HangSign(block, title, "south gate",
            new Vector3(0f, 3.55f, Z1 - T - 0.22f), 0f, 2.35f, 0.64f);
        block.Markers.Add(new Marker(MarkerKind.LeaveTown,
            new Vector3(0f, 0f, Z1 - T - 1.6f), 2.8f, "Leave town", 0));
        block.Lights.Add(new PointLight(new Vector3(0f, 4.4f, Z1 - 6f),
            new Vector3(1f, 0.86f, 0.62f) * 2.2f, 20f));
        block.Spawn = new Vector3(0f, WorldScale.EyeHeight, Z1 - T - 4.2f);
        return block;
    }

    private static readonly Color GateOak = new(86, 56, 34);
    private static readonly Color GateIron = new(68, 66, 70);

    /// <summary>Fills a south gate opening so the outside is not visible.</summary>
    private static void ShutGate(MapBlock block, float x0, float x1, float zInner, float zOuter,
        float leafTop)
    {
        Slab(block, x0, 0f, zInner, x1, leafTop, zOuter, GateOak, "timber");
        var face = zInner - 0.1f;
        var mid = (x0 + x1) * 0.5f;
        Slab(block, x0 + 0.1f, 0.06f, face, mid - 0.08f, leafTop - 0.12f, zInner, Wood, "timber");
        Slab(block, mid + 0.08f, 0.06f, face, x1 - 0.1f, leafTop - 0.12f, zInner, Wood, "timber");
        Slab(block, mid - 0.09f, 0.06f, face - 0.04f, mid + 0.09f, leafTop - 0.08f, zInner, GateIron, "stone");
        for (var t = 0.75f; t < leafTop - 0.35f; t += 1.55f)
            Slab(block, x0 + 0.14f, t, face - 0.03f, x1 - 0.14f, t + 0.13f, zInner, GateIron, "stone");
    }

    private static void Battlements(MapBlock block, float x0, float x1, float z0, float z1, float wallTop)
    {
        const float Step = 4.2f;
        const float Tooth = 1.15f;
        var alongX = x1 - x0 >= z1 - z0;
        if (alongX)
        {
            for (var x = x0 + 0.4f; x + Tooth < x1; x += Step)
                Slab(block, x, wallTop, z0, MathF.Min(x + Tooth, x1), wallTop + 1.15f, z1, Stone, "stone");
        }
        else
        {
            for (var z = z0 + 0.4f; z + Tooth < z1; z += Step)
                Slab(block, x0, wallTop, z, x1, wallTop + 1.15f, MathF.Min(z + Tooth, z1), Stone, "stone");
        }
    }

    public static MapBlock StreetNorthSouth()
    {
        var block = new MapBlock();
        Floor(block, -3.5f, -8f, 3.5f, 8f, Earth, "earth");
        Wall(block, -8f, -8f, -3.5f, 8f, 3.2f, Stone, "stone");
        Wall(block, 3.5f, -8f, 8f, 8f, 3.2f, Stone, "stone");
        Lamp(block, 0f, 0f);
        return block;
    }

    public static MapBlock StreetEastWest()
    {
        var block = new MapBlock();
        Floor(block, -8f, -3.5f, 8f, 3.5f, Earth, "earth");
        Wall(block, -8f, -8f, 8f, -3.5f, 3.2f, Stone, "stone");
        Wall(block, -8f, 3.5f, 8f, 8f, 3.2f, Stone, "stone");
        return block;
    }

    public static MapBlock HouseShell(int interiorId, string name, string bearing, DoorSide door)
    {
        var block = new MapBlock();
        Floor(block, -6f, -6f, 6f, 6f, Stone, "stone");
        const float T = 0.6f;
        const float H = 5.2f;
        const float D = 1.5f;
        GappedZ(block, -6f, 6f, -6f, -6f + T, H, Plaster, "stone", door == DoorSide.NegZ, D);
        GappedZ(block, -6f, 6f, 6f - T, 6f, H, Plaster, "stone", door == DoorSide.PosZ, D);
        GappedX(block, -6f, -6f + T, -6f, 6f, H, Plaster, "stone", door == DoorSide.NegX, D);
        GappedX(block, 6f - T, 6f, -6f, 6f, H, Plaster, "stone", door == DoorSide.PosX, D);
        block.Boxes.Add(new WorldBox(
            new Vector3(-6.4f, H, -6.4f), new Vector3(6.4f, H + 0.45f, 6.4f),
            Timber, "timber"));
        var (mx, mz, ox, oz) = door switch
        {
            DoorSide.NegZ => (0f, -5.7f, 0f, -0.55f),
            DoorSide.PosX => (5.7f, 0f, 0.55f, 0f),
            DoorSide.NegX => (-5.7f, 0f, -0.55f, 0f),
            _ => (0f, 5.7f, 0f, 0.55f)
        };
        DoorLeaf(block, door);
        block.Markers.Add(new Marker(MarkerKind.EnterInterior,
            new Vector3(mx, 0f, mz), 1.8f, $"Enter {name}", interiorId));
        HangSign(block, name, bearing, new Vector3(mx + ox, 3.12f, mz + oz), YawOf(door));
        return block;
    }

    public static MapBlock GateSouth()
    {
        var block = new MapBlock();
        Floor(block, -8f, -8f, 8f, 8f, Earth, "earth");
        Wall(block, -8f, 6.6f, -2.2f, 8f, 4.4f, Stone, "stone");
        Wall(block, 2.2f, 6.6f, 8f, 8f, 4.4f, Stone, "stone");
        block.Markers.Add(new Marker(MarkerKind.LeaveTown,
            new Vector3(0f, 0f, 7.2f), 2.4f, "Leave town", 0));
        HangSign(block, "South Gate", "wilderness",
            new Vector3(0f, 3.35f, 6.48f), 0f, 2.1f, 0.62f);
        return block;
    }

    public static MapBlock ShopInterior()
    {
        var block = WalledRoom(6.2f, 7.2f, Timber, "timber");
        Leave(block, 7.2f, "Leave the store");
        HangSign(block, "General Store", "north-west",
            new Vector3(0f, 3.08f, 7.2f - 0.55f), 0f);
        Furniture(block, -3.6f, 0f, -5.6f, 1.4f, 1.05f, -4.4f, Wood);
        block.Props.Add(new BillboardProp("trader", new Vector3(2.5f, 0f, -4.9f), 1.85f, 0f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Shop,
            new Vector3(0.2f, 0f, -3.4f), 2.2f, "Browse the counter", 0));
        Furniture(block, -5.8f, 0f, -3.2f, -4.6f, 1.6f, 2.4f, Wood);
        Furniture(block, 4.6f, 0f, -3.2f, 5.8f, 1.6f, 2.4f, Wood);
        Lamp(block, -3.2f, 2.4f);
        return block;
    }

    public static MapBlock InnInterior()
    {
        var block = WalledRoom(7.2f, 8.2f, Timber, "timber");
        Leave(block, 8.2f, "Leave the inn");
        HangSign(block, "The Resting Hound", "north-east",
            new Vector3(0f, 3.08f, 8.2f - 0.55f), 0f, 2.15f, 0.62f);
        Furniture(block, -6.6f, 0f, -4.2f, -4.8f, 1.1f, 2.2f, Wood);
        block.Props.Add(new BillboardProp("innkeep", new Vector3(-3.7f, 0f, -0.8f), 1.85f, 1.6f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Talk,
            new Vector3(-3.7f, 0f, -0.8f), 2.2f, "Talk to the innkeep", 1));
        block.Markers.Add(new Marker(MarkerKind.Drink,
            new Vector3(-3.5f, 0f, 0.8f), 1.7f, "Buy a drink", 4));
        Furniture(block, -1.2f, 0.7f, -1.4f, 1.2f, 0.85f, 0.8f, Wood);
        Furniture(block, 2.4f, 0.7f, 1.2f, 4.8f, 0.85f, 3.2f, Wood);
        AddBed(block, 4.8f, -6.2f, free: false);
        AddBed(block, 1.6f, -6.2f, free: false);
        block.Props.Add(new BillboardProp("thief", new Vector3(-4.2f, 0f, 5.1f), 1.85f, 2.4f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Join,
            new Vector3(-4.2f, 0f, 5.1f), 2.0f, "Speak to the hooded guest", (int)GuildKind.Thieves));
        Lamp(block, 3.6f, -4.4f);
        return block;
    }

    public static MapBlock HouseInterior()
    {
        var block = WalledRoom(5.4f, 5.6f, Plaster, "stone");
        Leave(block, 5.6f, "Leave the house");
        HangSign(block, "A Private House", "west",
            new Vector3(0f, 3.08f, 5.6f - 0.55f), 0f);
        AddBed(block, -3.2f, -3.6f, free: true);
        Furniture(block, 2.2f, 0f, -4.0f, 4.4f, 0.85f, -2.4f, Wood);
        Furniture(block, 2.8f, 0f, -3.8f, 4.0f, 0.9f, -2.6f, Wood);
        block.Markers.Add(new Marker(MarkerKind.Deed,
            new Vector3(0f, 0f, 0.4f), 1.6f, "Read the deed", 150));
        block.Markers.Add(new Marker(MarkerKind.Stash,
            new Vector3(3.4f, 0f, -3.2f), 1.8f, "Open the chest", 0));
        return block;
    }

    public static MapBlock FightersHall()
    {
        var block = WalledRoom(7.0f, 7.2f, Stone, "stone");
        Leave(block, 7.2f, "Leave the hall");
        HangSign(block, "Fighters Guild", "east",
            new Vector3(0f, 3.08f, 7.2f - 0.55f), 0f);
        block.Props.Add(new BillboardProp("fighter", new Vector3(0f, 0f, -3.2f), 1.9f, 0f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Join,
            new Vector3(0f, 0f, -3.2f), 2.2f, "Speak to the guildmaster", (int)GuildKind.Fighters));
        block.Props.Add(new BillboardProp("dummy", new Vector3(-4.4f, 0f, 1.6f), 1.85f, 1.2f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Train,
            new Vector3(-4.4f, 0f, 1.6f), 2.0f, "Train at the dummy", 6));
        Furniture(block, 5.0f, 0f, -4.4f, 6.4f, 1.4f, 3.2f, Wood);
        Lamp(block, 3.2f, -3.2f);
        return block;
    }

    public static MapBlock MagesHall()
    {
        var block = WalledRoom(6.4f, 6.6f, Plaster, "stone");
        Leave(block, 6.6f, "Leave the guild");
        HangSign(block, "Mages Guild", "south-west",
            new Vector3(0f, 3.08f, 6.6f - 0.55f), 0f);
        Furniture(block, -1.1f, 0f, -4.8f, 1.1f, 0.95f, -3.2f, Stone);
        block.Props.Add(new BillboardProp("mage", new Vector3(0f, 0f, -1.9f), 1.85f, 0.2f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Join,
            new Vector3(0f, 0f, -1.9f), 2.2f, "Speak to the magus", (int)GuildKind.Mages));
        Furniture(block, -5.8f, 0f, -2.4f, -4.4f, 1.5f, 2.4f, Wood);
        Furniture(block, 4.4f, 0f, -2.4f, 5.8f, 1.5f, 2.4f, Wood);
        Lamp(block, 0f, 2.4f);
        return block;
    }

    public static MapBlock Temple()
    {
        var block = WalledRoom(6.2f, 7.0f, Plaster, "stone");
        Leave(block, 7.0f, "Leave the temple");
        HangSign(block, "Temple of Kynareth", "south-east",
            new Vector3(0f, 3.08f, 7.0f - 0.55f), 0f, 2.2f, 0.62f);
        Furniture(block, -2.2f, 0f, -5.6f, 2.2f, 1.05f, -3.8f, Stone);
        block.Props.Add(new BillboardProp("priest", new Vector3(0f, 0f, -2.5f), 1.85f, 0f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Heal,
            new Vector3(0f, 0f, -2.5f), 2.2f, "Ask for healing", 0));
        Lamp(block, -3.4f, 0f);
        Lamp(block, 3.4f, 0f);
        return block;
    }

    public static MapBlock Corridor()
    {
        var block = new MapBlock();
        Floor(block, -2.2f, -8f, 2.2f, 8f, Stone, "stone");
        Ceiling(block, -2.2f, -8f, 2.2f, 8f, 3.4f, Stone);
        Wall(block, -2.2f, -8f, -1.8f, 8f, 3.4f, Stone, "stone");
        Wall(block, 1.8f, -8f, 2.2f, 8f, 3.4f, Stone, "stone");
        Lamp(block, 0f, 0f);
        return block;
    }

    public static MapBlock Chamber()
    {
        var block = Tile16(Stone, "stone", openNegZ: true, openPosZ: true);
        block.Props.Add(new BillboardProp("dummy", new Vector3(-4.4f, 0f, 0f), 1.85f, 0f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Dummy,
            new Vector3(-4.4f, 0f, 0f), 2.4f, "Strike the dummy", 0));
        block.Props.Add(new BillboardProp("key", new Vector3(4.2f, 0.4f, -3.2f), 0.55f, 0.4f, Color.White));
        block.Markers.Add(new Marker(MarkerKind.Key,
            new Vector3(4.2f, 0f, -3.2f), 1.8f, "Take the key", 0));
        return block;
    }

    public static MapBlock LockedGate()
    {
        var block = Corridor();
        block.Boxes.Add(new WorldBox(
            new Vector3(-1.85f, 0f, -0.35f), new Vector3(1.85f, 3.2f, 0.35f),
            new Color(92, 72, 54), "timber", Door: true));
        block.Markers.Add(new Marker(MarkerKind.LockedDoor,
            new Vector3(0f, 0f, 1.4f), 2.0f, "The iron door", 0));
        return block;
    }

    public static MapBlock Vault()
    {
        var block = Tile16(Stone, "stone", openNegZ: true, openPosZ: true);
        Furniture(block, 3.4f, 0f, -1.2f, 5.4f, 0.9f, 1.2f, Wood);
        block.Markers.Add(new Marker(MarkerKind.Loot,
            new Vector3(4.4f, 0f, 0f), 1.8f, "Search the vault", 1));
        return block;
    }

    public static MapBlock DeadEnd() =>
        Tile16(Stone, "stone", openNegZ: true, openPosZ: false);

    public static MapBlock Junction() =>
        Tile16(Stone, "stone", openNegZ: true, openPosZ: true);

    public static MapBlock DungeonEntrance()
    {
        var block = Tile16(Stone, "stone", openNegZ: false, openPosZ: true);
        block.Spawn = new Vector3(0f, WorldScale.EyeHeight, 0f);
        return block;
    }

    public static MapBlock DungeonExit()
    {
        var block = Tile16(Stone, "stone", openNegZ: true, openPosZ: false);
        block.Markers.Add(new Marker(MarkerKind.LeaveDungeon,
            new Vector3(0f, 0f, 5.4f), 2.2f, "Climb out", 0));
        return block;
    }

    private static MapBlock WalledRoom(float halfX, float halfZ, Color wallColour, string wallMat)
    {
        var block = new MapBlock();
        const float Top = 4.4f;
        Floor(block, -halfX, -halfZ, halfX, halfZ, Timber, "timber");
        Ceiling(block, -halfX, -halfZ, halfX, halfZ, Top, Plaster);
        Wall(block, -halfX, -halfZ, halfX, -halfZ + WallT, Top, wallColour, wallMat);
        Wall(block, -halfX, halfZ - WallT, halfX, halfZ, Top, wallColour, wallMat);
        Wall(block, -halfX, -halfZ, -halfX + WallT, halfZ, Top, wallColour, wallMat);
        Wall(block, halfX - WallT, -halfZ, halfX, halfZ, Top, wallColour, wallMat);
        Furniture(block, -1.15f, 0f, halfZ - 0.58f, 1.15f, 2.55f, halfZ - 0.42f, Wood);
        Furniture(block, -1.35f, 2.5f, halfZ - 0.62f, 1.35f, 2.85f, halfZ - 0.38f, Wood);
        Lamp(block, 0f, 0f);
        Lamp(block, -halfX * 0.55f, -halfZ * 0.45f);
        Lamp(block, halfX * 0.55f, halfZ * 0.25f);
        block.Spawn = new Vector3(0f, WorldScale.EyeHeight, halfZ - 2.4f);
        return block;
    }

    private static MapBlock Tile16(Color colour, string material, bool openNegZ, bool openPosZ)
    {
        var block = new MapBlock();
        Floor(block, -Tile, -Tile, Tile, Tile, colour, material);
        Ceiling(block, -Tile, -Tile, Tile, Tile, 4.2f, colour);
        Wall(block, -Tile, -Tile, -Tile + WallT, Tile, 4.2f, colour, material);
        Wall(block, Tile - WallT, -Tile, Tile, Tile, 4.2f, colour, material);
        GappedZ(block, -Tile, Tile, -Tile, -Tile + WallT, 4.2f, colour, material, openNegZ, Door);
        GappedZ(block, -Tile, Tile, Tile - WallT, Tile, 4.2f, colour, material, openPosZ, Door);
        Lamp(block, 0f, 0f);
        Lamp(block, -4.2f, -4.2f);
        Lamp(block, 4.2f, 4.2f);
        return block;
    }

    private static void DoorLeaf(MapBlock block, DoorSide door)
    {
        const float H = 2.65f;
        const float Top = 5.2f;
        const float W = 1.5f;
        const float T = 0.16f;
        switch (door)
        {
            case DoorSide.NegZ:
                Furniture(block, -W, 0f, -6f, W, H, -6f + T, Wood);
                Furniture(block, -W, H, -6f, W, Top, -6f + T, Plaster);
                break;
            case DoorSide.PosX:
                Furniture(block, 6f - T, 0f, -W, 6f, H, W, Wood);
                Furniture(block, 6f - T, H, -W, 6f, Top, W, Plaster);
                break;
            case DoorSide.NegX:
                Furniture(block, -6f, 0f, -W, -6f + T, H, W, Wood);
                Furniture(block, -6f, H, -W, -6f + T, Top, W, Plaster);
                break;
            default:
                Furniture(block, -W, 0f, 6f - T, W, H, 6f, Wood);
                Furniture(block, -W, H, 6f - T, W, Top, 6f, Plaster);
                break;
        }
    }

    private static void HangSign(MapBlock block, string title, string bearing, Vector3 centre,
        float yaw, float width = 1.9f, float height = 0.62f)
    {
        var right = new Vector3(MathF.Cos(yaw), 0f, MathF.Sin(yaw));
        var forward = Vector3.Transform(Vector3.Forward, Matrix.CreateRotationY(-yaw));
        var half = new Vector3(
            MathF.Abs(right.X) * width * 0.52f + MathF.Abs(forward.X) * 0.06f,
            height * 0.52f,
            MathF.Abs(right.Z) * width * 0.52f + MathF.Abs(forward.Z) * 0.06f);
        block.Boxes.Add(new WorldBox(centre - half, centre + half, Wood, "timber"));
        block.Signs.Add(new WorldSign(title, bearing, centre + forward * 0.07f, yaw, width, height));
    }

    private static float YawOf(DoorSide door) => door switch
    {
        DoorSide.NegZ => 0f,
        DoorSide.PosX => MathF.PI * 0.5f,
        DoorSide.NegX => -MathF.PI * 0.5f,
        _ => MathF.PI
    };

    private static void Leave(MapBlock block, float halfZ, string label) =>
        block.Markers.Add(new Marker(MarkerKind.LeaveInterior,
            new Vector3(0f, 0f, halfZ - 0.9f), 1.8f, label, 0));

    private static void AddBed(MapBlock block, float x, float z, bool free)
    {
        block.Boxes.Add(new WorldBox(
            new Vector3(x - 1.1f, 0f, z - 0.9f),
            new Vector3(x + 1.1f, 0.42f, z + 0.9f),
            Cloth, "cloth"));
        block.Markers.Add(new Marker(MarkerKind.Rest,
            new Vector3(x, 0f, z), 2.0f,
            free ? "Sleep" : "Rent a room",
            free ? 0 : 8));
    }

    private static void Furniture(MapBlock block, float x0, float y0, float z0,
        float x1, float y1, float z1, Color colour) =>
        block.Boxes.Add(new WorldBox(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), colour, "timber"));

    private static void GappedZ(MapBlock block, float xMin, float xMax, float z0, float z1, float height,
        Color colour, string material, bool open, float door)
    {
        if (!open)
        {
            Wall(block, xMin, z0, xMax, z1, height, colour, material);
            return;
        }

        Wall(block, xMin, z0, -door, z1, height, colour, material);
        Wall(block, door, z0, xMax, z1, height, colour, material);
    }

    private static void GappedX(MapBlock block, float x0, float x1, float zMin, float zMax, float height,
        Color colour, string material, bool open, float door)
    {
        if (!open)
        {
            Wall(block, x0, zMin, x1, zMax, height, colour, material);
            return;
        }

        Wall(block, x0, zMin, x1, -door, height, colour, material);
        Wall(block, x0, door, x1, zMax, height, colour, material);
    }

    private static void Slab(MapBlock block, float x0, float y0, float z0, float x1, float y1, float z1,
        Color colour, string material) =>
        block.Boxes.Add(new WorldBox(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), colour, material));

    private static void Floor(MapBlock block, float x0, float z0, float x1, float z1,
        Color colour, string material) =>
        block.Boxes.Add(new WorldBox(new Vector3(x0, -0.4f, z0), new Vector3(x1, 0f, z1), colour, material));

    private static void Ceiling(MapBlock block, float x0, float z0, float x1, float z1,
        float top, Color colour) =>
        block.Boxes.Add(new WorldBox(new Vector3(x0, top, z0), new Vector3(x1, top + 0.35f, z1), colour, "stone"));

    private static void Wall(MapBlock block, float x0, float z0, float x1, float z1,
        float height, Color colour, string material) =>
        block.Boxes.Add(new WorldBox(new Vector3(x0, 0f, z0), new Vector3(x1, height, z1), colour, material));

    private static void Lamp(MapBlock block, float x, float z) =>
        block.Lights.Add(new PointLight(new Vector3(x, 3.4f, z), new Vector3(1f, 0.82f, 0.58f) * 2.1f, 16f));
}

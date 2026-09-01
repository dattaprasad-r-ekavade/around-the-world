using Ember.Render;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Campaign;

public readonly record struct WorldBox(Vector3 Min, Vector3 Max, Color Colour, string Material, bool Door = false);

public enum MarkerKind
{
    EnterTown,
    LeaveTown,
    EnterInterior,
    LeaveInterior,
    EnterDungeon,
    LeaveDungeon,
    Dummy,
    Talk,
    Rest,
    Shop,
    Join,
    Heal,
    Train,
    Loot,
    Bank,
    Drink,
    Deed,
    Stash,
    Key,
    LockedDoor
}

public readonly record struct Marker(
    MarkerKind Kind,
    Vector3 Position,
    float Radius,
    string Label,
    int Target);

public readonly record struct BillboardProp(
    string Sprite,
    Vector3 Feet,
    float Height,
    float FacingYaw,
    Color Tint);

public readonly record struct WorldSign(
    string Title,
    string Bearing,
    Vector3 Position,
    float FacingYaw,
    float Width = 1.9f,
    float Height = 0.62f);

/// <summary>One reusable tile: boxes, markers, lights, props, a spawn.</summary>
public sealed class MapBlock
{
    public List<WorldBox> Boxes { get; } = new();
    public List<Marker> Markers { get; } = new();
    public List<PointLight> Lights { get; } = new();
    public List<BillboardProp> Props { get; } = new();
    public List<WorldSign> Signs { get; } = new();
    public Vector3 Spawn { get; set; } = new(0f, WorldScale.EyeHeight, 4f);

    public MapBlock Translated(Vector3 offset)
    {
        var copy = new MapBlock { Spawn = Spawn + offset };
        foreach (var box in Boxes)
            copy.Boxes.Add(new WorldBox(box.Min + offset, box.Max + offset, box.Colour, box.Material, box.Door));
        foreach (var marker in Markers)
            copy.Markers.Add(marker with { Position = marker.Position + offset });
        foreach (var light in Lights)
            copy.Lights.Add(new PointLight(light.Position + offset, light.Colour, light.Range));
        foreach (var prop in Props)
            copy.Props.Add(prop with { Feet = prop.Feet + offset });
        foreach (var sign in Signs)
            copy.Signs.Add(sign with { Position = sign.Position + offset });
        return copy;
    }

    public void AppendTo(MapBlock into)
    {
        into.Boxes.AddRange(Boxes);
        into.Markers.AddRange(Markers);
        into.Lights.AddRange(Lights);
        into.Props.AddRange(Props);
        into.Signs.AddRange(Signs);
    }
}

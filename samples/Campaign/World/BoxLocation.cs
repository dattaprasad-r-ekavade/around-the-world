using Ember.Render;
using Ember.Ui;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

/// <summary>Town, interior, or dungeon: boxes, markers, billboards, a constant floor.</summary>
public sealed class BoxLocation : ILocation
{
    private readonly BoxCollider _collider = new();
    private readonly List<WorldBox> _boxes;
    private readonly List<Marker> _markers;
    private readonly List<BillboardProp> _props;
    private readonly List<PointLight> _lights;
    private readonly List<WorldSign> _signs;
    private readonly float _floorY;
    private readonly float _minX;
    private readonly float _maxX;
    private readonly float _minZ;
    private readonly float _maxZ;

    public BoxLocation(string name, LocationKind kind, MapBlock block, float floorY,
        StoneTextures.StonePalette palette, Color clear)
    {
        Name = name;
        Kind = kind;
        Palette = palette;
        ClearColour = clear;
        _floorY = floorY;
        Settlement.UnclipBlock(block);
        _boxes = new List<WorldBox>(block.Boxes);
        _markers = new List<Marker>(block.Markers);
        _props = new List<BillboardProp>(block.Props);
        _lights = new List<PointLight>(block.Lights);
        _signs = new List<WorldSign>(block.Signs);
        Spawn = block.Spawn;
        _collider.Rebuild(_boxes);
        BoundsOf(block.Spawn, _boxes, _markers, out _minX, out _maxX, out _minZ, out _maxZ);
    }

    public string Name { get; }
    public LocationKind Kind { get; }
    public StoneTextures.StonePalette Palette { get; }
    public Color ClearColour { get; }
    public Vector3 Spawn { get; }
    public IReadOnlyList<PointLight> Lights => _lights;
    public IReadOnlyList<Marker> Markers => _markers;
    public IReadOnlyList<WorldSign> Signs => _signs;

    public bool DummyHit { get; set; }

    public void Unlock()
    {
        if (_boxes.RemoveAll(box => box.Door) > 0)
            _collider.Rebuild(_boxes);
    }

    public float SampleGround(float x, float z) => _floorY;

    public Vector3 Collide(Vector3 origin, Vector3 delta, float radius)
    {
        var wanted = origin + delta;
        wanted = new Vector3(
            Math.Clamp(wanted.X, _minX, _maxX),
            wanted.Y,
            Math.Clamp(wanted.Z, _minZ, _maxZ));
        return _collider.Resolve(origin, wanted - origin, radius);
    }

    private static void BoundsOf(Vector3 spawn, List<WorldBox> boxes, List<Marker> markers,
        out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = maxX = spawn.X;
        minZ = maxZ = spawn.Z;
        foreach (var box in boxes)
        {
            minX = MathF.Min(minX, box.Min.X);
            maxX = MathF.Max(maxX, box.Max.X);
            minZ = MathF.Min(minZ, box.Min.Z);
            maxZ = MathF.Max(maxZ, box.Max.Z);
        }

        foreach (var marker in markers)
        {
            minX = MathF.Min(minX, marker.Position.X);
            maxX = MathF.Max(maxX, marker.Position.X);
            minZ = MathF.Min(minZ, marker.Position.Z);
            maxZ = MathF.Max(maxZ, marker.Position.Z);
        }

        const float Margin = 4f;
        minX -= Margin;
        maxX += Margin;
        minZ -= Margin;
        maxZ += Margin;
    }

    public void Draw(SceneRenderer scene, BillboardRenderer billboards, GroundedView view,
        CampaignSprites sprites)
    {
        foreach (var box in _boxes)
            scene.DrawWorldBox(box.Min, box.Max, box.Colour, box.Material);

        billboards.Begin(view.View, view.Projection);
        foreach (var prop in _props)
        {
            if (prop.Sprite == "dummy" && DummyHit) continue;
            var texture = sprites.Get(prop.Sprite, view.Yaw, prop.FacingYaw);
            billboards.Draw(texture, prop.Feet, prop.Height, view.Yaw, prop.Tint);
        }

        foreach (var sign in _signs)
        {
            billboards.DrawMounted(sprites.Board(sign.Title, sign.Bearing),
                sign.Position, sign.Width, sign.Height, sign.FacingYaw, Color.White);
        }
    }

    public LookHint? Probe(Vector3 eye, Vector3 forward) =>
        Look.Nearest(_markers, eye, forward);
}

public enum LocationKind
{
    Town,
    Interior,
    Dungeon
}

public static class Look
{
    public static LookHint? Nearest(IReadOnlyList<Marker> markers, Vector3 eye, Vector3 forward)
    {
        LookHint? best = null;
        var bestScore = float.MaxValue;
        var flat = new Vector3(forward.X, 0f, forward.Z);
        if (flat.LengthSquared() < 0.0001f) return null;
        flat.Normalize();

        foreach (var marker in markers)
        {
            var to = marker.Position - eye;
            to.Y = 0f;
            var distance = to.Length();
            if (distance > marker.Radius + 2.4f || distance < 0.05f) continue;

            to /= distance;
            var facing = Vector3.Dot(flat, to);
            if (facing < 0.45f) continue;

            var score = distance * (2f - facing);
            if (score >= bestScore) continue;

            bestScore = score;
            best = new LookHint(marker.Label, RoleOf(marker.Kind), marker);
        }

        return best;
    }

    private static PromptRole RoleOf(MarkerKind kind) => kind switch
    {
        MarkerKind.Talk => PromptRole.Talk,
        MarkerKind.Shop => PromptRole.Talk,
        MarkerKind.Join => PromptRole.Talk,
        MarkerKind.Bank => PromptRole.Talk,
        MarkerKind.Heal => PromptRole.Interact,
        MarkerKind.Loot => PromptRole.Pocket,
        MarkerKind.Stash => PromptRole.Pocket,
        MarkerKind.Key => PromptRole.Pocket,
        MarkerKind.LockedDoor => PromptRole.Interact,
        MarkerKind.Dummy => PromptRole.Interact,
        MarkerKind.Train => PromptRole.Interact,
        MarkerKind.Rest => PromptRole.Interact,
        MarkerKind.Drink => PromptRole.Interact,
        MarkerKind.Deed => PromptRole.Interact,
        _ => PromptRole.Interact
    };
}

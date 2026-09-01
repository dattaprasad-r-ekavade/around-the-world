using Ember.Render;
using Ember.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Campaign;

/// <summary>Fast-travel chart. M opens it. Click a town or the land, Enter to go.</summary>
public sealed class TravelMap : IDisposable
{
    public static readonly Rectangle Chart = new(40, 100, 520, 500);
    public static readonly Rectangle List = new(580, 100, 656, 500);

    private Texture2D? _chart;

    public bool Open { get; set; }
    public int Selected { get; set; }
    public Vector3? PendingLand { get; set; }

    public void Rebuild(GraphicsDevice device, HeightNoise noise)
    {
        _chart?.Dispose();
        var size = WorldScale.MapSize;
        var pixels = new Color[size * size];
        var step = WorldScale.WorldMetres / size;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var wx = (x + 0.5f) * step;
            var wz = (y + 0.5f) * step;
            var biome = noise.BiomeAt(wx, wz);
            var land = noise.Continent(wx, wz);
            var colour = Biomes.Ground(biome);
            var shade = MathHelper.Clamp((land - 0.48f) * 0.7f, -0.22f, 0.28f);
            pixels[y * size + x] = new Color(
                (byte)Math.Clamp(colour.R * (1f + shade), 0, 255),
                (byte)Math.Clamp(colour.G * (1f + shade), 0, 255),
                (byte)Math.Clamp(colour.B * (1f + shade), 0, 255));
        }

        _chart = new Texture2D(device, size, size);
        _chart.SetData(pixels);
    }

    public void Draw(UiCanvas ui, WorldState world, Vector3 player, bool ship)
    {
        if (!Open || _chart is null) return;

        ui.Panel(UiLayout.FullScreen, UiTheme.Scrim, UiTheme.NoBorder);
        var frame = new Rectangle(20, 16, 1240, 688);
        HudChrome.Window(ui, frame, "The round earth",
            "300 BCE. Cities of then, marks of now. Click a name. Enter to go.");

        ui.Sprite(_chart, Chart, Color.White);
        ui.Border(Chart, UiTheme.GoldDim);

        for (var i = 0; i < world.TownPads.Length; i++)
        {
            if (world.TownBiomes[i] == BiomeKind.Ocean) continue;
            if (i >= EarthPlaces.StopCount) continue;
            var dot = WorldToChart(world.TownPads[i]);
            var on = i == Selected && PendingLand is null;
            var mark = EarthPlaces.IsMark(i);
            var colour = on ? UiTheme.Gold : mark ? UiTheme.Faint : Color.White;
            var size = mark ? 3 : 5;
            ui.Fill(new Rectangle(dot.X - size / 2, dot.Y - size / 2, size, size), colour);
        }

        if (PendingLand is { } land)
        {
            var mark = WorldToChart(land);
            ui.Fill(new Rectangle(mark.X - 3, mark.Y - 3, 7, 7), UiTheme.Accent);
        }

        var you = WorldToChart(player);
        ui.Fill(new Rectangle(you.X - 3, you.Y - 3, 7, 7), new Color(220, 72, 64));

        ui.Panel(List, UiTheme.PanelRaised, UiTheme.BorderDim);
        var start = Math.Max(0, Selected - 12);
        var y = List.Y + 12;
        var listed = Math.Min(world.TownNames.Length, EarthPlaces.StopCount);
        for (var i = start; i < listed && y < List.Bottom - 28; i++)
        {
            var place = EarthPlaces.StopAt(i);
            var line = EarthPlaces.IsMark(i)
                ? $"{place.Name}   now"
                : $"{place.Name}   {place.Polity}";
            var colour = i == Selected && PendingLand is null ? UiTheme.Gold : UiTheme.Body;
            ui.Text(line, new Vector2(List.X + 16, y), 14, colour);
            y += 20;
        }

        var dest = PendingLand ?? world.TownPads[Math.Clamp(Selected, 0, world.TownPads.Length - 1)];
        var hours = Travel.Hours(player, dest, ship,
            Travel.Coastal(world.Noise, player), Travel.Coastal(world.Noise, dest));
        var trip = Travel.Kilometres(player, dest);
        ui.Text($"You  {EarthGlobe.Lon(player.X):0}°  {EarthGlobe.Lat(player.Z):0}°   wrap   {EarthPlaces.CityCount} cities of 300 BCE   {EarthPlaces.MarkCount} marks of now",
            new Vector2(40, 616), 13, UiTheme.Muted);
        if (PendingLand is { } pending)
            ui.Text($"Land mark  {pending.X:0}  {pending.Z:0}   {trip:0} km  ·  {hours:0.0} hours on the road",
                new Vector2(40, 638), 14, UiTheme.Accent);
        else
            ui.Text($"Selected  {world.TownNames[Math.Clamp(Selected, 0, world.TownNames.Length - 1)]}   {trip:0} km  ·  {hours:0.0} hours on the road",
                new Vector2(40, 638), 14, UiTheme.Gold);
    }

    public bool Click(Vector2 pointer, WorldState world)
    {
        if (List.Contains(pointer.ToPoint()))
        {
            var row = (int)((pointer.Y - (List.Y + 12)) / 20f);
            var start = Math.Max(0, Selected - 12);
            var index = start + row;
            if (index < 0 || index >= EarthPlaces.StopCount) return false;
            Selected = index;
            PendingLand = null;
            return true;
        }

        if (!Chart.Contains(pointer.ToPoint())) return false;

        var worldPos = ChartToWorld(pointer);
        var best = -1;
        var bestD = float.MaxValue;
        for (var i = 0; i < EarthPlaces.StopCount && i < world.TownPads.Length; i++)
        {
            if (world.TownBiomes[i] == BiomeKind.Ocean) continue;
            var d = EarthGlobe.DistanceSq(worldPos.X, worldPos.Z, world.TownPads[i].X, world.TownPads[i].Z);
            if (d >= bestD) continue;
            bestD = d;
            best = i;
        }

        var metresPerPixel = WorldScale.WorldMetres / Chart.Width;
        var snap = metresPerPixel * 5f;
        if (best >= 0 && bestD < snap * snap)
        {
            Selected = best;
            PendingLand = null;
            return true;
        }

        PendingLand = worldPos;
        return true;
    }

    public void MoveSelection(int delta, int townCount)
    {
        if (townCount <= 0) return;
        PendingLand = null;
        Selected = (Selected + delta + townCount) % townCount;
    }

    public static Point WorldToChart(Vector3 world)
    {
        var x = (int)(world.X / WorldScale.WorldMetres * Chart.Width) + Chart.X;
        var y = (int)(world.Z / WorldScale.WorldMetres * Chart.Height) + Chart.Y;
        return new Point(Math.Clamp(x, Chart.X, Chart.Right - 1), Math.Clamp(y, Chart.Y, Chart.Bottom - 1));
    }

    public static Vector3 ChartToWorld(Vector2 pointer)
    {
        var u = Math.Clamp((pointer.X - Chart.X) / Chart.Width, 0f, 1f);
        var v = Math.Clamp((pointer.Y - Chart.Y) / Chart.Height, 0f, 1f);
        return new Vector3(u * WorldScale.WorldMetres, 0f, v * WorldScale.WorldMetres);
    }

    public void Dispose() => _chart?.Dispose();
}

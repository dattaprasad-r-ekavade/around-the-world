using Ember.Ui;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

public readonly record struct CompassMark(float WorldYaw, string Glyph, Color Colour);

/// <summary>
/// Classic Daggerfall compass: a disc that turns under a fixed mark. Top is the way you look.
/// Yaw 0 looks north (−Z).
/// </summary>
public sealed class CompassHud : IDisposable
{
    public void Draw(UiCanvas ui, float yaw, IReadOnlyList<CompassMark> marks)
    {
        var box = HudLayout.Compass;
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;
        var r = box.Width / 2 - 2;

        HudChrome.Disc(ui, cx, cy, r, UiTheme.WoodDark);
        HudChrome.Ring(ui, cx, cy, r, r - 7, UiTheme.GoldDim);
        HudChrome.Disc(ui, cx, cy, r - 8, new Color(28, 22, 14));
        HudChrome.Ring(ui, cx, cy, r - 8, r - 10, UiTheme.WoodLight);

        for (var deg = 0; deg < 360; deg += 15)
        {
            var a = deg * (MathF.PI / 180f) - yaw;
            var outer = r - 11;
            var inner = deg % 90 == 0 ? r - 22 : r - 16;
            var x0 = cx + (int)(MathF.Sin(a) * inner);
            var y0 = cy - (int)(MathF.Cos(a) * inner);
            var x1 = cx + (int)(MathF.Sin(a) * outer);
            var y1 = cy - (int)(MathF.Cos(a) * outer);
            var colour = deg % 90 == 0 ? UiTheme.Gold : UiTheme.WoodLight;
            Line(ui, x0, y0, x1, y1, colour);
        }

        Cardinal(ui, "N", 0f, yaw, cx, cy, r - 28, UiTheme.GoldBright);
        Cardinal(ui, "E", MathF.PI * 0.5f, yaw, cx, cy, r - 28, UiTheme.Parchment);
        Cardinal(ui, "S", MathF.PI, yaw, cx, cy, r - 28, UiTheme.Muted);
        Cardinal(ui, "W", -MathF.PI * 0.5f, yaw, cx, cy, r - 28, UiTheme.Parchment);

        foreach (var mark in marks)
        {
            var a = mark.WorldYaw - yaw;
            var x = cx + (int)(MathF.Sin(a) * (r - 34));
            var y = cy - (int)(MathF.Cos(a) * (r - 34));
            ui.TextCentred(mark.Glyph, x, y - 6, 11, mark.Colour);
        }

        ui.Fill(new Rectangle(cx - 4, box.Y + 6, 8, 8), UiTheme.Gold);
        ui.Fill(new Rectangle(cx - 1, box.Y + 10, 2, 14), UiTheme.GoldBright);
        HudChrome.Disc(ui, cx, cy, 4, UiTheme.Gold);
    }

    private static void Cardinal(UiCanvas ui, string letter, float worldYaw, float facing,
        int cx, int cy, int radius, Color colour)
    {
        var a = worldYaw - facing;
        var x = cx + (int)(MathF.Sin(a) * radius);
        var y = cy - (int)(MathF.Cos(a) * radius);
        ui.HeadingCentred(letter, x, y - 8, 13, colour);
    }

    private static void Line(UiCanvas ui, int x0, int y0, int x1, int y1, Color colour)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        while (true)
        {
            ui.Fill(new Rectangle(x0, y0, 2, 2), colour);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    public void Dispose() { }
}

using Ember.Ui;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

/// <summary>Dungeon automap: visited 16 m cells along the stitch.</summary>
public static class AutomapHud
{
    public static void Draw(UiCanvas ui, HashSet<int> cells, int current, float playerZ)
    {
        var frame = HudLayout.Automap;
        HudChrome.Window(ui, frame, "Automap", "Visited rooms. M closes.");

        const float Step = WorldScale.BlockMetres;
        var min = 0;
        var max = 12;
        foreach (var c in cells)
        {
            if (c < min) min = c;
            if (c > max) max = c;
        }

        var content = HudChrome.Content(frame);
        var map = new Rectangle(content.X + 16, content.Y + 8, content.Width - 32, content.Height - 16);
        HudChrome.Pane(ui, map);
        var span = Math.Max(1, max - min + 1);
        var h = Math.Max(12, map.Height / (span + 1) - 4);
        for (var c = min; c <= max; c++)
        {
            if (!cells.Contains(c) && c != current) continue;
            var t = (c - min) / (float)span;
            var y = map.Y + 8 + (int)(t * (map.Height - h - 16));
            var row = new Rectangle(map.X + 24, y, map.Width - 48, h);
            var fill = c == current ? UiTheme.Gold : UiTheme.Wood;
            ui.Fill(row, fill);
            ui.Border(row, c == current ? UiTheme.Gold : UiTheme.BorderDim);
        }

        var pip = map.Y + 8 + (int)(Math.Clamp(playerZ / (Step * (span + 0.01f)), 0f, 1f)
            * (map.Height - 20));
        ui.Fill(new Rectangle(map.X + 8, pip, 10, 10), UiTheme.Health);
    }
}

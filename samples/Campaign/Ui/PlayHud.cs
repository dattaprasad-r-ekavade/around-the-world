using Ember.Ui;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

/// <summary>
/// Daggerfall large HUD: oak dock, portrait, vertical vitals, action keys, spinning compass.
/// </summary>
public static class PlayHud
{
    public static readonly (string Key, string Label)[] Actions =
    [
        ("I", "Pack"),
        ("K", "Sheet"),
        ("4", "Magic"),
        ("J", "Log"),
        ("M", "Map"),
        ("R", "Rest"),
        ("E", "Use")
    ];

    public static void Draw(UiCanvas ui, CompassHud compass, float yaw, string place, string stance,
        string clock, string weather, Hero hero, Ledger pack, float health01, float fatigue01,
        float magicka01, float hunger01, string status, IReadOnlyList<CompassMark> marks, bool bot,
        string botGoal)
    {
        var dock = HudLayout.Dock;
        HudChrome.Wood(ui, dock);
        ui.Fill(new Rectangle(dock.X + 6, dock.Y + 6, dock.Width - 12, 3), UiTheme.GoldDim);

        DrawPortrait(ui, hero, pack);
        DrawVitals(ui, health01, fatigue01, magicka01, hunger01);
        DrawActions(ui);
        compass.Draw(ui, yaw, marks);

        var infoX = 900;
        var infoY = dock.Y + 18;
        ui.Heading(place, new Vector2(infoX, infoY), 16, UiTheme.GoldBright);
        ui.Text(clock, new Vector2(infoX, infoY + 24), 13, UiTheme.Parchment);
        ui.Text(weather, new Vector2(infoX, infoY + 42), 12, UiTheme.Muted);
        if (!string.IsNullOrEmpty(stance))
            ui.Text(stance, new Vector2(infoX, infoY + 58), 12, UiTheme.Gold);
        ui.Text($"{pack.Gold} gp", new Vector2(infoX, infoY + 76), 13, UiTheme.GoldBright);
        if (!string.IsNullOrEmpty(status))
            ui.Text(status, new Vector2(HudLayout.Actions.X, dock.Y + 86), 11, UiTheme.Accent);
        if (bot)
            ui.Text($"BOT  {botGoal}", new Vector2(20, 12), 12, UiTheme.Gold);
    }

    private static void DrawPortrait(UiCanvas ui, Hero hero, Ledger pack)
    {
        var box = HudLayout.Portrait;
        HudChrome.Wood(ui, box);
        var inner = new Rectangle(box.X + 8, box.Y + 8, box.Width - 16, box.Height - 28);
        ui.Fill(inner, ClassTint(hero.Class));
        var initial = string.IsNullOrEmpty(hero.Name) ? "?" : hero.Name[..1].ToUpperInvariant();
        ui.HeadingCentred(initial, inner.X + inner.Width * 0.5f, inner.Y + 18, 32, UiTheme.Parchment);
        ui.TextFitCentred(Hero.ClassNames[(int)hero.Class], box.X + box.Width * 0.5f, box.Bottom - 20,
            box.Width - 8, 12, UiTheme.Parchment);
        _ = pack;
    }

    private static void DrawVitals(UiCanvas ui, float health, float fatigue, float magicka, float hunger)
    {
        var box = HudLayout.Vitals;
        var w = 18;
        var gap = 8;
        V(ui, box.X, box.Y, w, box.Height - 16, health, UiTheme.Health, "H");
        V(ui, box.X + w + gap, box.Y, w, box.Height - 16, fatigue, UiTheme.Fatigue, "F");
        V(ui, box.X + (w + gap) * 2, box.Y, w, box.Height - 16, magicka, UiTheme.Magicka, "S");
        V(ui, box.X + (w + gap) * 3, box.Y, w, box.Height - 16, hunger, UiTheme.Hunger, "N");
    }

    private static void V(UiCanvas ui, int x, int y, int w, int h, float amount, Color fill, string tag)
    {
        HudChrome.VBar(ui, new Rectangle(x, y, w, h), amount, fill);
        ui.TextCentred(tag, x + w * 0.5f, y + h + 1, 10, UiTheme.Parchment);
    }

    private static void DrawActions(UiCanvas ui)
    {
        var box = HudLayout.Actions;
        var n = Actions.Length;
        var bw = 80;
        var gap = 8;
        for (var i = 0; i < n; i++)
        {
            var x = box.X + i * (bw + gap);
            var row = new Rectangle(x, box.Y, bw, 58);
            ui.Fill(row, UiTheme.Wood);
            ui.Fill(new Rectangle(row.X, row.Y, row.Width, 2), UiTheme.WoodLight);
            ui.Fill(new Rectangle(row.X, row.Bottom - 2, row.Width, 2), UiTheme.WoodDark);
            ui.Border(row, UiTheme.GoldDim);
            ui.HeadingCentred(Actions[i].Key, x + bw * 0.5f, row.Y + 6, 18, UiTheme.GoldBright);
            ui.TextCentred(Actions[i].Label, x + bw * 0.5f, row.Y + 34, 12, UiTheme.Parchment);
        }
    }

    private static Color ClassTint(ClassId cls) => cls switch
    {
        ClassId.Mage => new Color(36, 40, 92),
        ClassId.Thief => new Color(36, 56, 36),
        ClassId.Spellsword => new Color(72, 36, 72),
        _ => new Color(92, 40, 28)
    };
}

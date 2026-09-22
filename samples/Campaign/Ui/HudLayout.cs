using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Logical 1280×720. The play HUD is Daggerfall's bottom dock.</summary>
public static class HudLayout
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int ModalWidth = 560;
    public const int DockH = 128;

    public static readonly Rectangle Dock = new(0, Height - DockH, Width, DockH);
    public static readonly Rectangle Portrait = new(12, Height - DockH + 14, 100, 100);
    public static readonly Rectangle Vitals = new(124, Height - DockH + 18, 118, 96);
    public static readonly Rectangle Actions = new(256, Height - DockH + 22, 620, 84);
    public static readonly Rectangle Compass = new(1090, Height - DockH + 10, 108, 108);
    public static readonly Rectangle Toast = new(340, 400, 600, 48);
    public static readonly Rectangle Inventory = new(80, 36, 1120, Height - DockH - 48);
    public static readonly Rectangle Dialogue = new(140, 200, 1000, 360);
    public static readonly Rectangle Pause = new(400, 48, 480, Height - DockH - 64);
    public static readonly Rectangle PauseWide = new(160, 28, 960, Height - DockH - 44);
    public static readonly Rectangle Shop = new(140, 24, 1000, Height - DockH - 36);
    public static readonly Rectangle Journal = new(120, 28, 1040, Height - DockH - 40);
    public static readonly Rectangle Create = new(280, 40, 720, Height - DockH - 52);
    public static readonly Rectangle Automap = new(24, 24, 360, Height - DockH - 36);

    public static Rectangle Modal(int height)
    {
        height = MathHelper.Clamp(height, 240, Height - DockH - 40);
        return new Rectangle((Width - ModalWidth) / 2, Math.Max(24, (Height - DockH - height) / 2),
            ModalWidth, height);
    }

    public static void OptionRow(UiCanvas ui, Rectangle modal, int index, int selected, string line,
        int startY = 0)
    {
        _ = startY;
        HudChrome.ListRow(ui, OptionHit(modal, index), index == selected, line);
    }

    public static Rectangle OptionHit(Rectangle modal, int index, int startY = 0)
    {
        _ = startY;
        return HudChrome.MenuRow(modal, index, 36);
    }

    public static void Pointer(UiCanvas ui, Vector2 at)
    {
        var x = (int)at.X;
        var y = (int)at.Y;
        if ((uint)x > Width || (uint)y > Height) return;

        for (var i = 0; i < 16; i++)
        {
            var w = i < 11 ? i + 1 : Math.Max(1, 16 - i);
            ui.Fill(new Rectangle(x, y + i, w + 2, 1), UiTheme.PointerSkirt);
            ui.Fill(new Rectangle(x + 1, y + i, w, 1), i < 11 ? UiTheme.Parchment : UiTheme.Gold);
        }
    }
}

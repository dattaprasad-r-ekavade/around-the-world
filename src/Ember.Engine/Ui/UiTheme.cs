using Microsoft.Xna.Framework;

namespace Ember.Ui;

/// <summary>
/// Daggerfall chrome: oak frames, parchment fields, ink, brass. Vitals are green / red / blue.
/// </summary>
public static class UiTheme
{
    public static readonly Color Panel = new(214, 190, 148, 255);
    public static readonly Color PanelRaised = new(226, 204, 162, 255);
    public static readonly Color PanelSheer = new(214, 190, 148, 230);
    public static readonly Color Header = new(92, 54, 24, 255);
    public static readonly Color Wood = new(78, 46, 20, 255);
    public static readonly Color WoodLight = new(140, 92, 42, 255);
    public static readonly Color WoodDark = new(36, 20, 8, 255);
    public static readonly Color Parchment = new(222, 198, 154, 255);
    public static readonly Color Scrim = new(8, 4, 2, 196);
    public static readonly Color NoBorder = new(8, 4, 2, 0);

    public static readonly Color Border = new(176, 132, 52);
    public static readonly Color BorderDim = new(92, 58, 22);

    public static readonly Color Accent = new(168, 36, 28);
    public static readonly Color Gold = new(196, 148, 52);
    public static readonly Color GoldDim = new(148, 108, 36);
    public static readonly Color GoldBright = new(220, 176, 72);
    public static readonly Color Bronze = new(168, 112, 48);

    public static readonly Color Pocket = new(112, 48, 112);
    public static readonly Color Barred = new(120, 72, 48);
    public static readonly Color BarredText = new(80, 28, 16);
    public static readonly Color PointerSkirt = new(24, 12, 4, 220);

    public static readonly Color Heading = new(48, 24, 8);
    public static readonly Color Body = new(36, 22, 10);
    public static readonly Color Muted = new(96, 68, 36);
    public static readonly Color Hint = new(88, 56, 28);
    public static readonly Color HintDim = new(110, 82, 48);
    public static readonly Color Faint = new(120, 92, 56);
    public static readonly Color Warning = new(148, 32, 20);
    public static readonly Color Error = new(168, 28, 20);
    public static readonly Color Prompt = new(36, 22, 10);

    /// <summary>Classic Daggerfall: health green, fatigue red, magicka blue.</summary>
    public static readonly Color Health = new(36, 168, 40);
    public static readonly Color Fatigue = new(188, 32, 24);
    public static readonly Color Magicka = new(36, 64, 188);
    public static readonly Color Hunger = new(156, 96, 36);
    public static readonly Color BarBack = new(20, 12, 6, 230);

    public static readonly Color RowSelected = new(168, 132, 80, 255);
    public static readonly Color RowIdle = new(210, 186, 144, 0);
    public static readonly Color RowSelectedBorder = new(128, 28, 20);
    public static readonly Color RowIdleBorder = new(176, 148, 100);
    public static readonly Color RowDangerBorder = new(148, 32, 20);
    public static readonly Color RowSelectedText = new(112, 16, 12);
    public static readonly Color RowIdleText = new(36, 22, 10);

    public static (Color Fill, Color Border) Row(bool selected) => selected
        ? (RowSelected, RowSelectedBorder)
        : (RowIdle, RowIdleBorder);

    public static Color RowText(bool selected) => selected ? RowSelectedText : RowIdleText;
}

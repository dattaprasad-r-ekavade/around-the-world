using Microsoft.Xna.Framework;

namespace Ember.Ui;

/// <summary>
/// The interface's palette, by what each colour is for.
///
/// These were literals: <c>new Color(151, 206, 210)</c> appeared twenty-five times across the
/// screens, and the selected-row pair appeared nine times each. Restyling meant finding every
/// copy, and a copy that was missed did not fail a build — it just left one panel a different
/// shade of teal from the rest, which is the kind of defect nobody reports and nobody can find.
///
/// Named by role rather than by hue, so a change of mind about the accent colour is one edit
/// and "which of these greys is body text" has an answer.
/// </summary>
public static class UiTheme
{
    // ----------------------------------------------------------------- structure

    /// <summary>A panel that sits over the world.</summary>
    public static readonly Color Panel = new(5, 11, 18, 248);

    /// <summary>A panel that sits over another panel, so slightly lighter.</summary>
    public static readonly Color PanelRaised = new(6, 12, 19, 246);

    /// <summary>A prompt or ledger the world still shows through.</summary>
    public static readonly Color PanelSheer = new(5, 11, 18, 225);

    /// <summary>Behind a modal, dimming everything under it.</summary>
    public static readonly Color Scrim = new(3, 6, 10, 214);

    /// <summary>A scrim with no border of its own.</summary>
    public static readonly Color NoBorder = new(3, 6, 10, 0);

    /// <summary>The ordinary border of a panel.</summary>
    public static readonly Color Border = new(91, 146, 159);

    /// <summary>A quieter border, for a panel inside a panel.</summary>
    public static readonly Color BorderDim = new(65, 105, 119);

    // ----------------------------------------------------------------- accents

    /// <summary>Section labels, and the colour the interface is built around.</summary>
    public static readonly Color Accent = new(151, 206, 210);

    /// <summary>Money, keys, and anything the player is meant to reach for.</summary>
    public static readonly Color Gold = new(232, 194, 116);

    /// <summary>A heading in gold, dimmer than a value in gold.</summary>
    public static readonly Color GoldDim = new(214, 183, 108);

    /// <summary>A number counted in coin.</summary>
    public static readonly Color GoldBright = new(228, 197, 122);

    /// <summary>Trade, stone, and the warm frame around a decision.</summary>
    public static readonly Color Bronze = new(205, 157, 98);

    /// <summary>A pocket worth picking. Distinct from talking, or testers never find it.</summary>
    public static readonly Color Pocket = new(190, 148, 196);

    /// <summary>A door that will not open until the room is clear.</summary>
    public static readonly Color Barred = new(150, 120, 110);

    /// <summary>Text on a barred door.</summary>
    public static readonly Color BarredText = new(224, 196, 186);

    /// <summary>The dark skirt under the custom pointer, so it survives any background.</summary>
    public static readonly Color PointerSkirt = new(12, 14, 18, 220);

    // ----------------------------------------------------------------- text

    /// <summary>A heading over a panel.</summary>
    public static readonly Color Heading = new(214, 226, 226);

    /// <summary>Ordinary text.</summary>
    public static readonly Color Body = new(203, 216, 214);

    /// <summary>Text that is not the point of the panel.</summary>
    public static readonly Color Muted = new(150, 162, 170);

    /// <summary>A footer, or a line of key hints.</summary>
    public static readonly Color Hint = new(163, 191, 194);

    /// <summary>A hint under something already read.</summary>
    public static readonly Color HintDim = new(140, 156, 164);

    /// <summary>An empty list, or something the player cannot have.</summary>
    public static readonly Color Faint = new(142, 157, 157);

    /// <summary>Something is wrong and the player should know.</summary>
    public static readonly Color Warning = new(196, 118, 96);

    /// <summary>Something failed.</summary>
    public static readonly Color Error = new(228, 128, 118);

    /// <summary>A prompt or empty-state sentence.</summary>
    public static readonly Color Prompt = new(174, 188, 186);

    // ----------------------------------------------------------------- rows

    /// <summary>Fill of a row the player is on.</summary>
    public static readonly Color RowSelected = new(74, 67, 43, 245);

    /// <summary>Fill of a row they are not.</summary>
    public static readonly Color RowIdle = new(17, 27, 35, 220);

    /// <summary>Border of the row the player is on.</summary>
    public static readonly Color RowSelectedBorder = new(224, 181, 88);

    /// <summary>Border of a row they are not.</summary>
    public static readonly Color RowIdleBorder = new(54, 82, 91);

    /// <summary>Border of a selected row that would cost something irreversible.</summary>
    public static readonly Color RowDangerBorder = new(214, 118, 96);

    /// <summary>Label on the row the player is on.</summary>
    public static readonly Color RowSelectedText = Color.White;

    /// <summary>Label on a row they are not.</summary>
    public static readonly Color RowIdleText = new(192, 207, 205);

    /// <summary>The fill and border of a list row, given whether it is the current one.</summary>
    public static (Color Fill, Color Border) Row(bool selected) => selected
        ? (RowSelected, RowSelectedBorder)
        : (RowIdle, RowIdleBorder);

    /// <summary>The label colour of a list row.</summary>
    public static Color RowText(bool selected) => selected ? RowSelectedText : RowIdleText;
}

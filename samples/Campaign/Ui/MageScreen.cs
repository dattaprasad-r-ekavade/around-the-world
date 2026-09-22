using Ember.Ui;
using Microsoft.Xna.Framework;

namespace Campaign;

/// <summary>Mark, recall, hall travel, and the circle's errands.</summary>
public sealed class MageScreen
{
    public static readonly string[] Lines =
    [
        "Mark this hall",
        "Recall to the mark",
        "Travel to another hall",
        "Work of the circle",
        "Burn a sickness (10 gp)"
    ];

    public bool Open { get; set; }
    public int Selected { get; set; }

    public void Draw(UiCanvas ui, Ledger pack)
    {
        if (!Open) return;

        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var modal = HudLayout.Modal(400);
        HudChrome.Window(ui, modal, "Mages Guild",
            pack.HasMark ? "A mark is set. Recall returns you here."
                : "No mark. Set one before you wander.",
            "Up Down   Enter   Click   Esc");
        for (var i = 0; i < Lines.Length; i++)
            HudChrome.ListRow(ui, ItemRow(i), i == Selected, Lines[i]);
    }

    public Rectangle ItemRow(int index) =>
        HudChrome.MenuRow(HudLayout.Modal(400), index, 40);

    public void Move(int delta)
    {
        var n = Lines.Length;
        Selected = (Selected + delta % n + n) % n;
    }
}

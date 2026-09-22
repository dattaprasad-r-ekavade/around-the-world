using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Race and class before the road. Esc does not skip; Enter confirms the last page.</summary>
public sealed class CreateScreen
{
    public bool Open { get; set; } = true;
    public int Page { get; set; }
    public int Selected { get; set; }
    public RaceId Race { get; set; } = RaceId.Breton;
    public ClassId Class { get; set; } = ClassId.Warrior;

    public static readonly string[] RaceBlurb =
    [
        "Breton. Magicka and will. Restoration comes easy.",
        "Redguard. Sword-arm and endurance.",
        "Nord. Cold means less. A heavy blow.",
        "Bosmer. Bow and quiet feet."
    ];

    public static readonly string[] ClassBlurb =
    [
        "Warrior. Blade, iron, leather.",
        "Mage. Spark, heal, no hauberk.",
        "Thief. Bow, lockpicks, the night.",
        "Spellsword. A blade and a heal."
    ];

    public void Draw(UiCanvas ui)
    {
        if (!Open) return;
        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var frame = HudLayout.Create;
        var kicker = Page == 0 ? "The Bay asks who you are."
            : Page == 1 ? "How you mean to walk it."
            : "The letter is already in your pack.";
        HudChrome.Window(ui, frame, "Who walks", kicker, "Up Down   Enter   Click");

        if (Page == 0)
            DrawList(ui, Hero.RaceNames, RaceBlurb);
        else if (Page == 1)
            DrawList(ui, Hero.ClassNames, ClassBlurb);
        else
        {
            ui.Heading($"{Hero.RaceNames[(int)Race]}  {Hero.ClassNames[(int)Class]}",
                new Vector2(frame.X + HudChrome.Pad, frame.Y + HudChrome.Header + 8), 22, UiTheme.Heading);
            HudChrome.ListRow(ui, ItemRow(0), Selected == 0, "Walk the Bay");
        }
    }

    private void DrawList(UiCanvas ui, string[] lines, string[] blurbs)
    {
        for (var i = 0; i < lines.Length; i++)
            HudChrome.ListRow(ui, ItemRow(i), i == Selected, lines[i]);
        var pick = Math.Clamp(Selected, 0, blurbs.Length - 1);
        var frame = HudLayout.Create;
        ui.TextWrapped(blurbs[pick], new Vector2(frame.X + HudChrome.Pad, frame.Bottom - 88),
            frame.Width - HudChrome.Pad * 2, HudChrome.Body, UiTheme.Body, 3);
    }

    public Rectangle ItemRow(int index) => HudChrome.MenuRow(HudLayout.Create, index);

    public int Count => Page == 0 ? 4 : Page == 1 ? 4 : 1;
}

using Ember.Ui;
using Microsoft.Xna.Framework;

namespace Campaign;

public enum PausePage
{
    Main,
    Controls,
    Settings
}

/// <summary>Esc journal: resume, pack, map, a controls list, and quit. Does not kill the game.</summary>
public sealed class PauseMenu
{
    public static readonly string[] MainLines =
        ["Resume", "Inventory", "Character", "Magic", "Journal", "Map", "Controls", "Settings", "Quit"];

    public static readonly (string Action, string Bind)[] Controls =
    [
        ("Move", "W A S D"),
        ("Look", "Mouse"),
        ("Turn / look", "Arrow keys"),
        ("Use / talk", "E"),
        ("Attack", "F  or  left click"),
        ("Sprint", "Shift"),
        ("Jump / swim up", "Space"),
        ("Crouch", "C  hold"),
        ("Climb", "V  hold"),
        ("Inventory", "I"),
        ("Character", "K"),
        ("Spellmaker", "4"),
        ("Cast spell", "Q"),
        ("Quest journal", "J"),
        ("Map / automap", "M"),
        ("Mount / dismount", "H"),
        ("Rest / camp", "R"),
        ("Eat ration", "3"),
        ("Stamina / cure", "1 / 2"),
        ("Save / load", "F5 / F9"),
        ("Menu", "Esc  or  Tab")
    ];

    public bool Open { get; set; }
    public PausePage Page { get; set; }
    public int Selected { get; set; }

    public void Show()
    {
        Open = true;
        Page = PausePage.Main;
        Selected = 0;
    }

    public void Close()
    {
        Open = false;
        Page = PausePage.Main;
        Selected = 0;
    }

    public void Move(int delta)
    {
        var n = Count;
        if (n <= 0) return;
        Selected = (Selected + delta % n + n) % n;
    }

    public int Count => Page switch
    {
        PausePage.Settings => 4,
        PausePage.Controls => 1,
        _ => MainLines.Length
    };

    public void Draw(UiCanvas ui, bool music, float uiScale, float mouseLook)
    {
        if (!Open) return;

        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        if (Page == PausePage.Controls)
            DrawControls(ui);
        else if (Page == PausePage.Settings)
            DrawSettings(ui, music, uiScale, mouseLook);
        else
            DrawMain(ui);
    }

    private void DrawMain(UiCanvas ui)
    {
        var frame = HudLayout.Pause;
        HudChrome.Window(ui, frame, "Journal", "The road holds while you read.",
            "Up Down   Enter   Click   Esc");
        for (var i = 0; i < MainLines.Length; i++)
            HudChrome.ListRow(ui, MainRow(i), i == Selected, MainLines[i]);
    }

    public Rectangle MainRow(int index) => HudChrome.MenuRow(HudLayout.Pause, index, 42);

    public Rectangle SettingRow(int index) => HudChrome.MenuRow(HudLayout.Pause, index, 44);

    public Rectangle BackRow
    {
        get
        {
            var frame = Page == PausePage.Controls ? HudLayout.PauseWide : HudLayout.Pause;
            return new Rectangle(frame.X + HudChrome.Pad, frame.Bottom - 52, 200, 32);
        }
    }

    public Rectangle Row(int index) => Page switch
    {
        PausePage.Settings => SettingRow(index),
        PausePage.Controls => BackRow,
        _ => MainRow(index)
    };

    private void DrawControls(UiCanvas ui)
    {
        var frame = HudLayout.PauseWide;
        HudChrome.Window(ui, frame, "Controls", "Same hands on the wild road and in town.",
            "Click Back   Esc");
        var mid = Controls.Length / 2 + Controls.Length % 2;
        var content = HudChrome.Content(frame);
        for (var i = 0; i < Controls.Length; i++)
        {
            var col = i < mid ? 0 : 1;
            var row = col == 0 ? i : i - mid;
            var x = content.X + col * 440;
            var y = content.Y + row * 26;
            ui.Text(Controls[i].Action, new Vector2(x, y), HudChrome.Body, UiTheme.Body);
            ui.Text(Controls[i].Bind, new Vector2(x + 188, y), HudChrome.Body, UiTheme.Gold);
        }

        var back = BackRow;
        HudChrome.ListRow(ui, back, true, "Back");
    }

    private void DrawSettings(UiCanvas ui, bool music, float uiScale, float mouseLook)
    {
        var frame = HudLayout.Pause;
        HudChrome.Window(ui, frame, "Settings", null,
            "Enter toggle   Left Right   Click   Esc");
        DrawSetting(ui, 0, "Music", music ? "On" : "Off");
        DrawSetting(ui, 1, "UI scale", uiScale.ToString("0.00"));
        DrawSetting(ui, 2, "Mouse look", mouseLook.ToString("0.000"));
        DrawSetting(ui, 3, "Back", "");
    }

    private void DrawSetting(UiCanvas ui, int index, string label, string value)
    {
        HudChrome.ListRow(ui, SettingRow(index), index == Selected, label, value.Length > 0 ? value : null);
    }
}

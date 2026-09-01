using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Quest log. J opens it.</summary>
public sealed class JournalScreen
{
    public bool Open { get; set; }
    public int Selected { get; set; }

    public void Draw(UiCanvas ui, Hero hero)
    {
        if (!Open) return;
        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var frame = HudLayout.Journal;
        HudChrome.Window(ui, frame, "Journal", "Tasks you have taken.",
            "Up Down   J close");
        if (hero.Log.Count == 0)
        {
            ui.Text("No tasks. The road is the work.",
                new Vector2(frame.X + HudChrome.Pad, frame.Y + HudChrome.Header + 8),
                HudChrome.Body, UiTheme.Muted);
            return;
        }

        for (var i = 0; i < hero.Log.Count; i++)
        {
            var note = hero.Log[i];
            HudChrome.ListRow(ui, ItemRow(i), i == Selected, note.Done ? $"×  {note.Title}" : note.Title);
        }

        var pick = hero.Log[Math.Clamp(Selected, 0, hero.Log.Count - 1)];
        var pane = DetailPane();
        HudChrome.Pane(ui, pane);
        ui.Heading(pick.Title, new Vector2(pane.X + 16, pane.Y + 16), 20, UiTheme.Heading);
        ui.Text(pick.Done ? "Done" : "Open", new Vector2(pane.X + 16, pane.Y + 48),
            HudChrome.Caption, UiTheme.Gold);
        ui.TextWrapped(pick.Body, new Vector2(pane.X + 16, pane.Y + 78), pane.Width - 32,
            HudChrome.Body, UiTheme.Body, 10);
    }

    public Rectangle ItemRow(int index)
    {
        var frame = HudLayout.Journal;
        return HudChrome.ListHit(frame, index, frame.X + HudChrome.Pad, 360);
    }

    public static Rectangle DetailPane()
    {
        var frame = HudLayout.Journal;
        var content = HudChrome.Content(frame);
        return new Rectangle(frame.X + 404, content.Y, frame.Right - 24 - (frame.X + 404), content.Height);
    }
}

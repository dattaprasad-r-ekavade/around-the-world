using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

public readonly record struct DialogueOption(string Line, int Id);

/// <summary>Daggerfall talk: face, parchment line, then keywords in maroon when selected.</summary>
public sealed class DialogueScreen
{
    public bool Open { get; set; }
    public string Speaker { get; set; } = "";
    public string Body { get; set; } = "";
    public int Selected { get; set; }
    public DialogueOption[] Options { get; set; } = [];

    public void Show(string speaker, string body, DialogueOption[] options)
    {
        Open = true;
        Speaker = speaker;
        Body = body;
        Options = options;
        Selected = 0;
    }

    public void Close() => Open = false;

    public void Draw(UiCanvas ui)
    {
        if (!Open) return;

        var box = HudLayout.Dialogue;
        HudChrome.Wood(ui, box);
        var field = new Rectangle(box.X + 8, box.Y + 8, box.Width - 16, box.Height - 16);
        ui.Fill(field, UiTheme.Parchment);

        var face = new Rectangle(field.X + 12, field.Y + 12, 88, 88);
        HudChrome.Wood(ui, face);
        ui.Fill(new Rectangle(face.X + 8, face.Y + 8, face.Width - 16, face.Height - 16), UiTheme.WoodDark);
        ui.HeadingCentred(Speaker.Length == 0 ? "?" : Speaker[..1].ToUpperInvariant(),
            face.X + face.Width * 0.5f, face.Y + 28, 28, UiTheme.Parchment);

        ui.Heading(Speaker, new Vector2(face.Right + 16, field.Y + 12), 20, UiTheme.Heading);
        ui.TextWrapped(Body, new Vector2(face.Right + 16, field.Y + 42), field.Right - face.Right - 32,
            15, UiTheme.Body, 3);

        for (var i = 0; i < Options.Length; i++)
            HudChrome.ListRow(ui, OptionRow(i), i == Selected, Options[i].Line);
    }

    public Rectangle OptionRow(int index)
    {
        var box = HudLayout.Dialogue;
        return new Rectangle(box.X + 24, box.Y + 128 + index * 26, box.Width - 48, 24);
    }

    public void Move(int delta)
    {
        var n = Math.Max(1, Options.Length);
        Selected = (Selected + delta % n + n) % n;
    }

    public DialogueOption Current =>
        Options.Length == 0 ? new DialogueOption("Goodbye", 0) : Options[Selected];
}

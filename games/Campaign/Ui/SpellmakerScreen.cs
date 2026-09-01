using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Build a spell from effect and magnitude, then keep it. 4 opens this.</summary>
public sealed class SpellmakerScreen
{
    public bool Open { get; set; }
    public int Selected { get; set; }
    public int Effect { get; set; }
    public int Magnitude { get; set; } = 12;

    public static readonly string[] Lines =
        ["Effect", "Magnitude", "Save to book", "Cast selected"];

    public void Draw(UiCanvas ui, Hero hero)
    {
        if (!Open) return;
        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var modal = HudLayout.Modal(460);
        HudChrome.Window(ui, modal, "Spellmaker",
            $"Magicka {hero.Magicka:0}/{hero.MagickaMax:0}",
            "Up Down   Left Right   Enter   4");

        var effect = (SpellEffect)Math.Clamp(Effect, 0, 3);
        var cost = Spell.CostOf(effect, Magnitude);
        var labels = new[]
        {
            $"Effect   {Spell.Label(effect)}",
            $"Magnitude   {Magnitude}",
            $"Save   cost {cost:0}",
            hero.Spells.Count == 0
                ? "Cast   (empty book)"
                : $"Cast   {hero.Spells[Math.Clamp(hero.SpellSel, 0, hero.Spells.Count - 1)].Name}"
        };
        for (var i = 0; i < labels.Length; i++)
            HudChrome.ListRow(ui, ItemRow(i), i == Selected, labels[i]);

        ui.Text("Book", new Vector2(modal.X + HudChrome.Pad, modal.Y + HudChrome.Header + 168),
            HudChrome.Caption, UiTheme.Muted);
        var y = modal.Y + HudChrome.Header + 190;
        for (var i = 0; i < hero.Spells.Count && i < 5; i++)
        {
            var on = i == hero.SpellSel;
            ui.Text(hero.Spells[i].Name, new Vector2(modal.X + HudChrome.Pad + 8, y), HudChrome.Caption,
                on ? UiTheme.Gold : UiTheme.Body);
            y += 20;
        }
    }

    public Rectangle ItemRow(int index) => HudChrome.MenuRow(HudLayout.Modal(460), index, 40);
}

using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>Attributes, skills, faction standing. K or the journal.</summary>
public sealed class SheetScreen
{
    public bool Open { get; set; }
    public int Tab { get; set; }

    public void Draw(UiCanvas ui, Hero hero, Ledger pack)
    {
        if (!Open) return;
        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var frame = HudLayout.Inventory;
        HudChrome.Window(ui, frame, hero.Name,
            $"{Hero.RaceNames[(int)hero.Race]}  ·  {Hero.ClassNames[(int)hero.Class]}",
            "Left Right tabs   K close");

        HudChrome.Tab(ui, TabRow(0), Tab == 0, "ATTR");
        HudChrome.Tab(ui, TabRow(1), Tab == 1, "SKILL");
        HudChrome.Tab(ui, TabRow(2), Tab == 2, "FACTION");

        var x = frame.X + 196;
        var y = frame.Y + HudChrome.Header;
        if (Tab == 0)
        {
            for (var i = 0; i < Hero.AttrCount; i++)
            {
                ui.Text(Hero.AttrNames[i], new Vector2(x, y), HudChrome.Body, UiTheme.Body);
                ui.Text(hero.Attr[i].ToString(), new Vector2(x + 88, y), HudChrome.Body, UiTheme.Gold);
                y += 28;
            }

            ui.Text($"Magicka  {hero.Magicka:0}/{hero.MagickaMax:0}",
                new Vector2(x, y + 12), HudChrome.Body, UiTheme.Gold);
            var enc = hero.Encumbrance(pack.Gold);
            ui.Text($"Carry  {hero.BurdenKg(pack.Gold):0.0} / {hero.CarryMaxKg:0.0} kg   gold is heavy",
                new Vector2(x, y + 40), HudChrome.Caption, enc > 0.85f ? UiTheme.Warning : UiTheme.Muted);
            ui.Text($"{Gear.Of(hero.Weapon).Name}    {Gear.Of(hero.Armor).Name}    {Gear.Of(hero.Bow).Name}  ×{hero.Arrows}",
                new Vector2(x, y + 64), HudChrome.Caption, UiTheme.Body);
        }
        else if (Tab == 1)
        {
            for (var i = 0; i < Hero.SkillCount; i++)
            {
                ui.Text(Hero.SkillNames[i], new Vector2(x, y), HudChrome.Body, UiTheme.Body);
                ui.Text($"{hero.Skill[i]:0}", new Vector2(x + 180, y), HudChrome.Body, UiTheme.Gold);
                y += 28;
            }
        }
        else
        {
            for (var i = 0; i < Hero.FactionCount; i++)
            {
                var member = hero.Member[i] ? "  member" : "";
                ui.Text(Hero.FactionNames[i], new Vector2(x, y), HudChrome.Body, UiTheme.Body);
                ui.Text($"{Factions.Standing(hero.Rep[i])}  {hero.Rep[i]:+0;-0}{member}",
                    new Vector2(x + 220, y), HudChrome.Body, UiTheme.Gold);
                y += 34;
            }
        }
    }

    public Rectangle TabRow(int index) => HudChrome.TabHit(HudLayout.Inventory, index);
}

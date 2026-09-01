using Ember.Ui;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Campaign;

public readonly record struct PackItem(
    string Id, string Name, string Kind, int Count, string Detail, bool Usable);

/// <summary>Pack: categories, list, detail. Layout from HudChrome so hits match the paint.</summary>
public sealed class InventoryScreen
{
    public static readonly string[] Tabs = ["ALL", "AID", "WEAPONS", "APPAREL", "MISC"];

    public bool Open { get; set; }
    public int Tab { get; set; }
    public int Selected { get; set; }

    public void Draw(UiCanvas ui, Ledger pack, Hero hero)
    {
        if (!Open) return;

        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var frame = HudLayout.Inventory;
        HudChrome.Window(ui, frame, "Inventory",
            "What you carry has weight.",
            "Left Right tabs   Up Down   Enter   Click   Esc",
            $"{pack.Gold} gp   {hero.BurdenKg(pack.Gold):0.0}/{hero.CarryMaxKg:0.0} kg");

        var items = ListFor(pack, hero);
        if (Selected >= items.Count) Selected = Math.Max(0, items.Count - 1);

        for (var i = 0; i < Tabs.Length; i++)
            HudChrome.Tab(ui, TabRow(i), i == Tab, Tabs[i]);

        if (items.Count == 0)
            ui.Text("Nothing in this pocket.", new Vector2(frame.X + 196, frame.Y + HudChrome.Header + 8),
                HudChrome.Body, UiTheme.Muted);
        else
        {
            for (var i = 0; i < items.Count; i++)
                HudChrome.ListRow(ui, ItemRow(i), i == Selected, items[i].Name, items[i].Count.ToString());
        }

        var pane = DetailPane();
        HudChrome.Pane(ui, pane);
        if (items.Count > 0)
        {
            var item = items[Selected];
            ui.Heading(item.Name, new Vector2(pane.X + 16, pane.Y + 14), 20, UiTheme.Heading);
            ui.Text(item.Kind.ToUpperInvariant(), new Vector2(pane.X + 16, pane.Y + 46),
                HudChrome.Caption, UiTheme.GoldDim);
            ui.TextWrapped(item.Detail, new Vector2(pane.X + 16, pane.Y + 74), pane.Width - 32,
                HudChrome.Body, UiTheme.Body, 8);
            if (item.Usable)
                ui.Text(item.Kind is "weapon" or "bow" or "armor" ? "Enter  Equip" : "Enter  Use",
                    new Vector2(pane.X + 16, pane.Bottom - 36), HudChrome.Caption, UiTheme.Gold);
        }
    }

    public Rectangle TabRow(int index) => HudChrome.TabHit(HudLayout.Inventory, index);

    public Rectangle ItemRow(int index)
    {
        var frame = HudLayout.Inventory;
        return HudChrome.ListHit(frame, index, frame.X + 196, 360);
    }

    public static Rectangle DetailPane()
    {
        var frame = HudLayout.Inventory;
        var content = HudChrome.Content(frame);
        return new Rectangle(frame.X + 572, content.Y, frame.Right - 24 - (frame.X + 572), content.Height);
    }

    public List<PackItem> ListFor(Ledger pack, Hero hero)
    {
        var all = new List<PackItem>();
        Add(all, pack.Rations, "rations", "Rations", "aid",
            "Dried meat and hard bread. E to eat.", true);
        Add(all, pack.Meals, "stew", "Hot stew", "aid",
            "A bowl that warms the blood. E to eat.", true);
        Add(all, pack.Stamina, "stamina", "Stamina draught", "aid",
            "Restores your wind. E to drink.", true);
        Add(all, pack.Cures, "cure", "Potion of cure disease", "aid",
            "Burns swamp rot and fever. E to drink.", true);
        foreach (var id in hero.OwnedGear)
        {
            var item = Gear.Of(id);
            var worn = id == hero.Weapon || id == hero.Armor || id == hero.Bow;
            var mark = worn ? " (worn)" : "";
            Add(all, 1, item.Id, item.Name + mark, item.Slot, item.Detail, true);
        }

        Add(all, hero.Arrows, "arrows", "Arrows", "weapon",
            "Loose with F when a bow is equipped.", false);
        Add(all, pack.HasCloak ? 1 : 0, "cloak", "Wool cloak", "apparel",
            "Keeps the night off your shoulders.", false);
        Add(all, hero.Relic ? 1 : 0, "totem", "Totem of Tiber Septim", "misc",
            "Cold brass. A hall will take it.", false);
        Add(all, pack.Lockpicks, "lockpick", "Lockpicks", "misc",
            "For doors that were not meant for you.", false);
        Add(all, pack.HasWagon ? 1 : 0, "wagon", "Wagon and team", "misc",
            "A cart for the road.", false);
        Add(all, pack.HasShip ? 1 : 0, "ship", "Longboat", "misc",
            "For the coast roads.", false);
        Add(all, pack.Keys.Count, "keys", "Keys", "misc",
            "Iron that fits a lock you have seen.", false);

        if (Tab == 0) return all;
        var filtered = new List<PackItem>();
        foreach (var item in all)
        {
            if (Tab == 1 && item.Kind == "aid") filtered.Add(item);
            else if (Tab == 2 && item.Kind is "weapon" or "bow") filtered.Add(item);
            else if (Tab == 3 && item.Kind is "apparel" or "armor") filtered.Add(item);
            else if (Tab == 4 && item.Kind == "misc") filtered.Add(item);
        }

        return filtered;
    }

    public void MoveTab(int delta)
    {
        Tab = (Tab + delta % Tabs.Length + Tabs.Length) % Tabs.Length;
        Selected = 0;
    }

    public void Move(int delta, int count)
    {
        if (count <= 0) { Selected = 0; return; }
        Selected = (Selected + delta % count + count) % count;
    }

    private static void Add(List<PackItem> into, int count, string id, string name, string kind,
        string detail, bool usable)
    {
        if (count <= 0) return;
        into.Add(new PackItem(id, name, kind, count, detail, usable));
    }
}

using Ember.Ui;
using Microsoft.Xna.Framework;

namespace Campaign;

/// <summary>Barter. Layout lives on HudLayout.Shop so hits match the paint.</summary>
public sealed class ShopScreen
{
    public bool Open { get; set; }
    public int Selected { get; set; }

    public void Draw(UiCanvas ui, Ledger pack, Hero hero, bool openHours)
    {
        if (!Open) return;

        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var modal = HudLayout.Shop;
        HudChrome.Window(ui, modal, "Barter",
            openHours ? "Dawn to dusk. Steel and hide sit with the bread." : "Closed until morning.",
            "Up Down   E buy   Click   Esc",
            $"{pack.Gold} gp");

        var list = ListBounds();
        HudChrome.Pane(ui, list);
        for (var i = 0; i < Ledger.Goods.Length; i++)
        {
            var good = Ledger.Goods[i];
            var price = pack.PriceOf(good, hero);
            HudChrome.ListRow(ui, ItemRow(i), i == Selected, good.Name,
                $"{price} gp   ×{pack.Owned(good, hero)}");
        }

        var pane = DetailPane();
        HudChrome.Pane(ui, pane);
        var pick = Ledger.Goods[Selected];
        ui.Heading(pick.Name, new Vector2(pane.X + 16, pane.Y + 14), 20, UiTheme.Heading);
        var gear = Gear.FromShop(pick.Id);
        var detail = gear != Gear.None
            ? Gear.Of(gear).Detail
            : pick.Kind == "ammo"
                ? "A dozen shafts. Equip a bow, then F."
                : "The stall keeps what the road needs. E to buy.";
        ui.TextWrapped(detail, new Vector2(pane.X + 16, pane.Y + 52), pane.Width - 32,
            HudChrome.Body, UiTheme.Body, 8);
        ui.Text($"Have {pack.Owned(pick, hero)}", new Vector2(pane.X + 16, pane.Bottom - 36),
            HudChrome.Caption, UiTheme.Muted);
    }

    public static Rectangle ListBounds()
    {
        var modal = HudLayout.Shop;
        var content = HudChrome.Content(modal);
        return new Rectangle(content.X, content.Y, 560, content.Height);
    }

    public static Rectangle DetailPane()
    {
        var modal = HudLayout.Shop;
        var content = HudChrome.Content(modal);
        return new Rectangle(content.X + 576, content.Y, content.Width - 576, content.Height);
    }

    public Rectangle ItemRow(int index)
    {
        var list = ListBounds();
        return new Rectangle(list.X + 8, list.Y + 8 + index * 28, list.Width - 16, 26);
    }

    public void Move(int delta)
    {
        var n = Ledger.Goods.Length;
        Selected = (Selected + delta % n + n) % n;
    }
}

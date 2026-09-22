using Ember.Ui;
using Microsoft.Xna.Framework;

namespace Campaign;

/// <summary>Daggerfall bank: coin here is coin in every town.</summary>
public sealed class BankScreen
{
    public static readonly string[] Lines =
    [
        "Deposit 10 gold",
        "Deposit all",
        "Withdraw 10 gold",
        "Withdraw all"
    ];

    public bool Open { get; set; }
    public int Selected { get; set; }

    public void Draw(UiCanvas ui, Ledger pack)
    {
        if (!Open) return;

        ui.Scrim(UiTheme.Scrim, UiTheme.NoBorder);
        var modal = HudLayout.Modal(360);
        HudChrome.Window(ui, modal, "Bank of the Bay",
            "Letters of credit. Coin here is coin in every town.",
            "Up Down   Enter   Click   Esc",
            $"purse {pack.Gold}    vault {pack.BankGold}");
        for (var i = 0; i < Lines.Length; i++)
            HudChrome.ListRow(ui, ItemRow(i), i == Selected, Lines[i]);
    }

    public Rectangle ItemRow(int index) =>
        HudChrome.MenuRow(HudLayout.Modal(360), index, 42);

    public void Move(int delta)
    {
        var n = Lines.Length;
        Selected = (Selected + delta % n + n) % n;
    }
}

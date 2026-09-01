using Ember.Ui;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>
/// Oak frame, parchment field, and the list geometry every Daggerfall window shares.
/// </summary>
public static class HudChrome
{
    public const int Header = 56;
    public const int Footer = 38;
    public const int Pad = 22;
    public const int RowH = 32;
    public const int TabW = 148;
    public const int TabH = 30;
    public const int TabGap = 6;

    public const float Title = 22f;
    public const float Subtitle = 13f;
    public const float Body = 15f;
    public const float Caption = 13f;
    public const float Hint = 12f;
    public const float Label = 12f;

    public static Rectangle Content(Rectangle frame) =>
        new(frame.X + Pad, frame.Y + Header, frame.Width - Pad * 2,
            Math.Max(40, frame.Height - Header - Footer));

    public static void Window(UiCanvas ui, Rectangle frame, string title,
        string? kicker = null, string? hint = null, string? meta = null)
    {
        Wood(ui, frame);
        var field = new Rectangle(frame.X + 8, frame.Y + 8, frame.Width - 16, frame.Height - 16);
        ui.Fill(field, UiTheme.Parchment);
        ui.Fill(new Rectangle(field.X, field.Y, field.Width, Header - 12), UiTheme.Header);
        ui.Fill(new Rectangle(field.X, field.Y, field.Width, 2), UiTheme.WoodLight);
        ui.Rule(field.X + 8, frame.Y + Header - 6, field.Width - 16, UiTheme.GoldDim);
        ui.Heading(title, new Vector2(frame.X + Pad, frame.Y + 12), Title, UiTheme.GoldBright);
        if (!string.IsNullOrEmpty(kicker))
            ui.Text(kicker, new Vector2(frame.X + Pad, frame.Y + 36), Subtitle, UiTheme.Parchment);
        if (!string.IsNullOrEmpty(meta))
            ui.TextRight(meta, frame.Right - Pad, frame.Y + 16, Body, UiTheme.GoldBright);
        if (!string.IsNullOrEmpty(hint))
            ui.Text(hint, new Vector2(frame.X + Pad, frame.Bottom - 28), Hint, UiTheme.Hint);
    }

    public static void Wood(UiCanvas ui, Rectangle r)
    {
        ui.Fill(r, UiTheme.Wood);
        ui.Fill(new Rectangle(r.X, r.Y, r.Width, 3), UiTheme.WoodLight);
        ui.Fill(new Rectangle(r.X, r.Y, 3, r.Height), UiTheme.WoodLight);
        ui.Fill(new Rectangle(r.X, r.Bottom - 3, r.Width, 3), UiTheme.WoodDark);
        ui.Fill(new Rectangle(r.Right - 3, r.Y, 3, r.Height), UiTheme.WoodDark);
        ui.Border(r, UiTheme.GoldDim);
        if (r.Width > 10 && r.Height > 10)
            ui.Border(new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8), UiTheme.BorderDim);
    }

    public static void Pane(UiCanvas ui, Rectangle bounds)
    {
        ui.Fill(bounds, UiTheme.PanelRaised);
        ui.Border(bounds, UiTheme.BorderDim);
        ui.Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), UiTheme.WoodLight);
        ui.Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), UiTheme.WoodDark);
    }

    public static void ListRow(UiCanvas ui, Rectangle row, bool on, string left, string? right = null)
    {
        if (on)
        {
            ui.Fill(row, UiTheme.RowSelected);
            ui.Border(row, UiTheme.RowSelectedBorder);
        }

        var ty = row.Y + Math.Max(4, (row.Height - 16) / 2);
        ui.Text(left, new Vector2(row.X + 14, ty), Body, UiTheme.RowText(on));
        if (!string.IsNullOrEmpty(right))
            ui.TextRight(right, row.Right - 12, ty, Caption, on ? UiTheme.Accent : UiTheme.Muted);
    }

    public static void Tab(UiCanvas ui, Rectangle row, bool on, string label)
    {
        if (on)
        {
            ui.Fill(row, UiTheme.RowSelected);
            ui.Border(row, UiTheme.Gold);
        }
        else
        {
            ui.Fill(row, UiTheme.Wood);
            ui.Border(row, UiTheme.BorderDim);
        }

        ui.Text(label, new Vector2(row.X + 12, row.Y + 6), Caption,
            on ? UiTheme.RowSelectedText : UiTheme.Parchment);
    }

    public static void Disc(UiCanvas ui, int cx, int cy, int radius, Color color)
    {
        for (var y = -radius; y <= radius; y++)
        {
            var span = (int)MathF.Sqrt(Math.Max(0, radius * radius - y * y));
            if (span <= 0) continue;
            ui.Fill(new Rectangle(cx - span, cy + y, span * 2 + 1, 1), color);
        }
    }

    public static void Ring(UiCanvas ui, int cx, int cy, int outer, int inner, Color color)
    {
        for (var y = -outer; y <= outer; y++)
        {
            var o = (int)MathF.Sqrt(Math.Max(0, outer * outer - y * y));
            var i = Math.Abs(y) <= inner
                ? (int)MathF.Sqrt(Math.Max(0, inner * inner - y * y))
                : 0;
            if (o <= i) continue;
            ui.Fill(new Rectangle(cx - o, cy + y, o - i, 1), color);
            ui.Fill(new Rectangle(cx + i + 1, cy + y, o - i, 1), color);
        }
    }

    public static void VBar(UiCanvas ui, Rectangle well, float amount, Color fill)
    {
        ui.Fill(well, UiTheme.BarBack);
        ui.Border(well, UiTheme.GoldDim);
        var h = (int)(Math.Clamp(amount, 0f, 1f) * (well.Height - 2));
        if (h > 0)
            ui.Fill(new Rectangle(well.X + 1, well.Bottom - 1 - h, well.Width - 2, h), fill);
    }

    public static Rectangle TabHit(Rectangle frame, int index) =>
        new(frame.X + Pad, frame.Y + Header + index * (TabH + TabGap), TabW, TabH);

    public static Rectangle ListHit(Rectangle frame, int index, int listX, int listW, int rowH = RowH)
    {
        var y = frame.Y + Header + index * rowH;
        return new Rectangle(listX, y, listW, rowH - 4);
    }

    public static Rectangle MenuRow(Rectangle frame, int index, int rowH = 36) =>
        new(frame.X + Pad, frame.Y + Header + index * rowH, frame.Width - Pad * 2, rowH - 4);
}

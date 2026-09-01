using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Ui;

/// <summary>
/// The one drawing surface every screen and HUD renderer uses — and now the thing that
/// actually does the drawing, rather than a set of function pointers back into Game1.
///
/// **It used to be nine delegates.** Game1 owned the sprite batch, the fonts and the white
/// pixel, implemented every primitive, and handed nine <c>Action</c>s to this class so that
/// renderers had a boundary they could not reach through. The boundary was real and the
/// indirection was not: every call went renderer to canvas to delegate to Game1, and the
/// construction site needed a lambda to bridge one signature. Owning the resources outright
/// deletes all of that, and it makes Game1's own drawing go through the same surface the
/// screen renderers already use — which is the part that was actually worth having.
///
/// **Renderers still may not sample devices or open the batch themselves.** They receive this
/// canvas and a snapshot, then paint. That rule is what keeps a change to a panel from
/// touching the simulation loop.
///
/// Device resources arrive through <see cref="Attach"/> rather than the constructor, because
/// the canvas is built before <c>LoadContent</c> runs and the batch does not exist yet.
/// </summary>
public sealed class UiCanvas
{
    /// <summary>Implicit titles (22+) still use the display face. Prefer <see cref="Heading"/>.</summary>
    private const float HeadingThreshold = 22f;

    private readonly Dictionary<int, SpriteFontBase> _bodyFonts = new();
    private readonly Dictionary<int, SpriteFontBase> _headingFonts = new();

    private SpriteBatch _batch = null!;
    private Texture2D _white = null!;
    private FontSystem? _body;
    private FontSystem? _heading;
    private SpriteType? _type;
    private Matrix _transform = Matrix.Identity;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;

    /// <summary>
    /// Device pixels per logical pixel.
    ///
    /// Read outside this class by anything that has to convert between the two — the mouse,
    /// and the projection of a world point onto the panel.
    /// </summary>
    public float Scale { get; private set; }

    public int LogicalWidth => _logicalWidth;
    public int LogicalHeight => _logicalHeight;

    /// <summary>
    /// The batch itself, for the handful of draws the primitives do not cover.
    ///
    /// A deliberate and narrow leak. Source rectangles, rotation and the cover composition all
    /// need <c>SpriteBatch.Draw</c> overloads that would otherwise have to be mirrored here one
    /// at a time, and mirroring an API is a worse boundary than exposing it. Everything with a
    /// primitive above should use the primitive.
    /// </summary>
    public SpriteBatch Batch => _batch;

    /// <summary>
    /// Logical canvas size is the game's, not the engine's. Ratna Bay passes 1280×720;
    /// a different game passes whatever it letterboxes to.
    /// </summary>
    public UiCanvas(int logicalWidth = 1280, int logicalHeight = 720)
    {
        _logicalWidth = Math.Max(1, logicalWidth);
        _logicalHeight = Math.Max(1, logicalHeight);
    }

    /// <summary>Hand over the device resources, once, after LoadContent has made them.</summary>
    public void Attach(SpriteBatch batch, Texture2D white, FontSystem body, FontSystem heading)
    {
        _batch = batch;
        _white = white;
        _body = body;
        _heading = heading;
        _type = null;
    }

    /// <summary>Sprite glyphs instead of a TrueType face. Point-sampled, integer scale.</summary>
    public void Attach(SpriteBatch batch, Texture2D white, SpriteType type)
    {
        _batch = batch;
        _white = white;
        _type = type;
        _body = null;
        _heading = null;
    }

    /// <summary>
    /// Fit the logical panel to the window, letterboxing where the aspect does not match.
    ///
    /// A changed scale invalidates every cached atlas, because they are rasterized in device
    /// pixels rather than logical ones.
    /// </summary>
    public void Resize(Viewport viewport, float preference)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        var scale = MathF.Min(
            viewport.Width / (float)_logicalWidth,
            viewport.Height / (float)_logicalHeight) * preference;

        var offsetX = (viewport.Width - _logicalWidth * scale) * 0.5f;
        var offsetY = (viewport.Height - _logicalHeight * scale) * 0.5f;
        _transform = Matrix.CreateScale(scale) * Matrix.CreateTranslation(offsetX, offsetY, 0f);

        if (MathF.Abs(scale - Scale) < 0.001f) return;

        Scale = scale;
        _bodyFonts.Clear();
        _headingFonts.Clear();
    }

    /// <summary>
    /// Draw at one to one instead of fitting the logical panel.
    ///
    /// For the store cover, which is its own shape rather than 16:9 and is composed straight
    /// into an offscreen target. The font picker rasterises against this number, so telling it
    /// the truth is what keeps the type on the cover sharp.
    /// </summary>
    public void OverrideScale(float scale) => Scale = scale;

    /// <summary>
    /// Device mouse to logical canvas, accounting for letterboxing.
    ///
    /// Hit-tests and the pointer sprite both go through this so a click cannot land on a
    /// different row than the one drawn under it.
    /// </summary>
    public Vector2 PointerFromDevice(int deviceX, int deviceY, Viewport viewport)
    {
        if (Scale <= 0f) return Vector2.Zero;

        var offsetX = (viewport.Width - _logicalWidth * Scale) * 0.5f;
        var offsetY = (viewport.Height - _logicalHeight * Scale) * 0.5f;
        return new Vector2((deviceX - offsetX) / Scale, (deviceY - offsetY) / Scale);
    }

    public void Begin() => _batch.Begin(
        SpriteSortMode.Deferred, BlendState.AlphaBlend,
        _type is null ? SamplerState.LinearClamp : SamplerState.PointClamp,
        DepthStencilState.None, RasterizerState.CullNone, null, _transform);

    public void End() => _batch.End();

    // ------------------------------------------------------------------ panels

    public void Panel(Rectangle bounds, Color fill, Color border)
    {
        Fill(bounds, fill);
        Border(bounds, border);
    }

    /// <summary>
    /// One row of a list. Colours are arguments so this type does not know Ratna Bay's palette.
    /// Callers pass <c>UiTheme.Row(selected)</c>.
    /// </summary>
    public void Row(Rectangle bounds, Color fill, Color border) =>
        Panel(bounds, fill, border);

    /// <summary>
    /// Dim everything already drawn, for a modal to sit on.
    ///
    /// Colours are arguments so this type does not know Ratna Bay's palette. Callers pass
    /// <c>UiTheme.Scrim</c> / <c>UiTheme.NoBorder</c>.
    /// </summary>
    public void Scrim(Color fill, Color border) =>
        Panel(new Rectangle(0, 0, _logicalWidth, _logicalHeight), fill, border);

    public void Fill(Rectangle bounds, Color color) => _batch.Draw(_white, bounds, color);

    public void Border(Rectangle bounds, Color color)
    {
        Fill(new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
        Fill(new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
        Fill(new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
        Fill(new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
    }

    /// <summary>Outer gold and inner hairline, the usual window edge.</summary>
    public void DoubleBorder(Rectangle bounds, Color outer, Color inner)
    {
        Border(bounds, outer);
        if (bounds.Width < 6 || bounds.Height < 6) return;
        Border(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), inner);
    }

    public void Rule(int x, int y, int width, Color color) =>
        Fill(new Rectangle(x, y, Math.Max(1, width), 1), color);

    public void Sprite(Texture2D texture, Rectangle destination, Color color) =>
        _batch.Draw(texture, destination, color);

    // ------------------------------------------------------------------ text

    public void Text(string value, Vector2 position, float scale, Color color) =>
        Paint(value, position, scale, color, heading: false);

    /// <summary>Display face at any size — menu titles, speaker names, the compass location.</summary>
    public void Heading(string value, Vector2 position, float scale, Color color) =>
        Paint(value, position, scale, color, heading: true);

    public void TextFit(string value, Vector2 position, float maxWidth, float scale, Color color)
    {
        PaintFit(value, position, maxWidth, scale, color, heading: false, centre: false);
    }

    public void TextCentred(string value, float centreX, float y, float scale, Color color)
    {
        var width = MeasureText(value, scale);
        Paint(value, new Vector2(centreX - width * 0.5f, y), scale, color, heading: false);
    }

    public void HeadingCentred(string value, float centreX, float y, float scale, Color color)
    {
        var width = WidthOf(value, scale, heading: true);
        Paint(value, new Vector2(centreX - width * 0.5f, y), scale, color, heading: true);
    }

    public void TextRight(string value, float right, float y, float scale, Color color)
    {
        var width = MeasureText(value, scale);
        Paint(value, new Vector2(right - width, y), scale, color, heading: false);
    }

    public float TextWrapped(string value, Vector2 position, float maxWidth, float scale,
        Color color, int maxLines = 6)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0f;

        var lineHeight = _type is null
            ? scale * 1.38f
            : SpriteType.LineHeight(SpriteType.PixelScale(scale, false));
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var line = string.Empty;
        var lines = 0;
        var y = position.Y;

        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (MeasureText(candidate, scale) <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
            {
                Paint(line, new Vector2(position.X, y), scale, color, heading: false);
                y += lineHeight;
                if (++lines >= maxLines) return y - position.Y;
            }

            line = word;
        }

        if (line.Length > 0)
        {
            Paint(line, new Vector2(position.X, y), scale, color, heading: false);
            y += lineHeight;
        }

        return y - position.Y;
    }

    /// <summary>Centred, and shrunk to fit rather than running off its panel.</summary>
    public void TextFitCentred(string value, float centreX, float y, float maxWidth, float scale,
        Color color)
    {
        PaintFit(value, new Vector2(centreX, y), maxWidth, scale, color, heading: false, centre: true);
    }

    /// <summary>How wide a string will be, for anything that has to lay out around it.</summary>
    public float MeasureText(string value, float scale) => WidthOf(value, scale, heading: false);

    private void Paint(string value, Vector2 position, float scale, Color color, bool heading)
    {
        if (_type is { } type)
        {
            type.Draw(_batch, value, position, SpriteType.PixelScale(scale, heading), color);
            return;
        }

        var (font, drawScale) = SelectFont(scale, heading);
        DrawString(font, value, position, drawScale, color);
    }

    private void PaintFit(string value, Vector2 position, float maxWidth, float scale, Color color,
        bool heading, bool centre)
    {
        if (_type is { } type)
        {
            var pixel = SpriteType.PixelScale(scale, heading);
            while (pixel > 1 && type.Measure(value, pixel) > maxWidth)
                pixel--;
            var width = type.Measure(value, pixel);
            var at = centre
                ? new Vector2(position.X - width * 0.5f, position.Y)
                : position;
            type.Draw(_batch, value, at, pixel, color);
            return;
        }

        var (font, drawScale) = SelectFont(scale, heading);
        var measured = font.MeasureString(value).X * drawScale;
        if (measured > maxWidth && measured > 0f)
            drawScale *= maxWidth / measured;
        var x = centre
            ? position.X - font.MeasureString(value).X * drawScale * 0.5f
            : position.X;
        DrawString(font, value, new Vector2(x, position.Y), drawScale, color);
    }

    private float WidthOf(string value, float scale, bool heading)
    {
        if (_type is { } type)
            return type.Measure(value, SpriteType.PixelScale(scale, heading));

        var (font, drawScale) = SelectFont(scale, heading);
        return font.MeasureString(value).X * drawScale;
    }

    /// <summary>
    /// Pick a font rasterized at the size it will actually occupy on the display.
    ///
    /// An earlier version kept three fixed atlases (18/24/32 px) and scaled them to fit, so a
    /// 12 px label was an 18 px atlas squeezed to 0.67 and then stretched again by the canvas
    /// transform. Two resamples is why the HUD was soft and thin. Rasterizing at the device
    /// size and drawing at 1/scale lands every glyph 1:1 on the panel.
    /// </summary>
    private (SpriteFontBase Font, float Scale) SelectFont(float requestedSize, bool heading)
    {
        heading = heading || requestedSize >= HeadingThreshold;
        var cache = heading ? _headingFonts : _bodyFonts;

        // Clamped so an extreme display cannot ask for a 4 px or a 900 px atlas.
        var devicePixels = Math.Clamp((int)MathF.Round(requestedSize * Scale), 8, 384);

        if (!cache.TryGetValue(devicePixels, out var font))
        {
            var system = heading ? _heading : _body;
            font = system!.GetFont(devicePixels);
            cache[devicePixels] = font;
        }

        return (font, requestedSize / devicePixels);
    }

    /// <summary>Text with a one-pixel drop shadow, so it survives any background under it.</summary>
    private void DrawString(SpriteFontBase font, string value, Vector2 position, float scale,
        Color color)
    {
        var fontScale = new Vector2(scale);

        if (color.A > 20)
        {
            _batch.DrawString(font, value, position + new Vector2(1f, 1f),
                new Color(0, 0, 0, 150), 0f, Vector2.Zero, fontScale, 0f, 0f, 0f,
                TextStyle.None, FontSystemEffect.None, 0);
        }

        _batch.DrawString(font, value, position, color, 0f, Vector2.Zero, fontScale,
            0f, 0f, 0f, TextStyle.None, FontSystemEffect.None, 0);
    }
}

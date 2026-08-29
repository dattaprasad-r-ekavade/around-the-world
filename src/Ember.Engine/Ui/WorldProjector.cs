using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Ember.Ui;

/// <summary>
/// Turns a point in the world into a point on the logical canvas.
///
/// The seam AGENTS.md names for anything anchored to a thing in the world but drawn as flat
/// interface: nameplates, floating damage, carved signs. Those need one answer from the
/// renderer — where on screen is this? — and handing them the whole of <c>Game1</c> to get it
/// is what kept them in the game loop.
///
/// Rebuilt each frame from the live matrices, which is cheaper than it sounds and removes any
/// question about whether a marker was projected with this frame's camera or the last one's.
/// </summary>
public sealed class WorldProjector
{
    private readonly Viewport _viewport;
    private readonly Matrix _view;
    private readonly Matrix _projection;
    private readonly float _uiScale;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;

    public WorldProjector(Viewport viewport, Matrix view, Matrix projection, float uiScale,
        int logicalWidth, int logicalHeight)
    {
        _viewport = viewport;
        _view = view;
        _projection = projection;
        _uiScale = uiScale;
        _logicalWidth = logicalWidth;
        _logicalHeight = logicalHeight;
    }

    /// <summary>
    /// Where this world point lands on the logical canvas, if it is in front of the camera.
    ///
    /// The depth check is not optional: without it something behind the camera projects to a
    /// mirrored position in front of it, so an enemy the player has walked past grows a
    /// nameplate in the middle of the screen.
    /// </summary>
    public bool TryProject(Vector3 world, out Vector2 screen)
    {
        screen = Vector2.Zero;
        if (_uiScale <= 0f) return false;

        var projected = _viewport.Project(world, _projection, _view, Matrix.Identity);
        if (projected.Z is < 0f or > 1f) return false;

        var offsetX = (_viewport.Width - _logicalWidth * _uiScale) * 0.5f;
        var offsetY = (_viewport.Height - _logicalHeight * _uiScale) * 0.5f;

        screen = new Vector2((projected.X - offsetX) / _uiScale, (projected.Y - offsetY) / _uiScale);
        return true;
    }
}

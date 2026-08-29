using System;

namespace Ember.Ui;

/// <summary>
/// Draw order for the 2D pass: combat HUD under panels, then the overlay stack.
///
/// Game1 still builds snapshots and still draws the 3D world, the weapon pose, and
/// --show/--swing/--cast. This type must not take a <c>Game1</c> reference.
/// </summary>
public sealed class FramePresenter
{
    public static void Present(bool askingConsent, bool worldScene,
        Action drawConsent, Action drawMenu, Action drawWorld)
    {
        if (askingConsent)
        {
            drawConsent();
            return;
        }

        if (worldScene) drawWorld();
        else drawMenu();
    }

    public static void DrawWorldInterface(
        bool hidesHud,
        bool hideInterface,
        Action combatHud,
        Action toasts,
        Action panels,
        Action watches,
        Action console)
    {
        if (!hidesHud) combatHud();
        if (!hideInterface) toasts();
        panels();
        if (!hideInterface) watches();
        console();
    }
}

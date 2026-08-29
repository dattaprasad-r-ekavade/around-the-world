using Microsoft.Xna.Framework;

namespace Ember.Ui;

/// <summary>
/// The "press E" line over a door, a person, a pickup or a yard fixture.
///
/// Layout and colour only. Game1 builds a <see cref="PromptState"/> from the live world;
/// this class must not query a door, an actor or a pocket itself.
/// </summary>
public sealed class PromptRenderer
{
    private readonly UiCanvas _ui;

    public PromptRenderer(UiCanvas ui) => _ui = ui;

    public void Draw(PromptState state)
    {
        foreach (var chip in state.Chips)
        {
            var (border, ink) = Colours(chip.Role);
            _ui.Panel(chip.Bounds, UiTheme.PanelSheer, border);

            if (chip.Fit)
            {
                _ui.TextFit(chip.Line, UiLayout.PromptText(chip.Bounds),
                    UiLayout.PromptTextWidth(chip.Bounds), SizeOf(chip), ink);
            }
            else
            {
                _ui.Text(chip.Line, UiLayout.PromptText(chip.Bounds), SizeOf(chip), ink);
            }
        }
    }

    private static int SizeOf(PromptChip chip) =>
        chip.Bounds.Equals(UiLayout.SinglePrompt) ? 15 : 14;

    private static (Color Border, Color Ink) Colours(PromptRole role) => role switch
    {
        PromptRole.Talk => (UiTheme.Accent, Color.White),
        PromptRole.Pocket => (UiTheme.Pocket, Color.White),
        PromptRole.Barred => (UiTheme.Barred, UiTheme.BarredText),
        _ => (UiTheme.Bronze, Color.White)
    };
}

using System;

namespace Campaign;

/// <summary>Keyword talk: known topics become options. Rumors teach new words.</summary>
public static class TalkBook
{
    public static readonly (string Key, string Prompt)[] Catalogue =
    [
        ("road", "What news of the road?"),
        ("work", "Any work?"),
        ("guilds", "Tell me about the guilds."),
        ("temple", "Tell me about the temple."),
        ("knights", "Who are the knights?"),
        ("numidium", "Tell me about Numidium."),
        ("totem", "Where is the Totem?"),
        ("relic", "Where is the relic?"),
        ("underking", "Tell me about the Underking.")
    ];

    public static string Answer(string key, Hero hero, string town, string dungeon, int talkId)
    {
        return key.ToLowerInvariant() switch
        {
            "road" => talkId switch
            {
                1 => $"The Hound keeps a bed. {town} sleeps behind a shut gate after dusk.",
                2 => "Keep to the lamp. The gate is shut for a reason.",
                _ => $"The road to {town} is long. Hunger walks with you."
            },
            "work" => hero.MainBeat < 2
                ? "Ask at the guild halls, or the inn if a letter found you."
                : "The halls still pay for dummy, letter, and lock.",
            "guilds" => "Fighters for steel, Mages for working, Thieves for the night. Each hates a neighbour.",
            "temple" => "Kynareth keeps the sky. The priests sell a blessing and remember the knights.",
            "knights" => "The order drinks with the Fighters and will not take a thief's hand.",
            "numidium" => "A brass god. The Totem that drove it was seen in a hole in the earth.",
            "totem" or "relic" => hero.Relic
                ? "You already carry it. The halls will fight over who keeps it."
                : $"A vault in {dungeon}. Bring it to a hall you trust.",
            "underking" => "An old name on a new letter. He wants the Totem kept from the brass.",
            _ => "I have not heard that word."
        };
    }
}

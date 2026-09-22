using System;

namespace Campaign;

/// <summary>Main plot plus guild notes. J opens the log.</summary>
public static class QuestBook
{
    public static void EnsureMain(Hero hero, string dungeonName, int dungeonIndex)
    {
        if (hero.Log.Exists(q => q.Title == "The Totem")) return;
        hero.RelicDungeon = dungeonIndex;
        hero.Topics.Add("underking");
        hero.Topics.Add("numidium");
        hero.Log.Add(new QuestNote("The Totem",
            $"A letter: the Totem of Tiber Septim lies in {dungeonName}. Ask at an inn.",
            false));
    }

    public static string AdvanceInn(Hero hero, string dungeonName)
    {
        if (hero.MainBeat >= 1) return TalkBook.Answer("relic", hero, "", dungeonName, 1);
        hero.MainBeat = 1;
        hero.Topics.Add("totem");
        hero.Topics.Add("relic");
        Replace(hero, "The Totem",
            $"The innkeep names {dungeonName}. Take the Totem from its vault.",
            false);
        return $"I have heard it. {dungeonName}. A locked vault. The halls will pay.";
    }

    public static string TakeRelic(Hero hero)
    {
        if (hero.Relic) return "You already hold the Totem.";
        hero.Relic = true;
        hero.MainBeat = 2;
        Replace(hero, "The Totem",
            "The Totem is in your pack. Deliver it to Fighters, Mages, or the Temple.",
            false);
        return "Cold brass. The halls will want this.";
    }

    public static string Deliver(Hero hero, FactionId to)
    {
        if (!hero.Relic) return "You have no Totem to give.";
        hero.Relic = false;
        hero.MainBeat = 3;
        hero.Join(to);
        var line = to switch
        {
            FactionId.Fighters => "The Guild locks it in iron. The knights drink to you.",
            FactionId.Mages => "The circle wards it. Mysticism comes easier.",
            _ => "The temple hides it under Kynareth. The thieves will not forget."
        };
        if (to == FactionId.Mages)
            hero.Skill[(int)SkillId.Mysticism] = MathF.Min(100f, hero.Skill[(int)SkillId.Mysticism] + 8f);
        if (to == FactionId.Fighters)
            hero.Skill[(int)SkillId.Blade] = MathF.Min(100f, hero.Skill[(int)SkillId.Blade] + 8f);
        if (to == FactionId.Temple)
            hero.Skill[(int)SkillId.Restoration] = MathF.Min(100f, hero.Skill[(int)SkillId.Restoration] + 8f);
        Replace(hero, "The Totem", line, true);
        return line;
    }

    public static void Upsert(Hero hero, string title, string body, bool done)
    {
        Replace(hero, title, body, done);
    }

    private static void Replace(Hero hero, string title, string body, bool done)
    {
        hero.Log.RemoveAll(q => q.Title == title);
        hero.Log.Add(new QuestNote(title, body, done));
    }
}

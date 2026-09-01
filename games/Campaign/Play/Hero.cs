using System;

namespace Campaign;

public enum RaceId { Breton, Redguard, Nord, Bosmer }

public enum ClassId { Warrior, Mage, Thief, Spellsword }

public enum AttrId { Str, Int, Wil, Agi, End, Per, Spd, Lck }

public enum SkillId
{
    Blade, Archery, Dodging, Stealth,
    Destruction, Restoration, Mysticism,
    Mercantile, Lockpicking, Climbing, Running, Speech
}

public enum FactionId
{
    Fighters, Mages, Thieves, Temple, Knights, Merchants
}

/// <summary>Daggerfall-shaped hero: attributes, skills, magicka, carry, known topics.</summary>
public sealed class Hero
{
    public const int AttrCount = 8;
    public const int SkillCount = 12;
    public const int FactionCount = 6;

    public string Name = "Wanderer";
    public RaceId Race = RaceId.Breton;
    public ClassId Class = ClassId.Warrior;
    public readonly int[] Attr = new int[AttrCount];
    public readonly float[] Skill = new float[SkillCount];
    public float Magicka = 60f;
    public float MagickaMax = 60f;
    public int Weapon;
    public int Armor;
    public int Bow;
    public int Arrows;
    public readonly System.Collections.Generic.HashSet<int> OwnedGear = new();
    public int SpellSel;
    public readonly System.Collections.Generic.List<Spell> Spells = new();
    public readonly int[] Rep = new int[FactionCount];
    public readonly bool[] Member = new bool[FactionCount];
    public readonly System.Collections.Generic.HashSet<string> Topics = new(StringComparer.OrdinalIgnoreCase);
    public int MainBeat;
    public bool Relic;
    public int RelicDungeon = -1;
    public readonly System.Collections.Generic.List<QuestNote> Log = new();
    public readonly System.Collections.Generic.HashSet<int> AutoCells = new();
    public int AutoDungeon = -1;
    public Spell Draft = Spell.Heal;

    public int Str => Attr[(int)AttrId.Str];
    public int Int => Attr[(int)AttrId.Int];
    public int Wil => Attr[(int)AttrId.Wil];
    public int Agi => Attr[(int)AttrId.Agi];
    public int End => Attr[(int)AttrId.End];
    public int Per => Attr[(int)AttrId.Per];
    public int Spd => Attr[(int)AttrId.Spd];
    public int Lck => Attr[(int)AttrId.Lck];

    public float SkillOf(SkillId id) => Skill[(int)id];

    public static readonly string[] RaceNames = ["Breton", "Redguard", "Nord", "Bosmer"];
    public static readonly string[] ClassNames = ["Warrior", "Mage", "Thief", "Spellsword"];
    public static readonly string[] AttrNames = ["STR", "INT", "WIL", "AGI", "END", "PER", "SPD", "LCK"];
    public static readonly string[] SkillNames =
    [
        "Blade", "Archery", "Dodging", "Stealth",
        "Destruction", "Restoration", "Mysticism",
        "Mercantile", "Lockpicking", "Climbing", "Running", "Speech"
    ];
    public static readonly string[] FactionNames =
        ["Fighters Guild", "Mages Guild", "Thieves Guild", "Temple", "Knights", "Merchants"];

    public void Roll(RaceId race, ClassId cls, string name)
    {
        Race = race;
        Class = cls;
        Name = string.IsNullOrWhiteSpace(name) ? "Wanderer" : name.Trim();
        Array.Fill(Attr, 45);
        Array.Fill(Skill, 15f);
        switch (race)
        {
            case RaceId.Breton:
                Attr[(int)AttrId.Int] = 55;
                Attr[(int)AttrId.Wil] = 55;
                Skill[(int)SkillId.Restoration] = 30f;
                Skill[(int)SkillId.Mysticism] = 25f;
                break;
            case RaceId.Redguard:
                Attr[(int)AttrId.Str] = 55;
                Attr[(int)AttrId.End] = 52;
                Skill[(int)SkillId.Blade] = 35f;
                break;
            case RaceId.Nord:
                Attr[(int)AttrId.Str] = 52;
                Attr[(int)AttrId.End] = 58;
                Skill[(int)SkillId.Blade] = 28f;
                break;
            case RaceId.Bosmer:
                Attr[(int)AttrId.Agi] = 58;
                Attr[(int)AttrId.Spd] = 52;
                Skill[(int)SkillId.Archery] = 35f;
                Skill[(int)SkillId.Stealth] = 28f;
                break;
        }

        switch (cls)
        {
            case ClassId.Warrior:
                Skill[(int)SkillId.Blade] = MathF.Max(Skill[(int)SkillId.Blade], 40f);
                Skill[(int)SkillId.Dodging] = 30f;
                Skill[(int)SkillId.Running] = 25f;
                Weapon = Gear.IronSword;
                Armor = Gear.Leather;
                break;
            case ClassId.Mage:
                Skill[(int)SkillId.Destruction] = 38f;
                Skill[(int)SkillId.Restoration] = MathF.Max(Skill[(int)SkillId.Restoration], 36f);
                Skill[(int)SkillId.Mysticism] = MathF.Max(Skill[(int)SkillId.Mysticism], 32f);
                Spells.Add(Spell.Heal);
                Spells.Add(Spell.Spark);
                Weapon = Gear.IronSword;
                break;
            case ClassId.Thief:
                Skill[(int)SkillId.Stealth] = MathF.Max(Skill[(int)SkillId.Stealth], 40f);
                Skill[(int)SkillId.Lockpicking] = 36f;
                Skill[(int)SkillId.Speech] = 28f;
                Skill[(int)SkillId.Archery] = MathF.Max(Skill[(int)SkillId.Archery], 28f);
                Bow = Gear.ShortBow;
                Arrows = 24;
                Armor = Gear.Leather;
                break;
            case ClassId.Spellsword:
                Skill[(int)SkillId.Blade] = MathF.Max(Skill[(int)SkillId.Blade], 32f);
                Skill[(int)SkillId.Destruction] = 30f;
                Skill[(int)SkillId.Restoration] = 28f;
                Spells.Add(Spell.Heal);
                Weapon = Gear.IronSword;
                Armor = Gear.Leather;
                break;
        }

        RecalcMagicka();
        Magicka = MagickaMax;
        OwnedGear.Clear();
        if (Weapon != 0) OwnedGear.Add(Weapon);
        if (Armor != 0) OwnedGear.Add(Armor);
        if (Bow != 0) OwnedGear.Add(Bow);
        Topics.Clear();
        Topics.Add("road");
        Topics.Add("work");
        Topics.Add("guilds");
        Log.Clear();
        MainBeat = 0;
        Relic = false;
        RelicDungeon = -1;
        AutoCells.Clear();
        AutoDungeon = -1;
        Array.Fill(Rep, 0);
        Array.Fill(Member, false);
        SpellSel = 0;
        Draft = Spell.Heal;
    }

    public void RecalcMagicka()
    {
        MagickaMax = 20f + Int * 1.1f + Wil * 0.6f;
        Magicka = Math.Clamp(Magicka, 0f, MagickaMax);
    }

    public float HealthMax => 60f + End * 1.15f;

    public float GearKg =>
        Gear.WeightOf(Weapon) + Gear.WeightOf(Armor) + Gear.WeightOf(Bow) + Arrows * 0.04f;

    public float CarryMaxKg => 35f + Str * 1.35f;

    public float GoldKg(int gold) => gold * 0.012f;

    public float BurdenKg(int gold) => GoldKg(gold) + GearKg;

    public float Encumbrance(int gold)
    {
        var max = CarryMaxKg;
        if (max < 1f) return 1f;
        return BurdenKg(gold) / max;
    }

    public float BurdenSpeed(int gold)
    {
        var e = Encumbrance(gold);
        if (e < 0.55f) return 1f;
        if (e < 0.85f) return 0.82f;
        if (e < 1.05f) return 0.62f;
        return 0.42f;
    }

    public void UseSkill(SkillId id, float amount = 0.35f)
    {
        var i = (int)id;
        Skill[i] = MathF.Min(100f, Skill[i] + amount * (0.35f + Lck / 200f));
    }

    public bool Chance(SkillId id, float extra = 0f)
    {
        var roll = Random.Shared.NextSingle() * 100f;
        return roll < SkillOf(id) + extra + Lck * 0.15f;
    }

    public int PriceMul(int basePrice)
    {
        var merc = SkillOf(SkillId.Mercantile);
        var talk = SkillOf(SkillId.Speech);
        var cut = 1f - (merc + talk) / 500f - Rep[(int)FactionId.Merchants] / 800f;
        if (Member[(int)FactionId.Thieves]) cut -= 0.08f;
        return Math.Max(1, (int)(basePrice * Math.Clamp(cut, 0.55f, 1.15f)));
    }

    public void Join(FactionId id)
    {
        Member[(int)id] = true;
        Factions.ApplyJoin(Rep, id);
    }

    public bool CanCast(Spell spell) => Magicka + 0.01f >= spell.Cost;

    public void Equip(int id)
    {
        var item = Gear.Of(id);
        switch (item.Slot)
        {
            case "weapon":
                Weapon = id;
                break;
            case "bow":
                Bow = id;
                break;
            case "armor":
                Armor = id;
                break;
        }
    }

    public bool SpendMagicka(float cost)
    {
        if (Magicka < cost) return false;
        Magicka -= cost;
        return true;
    }

    public void TickMagicka(float seconds)
    {
        if (Magicka >= MagickaMax) return;
        Magicka = MathF.Min(MagickaMax, Magicka + seconds * (1.6f + Wil * 0.04f));
    }
}

public readonly record struct QuestNote(string Title, string Body, bool Done);

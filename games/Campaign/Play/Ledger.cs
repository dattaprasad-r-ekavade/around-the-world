using Microsoft.Xna.Framework;
using System;

namespace Campaign;

public enum Ailment
{
    None,
    SwampRot,
    WoundFever,
    Chill
}

public enum GuildKind
{
    Fighters,
    Mages,
    Thieves
}

public readonly record struct ShopGood(string Id, string Name, int Price, string Kind);

/// <summary>Coin, pack, guild papers, and whatever is in the blood.</summary>
public sealed class Ledger
{
    public const float NeedMax = 100f;

    public int Gold = 90;
    public int BankGold;
    public int HouseGold;
    public int WagonGold;
    public int HouseTown = -1;
    public bool HasWagon;
    public bool HasShip;
    public bool HasCloak;
    public bool Wanted;
    public float Health = 100f;
    public const float HealthMax = 100f;
    public int Rations = 3;
    public int Meals;
    public int Stamina;
    public int Cures;
    public int Lockpicks;
    public Ailment Ailment;
    public float Hunger = 85f;
    public float Cold = 10f;
    public float Wet;
    public readonly bool[] Guild = new bool[3];
    public readonly System.Collections.Generic.HashSet<int> Looted = new();
    public readonly System.Collections.Generic.HashSet<int> Keys = new();
    public readonly System.Collections.Generic.HashSet<int> Doors = new();

    public int FightJob = -1;
    public bool FightReady;
    public int MageJob = -1;
    public bool MageLetter;
    public int ThiefJob = -1;
    public bool ThiefReady;

    public bool HasMark;
    public int MarkTown = -1;

    public static readonly ShopGood[] Goods =
    [
        new("rations", "Rations", 6, "food"),
        new("stew", "Hot stew", 8, "meal"),
        new("stamina", "Stamina draught", 12, "stamina"),
        new("cure", "Potion of cure disease", 25, "cure"),
        new("lockpick", "Lockpicks", 7, "lock"),
        new("cloak", "Wool cloak", 35, "cloak"),
        new("iron", "Iron shortsword", 28, "weapon"),
        new("steel", "Steel longsword", 64, "weapon"),
        new("bow", "Short bow", 40, "bow"),
        new("longbow", "Long bow", 85, "bow"),
        new("leather", "Leather jerkin", 32, "armor"),
        new("chain", "Chain hauberk", 90, "armor"),
        new("arrows", "Arrows (12)", 8, "ammo"),
        new("wagon", "Wagon and team", 120, "wagon"),
        new("ship", "Longboat", 180, "ship")
    ];

    public bool In(GuildKind kind) => Guild[(int)kind];

    public int PriceOf(ShopGood good, Hero hero) => hero.PriceMul(good.Price);

    public int Owned(ShopGood good, Hero hero) => good.Kind switch
    {
        "food" => Rations,
        "meal" => Meals,
        "stamina" => Stamina,
        "cure" => Cures,
        "lock" => Lockpicks,
        "cloak" => HasCloak ? 1 : 0,
        "wagon" => HasWagon ? 1 : 0,
        "ship" => HasShip ? 1 : 0,
        "ammo" => hero.Arrows,
        "weapon" or "bow" or "armor" => hero.OwnedGear.Contains(Gear.FromShop(good.Id)) ? 1 : 0,
        _ => 0
    };

    public string AilmentName => Ailment switch
    {
        Ailment.SwampRot => "swamp rot",
        Ailment.WoundFever => "wound fever",
        Ailment.Chill => "the chill",
        _ => ""
    };

    public string HungerName => Hunger switch
    {
        < 12f => "starving",
        < 32f => "hungry",
        < 55f => "peckish",
        _ => "fed"
    };

    public string ColdName => Cold switch
    {
        > 82f => "freezing",
        > 58f => "cold",
        > 32f => "chilly",
        _ => "warm"
    };

    public float SpeedMul
    {
        get
        {
            var sick = Ailment switch
            {
                Ailment.SwampRot => 0.82f,
                Ailment.WoundFever => 0.78f,
                Ailment.Chill => 0.9f,
                _ => 1f
            };
            var food = Hunger < 12f ? 0.62f : Hunger < 32f ? 0.8f : Hunger < 55f ? 0.92f : 1f;
            var chill = Cold > 82f ? 0.6f : Cold > 58f ? 0.78f : Cold > 32f ? 0.9f : 1f;
            return sick * food * chill;
        }
    }

    public float FatigueMul
    {
        get
        {
            var sick = Ailment switch
            {
                Ailment.SwampRot => 1.7f,
                Ailment.WoundFever => 1.55f,
                Ailment.Chill => 1.35f,
                _ => 1f
            };
            if (Hunger < 12f) sick *= 1.45f;
            else if (Hunger < 32f) sick *= 1.2f;
            if (Cold > 82f) sick *= 1.4f;
            else if (Cold > 58f) sick *= 1.18f;
            return sick;
        }
    }

    public bool CanRegen => Hunger > 28f && Cold < 62f;

    public bool Infect(Ailment next)
    {
        if (next == Ailment.None || Ailment != Ailment.None) return false;
        Ailment = next;
        return true;
    }

    public bool Cure()
    {
        if (Ailment == Ailment.None) return false;
        Ailment = Ailment.None;
        return true;
    }

    public string? Eat()
    {
        if (Rations > 0)
        {
            Rations--;
            Hunger = MathF.Min(NeedMax, Hunger + 34f);
            return "You eat.";
        }

        if (Meals > 0)
        {
            Meals--;
            Hunger = MathF.Min(NeedMax, Hunger + 46f);
            Cold = MathF.Max(0f, Cold - 24f);
            Wet = MathF.Max(0f, Wet - 10f);
            return "The stew warms you.";
        }

        return null;
    }

    public void ClampNeeds()
    {
        Hunger = MathHelper.Clamp(Hunger, 0f, NeedMax);
        Cold = MathHelper.Clamp(Cold, 0f, NeedMax);
        Wet = MathHelper.Clamp(Wet, 0f, NeedMax);
    }

    public void RoadHours(float hours, float coldToward)
    {
        if (hours <= 0f) return;
        Hunger = MathF.Max(0f, Hunger - hours * WorldScale.RoadHungerPerHour);
        Cold += (coldToward - Cold) * MathF.Min(1f, hours * 0.18f);
        ClampNeeds();
    }
}

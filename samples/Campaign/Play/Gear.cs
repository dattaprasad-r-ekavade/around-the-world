using System;

namespace Campaign;

/// <summary>Wielded iron, hide, and bows. Ids are indices into <see cref="All"/>.</summary>
public static class Gear
{
    public const int None = 0;
    public const int IronSword = 1;
    public const int SteelSword = 2;
    public const int ShortBow = 3;
    public const int LongBow = 4;
    public const int Leather = 5;
    public const int Chain = 6;

    public readonly record struct Item(
        string Id, string Name, string Slot, int Price, float Damage, float Armor, float WeightKg,
        string Detail);

    public static readonly Item[] All =
    [
        new("", "Fists", "weapon", 0, 4f, 0f, 0f, "Bare hands."),
        new("iron", "Iron shortsword", "weapon", 28, 11f, 0f, 1.6f, "A common blade. Equip to swing it."),
        new("steel", "Steel longsword", "weapon", 64, 16f, 0f, 2.2f, "Holds an edge. Equip to swing it."),
        new("bow", "Short bow", "bow", 40, 10f, 0f, 1.1f, "Equip, then F to loose. Needs arrows."),
        new("longbow", "Long bow", "bow", 85, 15f, 0f, 1.5f, "A hunter's bow. Equip, then F."),
        new("leather", "Leather jerkin", "armor", 32, 0f, 5f, 3.2f, "Soft hide. Equip to wear."),
        new("chain", "Chain hauberk", "armor", 90, 0f, 11f, 8.4f, "Rings over linen. Equip to wear.")
    ];

    public static float WeightOf(int id)
    {
        if ((uint)id >= All.Length) return 0f;
        return All[id].WeightKg;
    }

    public static Item Of(int id) =>
        (uint)id < All.Length ? All[id] : All[0];

    public static int FromShop(string id) => id switch
    {
        "iron" => IronSword,
        "steel" => SteelSword,
        "bow" => ShortBow,
        "longbow" => LongBow,
        "leather" => Leather,
        "chain" => Chain,
        _ => None
    };
}

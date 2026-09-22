using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Campaign;

public sealed class SaveData
{
    public int Version { get; set; } = 2;
    public int Seed { get; set; }
    public int Day { get; set; } = 1;
    public float Hours { get; set; } = 10f;
    public float Fatigue { get; set; } = 88f;
    public float Health { get; set; } = 100f;
    public float WellRested { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public string Place { get; set; } = "wild";
    public int Town { get; set; }
    public int Dungeon { get; set; }
    public int Room { get; set; }

    public int Gold { get; set; }
    public int BankGold { get; set; }
    public int HouseGold { get; set; }
    public int WagonGold { get; set; }
    public int HouseTown { get; set; } = -1;
    public bool HasWagon { get; set; }
    public bool HasShip { get; set; }
    public bool HasCloak { get; set; }
    public bool Wanted { get; set; }
    public int Rations { get; set; }
    public int Meals { get; set; }
    public int Stamina { get; set; }
    public int Cures { get; set; }
    public int Lockpicks { get; set; }
    public int Ailment { get; set; }
    public float Hunger { get; set; }
    public float Cold { get; set; }
    public float Wet { get; set; }
    public bool[] Guild { get; set; } = new bool[3];
    public int[] Looted { get; set; } = [];
    public int[] Keys { get; set; } = [];
    public int[] Doors { get; set; } = [];
    public int FightJob { get; set; } = -1;
    public bool FightReady { get; set; }
    public int MageJob { get; set; } = -1;
    public bool MageLetter { get; set; }
    public int ThiefJob { get; set; } = -1;
    public bool ThiefReady { get; set; }
    public bool HasMark { get; set; }
    public int MarkTown { get; set; } = -1;

    public string HeroName { get; set; } = "Wanderer";
    public int Race { get; set; }
    public int Class { get; set; }
    public int[] Attr { get; set; } = [];
    public float[] Skill { get; set; } = [];
    public float Magicka { get; set; }
    public float MagickaMax { get; set; }
    public int Weapon { get; set; }
    public int Armor { get; set; }
    public int Bow { get; set; }
    public int Arrows { get; set; }
    public int[] OwnedGear { get; set; } = [];
    public int SpellSel { get; set; }
    public SpellSave[] Spells { get; set; } = [];
    public int[] Rep { get; set; } = [];
    public bool[] Member { get; set; } = [];
    public string[] Topics { get; set; } = [];
    public int MainBeat { get; set; }
    public bool Relic { get; set; }
    public int RelicDungeon { get; set; } = -1;
    public QuestSave[] Log { get; set; } = [];
    public int[] AutoCells { get; set; } = [];
    public int AutoDungeon { get; set; } = -1;
}

public sealed class SpellSave
{
    public string Name { get; set; } = "";
    public int Effect { get; set; }
    public int Magnitude { get; set; }
    public float Cost { get; set; }
}

public sealed class QuestSave
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Done { get; set; }
}

public static class SaveFile
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "campaign.save.json");

    public static void Write(SaveData data) =>
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Json));

    public static SaveData? Read()
    {
        if (!File.Exists(Path)) return null;
        return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(Path), Json);
    }
}

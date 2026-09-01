using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Campaign;

/// <summary>JSONL play log plus a last-run summary, for reading after a bot session.</summary>
public sealed class BotLog : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly StreamWriter _writer;
    private bool _closed;

    public BotLog(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) is { Length: > 0 } dir
            ? dir
            : ".");
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read),
            Encoding.UTF8)
        {
            AutoFlush = true
        };
        SummaryPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? ".", "bot-summary.json");
    }

    public string Path { get; }
    public string SummaryPath { get; }
    public float RealSeconds;
    public float Metres;
    public int Deaths;
    public int Ambushes;
    public int Rests;
    public int Eats;
    public int Buys;
    public int Towns;
    public int Dungeons;
    public int Uses;
    public int Swings;
    public int Ticks;
    public int GoldMin = int.MaxValue;
    public int GoldMax;
    public int GoldLast;

    public void StartRun(int worldSeed, int botSeed, string extra = "")
    {
        WriteRaw(new
        {
            t = 0f,
            kind = "run",
            worldSeed,
            botSeed,
            extra
        });
    }

    public void Tick(BotSight sight, string goal)
    {
        Ticks++;
        WriteRaw(new
        {
            t = Round(RealSeconds),
            kind = "tick",
            goal,
            place = sight.Place,
            hp = Round(sight.Health),
            hunger = Round(sight.Hunger),
            cold = Round(sight.Cold),
            fatigue = Round(sight.Fatigue),
            gold = sight.Gold,
            rations = sight.Rations,
            foes = sight.FoeCount,
            night = sight.Night,
            metres = Round(Metres)
        });
    }

    public void Event(string kind, string? detail, BotSight? sight)
    {
        WriteRaw(new
        {
            t = Round(RealSeconds),
            kind,
            detail,
            place = sight?.Place,
            hp = sight is null ? (float?)null : Round(sight.Health),
            gold = sight?.Gold,
            hunger = sight is null ? (float?)null : Round(sight.Hunger)
        });
    }

    public void Toast(string line) => Event("toast", line, null);

    public void NoteGold(int gold)
    {
        GoldLast = gold;
        if (gold < GoldMin) GoldMin = gold;
        if (gold > GoldMax) GoldMax = gold;
    }

    public string StatusLine() =>
        $"{RealSeconds:0}s  {Metres:0}m  deaths {Deaths}  ambush {Ambushes}  rest {Rests}  eat {Eats}  " +
        $"town {Towns}  delve {Dungeons}  gold {GoldMin}..{GoldMax}  log {Path}";

    public void WriteSummary(int worldSeed, int botSeed, string goal)
    {
        var min = GoldMin == int.MaxValue ? GoldLast : GoldMin;
        var body = JsonSerializer.Serialize(new
        {
            worldSeed,
            botSeed,
            seconds = Round(RealSeconds),
            metres = Round(Metres),
            deaths = Deaths,
            ambushes = Ambushes,
            rests = Rests,
            eats = Eats,
            buys = Buys,
            towns = Towns,
            dungeons = Dungeons,
            uses = Uses,
            swings = Swings,
            ticks = Ticks,
            goldMin = min,
            goldMax = GoldMax,
            gold = GoldLast,
            lastGoal = goal,
            log = Path
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(SummaryPath, body);
        Event("end", StatusLine(), null);
    }

    private void WriteRaw(object row)
    {
        if (_closed) return;
        _writer.WriteLine(JsonSerializer.Serialize(row, Json));
    }

    private static float Round(float value) =>
        MathF.Round(value, 2);

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _writer.Dispose();
    }
}

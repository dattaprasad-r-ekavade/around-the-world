using Ember;
using Ember.Audio;
using Ember.Input;
using Ember.Render;
using Ember.Scripting;
using Ember.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;

namespace Campaign;

public sealed class CampaignGame : EngineHost
{
    private readonly GroundedView _view = new();
    private readonly LocationRunner _runner = new();
    private readonly ConsoleRouter _console = new();
    private readonly ConsoleInput _consoleInput = new();
    private readonly List<string> _faults = new();
    private readonly List<string> _log = new();
    private readonly Queue<string> _script = new();
    private readonly string[] _launchArgs;

    private SceneRenderer _scene = null!;
    private PromptRenderer _prompts = null!;
    private CampaignSprites _sprites = null!;
    private LocationBeds _beds = null!;
    private WorldState _world = null!;
    private LookHint? _hint;
    private Vector3 _returnWild;
    private Vector3 _returnTown;
    private int _townIndex;
    private int _dungeonIndex;
    private int _seed;
    private float _stepDistance;
    private float _wait;
    private float _toastLife;
    private string _toast = "";
    private bool _centredMouse;
    private TravelMap _map = new();
    private HorizonSky _sky = null!;
    private WaterSurface _water = null!;
    private CompassHud _compass = null!;
    private readonly WorldClock _clock = new();
    private readonly Ledger _pack = new();
    private readonly ShopScreen _shop = new();
    private readonly BankScreen _bank = new();
    private readonly MageScreen _mage = new();
    private readonly InventoryScreen _inv = new();
    private readonly DialogueScreen _talk = new();
    private readonly PauseMenu _pause = new();
    private readonly CreateScreen _create = new();
    private readonly SheetScreen _sheet = new();
    private readonly SpellmakerScreen _spellUi = new();
    private readonly JournalScreen _journal = new();
    private readonly Hero _hero = new();
    private int _talkId;
    private bool _autoMap;
    private float _spellLight;
    private float _spellHide;
    private readonly SkyWeather _weather = new();
    private readonly Random _dice = new(0x51E2);
    private float _fatigue = 88f;
    private float _restFade;
    private float _wellRested;
    private bool _warnedTired;
    private bool _warnedHungry;
    private bool _warnedCold;
    private bool _mounted;
    private bool _wagonRide;
    private bool _mageTravel;
    private int _roomIndex;
    private WeatherKind _seenWeather = WeatherKind.Clear;
    private bool _weatherReady;
    private Vector3 _horseAt;
    private Vector3 _wagonAt;
    private float _horseYaw;
    private float _wagonYaw;
    private bool _wasSwimming;
    private readonly List<Foe> _foes = new();
    private float _hunt;
    private bool _campLit;
    private Vector3 _campAt;
    private float _hurtFlash;
    private AutoPlayer _bot = null!;
    private BotLog _botLog = null!;
    private readonly BotSight _sight = new();
    private BotIntent _botIntent;
    private float _lastMetres;
    private float _botLimit;
    private float _nearRefresh;
    private bool _botRunStarted;
    private bool _botSummaryWritten;

    public CampaignGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Campaign")
    {
        _launchArgs = args;
        _seed = int.TryParse(ParseOption(args, "--seed"), out var seed)
            ? seed
            : WorldScale.DefaultSeed;
    }

    protected override void LoadContent()
    {
        _scene = new SceneRenderer(GraphicsDevice);
        _prompts = new PromptRenderer(_ui);

        var root = AppContext.BaseDirectory;
        AttachCanvas();

        AttachScene(_faults);
        Ambience?.Dispose();

        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);
        _sprites = new CampaignSprites(GraphicsDevice);
        _sky = new HorizonSky(GraphicsDevice);
        _water = new WaterSurface(GraphicsDevice);
        _compass = new CompassHud();
        _beds = LocationBeds.Create(Path.Combine(root, "Content", "Audio"), out var bedFault);
        if (!string.IsNullOrWhiteSpace(bedFault)) _faults.Add(bedFault);

        RegisterCommands();
        EarthLand.Load(Path.Combine(root, "Content", "Earth", "land.bin"));
        BuildWorld(_seed);

        var botSeed = int.TryParse(ParseOption(_launchArgs, "--bot-seed"), out var rolled)
            ? rolled
            : Environment.TickCount;
        _bot = new AutoPlayer(botSeed, HasArgument(_launchArgs, "--bot"));
        var logPath = ParseOption(_launchArgs, "--bot-log")
            ?? Path.Combine(root, "bot-log.jsonl");
        _botLog = new BotLog(logPath);
        if (float.TryParse(ParseOption(_launchArgs, "--bot-minutes"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var minutes))
            _botLimit = MathF.Max(0f, minutes * 60f);
        else if (_bot.Enabled)
            _botLimit = 600f;

        if (_bot.Enabled)
        {
            if (!HasArgument(_launchArgs, "--fullscreen"))
                SetBorderlessFullscreen(false);
            EnsureBotRun();
            Toast($"Bot {_bot.Seed} is walking. F8 stops it.");
        }

        var scriptPath = ParseOption(_launchArgs, "--script");
        if (!string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath))
            QueueScript(File.ReadAllLines(scriptPath));
        else
        {
            var bundled = Path.Combine(root, "Scripts", "smoke.txt");
            if (HasArgument(_launchArgs, "--smoke") && File.Exists(bundled))
                QueueScript(File.ReadAllLines(bundled));
        }

        var exec = ParseOption(_launchArgs, "--exec");
        if (!string.IsNullOrWhiteSpace(exec))
            QueueScript(new[] { exec });

        foreach (var fault in _faults) Console.WriteLine($"campaign: {fault}");
        BeginHero();
    }

    private void BuildWorld(int seed)
    {
        _seed = seed;
        _mounted = false;
        _wagonRide = false;
        _wasSwimming = false;
        _fatigue = 88f;
        _shop.Open = false;
        _bank.Open = false;
        _mage.Open = false;
        _inv.Open = false;
        _talk.Close();
        _pause.Close();
        _sheet.Open = false;
        _spellUi.Open = false;
        _journal.Open = false;
        _autoMap = false;
        _spellLight = 0f;
        _spellHide = 0f;
        _mageTravel = false;
        _wellRested = 0f;
        _foes.Clear();
        _campLit = false;
        _hunt = 0f;
        _clock.Reset();
        _world?.Terrain?.Dispose();
        _map.Open = false;
        _world = WorldGenerator.Generate(seed);
        _world.AttachTerrain(GraphicsDevice);
        _map.Rebuild(GraphicsDevice, _world.Noise);
        _map.Selected = 0;
        _map.PendingLand = null;

        var spawn = _world.Spawn;
        var ground = _world.Heights.SampleWalk(spawn.X, spawn.Z);
        _returnWild = new Vector3(spawn.X, ground + WorldScale.EyeHeight, spawn.Z);

        var start = (ParseOption(_launchArgs, "--start") ?? "wild").ToLowerInvariant();
        if (start.StartsWith("town", StringComparison.Ordinal) &&
            int.TryParse(start.Length > 4 ? start[4..].Trim() : "0", out var townIndex) &&
            townIndex >= 0 && townIndex < _world.TownCount)
        {
            _townIndex = townIndex;
            var town = _world.Town(townIndex);
            _runner.Bind(town, town.Spawn, yaw: 0f, _view, OnLocation);
        }
        else if (start.StartsWith("dungeon", StringComparison.Ordinal) &&
            int.TryParse(start.Length > 7 ? start[7..].Trim() : "0", out var dungeonIndex) &&
            dungeonIndex >= 0 && dungeonIndex < _world.DungeonCount)
        {
            _dungeonIndex = dungeonIndex;
            var dungeon = _world.Dungeon(dungeonIndex);
            _runner.Bind(dungeon, dungeon.Spawn, yaw: MathF.PI, _view, OnLocation);
        }
        else
        {
            _runner.Bind(_world.Wilderness, _returnWild, yaw: 0f, _view, OnLocation);
        }

        ParkRides(_returnWild);
        ApplyMount();
        _view.SetProjection(GraphicsDevice.Viewport.AspectRatio);
        ApplyFog(_runner.Current.ClearColour);
    }

    private void RegisterCommands()
    {
        _console.Register("help", "help [name]", "List commands.",
            args => _console.Help(args.Text(0)));
        _console.Register("pos", "pos", "Print eye position.", _ =>
            $"{_view.Position.X:0.00} {_view.Position.Y:0.00} {_view.Position.Z:0.00}  {_runner.Current.Name}");
        _console.Register("seed", "seed", "Print the world seed.", _ => _seed.ToString());
        _console.Register("wait", "wait <seconds>", "Pause a script.", args =>
        {
            _wait = MathF.Max(0f, args.Number(0));
            return $"waiting {_wait:0.00}s";
        });
        _console.Register("goto", "goto town|dungeon|wild [index]", "Load a location.", args =>
        {
            var where = args.Text(0).ToLowerInvariant();
            var index = args.Integer(1);
            switch (where)
            {
                case "wild" or "wilderness":
                    GoWilderness(_returnWild);
                    return "wilderness";
                case "town":
                    return EnterTown(index, force: true) ? _world.TownNames[index] : "no town";
                case "dungeon":
                    return EnterDungeon(index) ? _world.DungeonNames[index] : "no dungeon";
                default:
                    return "goto town|dungeon|wild [index]";
            }
        });
        _console.Register("regen", "regen [seed]", "Rebuild the world.", args =>
        {
            var next = args.TryInteger(0, out var value) ? value : _seed;
            BuildWorld(next);
            return $"seed {next}";
        });
        _console.Register("travel", "travel <town index> | land <x> <z>", "Fast travel.", args =>
        {
            if (args.Text(0).Equals("land", StringComparison.OrdinalIgnoreCase))
            {
                var x = args.Number(1);
                var z = args.Number(2);
                JumpWilderness(x, z);
                return $"land {x:0} {z:0}";
            }

            var index = args.Integer(0);
            return EnterTown(index, force: true) ? _world.TownNames[index] : "no town";
        });
        _console.Register("towns", "towns", "How many towns, and the first few names.", _ =>
        {
            var take = Math.Min(8, _world.TownCount);
            return $"{_world.TownCount} towns, {_world.DungeonCount} delves. {string.Join(", ", _world.TownNames[..take])}…";
        });
        _console.Register("time", "time", "Print the hour.", _ => _clock.Stamp);
        _console.Register("pack", "pack", "Coin, papers, and blood.", _ =>
        {
            var papers = string.Concat(
                _pack.In(GuildKind.Fighters) ? " fighters" : "",
                _pack.In(GuildKind.Mages) ? " mages" : "",
                _pack.In(GuildKind.Thieves) ? " thieves" : "");
            var sick = _pack.Ailment == Ailment.None ? "well" : _pack.AilmentName;
            var wagon = _pack.HasWagon ? "wagon" : "no wagon";
            return $"{_pack.Gold} gp  vault {_pack.BankGold}  food {_pack.Rations}  stm {_pack.Stamina}  cur {_pack.Cures}  {wagon}  {_pack.HungerName}/{_pack.ColdName}  {sick}{papers}";
        });
        _console.Register("rest", "rest", "Sleep or wait.", _ =>
        {
            TryRest(fromBed: false, innFee: 0);
            return _clock.Stamp;
        });
        _console.Register("find", "find <name>", "Find town indices by name.", args =>
        {
            var query = args.Rest(0);
            if (string.IsNullOrWhiteSpace(query)) return "find <name>";
            var hits = new List<string>();
            for (var i = 0; i < _world.TownCount && hits.Count < 12; i++)
            {
                if (_world.TownNames[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    hits.Add($"{i} {_world.TownNames[i]}");
            }

            return hits.Count == 0 ? "none" : string.Join("  ", hits);
        });
        _console.Register("save", "save", "Write the pack to disk.", _ =>
        {
            WriteSave();
            return SaveFile.Path;
        });
        _console.Register("load", "load", "Read the last save.", _ =>
            ReadSave() ? "loaded" : "no save");
        _console.Register("status", "status", "Pack, place, clock, and bot.", _ => StatusText(),
            "stats");
        _console.Register("look", "look", "What E would use.", _ =>
            _hint is { } hint ? $"{hint.Marker.Kind}  {hint.Line}" : "nothing in reach");
        _console.Register("use", "use", "Press E on whatever you face.", _ =>
        {
            if (_hint is not { } hint) return "nothing in reach";
            Use(hint.Marker);
            return hint.Line;
        });
        _console.Register("swing", "swing", "Strike.", _ =>
        {
            Swing();
            return "swing";
        });
        _console.Register("eat", "eat", "Eat a ration or a meal.", _ =>
        {
            TryEat();
            return _pack.HungerName;
        });
        _console.Register("drink", "drink", "Stamina draught.", _ =>
        {
            DrinkStamina();
            return $"stamina {_pack.Stamina}";
        });
        _console.Register("cure", "cure", "Drink a cure.", _ =>
        {
            DrinkCure();
            return _pack.AilmentName.Length == 0 ? "well" : _pack.AilmentName;
        });
        _console.Register("mount", "mount", "Horse or wagon.", _ =>
        {
            ToggleMount();
            return _wagonRide ? "wagon" : _mounted ? "horse" : "foot";
        });
        _console.Register("give", "give <gold|rations|meals|stamina|cures|lockpicks> [n]",
            "Add to the pack.", args => Give(args.Text(0), Math.Max(1, args.Integer(1, 1))));
        _console.Register("clock", "clock [hour]", "Print or set the hour.", args =>
        {
            if (args.Count == 0) return _clock.Stamp;
            _clock.Load(_clock.Day, args.Number(0));
            return _clock.Stamp;
        });
        _console.Register("heal", "heal", "Full health and fatigue.", _ =>
        {
            _pack.Health = HpMax;
            _fatigue = WorldScale.FatigueMax;
            return "whole";
        });
        _console.Register("noclip", "noclip [on|off]", "Walk through walls.", args =>
        {
            _view.NoClip = args.Switch(0) ?? !_view.NoClip;
            return _view.NoClip ? "noclip on" : "noclip off";
        });
        _console.Register("foe", "foe [wolf|bandit|wisp|watch]", "Spawn a foe nearby.", args =>
        {
            var name = args.Text(0).ToLowerInvariant();
            var kind = name switch
            {
                "bandit" => FoeKind.Bandit,
                "wisp" or "wraith" => FoeKind.Wisp,
                "watch" => FoeKind.Watch,
                _ => FoeKind.Wolf
            };
            SpawnFoe(kind, _view.Position);
            return kind.ToString().ToLowerInvariant();
        });
        _console.Register("clearfoes", "clearfoes", "Remove living foes.", _ =>
        {
            var n = _foes.Count;
            _foes.Clear();
            return $"{n} gone";
        });
        _console.Register("bot", "bot [on|off]", "Auto-player. F8 also toggles.", args =>
        {
            var flag = args.Text(0).ToLowerInvariant();
            if (flag is "on" or "1" or "start") SetBot(true);
            else if (flag is "off" or "0" or "stop") SetBot(false);
            else if (flag is "report")
            {
                EnsureBotRun();
                _botLog.WriteSummary(_seed, _bot.Seed, _bot.GoalName);
                return _botLog.SummaryPath;
            }
            return _bot.Enabled
                ? $"on  {_bot.GoalName}  seed {_bot.Seed}  {_botLog.StatusLine()}"
                : $"off  seed {_bot.Seed}  {_botLog.Path}";
        });
        _console.Register("report", "report", "Write bot-summary.json now.", _ =>
        {
            EnsureBotRun();
            _botLog.WriteSummary(_seed, _bot.Seed, _bot.GoalName);
            return _botLog.SummaryPath;
        });
        _console.Register("script", "script <path>", "Queue a console script.", args =>
        {
            var path = args.Rest(0).Trim('"');
            if (string.IsNullOrWhiteSpace(path)) return "script <path>";
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, path);
            if (!File.Exists(path)) return "no file";
            QueueScript(File.ReadAllLines(path));
            return path;
        });
        _console.Register("music", "music [on|off]", "Looping beds. Off at launch.", args =>
        {
            _beds.Enabled = args.Switch(0) ?? !_beds.Enabled;
            return _beds.Enabled ? "music on" : "music off";
        });
        _console.Register("quit", "quit", "Leave the game.", _ =>
        {
            Exit();
            return "bye";
        }, "exit");
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();
        var seconds = RealSeconds(gameTime);
        _input.Sample();
        var keyboard = _input.CurrentKeyboard;

        var consoleAction = _consoleInput.Step(_input, keyboard);
        HandleConsole(consoleAction);

        if (_wait > 0f) _wait -= seconds;
        else PumpScript();

        if (_restFade <= 0.04f && !_consoleInput.Open && !_map.Open && !_autoMap && !OverlayOpen)
            _clock.AdvanceReal(seconds);

        TickWeather();
        _beds.SetRain(!Indoor && _weather.Wetting);
        _beds.SetWind(_weather.Kind is WeatherKind.Rain or WeatherKind.Snow or WeatherKind.Cloud ? 1.5f : 1f);
        if (_wellRested > 0f)
            _wellRested = MathF.Max(0f, _wellRested - seconds * WorldScale.GameSecondsPerReal / 3600f);

        _beds.Update(seconds);
        if (_toastLife > 0f) _toastLife -= seconds;

        if (_restFade > 0f)
            _restFade = MathF.Max(0f, _restFade - seconds * 1.6f);

        var blocking = _consoleInput.Open || _runner.IsFading || _wait > 0f
            || _capture.IsCapturing || _restFade > 0.04f;
        if (!blocking && _input.Pressed(keyboard, Keys.Escape))
        {
            if (_create.Open)
            {
                if (_create.Page > 0)
                {
                    _create.Page--;
                    _create.Selected = _create.Page == 0 ? (int)_create.Race : (int)_create.Class;
                }
            }
            else if (_shop.Open) _shop.Open = false;
            else if (_bank.Open) _bank.Open = false;
            else if (_mage.Open) _mage.Open = false;
            else if (_inv.Open) _inv.Open = false;
            else if (_sheet.Open) _sheet.Open = false;
            else if (_spellUi.Open) _spellUi.Open = false;
            else if (_journal.Open) _journal.Open = false;
            else if (_talk.Open) _talk.Close();
            else if (_autoMap) _autoMap = false;
            else if (_map.Open)
            {
                _map.Open = false;
                _mageTravel = false;
            }
            else if (_pause.Open && _pause.Page != PausePage.Main)
            {
                _pause.Page = PausePage.Main;
                _pause.Selected = 0;
            }
            else if (_pause.Open)
                _pause.Close();
            else
                _pause.Show();
        }

        if (!blocking && !_consoleInput.Open && !_map.Open && !OverlayOpen
            && (_input.Pressed(keyboard, Keys.Tab)))
            _pause.Show();
        else if (!blocking && _pause.Open && _input.Pressed(keyboard, Keys.Tab))
            _pause.Close();

        if (!blocking && !_consoleInput.Open && !_map.Open && !OverlayOpen
            && _input.Pressed(keyboard, Keys.I))
        {
            _inv.Open = true;
            _inv.Selected = 0;
        }
        else if (!blocking && _inv.Open && _input.Pressed(keyboard, Keys.I))
            _inv.Open = false;

        if (!blocking && !_consoleInput.Open && !_map.Open && !OverlayOpen
            && _input.Pressed(keyboard, Keys.K))
            _sheet.Open = true;
        else if (!blocking && _sheet.Open && _input.Pressed(keyboard, Keys.K))
            _sheet.Open = false;

        if (!blocking && !_consoleInput.Open && !_map.Open && !OverlayOpen
            && _input.Pressed(keyboard, Keys.J))
        {
            _journal.Open = true;
            _journal.Selected = 0;
        }
        else if (!blocking && _journal.Open && _input.Pressed(keyboard, Keys.J))
            _journal.Open = false;

        if (!blocking && !_consoleInput.Open && !_map.Open && !OverlayOpen
            && _input.Pressed(keyboard, Keys.D4))
            _spellUi.Open = true;
        else if (!blocking && _spellUi.Open && _input.Pressed(keyboard, Keys.D4))
            _spellUi.Open = false;

        if (!blocking && !_consoleInput.Open && !_map.Open && !OverlayOpen
            && _input.Pressed(keyboard, Keys.Q)
            && !_inv.Open)
            CastSelected();

        if (!blocking && !_consoleInput.Open && !OverlayOpen && _input.Pressed(keyboard, Keys.M))
        {
            if (InDungeon)
                _autoMap = !_autoMap;
            else
            {
                _autoMap = false;
                _map.Open = !_map.Open;
            }
        }
        else if (!blocking && _autoMap && _input.Pressed(keyboard, Keys.M))
            _autoMap = false;

        var mapOpen = _map.Open || _autoMap;
        var stallOpen = OverlayOpen;
        var botDrive = _bot.Enabled && !_consoleInput.Open;
        IsMouseVisible = false;

        if (!botDrive && _map.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleMap(keyboard);
        if (!botDrive && _shop.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleShop(keyboard);
        if (!botDrive && _bank.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleBank(keyboard);
        if (!botDrive && _mage.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleMage(keyboard);
        if (!botDrive && _inv.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleInventory(keyboard);
        if (!botDrive && _talk.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleDialogue(keyboard);
        if (!botDrive && _pause.Open && !_consoleInput.Open && !_runner.IsFading)
            HandlePause(keyboard);
        if (!botDrive && _create.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleCreate(keyboard);
        if (!botDrive && _sheet.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleSheet(keyboard);
        if (!botDrive && _spellUi.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleSpellmaker(keyboard);
        if (!botDrive && _journal.Open && !_consoleInput.Open && !_runner.IsFading)
            HandleJournal(keyboard);

        if (!_consoleInput.Open && !_runner.IsFading && _restFade <= 0.04f)
        {
            if (_input.Pressed(keyboard, Keys.D1)) DrinkStamina();
            if (_input.Pressed(keyboard, Keys.D2)) DrinkCure();
            if (_input.Pressed(keyboard, Keys.D3)) TryEat();
            if (_input.Pressed(keyboard, Keys.F5))
            {
                WriteSave();
                Toast("The book is written.");
            }

            if (_input.Pressed(keyboard, Keys.F9))
                Toast(ReadSave() ? "You remember." : "No book to read.");
            if (_input.Pressed(keyboard, Keys.F8))
                SetBot(!_bot.Enabled);

            var scaleBump = 0f;
            if (_input.Pressed(keyboard, Keys.OemCloseBrackets)
                || _input.Pressed(keyboard, Keys.OemPlus)
                || _input.Pressed(keyboard, Keys.Add))
                scaleBump = WorldScale.UiScaleStep;
            else if (_input.Pressed(keyboard, Keys.OemOpenBrackets)
                || _input.Pressed(keyboard, Keys.OemMinus)
                || _input.Pressed(keyboard, Keys.Subtract))
                scaleBump = -WorldScale.UiScaleStep;
            if (scaleBump != 0f)
            {
                _uiScalePreference = Math.Clamp(_uiScalePreference + scaleBump,
                    WorldScale.UiScaleMin, WorldScale.UiScaleMax);
                _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);
                Toast($"UI scale {_uiScalePreference:0.00}");
            }
        }

        _view.Crouching = !_consoleInput.Open && !mapOpen && !stallOpen && !_mounted
            && keyboard.IsKeyDown(Keys.C);

        if (!botDrive && !mapOpen && !stallOpen && !_consoleInput.Open && !_runner.IsFading
            && _input.Pressed(keyboard, Keys.H))
            ToggleMount();

        if (!botDrive && !mapOpen && !stallOpen && !_consoleInput.Open && !_runner.IsFading
            && _input.Pressed(keyboard, Keys.R))
            TryRest(fromBed: false, innFee: 0);

        FillSight();
        _botIntent = default;
        if (botDrive)
        {
            EnsureBotRun();
            _botIntent = _bot.Think(_sight, seconds, _botLog);
        }

        _view.WantClimb = botDrive
            ? _botIntent.Climb
            : !mapOpen && !stallOpen && !_consoleInput.Open && !_mounted
                && !_view.Swimming && keyboard.IsKeyDown(Keys.V);
        var tired = _fatigue < 12f ? 0.5f : _fatigue < 32f ? 0.78f : 1f;
        if (_wellRested > 0f) tired = MathF.Min(1.08f, tired + 0.08f);
        _view.SpeedScale = tired * _pack.SpeedMul * _hero.BurdenSpeed(_pack.Gold);
        if (_view.WantClimb) _hero.UseSkill(SkillId.Climbing, 0.012f);

        var look = Vector2.Zero;
        if (!botDrive && !_consoleInput.Open && !mapOpen && !stallOpen
            && !_capture.IsCapturing && !_runner.IsFading)
        {
            var mouse = _input.CurrentMouse;
            var viewport = GraphicsDevice.Viewport;
            if (_centredMouse)
                look = new Vector2(mouse.X - viewport.Width / 2f, mouse.Y - viewport.Height / 2f);
            CentreMouse();
            _centredMouse = true;
        }
        else
        {
            _centredMouse = false;
        }

        var frozen = _consoleInput.Open || mapOpen || stallOpen;
        WalkInput walk;
        if (botDrive)
            walk = _botIntent.Walk;
        else
            walk = new WalkInput(
                Forward: !frozen && keyboard.IsKeyDown(Keys.W),
                Back: !frozen && keyboard.IsKeyDown(Keys.S),
                Left: !frozen && keyboard.IsKeyDown(Keys.A),
                Right: !frozen && keyboard.IsKeyDown(Keys.D),
                Sprint: keyboard.IsKeyDown(Keys.LeftShift),
                Jump: !frozen && (_view.Swimming
                    ? keyboard.IsKeyDown(Keys.Space)
                    : _input.Pressed(keyboard, Keys.Space)),
                HeldYaw: frozen ? 0f
                    : (keyboard.IsKeyDown(Keys.Right) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.Left) ? 1f : 0f),
                HeldPitch: frozen ? 0f
                    : (keyboard.IsKeyDown(Keys.Down) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.Up) ? 1f : 0f));

        UpdateWater();
        SampleGround? bed = _runner.Current is WildernessLocation
            ? (x, z) => _world.Heights.Sample(x, z)
            : null;
        var moved = _view.Step(seconds, walk, look,
            _runner.Current.Collide, _runner.Current.SampleGround, bed);
        RescueUnderMap();
        _view.RebuildView();
        _lastMetres = moved.MetresWalked;
        if (_mounted && !_wagonRide && _runner.Current is WildernessLocation)
        {
            var g = _world.Heights.SampleWalk(_view.Position.X, _view.Position.Z);
            _horseAt = new Vector3(_view.Position.X, g, _view.Position.Z);
            _horseYaw = _view.Yaw;
        }

        if (moved.MetresWalked > 0.001f)
        {
            _stepDistance += moved.MetresWalked;
            var stride = _wagonRide ? 3.2f : _mounted ? 2.8f : _view.Swimming ? 2.2f : 1.45f;
            if (_stepDistance > stride)
            {
                Sounds?.Play(Sfx.Step, weight: 0.35f, volumeScale: _mounted ? 0.9f : 0.7f);
                _stepDistance = 0f;
            }
        }

        if (moved.Landed) Sounds?.Play(Sfx.Land, weight: 0.55f);
        TickFatigue(seconds, moved.MetresWalked, walk.Sprint);
        TickSurvival(seconds, moved.MetresWalked, walk.Sprint);
        TickFoes(seconds, moved.MetresWalked);
        if (walk.Sprint && moved.MetresWalked > 0.001f) _hero.UseSkill(SkillId.Running, 0.02f);
        _hero.TickMagicka(seconds);
        if (_spellLight > 0f) _spellLight = MathF.Max(0f, _spellLight - seconds);
        if (_spellHide > 0f) _spellHide = MathF.Max(0f, _spellHide - seconds);
        NoteAutoCell();
        if (_hurtFlash > 0f) _hurtFlash = MathF.Max(0f, _hurtFlash - seconds);

        if (_runner.Current is WildernessLocation wild)
        {
            wild.TickSky(_view.Position);
            if (_view.Swimming && _view.Position.Y < WorldScale.WaterLevel - 0.15f)
            {
                LitEffect.FogEnabled = true;
                LitEffect.FogColor = new Vector3(0.08f, 0.22f, 0.32f);
                LitEffect.FogStart = 6f;
                LitEffect.FogEnd = 28f;
            }
            else
                ApplyFog(_weather.TintFog(_clock.TintFog(wild.FogColour)));
        }
        else
            ApplyFog(_runner.Current.ClearColour);

        _hint = _runner.Current.Probe(_view.Position, _view.Forward);
        MaybeWagonHint();
        if (botDrive)
            ApplyBot(_botIntent);
        else if (!_consoleInput.Open && !_map.Open && !OverlayOpen && !_runner.IsFading)
        {
            if (_hint is { } hint && _input.Pressed(keyboard, Keys.E))
                Use(hint.Marker);

            var swing = _input.Pressed(keyboard, Keys.F) || _input.Clicked(_input.CurrentMouse);
            if (swing) Swing();
        }

        if (_bot.Enabled && _botLimit > 0f && _botLog.RealSeconds >= _botLimit)
        {
            _botLog.Event("limit", $"{_botLimit:0}s", _sight);
            FlushBotSummary();
            Exit();
        }

        _runner.Update(seconds, _view);
        _input.Commit();
        base.Update(gameTime);
    }

    private bool OverlayOpen =>
        _shop.Open || _bank.Open || _mage.Open || _inv.Open || _talk.Open || _pause.Open
        || _create.Open || _sheet.Open || _spellUi.Open || _journal.Open;

    private float HpMax => MathF.Max(40f, _hero.HealthMax);

    private bool InDungeon =>
        _runner.Current is BoxLocation { Kind: LocationKind.Dungeon };

    private bool SkipCreate =>
        _bot.Enabled
        || HasArgument(_launchArgs, "--smoke")
        || HasArgument(_launchArgs, "--start");

    private void BeginHero()
    {
        if (SkipCreate)
        {
            _create.Open = false;
            FinishCreate(RaceId.Redguard, ClassId.Warrior);
            return;
        }

        _create.Open = true;
        _create.Page = 0;
        _create.Selected = 0;
    }

    private void FinishCreate(RaceId race, ClassId cls)
    {
        _hero.Roll(race, cls, "Wanderer");
        _pack.Health = HpMax;
        if (cls == ClassId.Thief)
            _pack.Lockpicks = Math.Max(_pack.Lockpicks, 4);
        if (_hero.Spells.Count == 0 && cls is ClassId.Mage or ClassId.Spellsword)
            _hero.Spells.Add(Spell.Heal);
        var dungeon = Math.Clamp(0, 0, Math.Max(0, _world.DungeonCount - 1));
        QuestBook.EnsureMain(_hero, _world.DungeonNames[dungeon], dungeon);
        _create.Open = false;
    }

    private void SyncHeroFromGuild()
    {
        for (var i = 0; i < 3; i++)
        {
            if (_pack.Guild[i])
                _hero.Member[i] = true;
            else if (_hero.Member[i])
                _pack.Guild[i] = true;
        }
    }

    private string TalkTown() =>
        _world.TownNames[Math.Clamp(_townIndex, 0, _world.TownCount - 1)];

    private string RelicDungeonName()
    {
        var i = _hero.RelicDungeon >= 0 ? _hero.RelicDungeon : 0;
        i = Math.Clamp(i, 0, _world.DungeonCount - 1);
        return _world.DungeonNames[i];
    }

    private DialogueOption[] BuildTalkOptions(int talkId)
    {
        var list = new List<DialogueOption>();
        var added = 0;
        for (var i = 0; i < TalkBook.Catalogue.Length && added < 6; i++)
        {
            var (key, prompt) = TalkBook.Catalogue[i];
            if (!_hero.Topics.Contains(key)) continue;
            list.Add(new DialogueOption(prompt, 100 + i));
            added++;
        }

        if (talkId == 1 && _hero.MainBeat == 0)
            list.Add(new DialogueOption("I carry a letter about a relic.", 20));
        list.Add(new DialogueOption("Goodbye.", 0));
        return list.ToArray();
    }

    private void TeachRumor()
    {
        foreach (var (key, _) in TalkBook.Catalogue)
        {
            if (_hero.Topics.Contains(key)) continue;
            if (_dice.NextDouble() > 0.55) continue;
            _hero.Topics.Add(key);
            Toast($"You catch the word: {key}.");
            return;
        }
    }

    private void DeliverTotem(FactionId to)
    {
        var line = QuestBook.Deliver(_hero, to);
        SyncHeroFromGuild();
        _talk.Close();
        Sounds?.Play(Sfx.Chime, weight: 0.55f);
        Toast(line);
    }

    private void NoteAutoCell()
    {
        if (!InDungeon) return;
        if (_hero.AutoDungeon != _dungeonIndex)
        {
            _hero.AutoDungeon = _dungeonIndex;
            _hero.AutoCells.Clear();
        }

        _hero.AutoCells.Add((int)(_view.Position.Z / WorldScale.BlockMetres));
    }

    private bool HiddenFrom(Foe foe)
    {
        if (_spellHide > 0.05f) return true;
        if (!_view.Crouching) return false;
        var to = _view.Position - foe.Feet;
        to.Y = 0f;
        var dist = to.Length();
        if (dist < 0.01f) return false;
        to /= dist;
        var facing = new Vector3(MathF.Sin(foe.Yaw), 0f, MathF.Cos(foe.Yaw));
        var front = Vector3.Dot(to, facing) > 0.12f;
        if (dist < 2.4f) return _hero.Chance(SkillId.Stealth, front ? -8f : 18f);
        if (!front) return true;
        return _hero.Chance(SkillId.Stealth, 6f);
    }

    private void EquipPack(string id)
    {
        var gear = Gear.FromShop(id);
        if (gear == Gear.None)
        {
            Toast("You keep it.");
            return;
        }

        _hero.Equip(gear);
        Toast($"You ready the {Gear.Of(gear).Name.ToLowerInvariant()}.");
    }

    private void CastSelected()
    {
        if (_hero.Spells.Count == 0)
        {
            Toast("No spells in the book. Open spellmaker with 4.");
            return;
        }

        var i = Math.Clamp(_hero.SpellSel, 0, _hero.Spells.Count - 1);
        CastSpell(_hero.Spells[i]);
    }

    private void CastSpell(Spell spell)
    {
        if (!_hero.SpendMagicka(spell.Cost))
        {
            Toast("Not enough magicka.");
            return;
        }

        Sounds?.Play(Sfx.Chime, weight: 0.45f);
        switch (spell.Effect)
        {
            case SpellEffect.Heal:
                _pack.Health = MathF.Min(HpMax, _pack.Health + spell.Magnitude);
                _hero.UseSkill(SkillId.Restoration, 0.4f);
                Toast("Warmth in the blood.");
                break;
            case SpellEffect.Spark:
                _hero.UseSkill(SkillId.Destruction, 0.4f);
                if (!SparkFoe(spell.Magnitude))
                    Toast("Spark, and nothing in reach.");
                break;
            case SpellEffect.Light:
                _spellLight = 48f;
                _hero.UseSkill(SkillId.Mysticism, 0.25f);
                Toast("A pale light.");
                break;
            default:
                _spellHide = 20f;
                _hero.UseSkill(SkillId.Mysticism, 0.3f);
                Toast("You fade.");
                break;
        }
    }

    private bool SparkFoe(float magnitude)
    {
        Foe? best = null;
        var bestD = 9f;
        var forward = new Vector3(_view.Forward.X, 0f, _view.Forward.Z);
        if (forward.LengthSquared() < 0.001f) return false;
        forward.Normalize();
        foreach (var foe in _foes)
        {
            if (foe.Dead) continue;
            var to = foe.Feet - _view.Position;
            to.Y = 0f;
            var dist = to.Length();
            if (dist > bestD || dist < 0.2f) continue;
            to /= dist;
            if (Vector3.Dot(forward, to) < 0.2f) continue;
            best = foe;
            bestD = dist;
        }

        if (best is null) return false;
        Sounds?.Play(Sfx.HitFlesh, weight: 0.55f);
        best.Alerted = true;
        best.Health -= 8f + magnitude * 0.7f;
        if (best.Health > 0f)
        {
            Toast($"Spark hits the {best.Name}.");
            return true;
        }

        best.Dead = true;
        _pack.Gold += best.Gold;
        Sounds?.Play(Sfx.Death, weight: 0.55f);
        Toast($"The {best.Name} falls. {best.Gold} gp.");
        return true;
    }

    private void HandleCreate(KeyboardState keyboard)
    {
        var pick = PickRows(_create.Selected, keyboard, _create.Count, _create.ItemRow);
        _create.Selected = pick.Selection;
        if (!MenuConfirm(pick, keyboard)) return;
        if (_create.Page == 0)
        {
            _create.Race = (RaceId)_create.Selected;
            _create.Page = 1;
            _create.Selected = (int)_create.Class;
            return;
        }

        if (_create.Page == 1)
        {
            _create.Class = (ClassId)_create.Selected;
            _create.Page = 2;
            _create.Selected = 0;
            return;
        }

        FinishCreate(_create.Race, _create.Class);
    }

    private void HandleSheet(KeyboardState keyboard)
    {
        var pointer = LogicalMouse(_input.CurrentMouse);
        var tab = ListPicker.Hovered(pointer, 3, _sheet.TabRow);
        if (tab >= 0 && _input.Clicked(_input.CurrentMouse))
        {
            _sheet.Tab = tab;
            return;
        }

        if (_input.Pressed(keyboard, Keys.Left) || _input.Pressed(keyboard, Keys.Q))
            _sheet.Tab = (_sheet.Tab + 2) % 3;
        if (_input.Pressed(keyboard, Keys.Right))
            _sheet.Tab = (_sheet.Tab + 1) % 3;
    }

    private void HandleSpellmaker(KeyboardState keyboard)
    {
        var pick = PickRows(_spellUi.Selected, keyboard, SpellmakerScreen.Lines.Length,
            _spellUi.ItemRow);
        _spellUi.Selected = pick.Selection;
        if (_input.Pressed(keyboard, Keys.Left) || _input.Pressed(keyboard, Keys.Q))
            NudgeSpell(-1);
        if (_input.Pressed(keyboard, Keys.Right))
            NudgeSpell(1);
        if (!MenuConfirm(pick, keyboard)) return;
        if (_spellUi.Selected == 2)
            SaveDraftSpell();
        else if (_spellUi.Selected == 3)
            CastSelected();
    }

    private void NudgeSpell(int delta)
    {
        if (_spellUi.Selected == 0)
            _spellUi.Effect = (_spellUi.Effect + delta % 4 + 4) % 4;
        else if (_spellUi.Selected == 1)
            _spellUi.Magnitude = Math.Clamp(_spellUi.Magnitude + delta * 2, 1, 40);
        else if (_spellUi.Selected == 3 && _hero.Spells.Count > 0)
            _hero.SpellSel = (_hero.SpellSel + delta % _hero.Spells.Count + _hero.Spells.Count)
                % _hero.Spells.Count;
    }

    private void SaveDraftSpell()
    {
        var effect = (SpellEffect)Math.Clamp(_spellUi.Effect, 0, 3);
        var mag = Math.Clamp(_spellUi.Magnitude, 1, 40);
        var cost = Spell.CostOf(effect, mag);
        var name = $"{Spell.Label(effect)} {mag}";
        _hero.Spells.Add(new Spell(name, effect, mag, cost));
        _hero.SpellSel = _hero.Spells.Count - 1;
        Toast($"{name} is in the book. Cost {cost:0}.");
    }

    private void HandleJournal(KeyboardState keyboard)
    {
        var n = _hero.Log.Count;
        if (n <= 0) return;
        var pick = PickRows(_journal.Selected, keyboard, n, _journal.ItemRow);
        _journal.Selected = pick.Selection;
    }

    private void HandleMap(KeyboardState keyboard)
    {
        if (_input.Pressed(keyboard, Keys.Up) || _input.Pressed(keyboard, Keys.W))
            _map.MoveSelection(-1, EarthPlaces.StopCount);
        if (_input.Pressed(keyboard, Keys.Down) || _input.Pressed(keyboard, Keys.S))
            _map.MoveSelection(1, EarthPlaces.StopCount);

        if (_input.Clicked(_input.CurrentMouse))
            _map.Click(LogicalMouse(_input.CurrentMouse), _world);

        if (_input.Pressed(keyboard, Keys.Enter) || _input.Pressed(keyboard, Keys.E))
            ConfirmTravel();
    }

    private void ConfirmTravel()
    {
        Vector3 dest;
        var toTown = -1;
        if (_map.PendingLand is { } land)
            dest = land;
        else
        {
            toTown = _map.Selected;
            dest = _world.TownPads[toTown];
        }

        var hours = RoadHours(_view.Position, dest);
        if (_pack.Hunger < 10f && hours > 5f && !_mageTravel)
        {
            Toast("You are too weak for that road. Eat first.");
            return;
        }

        if (_mageTravel)
        {
            if (_pack.Gold < 15)
            {
                Toast("The working costs fifteen gold.");
                _mageTravel = false;
                _map.Open = false;
                return;
            }

            _pack.Gold -= 15;
            hours = 0.35f;
        }

        TakeRoad(hours, dest);
        _map.Open = false;

        if (_mageTravel && toTown >= 0 && EarthPlaces.IsHistoric(toTown))
        {
            _mageTravel = false;
            ArriveGuild(toTown);
            Toast($"The hall in {_world.TownNames[toTown]}.  {_clock.Stamp}");
            return;
        }

        _mageTravel = false;
        if (toTown < 0 || EarthPlaces.IsMark(toTown))
        {
            JumpWilderness(dest.X, dest.Z);
            Toast(toTown < 0
                ? $"A {hours:0.0} hour road.  {_clock.Stamp}"
                : $"A {hours:0.0} hour road to the mark called {_world.TownNames[toTown]}.  {_clock.Stamp}");
            return;
        }

        if (_clock.IsNight && !_pack.In(GuildKind.Thieves))
        {
            GoWilderness(LeaveTownStand(toTown));
            _townIndex = toTown;
            Toast($"A {hours:0.0} hour road. The gate is shut.  {_clock.Stamp}");
            return;
        }

        if (EnterTown(toTown, force: true))
            Toast($"A {hours:0.0} hour road to {_world.TownNames[toTown]}.  {_clock.Stamp}");
    }

    private void HandleShop(KeyboardState keyboard)
    {
        var pick = PickRows(_shop.Selected, keyboard, Ledger.Goods.Length, _shop.ItemRow);
        _shop.Selected = pick.Selection;
        if (MenuConfirm(pick, keyboard))
            TryBuy();
    }

    private void HandleBank(KeyboardState keyboard)
    {
        var pick = PickRows(_bank.Selected, keyboard, BankScreen.Lines.Length, _bank.ItemRow);
        _bank.Selected = pick.Selection;
        if (MenuConfirm(pick, keyboard))
            TryBank();
    }

    private void HandleMage(KeyboardState keyboard)
    {
        var pick = PickRows(_mage.Selected, keyboard, MageScreen.Lines.Length, _mage.ItemRow);
        _mage.Selected = pick.Selection;
        if (MenuConfirm(pick, keyboard))
            TryMage();
    }

    private void HandleInventory(KeyboardState keyboard)
    {
        var pointer = LogicalMouse(_input.CurrentMouse);
        var tab = ListPicker.Hovered(pointer, InventoryScreen.Tabs.Length, _inv.TabRow);
        if (tab >= 0 && _input.Clicked(_input.CurrentMouse))
        {
            _inv.Tab = tab;
            _inv.Selected = 0;
            return;
        }

        if (_input.Pressed(keyboard, Keys.Left) || _input.Pressed(keyboard, Keys.Q))
            _inv.MoveTab(-1);
        if (_input.Pressed(keyboard, Keys.Right))
            _inv.MoveTab(1);

        var items = _inv.ListFor(_pack, _hero);
        var pick = PickRows(_inv.Selected, keyboard, items.Count, _inv.ItemRow);
        _inv.Selected = pick.Selection;
        if (MenuConfirm(pick, keyboard))
            UseInventory();
    }

    private void UseInventory()
    {
        var items = _inv.ListFor(_pack, _hero);
        if (_inv.Selected < 0 || _inv.Selected >= items.Count) return;
        var item = items[_inv.Selected];
        if (!item.Usable)
        {
            Toast("You keep it.");
            return;
        }

        switch (item.Id)
        {
            case "rations": TryEat(); break;
            case "stew":
                if (_pack.Meals <= 0) break;
                _pack.Meals--;
                _pack.Hunger = MathF.Min(Ledger.NeedMax, _pack.Hunger + 38f);
                _pack.Cold = MathF.Max(0f, _pack.Cold - 12f);
                Toast("The stew settles you.");
                break;
            case "stamina": DrinkStamina(); break;
            case "cure": DrinkCure(); break;
            default:
                EquipPack(item.Id);
                break;
        }
    }

    private void HandleDialogue(KeyboardState keyboard)
    {
        var pick = PickRows(_talk.Selected, keyboard, _talk.Options.Length, _talk.OptionRow);
        _talk.Selected = pick.Selection;
        if (MenuConfirm(pick, keyboard))
            PickDialogue(_talk.Current.Id);
    }

    private void HandlePause(KeyboardState keyboard)
    {
        var pick = PickRows(_pause.Selected, keyboard, _pause.Count, _pause.Row);
        _pause.Selected = pick.Selection;

        if (_pause.Page == PausePage.Settings)
        {
            if (_input.Pressed(keyboard, Keys.Left) || _input.Pressed(keyboard, Keys.Q))
                NudgeSetting(-1);
            if (_input.Pressed(keyboard, Keys.Right))
                NudgeSetting(1);
            if (pick.ClickedOnRow(_input, _input.CurrentMouse)
                && (_pause.Selected == 1 || _pause.Selected == 2))
            {
                var row = _pause.SettingRow(_pause.Selected);
                NudgeSetting(LogicalMouse(_input.CurrentMouse).X >= row.Center.X ? 1 : -1);
                return;
            }
        }

        if (!MenuConfirm(pick, keyboard))
            return;

        if (_pause.Page == PausePage.Controls)
        {
            _pause.Page = PausePage.Main;
            _pause.Selected = 6;
            return;
        }

        if (_pause.Page == PausePage.Settings)
        {
            if (_pause.Selected == 0)
                _beds.Enabled = !_beds.Enabled;
            else if (_pause.Selected == 3)
            {
                _pause.Page = PausePage.Main;
                _pause.Selected = 7;
            }

            return;
        }

        switch (_pause.Selected)
        {
            case 0:
                _pause.Close();
                break;
            case 1:
                _pause.Close();
                _inv.Open = true;
                _inv.Selected = 0;
                break;
            case 2:
                _pause.Close();
                _sheet.Open = true;
                break;
            case 3:
                _pause.Close();
                _spellUi.Open = true;
                break;
            case 4:
                _pause.Close();
                _journal.Open = true;
                _journal.Selected = 0;
                break;
            case 5:
                _pause.Close();
                if (InDungeon) _autoMap = true;
                else _map.Open = true;
                break;
            case 6:
                _pause.Page = PausePage.Controls;
                _pause.Selected = 0;
                break;
            case 7:
                _pause.Page = PausePage.Settings;
                _pause.Selected = 0;
                break;
            case 8:
                Exit();
                break;
        }
    }

    private ListPick PickRows(int selected, KeyboardState keyboard, int count,
        Func<int, Rectangle> row)
    {
        var mouse = _input.CurrentMouse;
        var pointer = LogicalMouse(mouse);
        var pick = ListPicker.Step(selected, _input, keyboard, mouse, pointer, count, row);
        if (count <= 0) return pick;
        if (_input.Pressed(keyboard, Keys.W))
            return pick with
            {
                Selection = (pick.Selection + count - 1) % count,
                KeyboardMoved = true
            };
        if (_input.Pressed(keyboard, Keys.S))
            return pick with
            {
                Selection = (pick.Selection + 1) % count,
                KeyboardMoved = true
            };
        return pick;
    }

    private bool MenuConfirm(ListPick pick, KeyboardState keyboard) =>
        pick.Confirmed(_input, keyboard, _input.CurrentMouse)
        || _input.Pressed(keyboard, Keys.E);

    private void NudgeSetting(int delta)
    {
        if (_pause.Selected == 0)
        {
            _beds.Enabled = !_beds.Enabled;
            return;
        }

        if (_pause.Selected == 1)
        {
            _uiScalePreference = Math.Clamp(_uiScalePreference + delta * WorldScale.UiScaleStep,
                WorldScale.UiScaleMin, WorldScale.UiScaleMax);
            _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);
            return;
        }

        if (_pause.Selected == 2)
        {
            _view.MouseSensitivity = Math.Clamp(
                _view.MouseSensitivity + delta * 0.0004f, 0.0012f, 0.008f);
        }
    }

    private void PickDialogue(int id)
    {
        if (id >= 100 && id < 100 + TalkBook.Catalogue.Length)
        {
            var key = TalkBook.Catalogue[id - 100].Key;
            _talk.Body = TalkBook.Answer(key, _hero, TalkTown(), RelicDungeonName(), _talkId);
            _hero.UseSkill(SkillId.Speech, 0.2f);
            TeachRumor();
            _talk.Options = BuildTalkOptions(_talkId);
            return;
        }

        switch (id)
        {
            case 0:
                _talk.Close();
                break;
            case 1:
                _talk.Close();
                TryJoin(GuildKind.Fighters);
                break;
            case 2:
                _talk.Close();
                TryJoin(GuildKind.Mages);
                break;
            case 3:
                _talk.Close();
                TryJoin(GuildKind.Thieves);
                break;
            case 4:
                _talk.Close();
                TryHeal();
                break;
            case 5:
                _talk.Close();
                OpenShop();
                break;
            case 6:
                _talk.Close();
                OpenBank();
                break;
            case 7:
                _talk.Body = TalkLine(_talkId);
                TeachRumor();
                _talk.Options = BuildTalkOptions(_talkId);
                break;
            case 8:
                _talk.Close();
                TryJoin(GuildKind.Mages);
                break;
            case 9:
                _talk.Close();
                TryJoin(GuildKind.Fighters);
                break;
            case 10:
                _talk.Close();
                TryJoin(GuildKind.Thieves);
                break;
            case 20:
                _talk.Body = QuestBook.AdvanceInn(_hero, RelicDungeonName());
                _talk.Options = BuildTalkOptions(_talkId);
                break;
            case 21:
                DeliverTotem(FactionId.Fighters);
                break;
            case 22:
                DeliverTotem(FactionId.Mages);
                break;
            case 23:
                DeliverTotem(FactionId.Temple);
                break;
        }
    }

    private void TryBuy()
    {
        if (!_clock.ShopsOpen)
        {
            Toast("The shop is shuttered until morning.");
            return;
        }

        var good = Ledger.Goods[_shop.Selected];
        if (good.Kind == "wagon" && _pack.HasWagon)
        {
            Toast("You already have a team.");
            return;
        }

        if (good.Kind == "ship" && _pack.HasShip)
        {
            Toast("You already have a boat.");
            return;
        }

        if (good.Kind == "cloak" && _pack.HasCloak)
        {
            Toast("You already wear a cloak.");
            return;
        }

        if (good.Kind is "weapon" or "bow" or "armor")
        {
            var already = Gear.FromShop(good.Id);
            if (already != Gear.None && _hero.OwnedGear.Contains(already))
            {
                Toast("You already keep that.");
                return;
            }
        }

        var price = _pack.PriceOf(good, _hero);
        if (_pack.Gold < price)
        {
            Toast("You haven't the coin.");
            return;
        }

        _pack.Gold -= price;
        _hero.UseSkill(SkillId.Mercantile, 0.25f);
        if (_botRunStarted) _botLog.Buys++;
        switch (good.Kind)
        {
            case "food":
                _pack.Rations++;
                break;
            case "stamina":
                _pack.Stamina++;
                break;
            case "cure":
                _pack.Cures++;
                break;
            case "lock":
                _pack.Lockpicks++;
                break;
            case "cloak":
                _pack.HasCloak = true;
                break;
            case "meal":
                _pack.Meals++;
                break;
            case "wagon":
                _pack.HasWagon = true;
                break;
            case "ship":
                _pack.HasShip = true;
                break;
            case "ammo":
                _hero.Arrows += 12;
                break;
            case "weapon":
            case "bow":
            case "armor":
            {
                var id = Gear.FromShop(good.Id);
                if (id != Gear.None)
                {
                    _hero.OwnedGear.Add(id);
                    if (good.Kind == "weapon" && _hero.Weapon == 0) _hero.Weapon = id;
                    if (good.Kind == "bow" && _hero.Bow == 0) _hero.Bow = id;
                    if (good.Kind == "armor" && _hero.Armor == 0) _hero.Armor = id;
                }

                break;
            }
        }

        Sounds?.Play(Sfx.Coin, weight: 0.55f);
        Toast(good.Kind switch
        {
            "wagon" => "The wagon waits outside the walls.",
            "ship" => "The longboat is moored on the coast.",
            _ => $"You buy {good.Name.ToLowerInvariant()}."
        });
    }

    private void DrinkStamina()
    {
        if (_pack.Stamina <= 0)
        {
            Toast("No draughts.");
            return;
        }

        _pack.Stamina--;
        _fatigue = MathF.Min(WorldScale.FatigueMax, _fatigue + 48f);
        _pack.Health = MathF.Min(HpMax, _pack.Health + 14f);
        Sounds?.Play(Sfx.Chime, weight: 0.35f);
        Toast("The draught burns going down.");
    }

    private void DrinkCure()
    {
        if (_pack.Cures <= 0)
        {
            Toast("No cure potions.");
            return;
        }

        _pack.Cures--;
        Sounds?.Play(Sfx.Chime, weight: 0.4f);
        Toast(_pack.Cure() ? "The fever breaks." : "Nothing to cure. The potion is spent.");
    }

    private void TryEat()
    {
        var line = _pack.Eat();
        if (line is null)
        {
            Toast("Nothing to eat.");
            return;
        }

        Sounds?.Play(Sfx.Chime, weight: 0.25f);
        if (_botRunStarted) _botLog.Eats++;
        Toast(line);
    }

    private void JumpWilderness(float x, float z)
    {
        x = Math.Clamp(x, 8f, WorldScale.WorldMetres - 8f);
        z = Math.Clamp(z, 8f, WorldScale.WorldMetres - 8f);
        var y = _world.Heights.SampleWalk(x, z) + WorldScale.EyeHeight;
        if (_runner.Current is WildernessLocation)
            _returnWild = _view.Position;
        GoWilderness(new Vector3(x, y, z));
        ParkRides(new Vector3(x, _world.Heights.SampleWalk(x, z), z));
    }

    private void Swing()
    {
        Sounds?.Play(Sfx.Swing, weight: 0.45f);
        if (_hero.Bow != 0 && _hero.Arrows > 0 && LooseArrow()) return;
        if (StrikeFoe()) return;

        if (_runner.Current is BoxLocation { Kind: LocationKind.Town }
            && _hint is { Marker.Kind: MarkerKind.Talk })
        {
            _pack.Wanted = true;
            SpawnFoe(FoeKind.Watch, _hint.Value.Marker.Position);
            Sounds?.Play(Sfx.HitFlesh, weight: 0.5f);
            Toast("The watch draws.");
            return;
        }

        if (_hint is not { Marker.Kind: MarkerKind.Dummy }) return;
        if (_runner.Current is not BoxLocation box) return;
        Sounds?.Play(Sfx.HitFlesh, weight: 0.7f);
        if (box.DummyHit)
        {
            Toast("Splinters.");
            return;
        }

        box.DummyHit = true;
        _pack.Gold += 3;
        Sounds?.Play(Sfx.Coin, weight: 0.35f);
        if (_pack.FightJob == _dungeonIndex)
        {
            _pack.FightReady = true;
            Toast("The dummy comes apart. The guild will want to hear of this.");
            return;
        }

        Toast("The dummy comes apart. 3 gp in the stuffing.");
    }

    private void Use(Marker marker)
    {
        switch (marker.Kind)
        {
            case MarkerKind.EnterTown:
                EnterTown(marker.Target);
                break;
            case MarkerKind.LeaveTown:
                if (!CanPassGate()) break;
                GoWilderness(LeaveTownStand(_townIndex));
                break;
            case MarkerKind.EnterInterior:
                EnterInterior(marker.Target);
                break;
            case MarkerKind.LeaveInterior:
                _runner.Request(_world.Town(_townIndex), _returnTown, _view.Yaw);
                Sounds?.Play(Sfx.Door, weight: 0.4f);
                break;
            case MarkerKind.EnterDungeon:
                EnterDungeon(marker.Target);
                break;
            case MarkerKind.LeaveDungeon:
                GoWilderness(PadExit(_world.DungeonMouths[_dungeonIndex], 10f));
                break;
            case MarkerKind.Talk:
                if (_bot.Enabled)
                {
                    Toast(TalkLine(marker.Target));
                    break;
                }
                _talkId = marker.Target;
                _talk.Show(SpeakerName(marker), TalkLine(marker.Target),
                    BuildTalkOptions(marker.Target));
                break;
            case MarkerKind.Rest:
                TryRest(fromBed: true, innFee: marker.Target);
                break;
            case MarkerKind.Shop:
                OpenShop();
                break;
            case MarkerKind.Join:
                if (_bot.Enabled)
                {
                    TryJoin((GuildKind)marker.Target);
                    break;
                }
                OpenJoinTalk((GuildKind)marker.Target);
                break;
            case MarkerKind.Heal:
                if (_bot.Enabled)
                {
                    TryHeal();
                    break;
                }
                var bless = _pack.In(GuildKind.Mages) ? 12 : 20;
                var temple = new List<DialogueOption>
                {
                    new($"Bless me. ({bless} gp)", 4)
                };
                if (_hero.Relic)
                    temple.Add(new DialogueOption("I bring the Totem.", 23));
                temple.Add(new DialogueOption("Goodbye.", 0));
                _talk.Show("Priest of Kynareth", "Kynareth keeps the sky. What do you need?",
                    temple.ToArray());
                break;
            case MarkerKind.Loot:
                TryLoot(marker.Target != 0);
                break;
            case MarkerKind.Bank:
                if (_bot.Enabled)
                {
                    OpenBank();
                    break;
                }
                _talk.Show("Banker", "Coin here is coin in every town. Letters of credit.",
                [
                    new DialogueOption("I have business.", 6),
                    new DialogueOption("Goodbye.", 0)
                ]);
                break;
            case MarkerKind.Drink:
                TryDrink(marker.Target);
                break;
            case MarkerKind.Deed:
                TryDeed(marker.Target);
                break;
            case MarkerKind.Stash:
                TryStash(marker.Target);
                break;
            case MarkerKind.Train:
                TryTrain(marker.Target);
                break;
            case MarkerKind.Key:
                TryKey();
                break;
            case MarkerKind.LockedDoor:
                TryDoor();
                break;
            case MarkerKind.Dummy:
                Swing();
                break;
        }
    }

    private void OpenShop()
    {
        if (!_clock.ShopsOpen)
        {
            Toast("The shop is shuttered until morning.");
            return;
        }

        _shop.Open = true;
        _shop.Selected = 0;
    }

    private void OpenBank()
    {
        if (!_clock.ShopsOpen)
        {
            Toast("The banker has gone home.");
            return;
        }

        _bank.Open = true;
        _bank.Selected = 0;
    }

    private void TryBank()
    {
        switch (_bank.Selected)
        {
            case 0:
                ShiftCoin(10, toVault: true);
                break;
            case 1:
                ShiftCoin(_pack.Gold, toVault: true);
                break;
            case 2:
                ShiftCoin(10, toVault: false);
                break;
            default:
                ShiftCoin(_pack.BankGold, toVault: false);
                break;
        }
    }

    private void ShiftCoin(int amount, bool toVault)
    {
        if (amount <= 0)
        {
            Toast("Nothing to move.");
            return;
        }

        if (toVault)
        {
            amount = Math.Min(amount, _pack.Gold);
            _pack.Gold -= amount;
            _pack.BankGold += amount;
            Toast($"You leave {amount} gp with the bank.");
        }
        else
        {
            amount = Math.Min(amount, _pack.BankGold);
            _pack.BankGold -= amount;
            _pack.Gold += amount;
            Toast($"You take {amount} gp.");
        }

        Sounds?.Play(Sfx.Coin, weight: 0.45f);
    }

    private void TryDrink(int fee)
    {
        if (_pack.Gold < fee)
        {
            Toast("You haven't the coin for a drink.");
            return;
        }

        _pack.Gold -= fee;
        _pack.Hunger = MathF.Min(Ledger.NeedMax, _pack.Hunger + 22f);
        _pack.Cold = MathF.Max(0f, _pack.Cold - 14f);
        _fatigue = MathF.Min(WorldScale.FatigueMax, _fatigue + 16f);
        Sounds?.Play(Sfx.Coin, weight: 0.4f);
        Toast($"Warm ale. {TalkLine(1)}");
    }

    private void TryDeed(int price)
    {
        if (_pack.HouseTown == _townIndex)
        {
            Toast("You already keep this house.");
            return;
        }

        if (_pack.Gold < price)
        {
            Toast($"The deed is {price} gold.");
            return;
        }

        _pack.Gold -= price;
        _pack.HouseTown = _townIndex;
        Sounds?.Play(Sfx.Coin, weight: 0.5f);
        Toast($"The house in {_world.TownNames[_townIndex]} is yours.");
    }

    private void TryStash(int where)
    {
        if (where == 0 && _pack.HouseTown != _townIndex)
        {
            Toast("This is not your chest.");
            return;
        }

        var stored = where == 0 ? _pack.HouseGold : _pack.WagonGold;
        var name = where == 0 ? "chest" : "wagon";
        if (_pack.Gold > 0)
        {
            stored += _pack.Gold;
            Toast($"You put {_pack.Gold} gp in the {name}.");
            _pack.Gold = 0;
        }
        else if (stored > 0)
        {
            _pack.Gold += stored;
            Toast($"You take {stored} gp.");
            stored = 0;
        }
        else
            Toast($"The {name} is empty.");

        if (where == 0) _pack.HouseGold = stored;
        else _pack.WagonGold = stored;

        Sounds?.Play(Sfx.Coin, weight: 0.4f);
    }

    private void TryTrain(int fee)
    {
        if (!_pack.In(GuildKind.Fighters))
        {
            Toast("The yard is for members.");
            return;
        }

        if (_pack.Gold < fee)
        {
            Toast("The yard is for members who can pay.");
            return;
        }

        _pack.Gold -= fee;
        _fatigue = WorldScale.FatigueMax;
        _hero.UseSkill(SkillId.Blade, 1.2f);
        Sounds?.Play(Sfx.Chime, weight: 0.35f);
        Toast("You train until the ache leaves.");
    }

    private void TryKey()
    {
        if (!_pack.Keys.Add(_dungeonIndex))
        {
            Toast("You already have this key.");
            return;
        }

        Sounds?.Play(Sfx.Chime, weight: 0.35f);
        Toast("A cold iron key.");
    }

    private void TryDoor()
    {
        if (_pack.Doors.Contains(_dungeonIndex))
        {
            Toast("The way is open.");
            return;
        }

        if (!_pack.Keys.Contains(_dungeonIndex))
        {
            if (_pack.In(GuildKind.Thieves))
                Toast("A thief's hand. The lock yields.");
            else if (_pack.Lockpicks > 0)
            {
                _pack.Lockpicks--;
                if (!_hero.Chance(SkillId.Lockpicking, 8f))
                {
                    Sounds?.Play(Sfx.Denied, weight: 0.4f);
                    Toast("The pick snaps.");
                    return;
                }

                _hero.UseSkill(SkillId.Lockpicking);
                Toast("The pick turns.");
            }
            else
            {
                Toast("The door wants a key.");
                Sounds?.Play(Sfx.Denied, weight: 0.4f);
                return;
            }
        }

        _pack.Doors.Add(_dungeonIndex);
        if (_runner.Current is BoxLocation box)
            box.Unlock();
        Sounds?.Play(Sfx.Door, weight: 0.55f);
        Toast("The iron door swings.");
    }

    private static string SpeakerName(Marker marker) => marker.Kind switch
    {
        MarkerKind.Talk => marker.Target switch
        {
            0 => "Trader",
            1 => "Innkeep",
            2 => "Watch",
            3 => "Traveler",
            _ => "Stranger"
        },
        MarkerKind.Join => marker.Target switch
        {
            (int)GuildKind.Fighters => "Guildmaster",
            (int)GuildKind.Mages => "Magus",
            _ => "Hooded guest"
        },
        MarkerKind.Heal => "Priest of Kynareth",
        MarkerKind.Bank => "Banker",
        _ => "Stranger"
    };

    private void OpenJoinTalk(GuildKind kind)
    {
        var speaker = SpeakerName(new Marker(MarkerKind.Join, Vector3.Zero, 0f, "", (int)kind));
        var options = new List<DialogueOption>();
        if (_hero.Relic)
        {
            var deliverId = kind switch
            {
                GuildKind.Fighters => 21,
                GuildKind.Mages => 22,
                _ => 0
            };
            if (deliverId != 0)
                options.Add(new DialogueOption("I bring the Totem.", deliverId));
        }

        if (_pack.In(kind))
        {
            var work = kind switch
            {
                GuildKind.Fighters => new DialogueOption("About the work.", 9),
                GuildKind.Mages => new DialogueOption("The circle's business.", 8),
                _ => new DialogueOption("About the work.", 10)
            };
            options.Add(work);
            options.Add(new DialogueOption("Goodbye.", 0));
            _talk.Show(speaker, "You are known here.", options.ToArray());
            return;
        }

        var fee = kind switch
        {
            GuildKind.Fighters => 10,
            GuildKind.Mages => 20,
            _ => 8
        };
        var joinId = kind switch
        {
            GuildKind.Fighters => 1,
            GuildKind.Mages => 2,
            _ => 3
        };
        options.Add(new DialogueOption($"I would join. ({fee} gp)", joinId));
        options.Add(new DialogueOption("Goodbye.", 0));
        _talk.Show(speaker, "The hall is open to those who pay and keep their word.",
            options.ToArray());
    }

    private void TryJoin(GuildKind kind)
    {
        if (_pack.In(kind))
        {
            switch (kind)
            {
                case GuildKind.Fighters:
                    TryFighterJob();
                    return;
                case GuildKind.Mages:
                    if (_pack.MageLetter && _pack.MageJob == _townIndex)
                    {
                        FinishMageJob();
                        return;
                    }

                    _mage.Open = true;
                    _mage.Selected = 0;
                    return;
                default:
                    TryThiefJob();
                    return;
            }
        }

        var fee = kind switch
        {
            GuildKind.Fighters => 10,
            GuildKind.Mages => 20,
            GuildKind.Thieves => 8,
            _ => 10
        };
        if (_pack.Gold < fee)
        {
            Toast($"Membership is {fee} gold.");
            return;
        }

        _pack.Gold -= fee;
        _pack.Guild[(int)kind] = true;
        _hero.Join((FactionId)kind);
        Sounds?.Play(Sfx.Chime, weight: 0.5f);
        Toast(kind switch
        {
            GuildKind.Fighters => "The Fighters Guild takes your name.",
            GuildKind.Mages => "The Mages Guild marks you as their own.",
            GuildKind.Thieves => "A nod. The stalls will cost you less.",
            _ => "You are in."
        });
    }

    private void TryHeal()
    {
        var fee = _pack.In(GuildKind.Mages) ? 12 : 20;
        if (_pack.Gold < fee)
        {
            Toast($"The temple asks {fee} gold.");
            return;
        }

        _pack.Gold -= fee;
        _fatigue = WorldScale.FatigueMax;
        _pack.Health = HpMax;
        var cured = _pack.Cure();
        Sounds?.Play(Sfx.Chime, weight: 0.5f);
        Toast(cured ? "The blessing takes the fever." : "The ache leaves you.");
    }

    private void TryLoot(bool locked)
    {
        if (locked && !_pack.In(GuildKind.Thieves))
        {
            if (_pack.Lockpicks <= 0)
            {
                Toast("The lock holds.");
                return;
            }

            _pack.Lockpicks--;
            if (!_hero.Chance(SkillId.Lockpicking, 8f))
            {
                Sounds?.Play(Sfx.Denied, weight: 0.4f);
                Toast("The pick snaps.");
                return;
            }

            _hero.UseSkill(SkillId.Lockpicking);
            Toast("The pick turns.");
        }

        if (!_pack.Looted.Add(_dungeonIndex))
        {
            Toast("The chest is empty.");
            return;
        }

        if (_pack.ThiefJob == _dungeonIndex)
            _pack.ThiefReady = true;

        var take = 12 + Math.Abs(_dungeonIndex * 17 + _seed) % 29;
        _pack.Gold += take;
        Sounds?.Play(Sfx.Coin, weight: 0.55f);
        if (_dungeonIndex == _hero.RelicDungeon && !_hero.Relic)
        {
            Toast($"{QuestBook.TakeRelic(_hero)}  {take} gp.");
            return;
        }

        Toast(_pack.ThiefReady
            ? $"You take {take} gold. The guest at the inn will want this purse."
            : $"You take {take} gold.");
    }

    private bool EnterTown(int index, bool force = false)
    {
        if (index < 0 || index >= _world.TownCount) return false;
        if (EarthPlaces.IsMark(index))
        {
            var pad = _world.TownPads[index];
            JumpWilderness(pad.X, pad.Z);
            return false;
        }
        if (!force && !CanPassGate()) return false;
        if (_pack.Wanted && !_pack.In(GuildKind.Thieves))
        {
            var fine = Math.Min(_pack.Gold, Math.Clamp((int)(_pack.Gold * 0.18f), 8, 20));
            _pack.Gold -= fine;
            _pack.Wanted = false;
            _foes.Clear();
            if (_runner.Current is WildernessLocation)
                Toast($"The watch takes {fine} gp and turns you from the gate.");
            else
                Toast($"The watch takes {fine} gp and puts you out.");
            GoWilderness(LeaveTownStand(index));
            _townIndex = index;
            return false;
        }

        if (_pack.Wanted && _pack.In(GuildKind.Thieves))
            Toast("The watch looks past you.");

        if (_mounted) Dismount(null);
        _townIndex = index;
        if (_runner.Current is WildernessLocation)
            _returnWild = _view.Position;
        var town = _world.Town(index);
        _runner.Request(town, town.Spawn, yaw: 0f);
        Sounds?.Play(Sfx.Door, weight: 0.45f);
        if (_botRunStarted)
        {
            _botLog.Towns++;
            _botLog.Event("town", _world.TownNames[index], _sight);
        }
        return true;
    }

    private bool EnterDungeon(int index)
    {
        if (index < 0 || index >= _world.DungeonCount) return false;
        if (_mounted) Dismount(null);
        _dungeonIndex = index;
        if (_runner.Current is WildernessLocation)
            _returnWild = _view.Position;
        var dungeon = _world.Dungeon(index);
        _runner.Request(dungeon, dungeon.Spawn, yaw: MathF.PI);
        Sounds?.Play(Sfx.Door, weight: 0.5f);
        if (_botRunStarted)
        {
            _botLog.Dungeons++;
            _botLog.Event("dungeon", _world.DungeonNames[index], _sight);
        }
        return true;
    }

    private void EnterInterior(int index)
    {
        var rooms = _world.InteriorsFor(_townIndex);
        if (index < 0 || index >= rooms.Length) return;
        if (index == 0 && !_clock.ShopsOpen)
        {
            Toast("The shop is shuttered until morning.");
            return;
        }
        _roomIndex = index;
        _returnTown = _view.Position;
        _runner.Request(rooms[index], rooms[index].Spawn, yaw: 0f);
        Sounds?.Play(Sfx.Door, weight: 0.4f);
    }

    private void GoWilderness(Vector3 position)
    {
        _runner.Request(_world.Wilderness, position, yaw: 0f);
        Sounds?.Play(Sfx.Door, weight: 0.35f);
    }

    private Vector3 PadExit(Vector3 pad, float south)
    {
        if (DryStand(pad.X, pad.Z + south, out var southExit)) return southExit;

        for (var t = 0; t < 72; t++)
        {
            var angle = t * 0.61803399f * MathF.Tau;
            var radius = 14f + t * 7f;
            var x = pad.X + MathF.Sin(angle) * radius;
            var z = pad.Z + MathF.Cos(angle) * radius;
            if (DryStand(x, z, out var at)) return at;
        }

        var fx = pad.X;
        var fz = Math.Clamp(pad.Z + south, 8f, WorldScale.WorldMetres - 8f);
        return new Vector3(fx, _world.Heights.SampleWalk(fx, fz) + WorldScale.EyeHeight, fz);
    }

    private Vector3 LeaveTownStand(int index) =>
        PadExit(_world.Wilderness.TownGate(index), 8f);

    private bool DryStand(float x, float z, out Vector3 position)
    {
        x = Math.Clamp(x, 8f, WorldScale.WorldMetres - 8f);
        z = Math.Clamp(z, 8f, WorldScale.WorldMetres - 8f);
        position = default;
        if (!WorldGenerator.IsHabitable(_world.Noise, x, z)) return false;
        if (_world.Heights.Sample(x, z) < WorldScale.WaterLevel + 1f) return false;
        position = new Vector3(x, _world.Heights.SampleWalk(x, z) + WorldScale.EyeHeight, z);
        return true;
    }

    private void RescueUnderMap()
    {
        if (_runner.Current is not WildernessLocation) return;
        if (_view.NoClip || _view.Swimming) return;
        var p = _view.Position;
        var minEye = WorldScale.WaterLevel + 0.35f;
        if (p.Y >= minEye) return;
        var y = _world.Heights.SampleWalk(p.X, p.Z) + WorldScale.EyeHeight;
        _view.Place(new Vector3(p.X, MathF.Max(y, minEye + WorldScale.EyeHeight), p.Z));
    }

    private void OnLocation(string name)
    {
        if (_runner.Current is not WildernessLocation)
        {
            _mounted = false;
            _wagonRide = false;
        }
        else if (!_mounted)
            ParkRides(_view.Position);
        _shop.Open = false;
        _bank.Open = false;
        _mage.Open = false;
        _inv.Open = false;
        _talk.Close();
        _pause.Close();
        _sheet.Open = false;
        _spellUi.Open = false;
        _journal.Open = false;
        if (_runner.Current is not BoxLocation { Kind: LocationKind.Dungeon })
            _autoMap = false;
        if (_runner.Current is not WildernessLocation)
        {
            _foes.Clear();
            _campLit = false;
        }

        if (_runner.Current is BoxLocation { Kind: LocationKind.Dungeon } delve
            && _pack.Doors.Contains(_dungeonIndex))
            delve.Unlock();

        ApplyMount();
        var bed = _runner.Current switch
        {
            WildernessLocation => LocationBed.Wilderness,
            BoxLocation { Kind: LocationKind.Dungeon } => LocationBed.Dungeon,
            _ => LocationBed.Town
        };
        _beds.SetTarget(bed);
        if (_runner.Current is WildernessLocation wild)
            ApplyFog(_weather.TintFog(_clock.TintFog(wild.FogColour)));
        else
            ApplyFog(_runner.Current.ClearColour);
        Log(name);
    }

    private void UpdateWater()
    {
        if (_runner.Current is not WildernessLocation)
        {
            _view.Swimming = false;
            _wasSwimming = false;
            return;
        }

        var p = _view.Position;
        var bed = _world.Heights.Sample(p.X, p.Z);
        var deep = WorldScale.WaterLevel - bed > WorldScale.WadeDepth;
        if (deep && _mounted)
            Dismount(_wagonRide ? "The team will not take the water." : "The horse will not take the water.");

        var swim = deep && !_mounted;
        if (swim && !_wasSwimming)
        {
            Sounds?.Play(Sfx.Land, weight: 0.4f, volumeScale: 0.55f);
            Toast("You swim.");
            _pack.Wet = Ledger.NeedMax;
            _pack.Cold = MathF.Min(Ledger.NeedMax, _pack.Cold + 18f);
            TryMarshSick();
        }
        else if (!swim && _wasSwimming)
            Toast("You find your feet.");

        _view.Swimming = swim;
        _wasSwimming = swim;
    }

    private void ToggleMount()
    {
        if (_runner.Current is not WildernessLocation)
        {
            Toast("No mount indoors.");
            return;
        }

        if (_view.Swimming)
        {
            Toast("Not from the water.");
            return;
        }

        if (_mounted && !_wagonRide && _pack.HasWagon)
        {
            ParkOffset(_view.Position, 2.8f, out _horseAt, out _horseYaw);
            _wagonRide = true;
            ApplyMount();
            Sounds?.Play(Sfx.Land, weight: 0.35f);
            Toast("You climb into the wagon.");
            return;
        }

        if (_mounted)
        {
            Dismount("You dismount.");
            return;
        }

        _mounted = true;
        _wagonRide = false;
        ApplyMount();
        Sounds?.Play(Sfx.Land, weight: 0.35f);
        Toast("You mount.");
    }

    private void Dismount(string? line)
    {
        if (!_mounted) return;
        _mounted = false;
        _wagonRide = false;
        ApplyMount();
        ParkRides(_view.Position);
        if (line is not null) Toast(line);
    }

    private void ApplyMount()
    {
        _view.Mounted = _mounted;
        _view.Wagon = _mounted && _wagonRide;
        if (!_mounted)
        {
            _view.StandingEyeY = WorldScale.EyeHeight;
            _view.CollisionRadius = 0.38f;
        }
        else if (_wagonRide)
        {
            _view.StandingEyeY = WorldScale.WagonEye;
            _view.CollisionRadius = WorldScale.WagonRadius;
        }
        else
        {
            _view.StandingEyeY = WorldScale.HorseEye;
            _view.CollisionRadius = WorldScale.HorseRadius;
        }
    }

    private void ParkRides(Vector3 near)
    {
        ParkOffset(near, 2.8f, out _horseAt, out _horseYaw);
        if (_pack.HasWagon)
            ParkOffset(near, -3.4f, out _wagonAt, out _wagonYaw);
    }

    private void ParkOffset(Vector3 near, float side, out Vector3 at, out float yaw)
    {
        yaw = _view.Yaw + 1.1f;
        var right = new Vector3(MathF.Cos(_view.Yaw), 0f, MathF.Sin(_view.Yaw));
        var x = near.X + right.X * side;
        var z = near.Z + right.Z * side;
        if (DryStand(x, z, out var dry))
        {
            at = new Vector3(dry.X, _world.Heights.SampleWalk(dry.X, dry.Z), dry.Z);
            return;
        }

        x = Math.Clamp(near.X, 8f, WorldScale.WorldMetres - 8f);
        z = Math.Clamp(near.Z, 8f, WorldScale.WorldMetres - 8f);
        at = new Vector3(x, _world.Heights.SampleWalk(x, z), z);
    }

    private void DrawHorse()
    {
        if (_runner.Current is not WildernessLocation) return;

        var fog = WorldScale.FogEnd * WorldScale.FogEnd;
        Billboards.Begin(_view.View, _view.Projection);

        if (_mounted && !_wagonRide)
        {
            var yaw = _view.Yaw;
            var lookX = MathF.Sin(yaw);
            var lookZ = -MathF.Cos(yaw);
            var ground = _world.Heights.SampleWalk(_view.Position.X, _view.Position.Z);
            var at = new Vector3(
                _view.Position.X + lookX * 0.8f,
                ground,
                _view.Position.Z + lookZ * 0.8f);
            Billboards.Draw(_sprites.Get("horse-ride", yaw, yaw), at, 2.28f, yaw, Color.White);
        }
        else
        {
            var dx = _horseAt.X - _view.Position.X;
            var dz = _horseAt.Z - _view.Position.Z;
            if (dx * dx + dz * dz < fog)
            {
                var texture = _sprites.Get("horse", _view.Yaw, _horseYaw);
                Billboards.Draw(texture, _horseAt, 2.2f, _view.Yaw, Color.White);
            }
        }

        var showWagon = _pack.HasWagon && !_wagonRide;
        if (!showWagon) return;

        var wx = _wagonAt.X - _view.Position.X;
        var wz = _wagonAt.Z - _view.Position.Z;
        if (wx * wx + wz * wz < fog)
            Billboards.Draw(_sprites.Get("wagon", _view.Yaw, _wagonYaw),
                _wagonAt, 2.45f, _view.Yaw, Color.White);
    }

    private void TickFatigue(float seconds, float metres, bool sprint)
    {
        if (metres > 0.001f)
        {
            var rate = _view.Swimming ? 0.16f
                : _view.WantClimb ? 0.22f
                : _wagonRide ? 0.018f
                : _mounted ? 0.032f
                : sprint ? 0.12f
                : 0.048f;
            _fatigue -= metres * rate * _pack.FatigueMul;
        }
        else
            _fatigue += _pack.CanRegen
                ? seconds * (_pack.Ailment == Ailment.None ? 4.2f : 1.6f)
                : -seconds * 1.15f;

        _fatigue = MathHelper.Clamp(_fatigue, 0f, WorldScale.FatigueMax);
        if (_fatigue <= 0.05f)
        {
            if (!_warnedTired)
            {
                Toast("You are exhausted.");
                _warnedTired = true;
            }
        }
        else
            _warnedTired = false;
    }

    private void TryRest(bool fromBed, int innFee)
    {
        if (_restFade > 0.04f || _runner.IsFading) return;
        if (_view.Swimming)
        {
            Toast("Not in the water.");
            return;
        }

        if (_mounted) Dismount(null);

        var current = _runner.Current;
        float hours;
        var fee = 0;
        string line;

        if (fromBed)
        {
            if (innFee == 0 && _roomIndex == 2 && _pack.HouseTown != _townIndex)
            {
                Toast("This is not your house.");
                return;
            }

            hours = _clock.HoursUntilMorning();
            fee = innFee > 0 ? 8 : 0;
            line = fee > 0 ? "You take a room until morning." : "You sleep until morning.";
        }
        else if (current is WildernessLocation)
        {
            if (!CampNear())
            {
                PlaceCamp();
                return;
            }

            hours = 6f;
            line = _clock.IsNight
                ? "You sleep by the fire. Something sniffs the dark."
                : "You rest by the fire.";
        }
        else if (current is BoxLocation { Kind: LocationKind.Dungeon })
        {
            hours = 4f;
            line = "You sleep with one eye open.";
        }
        else if (current is BoxLocation { Kind: LocationKind.Interior })
        {
            hours = _clock.HoursUntilMorning();
            line = "You rest behind a closed door.";
        }
        else
        {
            Toast("Not in the street.");
            return;
        }

        if (fee > 0 && _pack.Gold < fee)
        {
            Toast("You haven't the coin for a room.");
            return;
        }

        var ate = false;
        if (current is WildernessLocation && _pack.Rations > 0)
        {
            _pack.Rations--;
            ate = true;
            line += " You eat.";
        }

        _pack.Gold -= fee;
        _clock.AddHours(hours);
        _wellRested = MathF.Max(0f, _wellRested - hours);
        if (fromBed)
        {
            _pack.Cold = MathF.Min(_pack.Cold, 10f);
            _pack.Wet = 0f;
            if (fee > 0)
                _pack.Hunger = MathF.Min(Ledger.NeedMax, _pack.Hunger + 18f);
            if (_pack.Hunger > 40f && _pack.Cold < 35f)
                _wellRested = 8f;
        }

        var cap = WorldScale.FatigueMax;
        if (!fromBed)
        {
            if (_pack.Cold > 60f) cap = 58f;
            if (_pack.Hunger < 25f) cap = MathF.Min(cap, 62f);
            if (!ate) _pack.Hunger = MathF.Max(0f, _pack.Hunger - hours * 1.4f);
        }

        _fatigue = cap;
        _pack.Health = MathF.Min(HpMax,
            _pack.Health + (fromBed ? 48f : CampNear() ? 22f : 10f));
        _hero.Magicka = MathF.Min(_hero.MagickaMax,
            _hero.Magicka + (fromBed ? 36f : 10f));
        _restFade = 1f;
        Sounds?.Play(Sfx.Chime, weight: 0.4f);
        if (_botRunStarted)
        {
            _botLog.Rests++;
            _botLog.Event("rest", fromBed ? "bed" : CampNear() ? "camp" : "wait", _sight);
        }
        var sick = TryInfectAfterRest(fromBed, ate);
        if (!fromBed && _pack.Cold > 72f && _pack.Ailment == Ailment.None && _dice.NextDouble() < 0.14)
        {
            _pack.Infect(Ailment.Chill);
            sick ??= "A chill settles in the bones.";
        }

        Toast(sick is null ? $"{line}  {_clock.Stamp}" : $"{line}  {sick}  {_clock.Stamp}");
        if (fromBed) WriteSave();
    }

    private string? TryInfectAfterRest(bool fromBed, bool ate)
    {
        if (fromBed) return null;
        if (_runner.Current is BoxLocation { Kind: LocationKind.Interior }) return null;

        Ailment next;
        var chance = 0f;
        if (_runner.Current is BoxLocation { Kind: LocationKind.Dungeon })
        {
            next = Ailment.WoundFever;
            chance = 0.22f;
        }
        else if (_runner.Current is WildernessLocation)
        {
            var biome = _world.Noise.BiomeAt(_view.Position.X, _view.Position.Z);
            if (biome == BiomeKind.Marsh)
            {
                next = Ailment.SwampRot;
                chance = 0.24f;
            }
            else if (_clock.IsNight)
            {
                next = Ailment.Chill;
                chance = 0.12f;
            }
            else
            {
                next = Ailment.Chill;
                chance = 0.04f;
            }
        }
        else
            return null;

        if (ate) chance *= 0.5f;
        if (_dice.NextDouble() > chance) return null;
        if (!_pack.Infect(next)) return null;
        return next switch
        {
            Ailment.SwampRot => "The marsh is in you.",
            Ailment.WoundFever => "Fever takes you.",
            Ailment.Chill => "A chill settles in the bones.",
            _ => null
        };
    }

    private void TryMarshSick()
    {
        if (_world.Noise.BiomeAt(_view.Position.X, _view.Position.Z) != BiomeKind.Marsh) return;
        if (_dice.NextDouble() > 0.12) return;
        if (_pack.Infect(Ailment.SwampRot))
            Toast("The fen water is foul.");
    }

    private void TickWeather()
    {
        var biome = CurrentBiome();
        _weather.Sync(_seed, _clock.Day, _clock.Hours, biome);
        if (!_weatherReady)
        {
            _seenWeather = _weather.Kind;
            _weatherReady = true;
            return;
        }
        if (_weather.Kind == _seenWeather) return;
        _seenWeather = _weather.Kind;
        if (_runner.Current is WildernessLocation || _runner.Current is BoxLocation { Kind: LocationKind.Town })
            Toast($"The sky turns: {_weather.Label}.");
    }

    private BiomeKind CurrentBiome()
    {
        if (_runner.Current is BoxLocation)
            return _world.TownBiomes[Math.Clamp(_townIndex, 0, _world.TownCount - 1)];
        return _world.Noise.BiomeAt(_view.Position.X, _view.Position.Z);
    }

    private bool Indoor =>
        _runner.Current is BoxLocation { Kind: LocationKind.Interior or LocationKind.Dungeon };

    private float AmbientCold(Vector3? at = null)
    {
        var p = at ?? _view.Position;
        var biome = _world.Noise.BiomeAt(p.X, p.Z);
        var cold = biome switch
        {
            BiomeKind.Snow => 72f,
            BiomeKind.Mountain => 58f,
            BiomeKind.Hills => 28f,
            BiomeKind.Desert => _clock.IsNight ? 36f : 6f,
            BiomeKind.Marsh => 32f,
            BiomeKind.Forest => 22f,
            BiomeKind.Ocean or BiomeKind.Coast => 24f,
            _ => 16f
        };
        if (_clock.IsNight) cold += 14f;
        cold += _weather.ColdBias;
        if (Indoor) cold = _runner.Current is BoxLocation { Kind: LocationKind.Dungeon } ? 22f : 4f;
        else if (_runner.Current is BoxLocation { Kind: LocationKind.Town }) cold *= 0.55f;
        if (_wagonRide) cold -= 10f;
        else if (_mounted) cold -= 4f;
        if (_pack.HasCloak) cold -= 14f;
        if (_hero.Race == RaceId.Nord) cold -= 12f;
        if (CampNear(p)) cold -= 32f;
        cold += _pack.Wet * 0.28f;
        return MathHelper.Clamp(cold, 0f, 95f);
    }

    private void TickSurvival(float seconds, float metres, bool sprint)
    {
        var hours = seconds * WorldScale.GameSecondsPerReal / 3600f;
        var drain = Indoor ? WorldScale.HungerIndoor
            : sprint ? WorldScale.HungerSprint
            : metres > 0.001f ? WorldScale.HungerWalk
            : WorldScale.HungerIdle;
        _pack.Hunger = MathF.Max(0f, _pack.Hunger - hours * drain);

        var target = AmbientCold();
        _pack.Cold += (target - _pack.Cold) * MathF.Min(1f, hours * 1.6f);

        var exposed = !Indoor && _weather.Wetting;
        if (_view.Swimming) _pack.Wet = Ledger.NeedMax;
        else if (exposed) _pack.Wet = MathF.Min(Ledger.NeedMax, _pack.Wet + hours * 18f);
        else
        {
            var dry = Indoor ? 48f : 9f;
            _pack.Wet = MathF.Max(0f, _pack.Wet - hours * dry);
        }

        _pack.ClampNeeds();

        if (_pack.Hunger < 14f)
        {
            if (!_warnedHungry)
            {
                Toast("You are starving.");
                _warnedHungry = true;
            }
        }
        else
            _warnedHungry = false;

        if (_pack.Cold > 78f)
        {
            if (!_warnedCold)
            {
                Toast("You are freezing.");
                _warnedCold = true;
            }
        }
        else
            _warnedCold = false;
    }

    private float RoadHours(Vector3 from, Vector3 dest) =>
        Travel.Hours(from, dest, _pack.HasShip,
            Travel.Coastal(_world.Noise, from), Travel.Coastal(_world.Noise, dest));

    private void TakeRoad(float hours, Vector3 dest)
    {
        _clock.AddHours(hours);
        _wellRested = MathF.Max(0f, _wellRested - hours);
        _pack.RoadHours(hours, AmbientCold(dest));
        _fatigue = MathF.Max(8f, _fatigue - hours * WorldScale.RoadFatiguePerHour);
        if (_mounted) Dismount(null);
        Sounds?.Play(Sfx.Door, weight: 0.3f);
        _campLit = false;
        if (_botRunStarted)
            _botLog.Event("road", $"{hours:0.0}h", _sight);
        if (hours > WorldScale.RoadAmbushHours && _dice.NextDouble() < WorldScale.RoadAmbushChance)
            Ambush(dest);
    }

    private void ArriveGuild(int town)
    {
        _townIndex = town;
        _roomIndex = 4;
        var rooms = _world.InteriorsFor(town);
        _runner.Request(rooms[4], rooms[4].Spawn, yaw: 0f);
        Sounds?.Play(Sfx.Chime, weight: 0.45f);
    }

    private bool CanPassGate()
    {
        if (!_clock.IsNight) return true;
        if (_pack.In(GuildKind.Thieves) || _view.WantClimb) return true;
        Toast("The gate is shut until morning. Hold V to climb the wall, or wait.");
        return false;
    }

    private void MaybeWagonHint()
    {
        if (_hint is not null) return;
        if (!_pack.HasWagon || _wagonRide || _runner.Current is not WildernessLocation) return;
        var dx = _wagonAt.X - _view.Position.X;
        var dz = _wagonAt.Z - _view.Position.Z;
        if (dx * dx + dz * dz > 9f) return;
        _hint = new LookHint("Open the wagon", PromptRole.Pocket,
            new Marker(MarkerKind.Stash, _wagonAt, 2.4f, "Open the wagon", 1));
    }

    private int JobDungeon()
    {
        var n = Math.Abs(_townIndex * 19 + _clock.Day * 5 + _seed) % Math.Max(1, _world.DungeonCount);
        return n;
    }

    private int JobTown()
    {
        var n = (_townIndex + 1 + _clock.Day % 5) % Math.Max(1, _world.TownCount);
        if (n == _townIndex) n = (n + 3) % _world.TownCount;
        return n;
    }

    private void TryFighterJob()
    {
        if (_pack.FightReady)
        {
            _pack.FightReady = false;
            _pack.FightJob = -1;
            _pack.Gold += 28;
            Sounds?.Play(Sfx.Coin, weight: 0.5f);
            Toast("The guildmaster nods. 28 gp for the work.");
            return;
        }

        if (_pack.FightJob >= 0)
        {
            Toast($"The dummy still stands in {_world.DungeonNames[_pack.FightJob]}.");
            return;
        }

        _pack.FightJob = JobDungeon();
        _pack.FightReady = false;
        Toast($"A man left a dummy in {_world.DungeonNames[_pack.FightJob]}. Break it.");
    }

    private void TryThiefJob()
    {
        if (_pack.ThiefReady)
        {
            _pack.ThiefReady = false;
            _pack.ThiefJob = -1;
            _pack.Gold += 22;
            Sounds?.Play(Sfx.Coin, weight: 0.5f);
            Toast("The guest takes the purse. 22 gp, and no questions.");
            return;
        }

        if (_pack.ThiefJob >= 0)
        {
            Toast($"The locked chest is in {_world.DungeonNames[_pack.ThiefJob]}.");
            return;
        }

        _pack.ThiefJob = JobDungeon();
        _pack.ThiefReady = false;
        Toast($"A locked chest in {_world.DungeonNames[_pack.ThiefJob]}. Bring me what it holds.");
    }

    private void FinishMageJob()
    {
        _pack.MageLetter = false;
        _pack.MageJob = -1;
        _pack.Gold += 20;
        Sounds?.Play(Sfx.Chime, weight: 0.45f);
        Toast("The letter is delivered. 20 gp from the circle.");
    }

    private void TryMage()
    {
        switch (_mage.Selected)
        {
            case 0:
                _pack.HasMark = true;
                _pack.MarkTown = _townIndex;
                Toast("You mark this hall.");
                break;
            case 1:
                if (!_pack.HasMark)
                {
                    Toast("No mark is set.");
                    return;
                }

                _mage.Open = false;
                TakeRoad(0.4f, _world.TownPads[_pack.MarkTown]);
                ArriveGuild(_pack.MarkTown);
                Toast($"Recall.  {_clock.Stamp}");
                break;
            case 2:
                _mage.Open = false;
                _mageTravel = true;
                _map.Open = true;
                Toast("Name a town. The circle will send you to its hall.");
                break;
            case 3:
                if (_pack.MageLetter && _pack.MageJob == _townIndex)
                {
                    FinishMageJob();
                    return;
                }

                if (_pack.MageLetter)
                {
                    Toast($"The letter is for {_world.TownNames[_pack.MageJob]}.");
                    return;
                }

                _pack.MageJob = JobTown();
                _pack.MageLetter = true;
                Toast($"Take this letter to the hall in {_world.TownNames[_pack.MageJob]}.");
                break;
            default:
                if (_pack.Ailment == Ailment.None)
                {
                    Toast("The circle remembers you.");
                    return;
                }

                if (_pack.Gold < 10)
                {
                    Toast("The working costs ten gold.");
                    return;
                }

                _pack.Gold -= 10;
                _pack.Cure();
                Sounds?.Play(Sfx.Chime, weight: 0.45f);
                Toast("The magus burns the sickness out.");
                break;
        }
    }

    private string TalkLine(int id)
    {
        var here = _world.TownNames[Math.Clamp(_townIndex, 0, _world.TownCount - 1)];
        var road = _world.TownNames[(_townIndex * 17 + 3 + _clock.Day) % _world.TownCount];
        var delve = _world.DungeonNames[(_townIndex * 9 + _clock.Day * 2) % _world.DungeonCount];
        return id switch
        {
            0 when !_clock.ShopsOpen => "We're shut. Dawn to dusk, same as my father.",
            0 => $"Wool and iron. If you ride for {road}, leave before dusk.",
            1 when _clock.IsNight => $"Beds are made. {here} sleeps. They say {delve} still holds a chest.",
            1 => $"A room is eight gold. A drink is four. Folk out of {road} talk of {delve}.",
            2 => _clock.IsNight
                ? "Keep to the lamp. The gate is shut for a reason. Climb if you must."
                : $"{here} holds. Keep to the road after dark. I heard {delve} from a drover.",
            3 => _clock.IsNight
                ? $"The marsh road to {road} is no place after dark."
                : $"I'm for {road} at first light. They say {delve} paid a man last month.",
            _ => "A long walk, a closed door, a warm room. That is the country."
        };
    }

    private void Toast(string line)
    {
        _toast = line;
        _toastLife = 4.2f;
        if (_botRunStarted) _botLog.Toast(line);
    }

    private void Log(string line)
    {
        _log.Add(line);
        if (_log.Count > 24) _log.RemoveAt(0);
    }

    private void HandleConsole(ConsoleAction action)
    {
        switch (action)
        {
            case ConsoleAction.Submit when _consoleInput.Buffer.Length > 0:
                foreach (var line in _console.Execute(_consoleInput.Buffer))
                    Log(line.Text);
                _consoleInput.Clear();
                break;
            case ConsoleAction.Complete:
                var matches = _console.Complete(_consoleInput.Buffer);
                if (matches.Count == 1) _consoleInput.Buffer = matches[0] + " ";
                break;
            case ConsoleAction.HistoryUp:
                _consoleInput.WalkHistory(_console.History, -1);
                break;
            case ConsoleAction.HistoryDown:
                _consoleInput.WalkHistory(_console.History, 1);
                break;
        }
    }

    private void QueueScript(IEnumerable<string> lines)
    {
        foreach (var statement in ConsoleRouter.ReadScript(lines))
            _script.Enqueue(statement);
    }

    private void PumpScript()
    {
        if (_script.Count == 0 || _wait > 0f || _runner.IsFading || _restFade > 0.04f) return;
        var line = _script.Dequeue();
        foreach (var output in _console.Execute(line))
            Log(output.Text);
    }

    protected override void Draw(GameTime gameTime)
    {
        var cave = _runner.Current is BoxLocation { Kind: LocationKind.Dungeon };
        var clear = _clock.TintClear(_runner.Current.ClearColour, cave);
        if (!cave)
            clear = _weather.TintSky(clear);
        GraphicsDevice.Clear(clear);
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        var lights = SceneLights();
        _scene.Begin(LitEffect, _view.View, _view.Projection, _view.Position,
            _view.Yaw, _runner.Current.Palette, lights);

        DrawHorizonSky();
        if (_runner.Current is WildernessLocation)
        {
            var fog = _weather.TintFog(_clock.TintFog(
                _world.Wilderness.FogColour));
            _world.Terrain.Draw(_view.View, _view.Projection, _view.Position, fog.ToVector3());
            _water.Draw(_view.View, _view.Projection, _view.Position, fog.ToVector3(),
                (float)gameTime.TotalGameTime.TotalSeconds);
        }

        _runner.Current.Draw(_scene, Billboards, _view, _sprites);
        DrawHorse();
        DrawCampAndFoes();

        _ui.Begin();
        DrawHud();
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: _script.Count > 0 || _wait > 0f || _runner.IsFading || _restFade > 0.04f,
            exit: Exit);
    }

    private void DrawHorizonSky()
    {
        if (_runner.Current is BoxLocation { Kind: LocationKind.Interior or LocationKind.Dungeon })
            return;

        Color sky;
        Color fog;
        if (_runner.Current is WildernessLocation wild)
        {
            sky = _weather.TintSky(_clock.TintSky(wild.ClearColour));
            fog = _weather.TintFog(_clock.TintFog(wild.FogColour));
        }
        else
        {
            var biome = CurrentBiome();
            sky = _weather.TintSky(_clock.TintSky(Biomes.Sky(biome)));
            fog = _weather.TintFog(_clock.TintFog(Biomes.Horizon(biome)));
        }

        _sky.Draw(_view.View, _view.Projection, _view.Position, sky, fog);
    }

    private void DrawHud()
    {
        if (_view.Swimming && _view.Position.Y < WorldScale.WaterLevel - 0.12f)
            _ui.Fill(UiLayout.FullScreen, new Color(8, 36, 58, 92));
        else if (_runner.Current is WildernessLocation && _clock.NightVeil > 4)
            _ui.Fill(UiLayout.FullScreen, new Color((byte)6, (byte)8, (byte)22, _clock.NightVeil));

        if (!Indoor && _weather.Veil > 0)
        {
            var rain = _weather.Kind == WeatherKind.Snow
                ? new Color((byte)210, (byte)218, (byte)228, _weather.Veil)
                : new Color((byte)40, (byte)52, (byte)62, _weather.Veil);
            _ui.Fill(UiLayout.FullScreen, rain);
        }

        if (_hurtFlash > 0.02f)
            _ui.Fill(UiLayout.FullScreen, new Color((byte)90, (byte)12, (byte)12,
                (byte)Math.Clamp((int)(_hurtFlash * 90f), 0, 90)));

        var overlayHidesHud = _shop.Open || _bank.Open || _mage.Open || _inv.Open
            || _pause.Open || _create.Open || _sheet.Open || _spellUi.Open || _journal.Open;
        var playHud = !_map.Open && !_consoleInput.Open && !overlayHidesHud;
        if (playHud)
        {
            var stance = _view.Swimming ? "swim" : _wagonRide ? "wagon" : _mounted ? "horse"
                : _spellHide > 0f ? "hidden" : _view.Crouching ? "sneak" : "";
            var papers = string.Concat(
                _pack.In(GuildKind.Fighters) ? " Fg" : "",
                _pack.In(GuildKind.Mages) ? " Mg" : "",
                _pack.In(GuildKind.Thieves) ? " Th" : "");
            var status = string.Concat(
                _pack.HungerName, "  ", _pack.ColdName, papers,
                _pack.Wanted ? "  wanted" : "",
                _pack.Ailment != Ailment.None ? "  " + _pack.AilmentName : "",
                _hero.Encumbrance(_pack.Gold) > 0.85f ? "  encumbered" : "");
            PlayHud.Draw(_ui, _compass, _view.Yaw, _runner.Current.Name, stance,
                _clock.Stamp, _weather.Label, _hero, _pack,
                _pack.Health / HpMax,
                _fatigue / WorldScale.FatigueMax,
                _hero.MagickaMax < 1f ? 0f : _hero.Magicka / _hero.MagickaMax,
                _pack.Hunger / Ledger.NeedMax,
                status, CompassMarks(), _bot.Enabled, _bot.Enabled ? _bot.GoalName : "");
            _ui.Fill(new Rectangle(638, 358, 4, 4), new Color(236, 226, 204, 200));
        }

        if (_toastLife > 0f)
        {
            HudChrome.Wood(_ui, HudLayout.Toast);
            _ui.Fill(new Rectangle(HudLayout.Toast.X + 6, HudLayout.Toast.Y + 6,
                HudLayout.Toast.Width - 12, HudLayout.Toast.Height - 12), UiTheme.Parchment);
            _ui.TextFit(_toast, new Vector2(HudLayout.Toast.X + 16, HudLayout.Toast.Y + 14),
                HudLayout.Toast.Width - 32, 16, UiTheme.Heading);
        }

        if (_hint is { } hint && !_talk.Open && !_inv.Open && !_pause.Open && !_map.Open
            && !_autoMap && !_create.Open && !_sheet.Open && !_spellUi.Open && !_journal.Open)
        {
            var state = new PromptState(new[]
            {
                new PromptChip(hint.Line, UiLayout.SinglePrompt, hint.Role)
            });
            _prompts.Draw(state);
        }

        if (_consoleInput.Open)
        {
            _ui.Fill(new Rectangle(24, 430, 820, 250), UiTheme.Panel);
            _ui.DoubleBorder(new Rectangle(24, 430, 820, 250), UiTheme.GoldDim, UiTheme.BorderDim);
            var y = 442;
            var start = Math.Max(0, _log.Count - 10);
            for (var i = start; i < _log.Count; i++)
            {
                _ui.Text(_log[i], new Vector2(40, y), 14, UiTheme.Body);
                y += 18;
            }

            _ui.Text("> " + _consoleInput.Buffer, new Vector2(40, 648), 15, UiTheme.Gold);
        }

        _map.Draw(_ui, _world, _view.Position, _pack.HasShip);
        if (_autoMap)
        {
            var cell = (int)(_view.Position.Z / WorldScale.BlockMetres);
            AutomapHud.Draw(_ui, _hero.AutoCells, cell, _view.Position.Z);
        }

        _shop.Draw(_ui, _pack, _hero, _clock.ShopsOpen);
        _bank.Draw(_ui, _pack);
        _mage.Draw(_ui, _pack);
        _inv.Draw(_ui, _pack, _hero);
        _talk.Draw(_ui);
        _create.Draw(_ui);
        _sheet.Draw(_ui, _hero, _pack);
        _spellUi.Draw(_ui, _hero);
        _journal.Draw(_ui, _hero);
        _pause.Draw(_ui, _beds.Enabled, _uiScalePreference, _view.MouseSensitivity);

        if (_map.Open || _autoMap || OverlayOpen || _consoleInput.Open)
            HudLayout.Pointer(_ui, LogicalMouse(_input.CurrentMouse));

        if (_runner.Fade > 0.01f || _restFade > 0.01f)
        {
            var fade = MathF.Max(_runner.Fade, _restFade);
            var alpha = (byte)Math.Clamp((int)(fade * 255f), 0, 255);
            _ui.Panel(UiLayout.FullScreen, new Color((byte)0, (byte)0, (byte)0, alpha), UiTheme.NoBorder);
        }
    }

    private List<PointLight> SceneLights()
    {
        var src = _runner.Current.Lights;
        var nightTown = _clock.IsNight && _runner.Current is BoxLocation { Kind: LocationKind.Town };
        var list = new List<PointLight>(src.Count + 1);
        foreach (var light in src)
        {
            if (nightTown)
                list.Add(light with { Colour = light.Colour * 2.35f, Range = light.Range * 1.4f });
            else
                list.Add(light);
        }

        if (_campLit && _runner.Current is WildernessLocation)
            list.Add(new PointLight(_campAt + new Vector3(0f, 1.4f, 0f),
                new Vector3(1.4f, 0.72f, 0.28f) * 2.4f, 14f));
        if (_spellLight > 0.05f)
            list.Add(new PointLight(_view.Position, new Vector3(1.15f, 1.05f, 0.72f) * 2.2f, 16f));
        return list;
    }

    private bool CampNear(Vector3? at = null)
    {
        if (!_campLit || _runner.Current is not WildernessLocation) return false;
        var p = at ?? _view.Position;
        var dx = _campAt.X - p.X;
        var dz = _campAt.Z - p.Z;
        return dx * dx + dz * dz < 20f;
    }

    private void PlaceCamp()
    {
        var p = _view.Position;
        _campLit = true;
        _campAt = new Vector3(p.X, _world.Heights.SampleWalk(p.X, p.Z), p.Z);
        Sounds?.Play(Sfx.Chime, weight: 0.3f);
        Toast("You strike a fire. R again to sleep.");
        if (_clock.IsNight && _dice.NextDouble() < WorldScale.CampWolfChance)
        {
            SpawnFoe(FoeKind.Wolf, _campAt + new Vector3(9f, 0f, 6f));
            Toast("A wolf has the scent.");
        }
    }

    private void DrawCampAndFoes()
    {
        var wild = _runner.Current is WildernessLocation;
        var anyFoe = false;
        foreach (var foe in _foes)
        {
            if (!foe.Dead) { anyFoe = true; break; }
        }

        if ((!_campLit || !wild) && !anyFoe) return;

        Billboards.Begin(_view.View, _view.Projection);
        if (_campLit && wild)
            Billboards.Draw(_sprites.Get("fire", _view.Yaw, 0f), _campAt, 0.95f, _view.Yaw, Color.White);

        foreach (var foe in _foes)
        {
            if (foe.Dead) continue;
            var dx = foe.Feet.X - _view.Position.X;
            var dz = foe.Feet.Z - _view.Position.Z;
            if (dx * dx + dz * dz > WorldScale.FogEnd * WorldScale.FogEnd) continue;
            var tint = foe.Kind == FoeKind.Wisp ? new Color(180, 220, 230) : Color.White;
            Billboards.Draw(_sprites.Get(foe.Sprite, _view.Yaw, foe.Yaw),
                foe.Feet, foe.Height, _view.Yaw, tint);
        }
    }

    private void TickFoes(float seconds, float metres)
    {
        if (_runner.Current is WildernessLocation && !_view.Swimming && _foes.Count < 2)
        {
            _hunt += metres;
            var chance = _clock.IsNight ? WorldScale.EncounterNight : WorldScale.EncounterDay;
            if (_weather.Kind == WeatherKind.Fog) chance += WorldScale.EncounterFog;
            if (_hunt > WorldScale.EncounterMetres)
            {
                _hunt = 0f;
                if (_dice.NextDouble() < chance)
                    Ambush(_view.Position);
            }
        }

        for (var i = _foes.Count - 1; i >= 0; i--)
        {
            var foe = _foes[i];
            if (foe.Dead)
            {
                _foes.RemoveAt(i);
                continue;
            }

            var to = _view.Position - foe.Feet;
            to.Y = 0f;
            var dist = to.Length();
            if (dist > 70f)
            {
                _foes.RemoveAt(i);
                continue;
            }

            var hidden = HiddenFrom(foe);
            if (!foe.Alerted)
            {
                if (hidden) continue;
                if (dist < 16f) foe.Alerted = true;
            }

            if (!foe.Alerted) continue;

            if (dist > 0.4f)
            {
                to /= dist;
                var step = to * foe.Speed * seconds;
                var nx = foe.Feet.X + step.X;
                var nz = foe.Feet.Z + step.Z;
                if (_runner.Current is WildernessLocation
                    && _world.Heights.Sample(nx, nz) < WorldScale.WaterLevel + 0.4f)
                    continue;
                var y = _runner.Current.SampleGround(nx, nz);
                foe.Feet = new Vector3(nx, y, nz);
                foe.Yaw = MathF.Atan2(to.X, to.Z);
            }

            foe.Cool = MathF.Max(0f, foe.Cool - seconds);
            if (dist < foe.Reach && foe.Cool <= 0f)
            {
                foe.Cool = foe.AttackCool;
                Hurt(foe.Damage);
            }
        }
    }

    private void Ambush(Vector3 near)
    {
        var kind = _weather.Kind is WeatherKind.Fog or WeatherKind.Rain && _dice.NextDouble() < 0.45
            ? FoeKind.Wisp
            : _clock.IsNight && _dice.NextDouble() < 0.5 ? FoeKind.Wolf : FoeKind.Bandit;
        SpawnFoe(kind, near);
        if (_dice.NextDouble() < 0.18)
            SpawnFoe(kind == FoeKind.Wisp ? FoeKind.Wolf : FoeKind.Bandit, near);
        if (_botRunStarted)
        {
            _botLog.Ambushes++;
            _botLog.Event("ambush", kind.ToString().ToLowerInvariant(), _sight);
        }
        Toast($"A {kind.ToString().ToLowerInvariant()} on the road.");
    }

    private void SpawnFoe(FoeKind kind, Vector3 near)
    {
        if (_foes.Count >= 3) return;
        var angle = (float)_dice.NextDouble() * MathF.Tau;
        var radius = 8f + (float)_dice.NextDouble() * 6f;
        var x = near.X + MathF.Sin(angle) * radius;
        var z = near.Z + MathF.Cos(angle) * radius;
        if (!DryStand(x, z, out var stand) && _runner.Current is WildernessLocation)
        {
            x = Math.Clamp(near.X + 6f, 8f, WorldScale.WorldMetres - 8f);
            z = Math.Clamp(near.Z + 6f, 8f, WorldScale.WorldMetres - 8f);
            stand = new Vector3(x, _world.Heights.SampleWalk(x, z) + WorldScale.EyeHeight, z);
        }

        var feet = _runner.Current is WildernessLocation
            ? new Vector3(stand.X, _world.Heights.SampleWalk(stand.X, stand.Z), stand.Z)
            : new Vector3(near.X + 2.4f, 0f, near.Z + 1.2f);
        var hp = kind switch
        {
            FoeKind.Bandit => 28f,
            FoeKind.Watch => 32f,
            FoeKind.Wisp => 18f,
            _ => 22f
        };
        _foes.Add(new Foe
        {
            Kind = kind,
            Feet = feet,
            Yaw = _view.Yaw + 3.1f,
            Health = hp,
            Max = hp,
            Alerted = kind == FoeKind.Watch
                || !(_view.Crouching && _hero.Chance(SkillId.Stealth, 8f))
        });
    }

    private bool StrikeFoe()
    {
        Foe? best = null;
        var bestD = 3.6f;
        var forward = new Vector3(_view.Forward.X, 0f, _view.Forward.Z);
        if (forward.LengthSquared() < 0.001f) return false;
        forward.Normalize();
        foreach (var foe in _foes)
        {
            if (foe.Dead) continue;
            var to = foe.Feet - _view.Position;
            to.Y = 0f;
            var dist = to.Length();
            if (dist > bestD || dist < 0.2f) continue;
            to /= dist;
            if (Vector3.Dot(forward, to) < 0.25f) continue;
            best = foe;
            bestD = dist;
        }

        if (best is null) return false;
        Sounds?.Play(Sfx.HitFlesh, weight: 0.7f);
        var sneak = HiddenFrom(best);
        var hit = Gear.Of(_hero.Weapon).Damage + _hero.SkillOf(SkillId.Blade) * 0.08f
            + (float)_dice.NextDouble() * 3f;
        if (_fatigue < 18f) hit *= 0.7f;
        if (sneak)
        {
            hit *= 1.85f;
            _hero.UseSkill(SkillId.Stealth, 0.6f);
            Toast($"A backstab on the {best.Name}.");
        }

        _hero.UseSkill(SkillId.Blade, 0.18f);
        best.Alerted = true;
        best.Health -= hit;
        if (best.Health > 0f)
        {
            if (!sneak) Toast($"You strike the {best.Name}.");
            return true;
        }

        best.Dead = true;
        _pack.Gold += best.Gold;
        Sounds?.Play(Sfx.Death, weight: 0.55f);
        Sounds?.Play(Sfx.Coin, weight: 0.3f);
        Toast($"The {best.Name} falls. {best.Gold} gp.");
        return true;
    }

    private bool LooseArrow()
    {
        Foe? best = null;
        var bestD = 22f;
        var forward = new Vector3(_view.Forward.X, 0f, _view.Forward.Z);
        if (forward.LengthSquared() < 0.001f) return false;
        forward.Normalize();
        foreach (var foe in _foes)
        {
            if (foe.Dead) continue;
            var to = foe.Feet - _view.Position;
            to.Y = 0f;
            var dist = to.Length();
            if (dist > bestD || dist < 1.4f) continue;
            to /= dist;
            if (Vector3.Dot(forward, to) < 0.55f) continue;
            best = foe;
            bestD = dist;
        }

        if (best is null) return false;
        _hero.Arrows--;
        _hero.UseSkill(SkillId.Archery, 0.28f);
        var hit = Gear.Of(_hero.Bow).Damage + _hero.SkillOf(SkillId.Archery) * 0.1f;
        if (!_hero.Chance(SkillId.Archery, 15f - bestD * 0.4f))
        {
            Sounds?.Play(Sfx.Swing, weight: 0.3f);
            Toast("The shaft flies wide.");
            return true;
        }

        Sounds?.Play(Sfx.HitFlesh, weight: 0.65f);
        best.Alerted = true;
        best.Health -= hit;
        if (best.Health > 0f)
        {
            Toast($"The arrow finds the {best.Name}.");
            return true;
        }

        best.Dead = true;
        _pack.Gold += best.Gold;
        Sounds?.Play(Sfx.Death, weight: 0.55f);
        Sounds?.Play(Sfx.Coin, weight: 0.3f);
        Toast($"The {best.Name} falls. {best.Gold} gp.");
        return true;
    }

    private void Hurt(float amount)
    {
        var soak = Gear.Of(_hero.Armor).Armor * 0.32f;
        amount = MathF.Max(1f, amount - soak);
        if (_hero.Chance(SkillId.Dodging, -18f))
        {
            amount *= 0.45f;
            _hero.UseSkill(SkillId.Dodging, 0.22f);
        }

        _pack.Health = MathF.Max(0f, _pack.Health - amount);
        _hurtFlash = 0.55f;
        Sounds?.Play(Sfx.Hurt, weight: 0.6f);
        if (_pack.Health <= 0.05f) Die();
    }

    private void Die()
    {
        if (_botRunStarted) _bot.OnDeath(_botLog);
        _pack.Health = HpMax * 0.5f;
        _pack.Gold = _pack.Gold * 2 / 3;
        _foes.Clear();
        _campLit = false;
        if (_mounted) Dismount(null);
        GoWilderness(_returnWild.LengthSquared() > 1f ? _returnWild
            : new Vector3(_world.Spawn.X,
                _world.Heights.SampleWalk(_world.Spawn.X, _world.Spawn.Z) + WorldScale.EyeHeight,
                _world.Spawn.Z));
        Toast("You wake on the road, lighter and hurt.");
    }

    private void WriteSave()
    {
        var place = _runner.Current switch
        {
            WildernessLocation => "wild",
            BoxLocation { Kind: LocationKind.Town } => "town",
            BoxLocation { Kind: LocationKind.Dungeon } => "dungeon",
            _ => "room"
        };
        var p = _view.Position;
        SaveFile.Write(new SaveData
        {
            Seed = _seed,
            Day = _clock.Day,
            Hours = _clock.Hours,
            Fatigue = _fatigue,
            Health = _pack.Health,
            WellRested = _wellRested,
            X = p.X,
            Y = p.Y,
            Z = p.Z,
            Yaw = _view.Yaw,
            Place = place,
            Town = _townIndex,
            Dungeon = _dungeonIndex,
            Room = _roomIndex,
            Gold = _pack.Gold,
            BankGold = _pack.BankGold,
            HouseGold = _pack.HouseGold,
            WagonGold = _pack.WagonGold,
            HouseTown = _pack.HouseTown,
            HasWagon = _pack.HasWagon,
            HasShip = _pack.HasShip,
            HasCloak = _pack.HasCloak,
            Wanted = _pack.Wanted,
            Rations = _pack.Rations,
            Meals = _pack.Meals,
            Stamina = _pack.Stamina,
            Cures = _pack.Cures,
            Lockpicks = _pack.Lockpicks,
            Ailment = (int)_pack.Ailment,
            Hunger = _pack.Hunger,
            Cold = _pack.Cold,
            Wet = _pack.Wet,
            Guild = (bool[])_pack.Guild.Clone(),
            Looted = SetIds(_pack.Looted),
            Keys = SetIds(_pack.Keys),
            Doors = SetIds(_pack.Doors),
            FightJob = _pack.FightJob,
            FightReady = _pack.FightReady,
            MageJob = _pack.MageJob,
            MageLetter = _pack.MageLetter,
            ThiefJob = _pack.ThiefJob,
            ThiefReady = _pack.ThiefReady,
            HasMark = _pack.HasMark,
            MarkTown = _pack.MarkTown,
            HeroName = _hero.Name,
            Race = (int)_hero.Race,
            Class = (int)_hero.Class,
            Attr = (int[])_hero.Attr.Clone(),
            Skill = (float[])_hero.Skill.Clone(),
            Magicka = _hero.Magicka,
            MagickaMax = _hero.MagickaMax,
            Weapon = _hero.Weapon,
            Armor = _hero.Armor,
            Bow = _hero.Bow,
            Arrows = _hero.Arrows,
            OwnedGear = SetIds(_hero.OwnedGear),
            SpellSel = _hero.SpellSel,
            Spells = _hero.Spells.ConvertAll(s => new SpellSave
            {
                Name = s.Name,
                Effect = (int)s.Effect,
                Magnitude = s.Magnitude,
                Cost = s.Cost
            }).ToArray(),
            Rep = (int[])_hero.Rep.Clone(),
            Member = (bool[])_hero.Member.Clone(),
            Topics = [.. _hero.Topics],
            MainBeat = _hero.MainBeat,
            Relic = _hero.Relic,
            RelicDungeon = _hero.RelicDungeon,
            Log = _hero.Log.ConvertAll(q => new QuestSave
            {
                Title = q.Title,
                Body = q.Body,
                Done = q.Done
            }).ToArray(),
            AutoCells = SetIds(_hero.AutoCells),
            AutoDungeon = _hero.AutoDungeon
        });
    }

    private bool ReadSave()
    {
        var data = SaveFile.Read();
        if (data is null) return false;
        BuildWorld(data.Seed);
        _clock.Load(data.Day, data.Hours);
        _fatigue = data.Fatigue;
        _wellRested = data.WellRested;
        _townIndex = data.Town;
        _dungeonIndex = data.Dungeon;
        _roomIndex = data.Room;
        ApplyPack(data);
        ApplyHero(data);
        _create.Open = false;
        var pos = new Vector3(data.X, data.Y, data.Z);
        switch (data.Place)
        {
            case "town" when _townIndex >= 0 && _townIndex < _world.TownCount:
                _runner.Bind(_world.Town(_townIndex), pos, data.Yaw, _view, OnLocation);
                break;
            case "dungeon" when _dungeonIndex >= 0 && _dungeonIndex < _world.DungeonCount:
                _runner.Bind(_world.Dungeon(_dungeonIndex), pos, data.Yaw, _view, OnLocation);
                break;
            case "room" when _townIndex >= 0 && _townIndex < _world.TownCount:
            {
                var rooms = _world.InteriorsFor(_townIndex);
                var i = Math.Clamp(_roomIndex, 0, rooms.Length - 1);
                _runner.Bind(rooms[i], pos, data.Yaw, _view, OnLocation);
                break;
            }
            default:
                _runner.Bind(_world.Wilderness, pos, data.Yaw, _view, OnLocation);
                break;
        }

        ApplyMount();
        return true;
    }

    private void ApplyPack(SaveData data)
    {
        _pack.Gold = data.Gold;
        _pack.BankGold = data.BankGold;
        _pack.HouseGold = data.HouseGold;
        _pack.WagonGold = data.WagonGold;
        _pack.HouseTown = data.HouseTown;
        _pack.HasWagon = data.HasWagon;
        _pack.HasShip = data.HasShip;
        _pack.HasCloak = data.HasCloak;
        _pack.Wanted = data.Wanted;
        _pack.Rations = data.Rations;
        _pack.Meals = data.Meals;
        _pack.Stamina = data.Stamina;
        _pack.Cures = data.Cures;
        _pack.Lockpicks = data.Lockpicks;
        _pack.Ailment = (Ailment)data.Ailment;
        _pack.Hunger = data.Hunger;
        _pack.Cold = data.Cold;
        _pack.Wet = data.Wet;
        _pack.Health = data.Health;
        if (data.Guild is { Length: > 0 })
        {
            for (var i = 0; i < Math.Min(3, data.Guild.Length); i++)
                _pack.Guild[i] = data.Guild[i];
        }

        FillSet(_pack.Looted, data.Looted);
        FillSet(_pack.Keys, data.Keys);
        FillSet(_pack.Doors, data.Doors);
        _pack.FightJob = data.FightJob;
        _pack.FightReady = data.FightReady;
        _pack.MageJob = data.MageJob;
        _pack.MageLetter = data.MageLetter;
        _pack.ThiefJob = data.ThiefJob;
        _pack.ThiefReady = data.ThiefReady;
        _pack.HasMark = data.HasMark;
        _pack.MarkTown = data.MarkTown;
        _pack.ClampNeeds();
    }

    private void ApplyHero(SaveData data)
    {
        if (data.Attr is not { Length: > 0 })
        {
            FinishCreate(RaceId.Breton, ClassId.Warrior);
            SyncHeroFromGuild();
            return;
        }

        _hero.Name = string.IsNullOrWhiteSpace(data.HeroName) ? "Wanderer" : data.HeroName;
        _hero.Race = (RaceId)data.Race;
        _hero.Class = (ClassId)data.Class;
        CopyInts(_hero.Attr, data.Attr);
        CopyFloats(_hero.Skill, data.Skill);
        _hero.Weapon = data.Weapon;
        _hero.Armor = data.Armor;
        _hero.Bow = data.Bow;
        _hero.Arrows = data.Arrows;
        FillSet(_hero.OwnedGear, data.OwnedGear);
        _hero.SpellSel = data.SpellSel;
        _hero.Spells.Clear();
        if (data.Spells is not null)
        {
            foreach (var spell in data.Spells)
                _hero.Spells.Add(new Spell(spell.Name, (SpellEffect)spell.Effect,
                    spell.Magnitude, spell.Cost));
        }

        CopyInts(_hero.Rep, data.Rep);
        if (data.Member is { Length: > 0 })
        {
            for (var i = 0; i < Math.Min(Hero.FactionCount, data.Member.Length); i++)
                _hero.Member[i] = data.Member[i];
        }

        _hero.Topics.Clear();
        if (data.Topics is not null)
        {
            foreach (var topic in data.Topics)
                _hero.Topics.Add(topic);
        }

        _hero.MainBeat = data.MainBeat;
        _hero.Relic = data.Relic;
        _hero.RelicDungeon = data.RelicDungeon;
        _hero.Log.Clear();
        if (data.Log is not null)
        {
            foreach (var note in data.Log)
                _hero.Log.Add(new QuestNote(note.Title, note.Body, note.Done));
        }

        FillSet(_hero.AutoCells, data.AutoCells);
        _hero.AutoDungeon = data.AutoDungeon;
        _hero.RecalcMagicka();
        _hero.Magicka = data.Magicka > 0f ? Math.Clamp(data.Magicka, 0f, _hero.MagickaMax)
            : _hero.MagickaMax;
        SyncHeroFromGuild();
        if (_hero.Log.Count == 0)
            QuestBook.EnsureMain(_hero, RelicDungeonName(),
                Math.Clamp(_hero.RelicDungeon, 0, Math.Max(0, _world.DungeonCount - 1)));
    }

    private static void CopyInts(int[] dest, int[]? src)
    {
        if (src is null) return;
        for (var i = 0; i < Math.Min(dest.Length, src.Length); i++)
            dest[i] = src[i];
    }

    private static void CopyFloats(float[] dest, float[]? src)
    {
        if (src is null) return;
        for (var i = 0; i < Math.Min(dest.Length, src.Length); i++)
            dest[i] = src[i];
    }

    private static int[] SetIds(System.Collections.Generic.HashSet<int> set)
    {
        var ids = new int[set.Count];
        set.CopyTo(ids);
        return ids;
    }

    private static void FillSet(System.Collections.Generic.HashSet<int> set, int[]? ids)
    {
        set.Clear();
        if (ids is null) return;
        foreach (var id in ids) set.Add(id);
    }

    private void EnsureBotRun()
    {
        if (_botRunStarted) return;
        _botRunStarted = true;
        _botSummaryWritten = false;
        _botLog.StartRun(_seed, _bot.Seed);
    }

    private void FlushBotSummary()
    {
        if (!_botRunStarted || _botSummaryWritten) return;
        _botSummaryWritten = true;
        _botLog.WriteSummary(_seed, _bot.Seed, _bot.GoalName);
    }

    private void SetBot(bool on)
    {
        _bot.Enabled = on;
        if (on)
        {
            EnsureBotRun();
            _botSummaryWritten = false;
            Toast($"Bot {_bot.Seed} is walking.");
            return;
        }

        FlushBotSummary();
        Toast($"Bot stopped.  {_botLog.StatusLine()}");
    }

    private string StatusText()
    {
        var papers = string.Concat(
            _pack.In(GuildKind.Fighters) ? " fighters" : "",
            _pack.In(GuildKind.Mages) ? " mages" : "",
            _pack.In(GuildKind.Thieves) ? " thieves" : "");
        var bot = _bot.Enabled ? $"bot {_bot.GoalName} seed {_bot.Seed}" : $"bot off seed {_bot.Seed}";
        return $"{_view.Position.X:0} {_view.Position.Z:0}  {_runner.Current.Name}\n{_clock.Stamp}\n" +
               $"{_pack.Gold} gp  hp {_pack.Health:0}  hunger {_pack.Hunger:0}  cold {_pack.Cold:0}  " +
               $"fatigue {_fatigue:0}  food {_pack.Rations}/{_pack.Meals}{papers}\n{bot}\n{_botLog.StatusLine()}";
    }

    private string Give(string what, int n)
    {
        switch (what.ToLowerInvariant())
        {
            case "gold" or "gp":
                _pack.Gold += n;
                return $"{_pack.Gold} gp";
            case "rations" or "food":
                _pack.Rations += n;
                return $"{_pack.Rations} rations";
            case "meals" or "stew":
                _pack.Meals += n;
                return $"{_pack.Meals} meals";
            case "stamina":
                _pack.Stamina += n;
                return $"{_pack.Stamina} draughts";
            case "cures" or "cure":
                _pack.Cures += n;
                return $"{_pack.Cures} cures";
            case "lockpicks" or "picks":
                _pack.Lockpicks += n;
                return $"{_pack.Lockpicks} picks";
            default:
                return "give gold|rations|meals|stamina|cures|lockpicks [n]";
        }
    }

    private void FillSight()
    {
        _nearRefresh -= 1f;
        if (_nearRefresh <= 0f)
        {
            _nearRefresh = 1.2f;
            _sight.TownIndex = AutoPlayer.NearestPad(_world.TownPads, _view.Position);
            _sight.TownPad = _world.Wilderness.TownGate(_sight.TownIndex);
            _sight.MouthIndex = AutoPlayer.NearestPad(_world.DungeonMouths, _view.Position);
            _sight.MouthPad = _world.DungeonMouths[_sight.MouthIndex];
        }

        var dx = _sight.TownPad.X - _view.Position.X;
        var dz = _sight.TownPad.Z - _view.Position.Z;
        _sight.TownMetres = MathF.Sqrt(dx * dx + dz * dz);
        dx = _sight.MouthPad.X - _view.Position.X;
        dz = _sight.MouthPad.Z - _view.Position.Z;
        _sight.MouthMetres = MathF.Sqrt(dx * dx + dz * dz);

        var foes = 0;
        var nearest = 999f;
        foreach (var foe in _foes)
        {
            if (foe.Dead) continue;
            foes++;
            var fx = foe.Feet.X - _view.Position.X;
            var fz = foe.Feet.Z - _view.Position.Z;
            var d = MathF.Sqrt(fx * fx + fz * fz);
            if (d < nearest) nearest = d;
        }

        _sight.Place = _runner.Current switch
        {
            WildernessLocation => "wild",
            BoxLocation { Kind: LocationKind.Town } => "town",
            BoxLocation { Kind: LocationKind.Dungeon } => "dungeon",
            _ => "room"
        };
        _sight.Position = _view.Position;
        _sight.Yaw = _view.Yaw;
        _sight.Health = _pack.Health;
        _sight.Hunger = _pack.Hunger;
        _sight.Cold = _pack.Cold;
        _sight.Fatigue = _fatigue;
        _sight.Wet = _pack.Wet;
        _sight.Gold = _pack.Gold;
        _sight.Rations = _pack.Rations;
        _sight.Meals = _pack.Meals;
        _sight.Stamina = _pack.Stamina;
        _sight.Cures = _pack.Cures;
        _sight.Night = _clock.IsNight;
        _sight.ShopsOpen = _clock.ShopsOpen;
        _sight.Wild = _runner.Current is WildernessLocation;
        _sight.Town = _runner.Current is BoxLocation { Kind: LocationKind.Town };
        _sight.Dungeon = _runner.Current is BoxLocation { Kind: LocationKind.Dungeon };
        _sight.Swimming = _view.Swimming;
        _sight.Mounted = _mounted || _wagonRide;
        _sight.Wanted = _pack.Wanted;
        _sight.ShopOpen = _shop.Open;
        _sight.BankOpen = _bank.Open;
        _sight.MageOpen = _mage.Open;
        _sight.FoeCount = foes;
        _sight.NearestFoe = nearest;
        _sight.HintKind = _hint?.Marker.Kind.ToString();
        _sight.HintLine = _hint?.Line;
        _sight.MetresThisFrame = _lastMetres;
        _sight.Ailment = _pack.Ailment;
        _sight.HasAim = false;
        if (_runner.Current is BoxLocation box)
        {
            Vector3? work = null;
            Vector3? leave = null;
            var workD = float.MaxValue;
            var p = _view.Position;
            foreach (var marker in box.Markers)
            {
                var mx = marker.Position.X - p.X;
                var mz = marker.Position.Z - p.Z;
                var d = MathF.Sqrt(mx * mx + mz * mz);
                var isLeave = marker.Kind is MarkerKind.LeaveInterior or MarkerKind.LeaveTown
                    or MarkerKind.LeaveDungeon;
                if (d < 1.5f) continue;
                if (isLeave)
                {
                    leave = marker.Position;
                    continue;
                }

                if (d >= workD) continue;
                workD = d;
                work = marker.Position;
            }

            var aim = work ?? leave;
            if (aim is { } at)
            {
                _sight.Aim = at;
                _sight.HasAim = true;
            }
        }
    }

    private void ApplyBot(BotIntent intent)
    {
        if (intent.CloseOverlay)
        {
            _shop.Open = false;
            _bank.Open = false;
            _mage.Open = false;
            _inv.Open = false;
            _talk.Close();
            _map.Open = false;
            _pause.Close();
            _sheet.Open = false;
            _spellUi.Open = false;
            _journal.Open = false;
            _autoMap = false;
        }

        if (intent.ConfirmOverlay && _shop.Open)
        {
            _shop.Selected = _pack.Hunger < 40f && _pack.Gold >= 8 ? 1 : 0;
            TryBuy();
        }

        if (intent.Eat) TryEat();
        if (intent.Stamina) DrinkStamina();
        if (intent.Cure) DrinkCure();
        if (intent.Rest) TryRest(fromBed: false, innFee: 0);
        if (intent.Mount) ToggleMount();
        if (intent.Swing)
        {
            Swing();
            _botLog.Swings++;
        }

        if (intent.Use && _hint is { } hint)
        {
            Use(hint.Marker);
            _botLog.Uses++;
        }

        if (!intent.RoadToTown) return;
        var i = _sight.TownIndex;
        var dest = _world.TownPads[i];
        TakeRoad(RoadHours(_view.Position, dest), dest);
        EnterTown(i, force: true);
    }

    private List<CompassMark> CompassMarks()
    {
        var marks = new List<CompassMark>();
        if (_runner.Current is not WildernessLocation) return marks;
        var ti = AutoPlayer.NearestPad(_world.TownPads, _view.Position);
        marks.Add(new CompassMark(AutoPlayer.YawTo(_view.Position, _world.Wilderness.TownGate(ti)),
            "▲", UiTheme.Gold));
        var di = AutoPlayer.NearestPad(_world.DungeonMouths, _view.Position);
        marks.Add(new CompassMark(AutoPlayer.YawTo(_view.Position, _world.DungeonMouths[di]),
            "▼", new Color(176, 158, 210)));
        return marks;
    }

    private void ApplyFog(Color clear)
    {
        if (_runner.Current is BoxLocation { Kind: LocationKind.Dungeon })
        {
            LitEffect.FogEnabled = false;
            LitEffect.AmbientLightColor = new Vector3(0.42f, 0.40f, 0.38f);
            LitEffect.DirectionalLight0.DiffuseColor = new Vector3(0.35f, 0.34f, 0.32f);
            return;
        }

        if (_runner.Current is BoxLocation { Kind: LocationKind.Town })
        {
            LitEffect.FogEnabled = false;
            LitEffect.AmbientLightColor = new Vector3(0.64f, 0.58f, 0.48f);
            LitEffect.DirectionalLight0.DiffuseColor = new Vector3(1f, 0.88f, 0.66f);
            return;
        }

        if (_runner.Current is BoxLocation { Kind: LocationKind.Interior })
        {
            LitEffect.FogEnabled = false;
            var day = MathHelper.Lerp(0.42f, 0.82f, _clock.DayFactor);
            LitEffect.AmbientLightColor = new Vector3(day, day * 0.93f, day * 0.82f);
            LitEffect.DirectionalLight0.DiffuseColor = new Vector3(0.9f, 0.8f, 0.6f) * day;
            return;
        }

        LitEffect.FogEnabled = true;
        LitEffect.AmbientLightColor = new Vector3(0.54f, 0.57f, 0.62f);
        LitEffect.DirectionalLight0.DiffuseColor = new Vector3(1f, 0.83f, 0.64f);
        LitEffect.FogColor = clear.ToVector3();
        LitEffect.FogStart = WorldScale.FogStart;
        LitEffect.FogEnd = WorldScale.FogEnd;
    }

    protected override void OnDisplayChanged() =>
        _view.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        if (_botRunStarted)
        {
            try { FlushBotSummary(); }
            catch { /* leaving anyway */ }
        }

        _botLog?.Dispose();
        _beds.Dispose();
        _sprites.Dispose();
        _sky.Dispose();
        _compass.Dispose();
        _water.Dispose();
        _map.Dispose();
        _world?.Terrain.Dispose();
        DisposeHost();
        base.UnloadContent();
    }
}

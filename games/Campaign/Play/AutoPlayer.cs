using Ember;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

public enum BotGoal
{
    Wander,
    SeekTown,
    SeekDungeon,
    Explore,
    Fight,
    Flee,
    Eat,
    Rest,
    Shop
}

/// <summary>What the auto-player wants this frame. CampaignGame applies it like a keyboard.</summary>
public readonly struct BotIntent
{
    public WalkInput Walk { get; init; }
    public bool Use { get; init; }
    public bool Swing { get; init; }
    public bool Rest { get; init; }
    public bool Eat { get; init; }
    public bool Stamina { get; init; }
    public bool Cure { get; init; }
    public bool Mount { get; init; }
    public bool Climb { get; init; }
    public bool CloseOverlay { get; init; }
    public bool ConfirmOverlay { get; init; }
    public int OverlayDelta { get; init; }
    public bool RoadToTown { get; init; }
}

/// <summary>A snapshot the bot can decide from without touching the sim.</summary>
public sealed class BotSight
{
    public string Place = "wild";
    public Vector3 Position;
    public float Yaw;
    public float Health;
    public float Hunger;
    public float Cold;
    public float Fatigue;
    public float Wet;
    public int Gold;
    public int Rations;
    public int Meals;
    public int Stamina;
    public int Cures;
    public bool Night;
    public bool ShopsOpen;
    public bool Wild;
    public bool Town;
    public bool Dungeon;
    public bool Swimming;
    public bool Mounted;
    public bool Wanted;
    public bool ShopOpen;
    public bool BankOpen;
    public bool MageOpen;
    public int FoeCount;
    public float NearestFoe;
    public string? HintKind;
    public string? HintLine;
    public int TownIndex;
    public Vector3 TownPad;
    public float TownMetres;
    public int MouthIndex;
    public Vector3 MouthPad;
    public float MouthMetres;
    public float MetresThisFrame;
    public Ailment Ailment;
    public bool HasAim;
    public Vector3 Aim;
}

/// <summary>
/// Walks, uses, and rests on its own. Choices are rolled from a private RNG so a run is
/// not a scripted path, and two bots on the same world seed still diverge.
/// </summary>
public sealed class AutoPlayer
{
    private readonly Random _rng;
    private BotGoal _goal = BotGoal.Wander;
    private float _goalLife;
    private float _actionCool;
    private float _stuck;
    private float _backup;
    private int _turnDir = 1;
    private float _tick;
    private Vector3 _wanderAt;
    private bool _wanderSet;
    private bool _hungryLogged;
    private bool _coldLogged;
    private bool _hurtLogged;
    private bool _shopTried;

    public int Seed { get; }
    public bool Enabled { get; set; }
    public BotGoal Goal => _goal;
    public string GoalName => _goal.ToString();

    public AutoPlayer(int seed, bool enabled = false)
    {
        Seed = seed;
        Enabled = enabled;
        _rng = new Random(seed);
    }

    public BotIntent Think(BotSight sight, float seconds, BotLog log)
    {
        log.RealSeconds += seconds;
        log.Metres += sight.MetresThisFrame;
        log.NoteGold(sight.Gold);
        if (!sight.ShopOpen) _shopTried = false;

        var turning = false;
        if (sight.HasAim || _goal is BotGoal.SeekTown or BotGoal.SeekDungeon or BotGoal.Wander)
        {
            var face = FacePoint(sight);
            turning = MathF.Abs(AngleDelta(sight.Yaw, face)) > 0.28f;
        }

        if (sight.MetresThisFrame < 0.07f && !turning && _backup <= 0f
            && _goal is not BotGoal.Eat and not BotGoal.Rest)
            _stuck += seconds;
        else if (sight.MetresThisFrame > 0.12f)
            _stuck = 0f;

        _actionCool = MathF.Max(0f, _actionCool - seconds);
        _backup = MathF.Max(0f, _backup - seconds);
        _goalLife -= seconds;
        _tick += seconds;

        Thresholds(sight, log);

        if (_goalLife <= 0f || MustSwitch(sight))
            PickGoal(sight);

        if (_tick >= 12f)
        {
            _tick = 0f;
            log.Tick(sight, GoalName);
        }

        if (sight.ShopOpen || sight.BankOpen || sight.MageOpen)
            return OverlayIntent(sight);

        return PlayIntent(sight);
    }

    public void OnDeath(BotLog log)
    {
        log.Deaths++;
        log.Event("death", GoalName, null);
        _goal = BotGoal.Flee;
        _goalLife = 6f;
        _stuck = 0f;
        _hurtLogged = false;
    }

    private void Thresholds(BotSight sight, BotLog log)
    {
        if (sight.Hunger < 16f)
        {
            if (!_hungryLogged)
            {
                log.Event("starve", $"hunger {sight.Hunger:0}", sight);
                _hungryLogged = true;
            }
        }
        else _hungryLogged = false;

        if (sight.Cold > 76f)
        {
            if (!_coldLogged)
            {
                log.Event("freeze", $"cold {sight.Cold:0}", sight);
                _coldLogged = true;
            }
        }
        else _coldLogged = false;

        if (sight.Health < 32f)
        {
            if (!_hurtLogged)
            {
                log.Event("hurt", $"hp {sight.Health:0}", sight);
                _hurtLogged = true;
            }
        }
        else if (sight.Health > 55f) _hurtLogged = false;
    }

    private bool MustSwitch(BotSight sight)
    {
        if (sight.Health < 28f && sight.FoeCount > 0 && _goal != BotGoal.Flee) return true;
        if (sight.Hunger < 18f && (sight.Rations > 0 || sight.Meals > 0) && _goal != BotGoal.Eat)
            return true;
        if (sight.FoeCount > 0 && sight.NearestFoe < 9f && sight.Health >= 28f
            && _goal is not BotGoal.Fight and not BotGoal.Flee)
            return true;
        if (sight.Fatigue < 14f && _goal != BotGoal.Rest && sight.FoeCount == 0) return true;
        return false;
    }

    private void PickGoal(BotSight sight)
    {
        _goalLife = 7f + (float)_rng.NextDouble() * 14f;

        if (sight.Health < 28f && sight.FoeCount > 0)
        {
            _goal = BotGoal.Flee;
            _goalLife = 5f;
            return;
        }

        if (sight.Hunger < 18f && (sight.Rations > 0 || sight.Meals > 0))
        {
            _goal = BotGoal.Eat;
            _goalLife = 2.5f;
            return;
        }

        if (sight.FoeCount > 0 && sight.NearestFoe < 10f && sight.Health >= 28f)
        {
            _goal = BotGoal.Fight;
            _goalLife = 4f;
            return;
        }

        if (sight.Fatigue < 14f && sight.FoeCount == 0)
        {
            _goal = BotGoal.Rest;
            _goalLife = 3f;
            return;
        }

        if (sight.ShopOpen)
        {
            _goal = BotGoal.Shop;
            _goalLife = 4f;
            return;
        }

        var roll = _rng.NextDouble();
        if (sight.Wild)
        {
            if (sight.Night && roll < 0.18) _goal = BotGoal.Rest;
            else if (roll < 0.48) _goal = BotGoal.SeekTown;
            else if (roll < 0.66) _goal = BotGoal.SeekDungeon;
            else
            {
                _goal = BotGoal.Wander;
                PickWander(sight.Position);
            }
            return;
        }

        if (sight.Town)
        {
            if (sight.Wanted && roll < 0.55) _goal = BotGoal.Wander;
            else if (roll < 0.22) _goal = BotGoal.Shop;
            else if (roll < 0.38) _goal = BotGoal.Rest;
            else _goal = BotGoal.Explore;
            return;
        }

        _goal = roll < 0.35 ? BotGoal.Wander : BotGoal.Explore;
    }

    private void PickWander(Vector3 from)
    {
        var angle = (float)_rng.NextDouble() * MathF.Tau;
        var dist = 40f + (float)_rng.NextDouble() * 90f;
        _wanderAt = from + new Vector3(MathF.Sin(angle) * dist, 0f, MathF.Cos(angle) * dist);
        _wanderSet = true;
    }

    private BotIntent OverlayIntent(BotSight sight)
    {
        if (_actionCool > 0f) return default;
        _actionCool = 0.32f;

        if (sight.ShopOpen && !_shopTried && (sight.Hunger < 55f || sight.Rations < 2) && sight.Gold >= 6)
        {
            _shopTried = true;
            return new BotIntent { ConfirmOverlay = true };
        }

        _shopTried = false;
        return new BotIntent { CloseOverlay = true };
    }

    private BotIntent PlayIntent(BotSight sight)
    {
        var use = false;
        var swing = false;
        var rest = false;
        var eat = false;
        var stamina = false;
        var cure = false;
        var mount = false;
        var climb = false;
        var road = false;
        var jump = false;
        var back = false;
        var heldYaw = 0f;
        var forward = false;

        if (_backup > 0f)
        {
            back = true;
            heldYaw = _turnDir * 0.35f;
        }
        else if (_stuck > 1.15f)
        {
            _backup = 0.55f;
            _stuck = 0f;
            _turnDir = -_turnDir;
            if (sight.Wild && _goal == BotGoal.Wander)
                PickWander(sight.Position);
            else if (sight.HasAim)
            {
                // Next think aims at whatever is left; this frame we step off the wall.
            }
            else if (sight.Wild && _goal == BotGoal.SeekTown && sight.TownMetres > 80f)
                road = true;
            back = true;
        }
        else
        {
            if (_actionCool <= 0f)
            {
                if (_goal == BotGoal.Eat)
                {
                    eat = true;
                    _actionCool = 1.1f;
                }
                else if (_goal == BotGoal.Rest)
                {
                    rest = true;
                    _actionCool = 1.6f;
                }
                else if (_goal == BotGoal.Fight && sight.NearestFoe < 3.4f)
                {
                    swing = true;
                    _actionCool = 0.45f;
                }
                else if (sight.HintKind is not null && WantHint(sight))
                {
                    use = true;
                    _actionCool = 0.55f;
                }
                else if (sight.Ailment != Ailment.None && sight.Cures > 0 && Chance(0.04))
                {
                    cure = true;
                    _actionCool = 1.2f;
                }
                else if (sight.Fatigue < 28f && sight.Stamina > 0 && Chance(0.03))
                {
                    stamina = true;
                    _actionCool = 1.2f;
                }
                else if (sight.Wild && !sight.Mounted && !sight.Swimming
                    && _goal is BotGoal.SeekTown or BotGoal.SeekDungeon && Chance(0.02))
                {
                    mount = true;
                    _actionCool = 1.4f;
                }
            }

            var face = FacePoint(sight);
            var error = AngleDelta(sight.Yaw, face);
            var turning = MathF.Abs(error) > 0.22f;
            heldYaw = Math.Clamp(error * 2.4f, -1f, 1f);
            forward = !turning && _goal != BotGoal.Eat && _goal != BotGoal.Rest;
            if (_goal == BotGoal.Flee)
            {
                forward = MathF.Abs(error) < 1.2f;
                heldYaw = Math.Clamp(error * 2.4f, -1f, 1f);
            }

            climb = sight.Wild && _stuck > 0.8f;
            jump = false;
        }

        var sprint = _goal == BotGoal.Flee
            || (_goal is BotGoal.SeekTown or BotGoal.SeekDungeon && sight.Fatigue > 36f && forward);

        var strafe = 0;
        if (_goal == BotGoal.Fight && sight.NearestFoe < 2.4f)
            strafe = _rng.Next(0, 3) == 0 ? -1 : _rng.Next(0, 2) == 0 ? 1 : 0;

        return new BotIntent
        {
            Walk = new WalkInput(
                Forward: forward,
                Back: back || (_goal == BotGoal.Flee && sight.NearestFoe < 2.2f),
                Left: strafe < 0,
                Right: strafe > 0,
                Sprint: sprint && forward,
                Jump: jump,
                HeldYaw: heldYaw,
                HeldPitch: 0f),
            Use = use,
            Swing = swing,
            Rest = rest,
            Eat = eat,
            Stamina = stamina,
            Cure = cure,
            Mount = mount,
            Climb = climb,
            RoadToTown = road
        };
    }

    private bool WantHint(BotSight sight)
    {
        var kind = sight.HintKind;
        if (kind is null) return false;

        if (_goal == BotGoal.SeekTown)
            return kind is "EnterTown" or "EnterInterior" or "Shop" or "Talk" or "Rest";
        if (_goal == BotGoal.SeekDungeon)
            return kind is "EnterDungeon" or "Dummy" or "Loot" or "Key" or "LockedDoor" or "LeaveDungeon";
        if (_goal == BotGoal.Shop)
            return kind is "Shop" or "Bank" or "EnterInterior";
        if (_goal == BotGoal.Rest)
            return kind is "Rest" or "Drink";
        if (_goal == BotGoal.Explore)
            return kind is not "LeaveTown" || Chance(0.12);
        if (_goal == BotGoal.Wander && sight.Town)
            return kind is "LeaveTown" or "Talk" or "Shop";
        if (kind is "LeaveDungeon" && _goal == BotGoal.Wander) return true;
        return Chance(0.35);
    }

    private float FacePoint(BotSight sight)
    {
        if (_goal == BotGoal.Flee && sight.FoeCount > 0)
            return sight.Yaw + MathF.PI;

        if (sight.HasAim)
            return YawTo(sight.Position, sight.Aim);

        Vector3 dest;
        if (_goal == BotGoal.SeekTown) dest = sight.TownPad;
        else if (_goal == BotGoal.SeekDungeon) dest = sight.MouthPad;
        else if (_goal == BotGoal.Wander && _wanderSet) dest = _wanderAt;
        else return sight.Yaw;

        return YawTo(sight.Position, dest);
    }

    private bool Chance(double p) => _rng.NextDouble() < p;

    public static float YawTo(Vector3 from, Vector3 to) =>
        MathF.Atan2(to.X - from.X, -(to.Z - from.Z));

    public static float AngleDelta(float from, float to)
    {
        var d = to - from;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return d;
    }

    public static int NearestPad(Vector3[] pads, Vector3 p)
    {
        var best = 0;
        var bestD = float.MaxValue;
        for (var i = 0; i < pads.Length; i++)
        {
            var dx = pads[i].X - p.X;
            var dz = pads[i].Z - p.Z;
            var d = dx * dx + dz * dz;
            if (d >= bestD) continue;
            bestD = d;
            best = i;
        }

        return best;
    }
}

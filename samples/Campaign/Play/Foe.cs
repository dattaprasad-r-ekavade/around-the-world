using Microsoft.Xna.Framework;

namespace Campaign;

public enum FoeKind
{
    Wolf,
    Bandit,
    Wisp,
    Watch
}

public sealed class Foe
{
    public FoeKind Kind;
    public Vector3 Feet;
    public float Yaw;
    public float Health;
    public float Max;
    public float Cool;
    public bool Dead;
    public bool Alerted;

    public string Sprite => Kind switch
    {
        FoeKind.Wolf => "wolf",
        FoeKind.Bandit => "bandit",
        FoeKind.Wisp => "wisp",
        _ => "watch"
    };

    public string Name => Kind switch
    {
        FoeKind.Wolf => "wolf",
        FoeKind.Bandit => "bandit",
        FoeKind.Wisp => "wraith",
        _ => "watch"
    };

    public float Height => Kind == FoeKind.Wolf ? 1.15f : Kind == FoeKind.Wisp ? 1.45f : 1.85f;
    public float Speed => Kind switch
    {
        FoeKind.Wolf => 4.8f,
        FoeKind.Wisp => 3.2f,
        FoeKind.Bandit => 3.8f,
        _ => 3.6f
    };
    public float Reach => Kind == FoeKind.Wisp ? 2.6f : 2.2f;
    public float Damage => Kind == FoeKind.Watch ? 7f : Kind == FoeKind.Bandit ? 6f : 5f;
    public float AttackCool => 1.75f;
    public int Gold => Kind == FoeKind.Bandit ? 12 : Kind == FoeKind.Watch ? 5 : 4;
}

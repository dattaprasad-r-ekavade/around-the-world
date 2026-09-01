using System;

namespace Campaign;

/// <summary>Joining one hall moves the others. Daggerfall's web, six nodes.</summary>
public static class Factions
{
    public static void ApplyJoin(int[] rep, FactionId id)
    {
        Shift(rep, id, 35);
        switch (id)
        {
            case FactionId.Fighters:
                Shift(rep, FactionId.Knights, 12);
                Shift(rep, FactionId.Thieves, -18);
                Shift(rep, FactionId.Merchants, 6);
                break;
            case FactionId.Mages:
                Shift(rep, FactionId.Temple, 8);
                Shift(rep, FactionId.Thieves, -6);
                break;
            case FactionId.Thieves:
                Shift(rep, FactionId.Fighters, -22);
                Shift(rep, FactionId.Knights, -25);
                Shift(rep, FactionId.Merchants, -10);
                Shift(rep, FactionId.Temple, -8);
                break;
            case FactionId.Temple:
                Shift(rep, FactionId.Mages, 6);
                Shift(rep, FactionId.Thieves, -12);
                Shift(rep, FactionId.Knights, 8);
                break;
            case FactionId.Knights:
                Shift(rep, FactionId.Fighters, 16);
                Shift(rep, FactionId.Thieves, -20);
                Shift(rep, FactionId.Temple, 10);
                break;
            case FactionId.Merchants:
                Shift(rep, FactionId.Thieves, -8);
                Shift(rep, FactionId.Fighters, 4);
                break;
        }
    }

    public static void Shift(int[] rep, FactionId id, int delta)
    {
        var i = (int)id;
        if ((uint)i >= rep.Length) return;
        rep[i] = Math.Clamp(rep[i] + delta, -100, 100);
    }

    public static string Standing(int rep) => rep switch
    {
        > 40 => "honoured",
        > 15 => "known",
        < -40 => "hated",
        < -15 => "mistrusted",
        _ => "unknown"
    };
}

using System;

namespace RpgSlice;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var game = new RpgSliceGame(args);
        game.Run();
    }
}

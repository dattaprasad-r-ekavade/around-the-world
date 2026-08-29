using System;

namespace FirstLight;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var game = new FirstLightGame(args);
        game.Run();
    }
}

using System;

namespace CharacterStudio;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var game = new CharacterStudioGame(args);
        game.Run();
    }
}

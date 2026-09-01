using System;

namespace Campaign;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var game = new CampaignGame(args);
        game.Run();
    }
}

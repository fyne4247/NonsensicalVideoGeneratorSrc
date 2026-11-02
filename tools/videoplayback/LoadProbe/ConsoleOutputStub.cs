using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator;

/// <summary>
/// VideoLoad logs through the in-app console. This probe is not the game.
/// </summary>
static class ConsoleOutput
{
    public static void WriteLine(string? line, Color? color = null)
    {
        if (line != null)
            Console.WriteLine(line);
    }
}

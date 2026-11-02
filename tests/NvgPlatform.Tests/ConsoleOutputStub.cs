using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator;

/// <summary>
/// The platform sources call the in-app console. The test assembly is not the game,
/// so this stub prints the same line without opening a window or a save file.
/// </summary>
public static class ConsoleOutput
{
    public static void WriteLine(string? line, Color? color = null)
    {
        if (line != null)
            Console.WriteLine(line);
    }
}

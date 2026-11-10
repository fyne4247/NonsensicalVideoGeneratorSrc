using System;
using Microsoft.Xna.Framework;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Diagnostic only. NVG_TRACE=1 logs File.Delete, File.Move, and Directory.Delete
    /// with a stack. Unset, this returns before any log and does not change the operation.
    /// </summary>
    public static class NvgTrace
    {
        public static void FileOp(string op, params object?[] parts)
        {
            if (Environment.GetEnvironmentVariable("NVG_TRACE") != "1")
                return;
            try
            {
                ConsoleOutput.WriteLine("NVG_TRACE " + op + " " + string.Join(" ", parts), Color.Yellow);
                ConsoleOutput.WriteLine(Environment.StackTrace ?? "", Color.Gray);
            }
            catch
            {
            }
        }
    }
}

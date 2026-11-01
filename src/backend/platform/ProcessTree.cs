using System;
using System.Diagnostics;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Kills direct child processes of this app. Does not scan every ffmpeg on the machine.
    /// </summary>
    public static class ProcessTree
    {
        public static void KillChildrenOf(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo("/bin/ps", "-ax -o pid= -o ppid=")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using Process? process = Process.Start(psi);
                if (process == null)
                    return;
                string text = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                foreach (string line in text.Split('\n'))
                {
                    string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;
                    if (!int.TryParse(parts[0], out int child) || !int.TryParse(parts[1], out int parent))
                        continue;
                    if (parent != pid || child == pid)
                        continue;
                    try
                    {
                        Process.GetProcessById(child).Kill(entireProcessTree: true);
                        Console.WriteLine("Killed child process " + child);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not kill child " + child + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Process tree kill failed: " + ex.Message);
            }
        }
    }
}

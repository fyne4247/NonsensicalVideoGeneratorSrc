using System;
using System.IO;
using System.Reflection;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Writable files go to the data root on DesktopGL.
    /// On Windows they stay in the assembly directory, which is where they were written before.
    /// Reads of bundled content use <see cref="Resources"/>.
    /// </summary>
    public static class AppPaths
    {
        public static string Resources => DataRoots.ResourceRoot;

        /// <summary>
        /// Directory this process may create files in.
        /// </summary>
        public static string Writable
        {
            get
            {
                string platform = DataRoots.PlatformKind();
                string data = DataRoots.DataRoot;
                if (!string.Equals(platform, "windows", StringComparison.Ordinal) && string.IsNullOrEmpty(data))
                    data = DataRoots.ResolveDataRoot();
                return Resolve(platform, data, AssemblyDirectory());
            }
        }

        /// <summary>
        /// Windows returns <paramref name="assemblyDirectory"/>.
        /// Every other platform returns <paramref name="dataRoot"/>.
        /// </summary>
        public static string Resolve(string platform, string? dataRoot, string? assemblyDirectory)
        {
            if (string.Equals(platform, "windows", StringComparison.Ordinal))
                return string.IsNullOrEmpty(assemblyDirectory) ? "." : assemblyDirectory;
            return dataRoot ?? "";
        }

        public static string AssemblyDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        }
    }
}

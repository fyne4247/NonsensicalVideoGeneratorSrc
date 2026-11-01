using System.IO;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// On Windows the path is returned unchanged.
    /// On macOS and Linux, backslashes become the native separator.
    /// </summary>
    public static class NvgPaths
    {
        public static string Native(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.DirectorySeparatorChar == '\\')
                return path;
            return path.Replace('\\', '/');
        }
    }
}

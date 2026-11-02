using System;
using System.IO;

namespace NonsensicalVideoGenerator
{
    /// <summary>
    /// Stock library paths are verbatim Windows strings ("audio\\music").
    /// Plugin Query stores Path.Join(root, name), which is "video\\fear" on Windows
    /// and "video/fear" on macOS and Linux. Subtype checks must accept both.
    /// An empty library pick joins to "..\\.." on Windows and "../.." elsewhere.
    /// </summary>
    public static class LibraryPathRules
    {
        public static bool Matches(string? stored, string? rootType, string? subType)
        {
            if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(rootType) || subType == null)
                return false;
            return Same(stored, rootType + "\\" + subType);
        }

        public static bool Same(string? left, string? right)
        {
            if (left == null || right == null)
                return false;
            return string.Equals(ToBackslash(left), ToBackslash(right), StringComparison.Ordinal);
        }

        public static string ToBackslash(string path)
        {
            return path.Replace('/', '\\');
        }

        /// <summary>
        /// True when GetRandomLibraryFile has nothing to hand back.
        /// A non-empty pick whose joined path is only the parent sentinel is also empty:
        /// Path.Join uses the native separator, so the Windows check against "..\\.." misses "../..".
        /// </summary>
        public static bool IsEmptyLibraryPick(string? file, string? joined)
        {
            if (string.IsNullOrEmpty(file))
                return true;
            if (string.IsNullOrEmpty(joined))
                return true;
            return ToBackslash(joined).TrimEnd('\\') == "..\\..";
        }

        /// <summary>
        /// SaveData stores these switches as "true" or "false".
        /// Generation may delete a library file only when this is true for DeleteClipsAfterMaxUniqueClips.
        /// </summary>
        public static bool FlagIsTrue(string? setting)
        {
            return bool.TryParse(setting, out bool value) && value;
        }

        /// <summary>
        /// True when a plugin path, after placeholder replacement, stays inside the job directory.
        /// Rejects Windows "..\", POSIX "../", a leading "/", and a drive-rooted path.
        /// Path.Combine drops the job directory when the replacement is absolute.
        /// </summary>
        public static bool StaysInsideJob(string? jobDirectory, string? replacedPath)
        {
            if (string.IsNullOrEmpty(jobDirectory) || string.IsNullOrWhiteSpace(replacedPath))
                return false;
            if (EscapesJob(replacedPath))
                return false;
            try
            {
                string root = Path.GetFullPath(jobDirectory);
                if (root[root.Length - 1] != Path.DirectorySeparatorChar && root[root.Length - 1] != Path.AltDirectorySeparatorChar)
                    root += Path.DirectorySeparatorChar;
                string combined = Path.GetFullPath(Path.Combine(jobDirectory, replacedPath));
                StringComparison compare = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return combined.StartsWith(root, compare);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool EscapesJob(string replacedPath)
        {
            if (replacedPath[0] == '/' || replacedPath[0] == '\\')
                return true;
            if (replacedPath.Length >= 2 && replacedPath[1] == ':' && char.IsLetter(replacedPath[0]))
                return true;
            string normalized = replacedPath.Replace('\\', '/');
            foreach (string part in normalized.Split('/'))
            {
                if (part == "..")
                    return true;
            }
            return false;
        }
    }
}

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
        /// Full path of a plugin argument after placeholder replacement.
        /// Backslashes are normalized so a Windows "..\library\..." form resolves on macOS tests too.
        /// An absolute replacement ignores the job directory, which is what Path.Combine does.
        /// </summary>
        public static string? ResolveReplaced(string? baseDirectory, string? replacedPath)
        {
            if (string.IsNullOrWhiteSpace(replacedPath))
                return null;
            string path = replacedPath.Replace('\\', '/');
            try
            {
                if (IsAbsolute(path))
                    return Path.GetFullPath(path);
                if (string.IsNullOrWhiteSpace(baseDirectory))
                    return null;
                return Path.GetFullPath(Path.Combine(baseDirectory, path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// True when the resolved path is the root directory or a file inside it.
        /// </summary>
        public static bool IsUnderRoot(string? root, string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
                return false;
            try
            {
                string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                StringComparison compare = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (string.Equals(candidate, rootFull, compare))
                    return true;
                return candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, compare);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Delete, move, and overwrite may touch this path only when it stays inside the job
        /// and outside the library. Normal generation must not move or delete library files.
        /// </summary>
        public static bool CanAlter(string? jobDirectory, string? libraryRoot, string? replacedPath)
        {
            string? full = ResolveReplaced(jobDirectory, replacedPath);
            if (full == null)
                return false;
            if (IsUnderRoot(libraryRoot, full))
                return false;
            return IsUnderRoot(jobDirectory, full);
        }

        /// <summary>
        /// A copy source may be a job file or a library file. Addons read library files into the job.
        /// </summary>
        public static bool CanCopyFrom(string? jobDirectory, string? libraryRoot, string? replacedPath)
        {
            string? full = ResolveReplaced(jobDirectory, replacedPath);
            if (full == null)
                return false;
            if (IsUnderRoot(libraryRoot, full))
                return true;
            return IsUnderRoot(jobDirectory, full);
        }

        /// <summary>
        /// True when both paths name the same file after normalization.
        /// Organize must not delete that file when a move onto itself fails.
        /// </summary>
        public static bool SameFile(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;
            try
            {
                StringComparison compare = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), compare);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool IsAbsolute(string path)
        {
            if (path.Length >= 1 && path[0] == '/')
                return true;
            if (path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]))
                return true;
            return false;
        }
    }
}

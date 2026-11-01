using System;

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
    }
}

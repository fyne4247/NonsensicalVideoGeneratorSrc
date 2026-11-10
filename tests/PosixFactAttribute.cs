using System;

namespace Xunit;

/// <summary>Marks checks of native macOS/Linux behavior or POSIX-only helpers.</summary>
public sealed class PosixFactAttribute : FactAttribute
{
    public PosixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Requires macOS/Linux behavior or POSIX helpers; Windows uses WindowsDX.";
    }
}

using System.Runtime.InteropServices;

namespace Sharlayan.Core.Native;

public static class NativePlatform
{
    public static INativeMemory CreateMemory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxNativeMemory();

        throw new PlatformNotSupportedException(
            $"Sharlayan.Core does not yet have a native memory backend for {RuntimeInformation.OSDescription}. " +
            "Implement INativeMemory for this platform.");
    }
}

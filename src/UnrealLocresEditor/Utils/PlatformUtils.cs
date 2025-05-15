using System.Runtime.InteropServices;

namespace UnrealLocresEditor.Utils
{
    public static class PlatformUtils
    {
        public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }
}
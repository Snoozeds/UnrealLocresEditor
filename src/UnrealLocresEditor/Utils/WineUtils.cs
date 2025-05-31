using System;
using System.Diagnostics;
using System.IO;

namespace UnrealLocresEditor.Utils
{
    public static class WineUtils
    {
        public static string WinePrefixDirectory { get; } =
            Path.Combine(Directory.GetCurrentDirectory(), "wineprefix");

        public static void InitializeWinePrefix()
        {
            if (PlatformUtils.IsLinux() && !Directory.Exists(WinePrefixDirectory))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wineboot",
                        Arguments = $"--init",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                };

                process.StartInfo.Environment["WINEPREFIX"] = WinePrefixDirectory;
                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception(
                        $"Error initializing wine prefix: {process.StandardError.ReadToEnd()}"
                    );
                }
            }
        }
    }
}

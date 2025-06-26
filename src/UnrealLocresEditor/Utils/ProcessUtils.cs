using System.Diagnostics;
using System.Text;

namespace UnrealLocresEditor.Utils
{
    public static class ProcessUtils
    {
        public static string GetExecutablePath(bool useWine)
        {
            if (PlatformUtils.IsLinux())
                return useWine ? "wine UnrealLocres.exe" : "./UnrealLocres";
            else // Windows
                return "UnrealLocres.exe";
        }

        public static ProcessStartInfo GetProcessStartInfo(
            string command,
            string locresFilePath,
            bool useWine,
            string csvFileName = null
        )
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(useWine),
                Arguments = GetArguments(command, locresFilePath, useWine, csvFileName),
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            if (PlatformUtils.IsLinux() && useWine)
                startInfo.Environment["WINEPREFIX"] = WineUtils.WinePrefixDirectory;

            return startInfo;
        }

        private static string GetArguments(
            string command,
            string locresFilePath,
            bool useWine,
            string csvFileName = null
        )
        {
            if (PlatformUtils.IsLinux())
            {
                if (useWine)
                {
                    return csvFileName == null
                        ? $"UnrealLocres.exe {command} \"{locresFilePath}\""
                        : $"UnrealLocres.exe {command} \"{locresFilePath}\" \"{csvFileName}\"";
                }
                else
                {
                    return csvFileName == null
                        ? $"./UnrealLocres {command} \"{locresFilePath}\""
                        : $"./UnrealLocres {command} \"{locresFilePath}\" \"{csvFileName}\"";
                }
            }
            else
            {
                return csvFileName == null
                    ? $"{command} \"{locresFilePath}\""
                    : $"{command} \"{locresFilePath}\" \"{csvFileName}\"";
            }
        }

        public static ProcessStartInfo GetMergeProcessStartInfo(
            string targetLocresPath,
            string sourceLocresPath,
            bool useWine,
            string outputPath = null
        )
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(useWine),
                Arguments = GetMergeArguments(
                    targetLocresPath,
                    sourceLocresPath,
                    useWine,
                    outputPath
                ),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            if (PlatformUtils.IsLinux() && useWine)
                startInfo.Environment["WINEPREFIX"] = WineUtils.WinePrefixDirectory;
            return startInfo;
        }

        private static string GetMergeArguments(
            string targetLocresPath,
            string sourceLocresPath,
            bool useWine,
            string outputPath = null
        )
        {
            var args = new StringBuilder();
            args.Append("merge ");
            args.Append($"\"{targetLocresPath}\" \"{sourceLocresPath}\"");

            if (!string.IsNullOrEmpty(outputPath))
                args.Append($" -o \"{outputPath}\"");

            return args.ToString();
        }
    }
}

using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnrealLocresEditor.Utils;

public class AutoUpdater
{
    private const string VersionUrl = "https://raw.githubusercontent.com/Snoozeds/UnrealLocresEditor/main/version.txt";
    private const string LocalVersionFile = "version.txt";
    private const string TempUpdatePath = "update.zip";
    private readonly INotificationManager _notificationManager;
    private AppConfig _appConfig;

    public AutoUpdater(INotificationManager notificationManager)
    {
        _notificationManager = notificationManager;
    }

    public async Task CheckForUpdates()
    {

        if (System.Diagnostics.Debugger.IsAttached)
        {
            Console.WriteLine("Skipping update check - debug mode.");
            return;
        }

        try
        {
            string latestVersion = await GetLatestVersion();
            string currentVersion = File.Exists(LocalVersionFile) ? File.ReadAllText(LocalVersionFile) : "0.0.0";

            if (latestVersion != currentVersion)
            {
                await ShowUpdateNotification();

                string platformSpecificUrl = GetPlatformSpecificUrl(latestVersion);
                await DownloadUpdate(platformSpecificUrl);
                LaunchUpdateProcess();
            }
            else
            {
                Console.WriteLine("You are running the latest version.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking for updates: {ex.Message}");
        }
    }

    private async Task ShowUpdateNotification()
    {
        var notification = new Notification
        {
            Title = "Update in progress",
            Message = "Unsaved work will be lost.",
            Type = NotificationType.Information,
            Expiration = TimeSpan.FromSeconds(10),
        };

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _notificationManager.Show(notification);
        });
    }

    private void LaunchUpdateProcess()
    {
        string currentProcessId = Process.GetCurrentProcess().Id.ToString();
        string currentExePath = Process.GetCurrentProcess().MainModule.FileName;
        string updateBatchPath = Path.Combine(Path.GetTempPath(), "update.bat");

        string batchContent = @$"
@echo off
timeout /t 1 /nobreak >nul
:loop
tasklist /fi ""PID eq {currentProcessId}"" 2>nul | find ""{currentProcessId}"" >nul
if errorlevel 1 (
    powershell -Command ""Expand-Archive -Path '{TempUpdatePath}' -DestinationPath '{AppDomain.CurrentDomain.BaseDirectory}' -Force""
    del ""{TempUpdatePath}""
    start """" ""{currentExePath}""
    del ""%~f0""
    exit
) else (
    timeout /t 1 /nobreak >nul
    goto loop
)";

        File.WriteAllText(updateBatchPath, batchContent);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start /min \"\" \"{updateBatchPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true
        });

        Environment.Exit(0);
    }

    private async Task<string> GetLatestVersion()
    {
        using (HttpClient client = new HttpClient())
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(VersionUrl);
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to fetch version file. HTTP Status Code: {response.StatusCode}");
                }
                string version = await response.Content.ReadAsStringAsync();
                return version.Replace("\r", "").Replace("\n", "").Trim();
            }
            catch (Exception ex)
            {
                throw new Exception("Unexpected error: " + ex.Message);
            }
        }
    }

    private string GetPlatformSpecificUrl(string version)
    {
        string os = GetOperatingSystem();
        string arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        string fileName = $"UnrealLocresEditor-{version}-{os}-{arch}.zip";
        return $"https://github.com/Snoozeds/UnrealLocresEditor/releases/latest/download/{fileName}";
    }

    private string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }
        else
        {
            throw new NotSupportedException("Unsupported OS platform.");
        }
    }

    private async Task DownloadUpdate(string url)
    {
        using (HttpClient client = new HttpClient())
        {
            byte[] updateData = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(TempUpdatePath, updateData);
        }
    }
}

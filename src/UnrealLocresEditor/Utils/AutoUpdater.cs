using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnrealLocresEditor.Utils;
using UnrealLocresEditor.Views;

public class AutoUpdater
{
    private const string VersionUrl = "https://raw.githubusercontent.com/Snoozeds/UnrealLocresEditor/main/version.txt";
    private const string LocalVersionFile = "version.txt";
    private const string TempUpdatePath = "update.zip";
    private readonly INotificationManager _notificationManager;
    private readonly MainWindow _mainWindow;
    private AppConfig _appConfig;

    public AutoUpdater(INotificationManager notificationManager, MainWindow mainWindow)
    {
        _notificationManager = notificationManager;
        _mainWindow = mainWindow;
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
                // Check for unsaved changes
                if (_mainWindow._hasUnsavedChanges)
                {
                    var result = await ShowUpdateConfirmDialog();
                    if (result != "Update")
                    {
                        return;
                    }
                }

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
            throw;
        }
    }

    private async Task<string> ShowUpdateConfirmDialog()
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = "You have unsaved changes. Would you like to save your changes before updating?",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new Button { Content = "Save and Update" },
                            new Button { Content = "Update without Saving" },
                            new Button { Content = "Cancel" }
                        }
                    }
                }
            }
        };

        var taskCompletionSource = new TaskCompletionSource<string>();

        var buttons = ((StackPanel)((StackPanel)dialog.Content).Children[1]).Children;
        ((Button)buttons[0]).Click += async (s, e) =>
        {
            try
            {
                _mainWindow.SaveEditedData();
                taskCompletionSource.SetResult("Update");
                dialog.Close();
            }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification(
                    "Save Error",
                    $"Failed to save changes: {ex.Message}",
                    NotificationType.Error));
                taskCompletionSource.SetResult("Cancel");
                dialog.Close();
            }
        };

        ((Button)buttons[1]).Click += (s, e) =>
        {
            taskCompletionSource.SetResult("Update");
            dialog.Close();
        };

        ((Button)buttons[2]).Click += (s, e) =>
        {
            taskCompletionSource.SetResult("Cancel");
            dialog.Close();
        };

        await dialog.ShowDialog(_mainWindow);
        return await taskCompletionSource.Task;
    }
    private async Task ShowUpdateNotification()
    {
        var notification = new Notification
        {
            Title = "Update in progress",
            Message = "The application will restart after the update.",
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
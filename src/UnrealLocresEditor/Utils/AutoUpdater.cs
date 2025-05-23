﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UnrealLocresEditor.Utils;
using UnrealLocresEditor.Views;

public class AutoUpdater
{
    private const string VersionUrl =
        "https://raw.githubusercontent.com/Snoozeds/UnrealLocresEditor/main/version.txt";
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

    public async Task CheckForUpdates(bool manualCheck = false)
    {
        if (System.Diagnostics.Debugger.IsAttached)
        {
            Console.WriteLine("Skipping update check - debug mode.");
            return;
        }

        try
        {
            string latestVersion = await GetLatestVersion();
            string currentVersion = File.Exists(LocalVersionFile)
                ? File.ReadAllText(LocalVersionFile).Replace("\r", "").Replace("\n", "").TrimEnd()
                : "0.0.0";

            if (latestVersion != currentVersion)
            {
                // Show a dialog asking the user if they want to update
                if (manualCheck)
                {
                    var manualUpdateDialog = await ShowManualUpdateDialog(latestVersion);
                    if (manualUpdateDialog != "Update")
                    {
                        return;
                    }
                }
                else
                {
                    if (_mainWindow._hasUnsavedChanges)
                    {
                        var result = await ShowUpdateConfirmDialog();
                        if (result != "Update")
                        {
                            return;
                        }
                    }
                }

                await ShowUpdateNotification();
                string platformSpecificUrl = GetPlatformSpecificUrl(latestVersion);
                await DownloadUpdate(platformSpecificUrl);
                LaunchUpdateProcess();
            }
            else
            {
                if (manualCheck)
                {
                    _notificationManager.Show(
                        new Notification(
                            "No Updates Available",
                            "You are running the latest version.",
                            NotificationType.Information
                        )
                    );
                }
                else
                {
                    Console.WriteLine("You are running the latest version.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking for updates: {ex.Message}");

            if (manualCheck)
            {
                _notificationManager.Show(
                    new Notification(
                        "Update Check Failed",
                        $"Could not check for updates: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
            else
            {
                throw;
            }
        }
    }

    private async Task<string> ShowManualUpdateDialog(string latestVersion)
    {
        var dialog = new Window
        {
            Title = "Update Available",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text =
                            $"A new version {latestVersion} is available. Would you like to update?",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new Button { Content = "Update" },
                            new Button { Content = "Cancel" },
                        },
                    },
                },
            },
        };

        var taskCompletionSource = new TaskCompletionSource<string>();

        var buttons = ((StackPanel)((StackPanel)dialog.Content).Children[1]).Children;
        ((Button)buttons[0]).Click += (s, e) =>
        {
            taskCompletionSource.SetResult("Update");
            dialog.Close();
        };

        ((Button)buttons[1]).Click += (s, e) =>
        {
            taskCompletionSource.SetResult("Cancel");
            dialog.Close();
        };

        await dialog.ShowDialog(_mainWindow);
        return await taskCompletionSource.Task;
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
                        Text =
                            "You have unsaved changes. Would you like to save your changes before updating?",
                        TextWrapping = TextWrapping.Wrap,
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
                            new Button { Content = "Cancel" },
                        },
                    },
                },
            },
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
                _notificationManager.Show(
                    new Notification(
                        "Save Error",
                        $"Failed to save changes: {ex.Message}",
                        NotificationType.Error
                    )
                );
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
            Expiration = TimeSpan.FromSeconds(9999),
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
        string updateScriptPath = Path.Combine(Path.GetTempPath(), "update_script");

        string scriptContent;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows script
            scriptContent =
                @$"
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
            File.WriteAllText(updateScriptPath + ".bat", scriptContent);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux script
            scriptContent =
                @$"
#!/bin/bash
while true; do
    if ! ps -p {currentProcessId} > /dev/null; then
        unzip -o {TempUpdatePath} -d {AppDomain.CurrentDomain.BaseDirectory}
        rm {TempUpdatePath}
        nohup {currentExePath} &
        rm -- ""$0""
        exit
    else
        sleep 1
    fi
done";
            File.WriteAllText(updateScriptPath + ".sh", scriptContent);
            // Make the script executable
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x {updateScriptPath}.sh",
                    UseShellExecute = true,
                }
            );
        }
        else
        {
            throw new NotSupportedException("Unsupported OS platform.");
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash",
                Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? $"/c start /min \"\" \"{updateScriptPath}.bat\""
                    : updateScriptPath + ".sh",
                UseShellExecute = true,
                CreateNoWindow = true,
            }
        );

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
                    throw new Exception(
                        $"Failed to fetch version file. HTTP Status Code: {response.StatusCode}"
                    );
                }
                string version = await response.Content.ReadAsStringAsync();
                return version.Replace("\r", "").Replace("\n", "").TrimEnd();
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

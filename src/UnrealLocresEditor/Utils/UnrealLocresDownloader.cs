using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Media;

namespace UnrealLocresEditor.Utils
{
    public class UnrealLocresDownloader
    {
        private const string UnrealLocresReleaseUrl =
            "https://github.com/akintos/UnrealLocres/releases/latest/download/UnrealLocres.exe";
        private readonly WindowNotificationManager _notificationManager;
        private readonly Window _parentWindow;

        public UnrealLocresDownloader(
            Window parentWindow,
            WindowNotificationManager notificationManager
        )
        {
            _parentWindow = parentWindow;
            _notificationManager = notificationManager;
        }

        public async Task<bool> CheckAndDownloadUnrealLocres()
        {
            var unrealLocresExePath = Path.Combine(
                Path.GetDirectoryName(Environment.ProcessPath),
                "UnrealLocres.exe"
            );
            if (!File.Exists(unrealLocresExePath))
            {
                return await PromptAndDownloadUnrealLocres();
            }
            return true;
        }

        private async Task<bool> PromptAndDownloadUnrealLocres()
        {
            var dialog = new Window
            {
                Title = "UnrealLocres Required",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock
                        {
                            Text =
                                "UnrealLocres is required but not found. Would you like to download it now?",
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                new Button { Content = "Download" },
                                new Button { Content = "Cancel" },
                            },
                        },
                    },
                },
            };

            var result = await ShowCustomDialog(dialog);

            if (result == "Download")
            {
                return await DownloadUnrealLocres();
            }

            return false;
        }

        private async Task<bool> DownloadUnrealLocres()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    _notificationManager.Show(
                        new Notification(
                            "Downloading",
                            "Downloading UnrealLocres...",
                            NotificationType.Information
                        )
                    );

                    var response = await client.GetByteArrayAsync(UnrealLocresReleaseUrl);
                    var exeDirectory = Directory.GetCurrentDirectory();
                    var unrealLocresPath = Path.Combine(exeDirectory, "UnrealLocres.exe");

                    await File.WriteAllBytesAsync(unrealLocresPath, response);

                    _notificationManager.Show(
                        new Notification(
                            "Success",
                            "UnrealLocres has been downloaded successfully.",
                            NotificationType.Success
                        )
                    );

                    return true;
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        $"Failed to download UnrealLocres: {ex.Message}",
                        NotificationType.Error
                    )
                );
                return false;
            }
        }

        private TaskCompletionSource<string> _dialogResult;

        private async Task<string> ShowCustomDialog(Window dialog)
        {
            _dialogResult = new TaskCompletionSource<string>();

            var buttons = (
                (StackPanel)((StackPanel)dialog.Content).Children[1]
            ).Children.OfType<Button>();

            foreach (var button in buttons)
            {
                button.Click += (s, e) =>
                {
                    _dialogResult.SetResult(((Button)s).Content.ToString());
                    dialog.Close();
                };
            }

            await dialog.ShowDialog(_parentWindow);
            return await _dialogResult.Task;
        }
    }
}

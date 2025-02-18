using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
using System.IO;

namespace UnrealLocresEditor.Views
{
    public partial class AboutWindow : Window
    {
        public string AppName { get; } = "Unreal Locres Editor";
        public string Version { get; private set; } = "Version: Unknown";
        private const string GitHubUrl = "https://github.com/Snoozeds/UnrealLocresEditor";
        private WindowNotificationManager _notificationManager;
        private MainWindow _mainWindow;

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void Initialize(WindowNotificationManager notificationManager, MainWindow mainWindow)
        {
            _notificationManager = notificationManager;
            _mainWindow = mainWindow;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            LoadVersion();
        }

        private void LoadVersion()
        {
            try
            {
                string versionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");
                if (File.Exists(versionPath))
                {
                    string version = File.ReadAllText(versionPath).Trim();
                    Version = $"Version: {version}";
                }
            }
            catch (Exception)
            {
                Version = "Version: Error loading version";
            }
        }

        private void OnGitHubButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = GitHubUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception)
            {
            }
        }

        private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
        {
            if (_notificationManager == null || _mainWindow == null)
            {
                return;
            }

            AutoUpdater updater = new AutoUpdater(_notificationManager, _mainWindow);
            try
            {
                await updater.CheckForUpdates(true);
                _notificationManager.Show(new Notification(
                    "Update Check",
                    "Your application is up to date!",
                    NotificationType.Information));
            }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification(
                    "Update Check Failed",
                    $"Could not check for updates: {ex.Message}",
                    NotificationType.Error));
            }
        }
    }
}
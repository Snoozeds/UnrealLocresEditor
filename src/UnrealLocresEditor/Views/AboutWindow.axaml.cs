﻿using Avalonia.Controls;
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

        public AboutWindow(WindowNotificationManager notificationManager)
        {
            InitializeComponent();
            DataContext = this;
            _notificationManager = notificationManager;
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
            AutoUpdater updater = new AutoUpdater(_notificationManager);
            try
            {
                await updater.CheckForUpdates();
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
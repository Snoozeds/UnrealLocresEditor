using Avalonia.Controls;
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

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = this;
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
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using UnrealLocresEditor.Utils;
using UnrealLocresEditor.Views;

#nullable disable

namespace UnrealLocresEditor.ViewModels
{
    public class PreferencesWindowViewModel : ReactiveObject
    {
        private readonly Window _window;
        private readonly MainWindow _mainWindow;
        private bool _isDarkTheme;
        private Color _accentColor;
        private DiscordRPC _discordRPC;
        private bool _discordRPCEnabled;
        private bool _discordRPCPrivacy;
        private string _discordRPCPrivacyString;
        private bool _useWine;
        private TimeSpan _selectedAutoSaveInterval;
        private bool _autoSaveEnabled;
        private bool _autoUpdateEnabled;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                var hexColor = value.ToString();

                // Ensure it's a valid hex color
                if (AppConfig.IsValidHexColor(hexColor))
                {
                    this.RaiseAndSetIfChanged(ref _accentColor, value);
                }
                else
                {
                    Console.WriteLine(
                        $"Invalid AccentColor selected: {hexColor}. Reverting to default."
                    );
                    this.RaiseAndSetIfChanged(ref _accentColor, Color.Parse("#4e3cb2"));
                }
            }
        }

        public bool DiscordRPCEnabled
        {
            get => _discordRPCEnabled;
            set => this.RaiseAndSetIfChanged(ref _discordRPCEnabled, value);
        }

        public bool DiscordRPCPrivacy
        {
            get => _discordRPCPrivacy;
            set => this.RaiseAndSetIfChanged(ref _discordRPCPrivacy, value);
        }

        public string DiscordRPCPrivacyString
        {
            get => _discordRPCPrivacyString;
            set => this.RaiseAndSetIfChanged(ref _discordRPCPrivacyString, value);
        }

        public bool UseWine
        {
            get => _useWine;
            set => this.RaiseAndSetIfChanged(ref _useWine, value);
        }

        public TimeSpan SelectedAutoSaveInterval
        {
            get => _selectedAutoSaveInterval;
            set => this.RaiseAndSetIfChanged(ref _selectedAutoSaveInterval, value);
        }

        public IEnumerable<TimeSpan> AutoSaveIntervals { get; } =
            new[]
            {
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(30),
            };
        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoSaveEnabled, value);
        }
        public bool AutoUpdateEnabled
        {
            get => _autoUpdateEnabled;
            set => this.RaiseAndSetIfChanged(ref _autoUpdateEnabled, value);
        }

        public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public PreferencesWindowViewModel(Window window, MainWindow mainWindow)
        {
            _window = window;
            _mainWindow = mainWindow;

            // Load current settings
            var config = AppConfig.Instance;
            IsDarkTheme = config.IsDarkTheme;
            AccentColor = Color.Parse(config.AccentColor);
            DiscordRPCEnabled = config.DiscordRPCEnabled;
            DiscordRPCPrivacy = config.DiscordRPCPrivacy;
            DiscordRPCPrivacyString = config.DiscordRPCPrivacyString;
            UseWine = config.UseWine;
            SelectedAutoSaveInterval = config.AutoSaveInterval;
            AutoSaveEnabled = config.AutoSaveEnabled;
            AutoUpdateEnabled = config.AutoUpdateEnabled;

            if (!AutoSaveIntervals.Contains(SelectedAutoSaveInterval))
            {
                SelectedAutoSaveInterval = TimeSpan.FromMinutes(5);
            }

            // Initialize commands
            SaveCommand = ReactiveCommand.Create(Save);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        private void Save()
        {
            var config = AppConfig.Instance;
            config.IsDarkTheme = IsDarkTheme;
            config.AccentColor = AccentColor.ToString();
            config.DiscordRPCEnabled = DiscordRPCEnabled;
            config.DiscordRPCPrivacy = DiscordRPCPrivacy;
            config.DiscordRPCPrivacyString = DiscordRPCPrivacyString;
            config.UseWine = UseWine;
            config.AutoSaveInterval = SelectedAutoSaveInterval;
            config.AutoSaveEnabled = AutoSaveEnabled;
            config.AutoUpdateEnabled = AutoUpdateEnabled;

            _discordRPC?.UpdatePresence(DiscordRPCEnabled, _mainWindow._currentLocresFilePath);

            config.Save();
            Console.WriteLine("Config saved.");
            _window.Close();
        }

        private void Cancel()
        {
            _window.Close();
        }
    }
}

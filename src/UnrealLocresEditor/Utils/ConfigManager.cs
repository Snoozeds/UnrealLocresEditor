using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace UnrealLocresEditor.Utils
{
    public static class DefaultConfig
    {
        public static readonly bool IsDarkTheme = true;
        public static readonly string AccentColor = "#4e3cb2";
        public static readonly bool DiscordRPCEnabled = true;
        public static readonly bool DiscordRPCPrivacy = false;
        public static readonly string DiscordRPCPrivacyString = "Editing a file";
        public static readonly bool UseWine = false;
        public static readonly TimeSpan AutoSaveInterval = TimeSpan.FromMinutes(5);
        public static readonly bool AutoSaveEnabled = true;
        public static readonly bool AutoUpdateEnabled = true;
        public static readonly double DefaultColumnWidth = 300;
    }

    public class AppConfig
    {
        private static AppConfig? _instance;
        private static readonly object _lock = new object();

        public bool IsDarkTheme { get; set; } = DefaultConfig.IsDarkTheme;
        public string AccentColor { get; set; } = DefaultConfig.AccentColor;
        public bool DiscordRPCEnabled { get; set; } = DefaultConfig.DiscordRPCEnabled;
        public bool DiscordRPCPrivacy { get; set; } = DefaultConfig.DiscordRPCPrivacy;
        public string DiscordRPCPrivacyString { get; set; } = DefaultConfig.DiscordRPCPrivacyString;
        public bool UseWine { get; set; } = DefaultConfig.UseWine;
        public TimeSpan AutoSaveInterval { get; set; } = DefaultConfig.AutoSaveInterval;
        public bool AutoSaveEnabled { get; set; } = DefaultConfig.AutoSaveEnabled;
        public bool AutoUpdateEnabled { get; set; } = DefaultConfig.AutoUpdateEnabled;
        public double DefaultColumnWidth { get; set; } = DefaultConfig.DefaultColumnWidth;

        public static AppConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= Load();
                    }
                }
                return _instance;
            }
        }

        private static string GetConfigDirectory()
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "UnrealLocresEditor"
                );
            }
            else if (OperatingSystem.IsLinux())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config",
                    "UnrealLocresEditor"
                );
            }

            throw new PlatformNotSupportedException("Unsupported OS.");
        }

        private static string GetConfigFilePath()
        {
            string configDirectory = GetConfigDirectory();
            Directory.CreateDirectory(configDirectory);
            return Path.Combine(configDirectory, "config.json");
        }

        private static Dictionary<string, Func<AppConfig, bool>> GetValidationRules()
        {
            return new Dictionary<string, Func<AppConfig, bool>>()
            {
                {
                    "IsDarkTheme",
                    config => config.IsDarkTheme == true || config.IsDarkTheme == false
                },
                { "AccentColor", config => IsValidHexColor(config.AccentColor) },
                {
                    "DiscordRPCEnabled",
                    config => config.DiscordRPCEnabled == true || config.DiscordRPCEnabled == false
                },
                {
                    "DiscordRPCPrivacy",
                    config => config.DiscordRPCPrivacy == true || config.DiscordRPCPrivacy == false
                },
                {
                    "DiscordRPCPrivacyString",
                    config => !string.IsNullOrEmpty(config.DiscordRPCPrivacyString)
                },
                { "UseWine", config => config.UseWine == true || config.UseWine == false },
                {
                    "AutoSaveInterval",
                    config =>
                        config.AutoSaveInterval > TimeSpan.Zero
                        && config.AutoSaveInterval.TotalMilliseconds <= int.MaxValue
                },
                {
                    "AutoSaveEnabled",
                    config => config.AutoSaveEnabled == true || config.AutoSaveEnabled == false
                },
                {
                    "AutoUpdateEnabled",
                    config => config.AutoUpdateEnabled == true || config.AutoUpdateEnabled == false
                },
                {
                    "DefaultColumnWidth",
                    config => config.DefaultColumnWidth > 0 && config.DefaultColumnWidth <= 2500
                },
            };
        }

        public static bool IsValidHexColor(string color)
        {
            // Avalonia only seems to support 6 digit hex codes (not including #)
            if (color.Length > 7)
            {
                color = color.Substring(0, 7);
            }
            string hexColorPattern = @"^#[0-9A-Fa-f]{6}$";
            return Regex.IsMatch(color.Trim(), hexColorPattern);
        }

        public static AppConfig Load()
        {
            try
            {
                string filePath = GetConfigFilePath();

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);

                    if (config != null)
                    {
                        var validationRules = GetValidationRules();

                        foreach (var rule in validationRules)
                        {
                            var property = typeof(AppConfig).GetProperty(rule.Key);
                            if (property != null)
                            {
                                var value = property.GetValue(config);
                                if (!rule.Value(config))
                                {
                                    // If validation fails, revert to the default config value
                                    property.SetValue(
                                        config,
                                        typeof(DefaultConfig).GetProperty(rule.Key)?.GetValue(null)
                                    );
                                }
                            }
                        }

                        return config;
                    }
                }
            }
            catch (Exception) { }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                Console.WriteLine(
                    $"Saving config: {JsonConvert.SerializeObject(this, Formatting.Indented)}"
                );
                string filePath = GetConfigFilePath();
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to save config: {e}");
            }
        }
    }
}

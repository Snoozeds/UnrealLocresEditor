using Newtonsoft.Json;
using System;
using System.IO;

namespace UnrealLocresEditor.Config
{
    public static class DefaultConfig
    {
        public static readonly bool DiscordRPCEnabled = true;
        public static readonly bool UseWine = true;
        public static readonly TimeSpan AutoSaveInterval = TimeSpan.FromMinutes(5);
    }

    public class AppConfig
    {
        private static AppConfig ?_instance;
        private static readonly object _lock = new object();

        public bool DiscordRPCEnabled { get; set; } = DefaultConfig.DiscordRPCEnabled;
        public bool UseWine { get; set; } = DefaultConfig.UseWine;
        public TimeSpan AutoSaveInterval { get; set; } = DefaultConfig.AutoSaveInterval;

        public AppConfig() { }

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
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnrealLocresEditor");
            }
            else if (OperatingSystem.IsLinux())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "UnrealLocresEditor");
            }

            throw new PlatformNotSupportedException("Unsupported OS.");
        }

        private static string GetConfigFilePath()
        {
            string configDirectory = GetConfigDirectory();
            Directory.CreateDirectory(configDirectory);
            return Path.Combine(configDirectory, "config.json");
        }

        private static bool IsValidBoolean(bool? value)
        {
            return value != null;
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
                        // Validate config
                        if (!IsValidBoolean(config.DiscordRPCEnabled))
                        {
                            config.DiscordRPCEnabled = DefaultConfig.DiscordRPCEnabled;
                        }
                        if (!IsValidBoolean(config.UseWine))
                        {
                            config.UseWine = DefaultConfig.UseWine;
                        }
                        if (config.AutoSaveInterval <= TimeSpan.Zero || config.AutoSaveInterval.TotalMilliseconds > int.MaxValue)
                        {
                            config.AutoSaveInterval = DefaultConfig.AutoSaveInterval;
                        }
                        return config;
                    }
                }
            }
            catch (Exception)
            {
            }

            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                string filePath = GetConfigFilePath();
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception)
            {
            }
        }
    }
}

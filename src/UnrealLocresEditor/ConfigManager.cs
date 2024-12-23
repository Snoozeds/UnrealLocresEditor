using Newtonsoft.Json;
using System;
using System.IO;

namespace UnrealLocresEditor.Config
{
    public class AppConfig
    {
        private static AppConfig _instance;
        private static readonly object _lock = new object();

        public bool DiscordRPCEnabled { get; set; } = true;
        public bool UseWine { get; set; } = true;

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

        public static AppConfig Load()
        {
            try
            {
                string filePath = GetConfigFilePath();

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    return config ?? new AppConfig();
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

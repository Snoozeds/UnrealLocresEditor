using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CsvHelper;
using CsvHelper.Configuration;
using DiscordRPC;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

#nullable disable

namespace UnrealLocresEditor.Views
{
    public partial class MainWindow : Window
    {
        public DataGrid _dataGrid;
        private TextBox _searchTextBox;
        public ObservableCollection<DataRow> _rows;
        private string _currentLocresFilePath;
        private WindowNotificationManager _notificationManager;

        public bool DiscordRPCEnabled { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += OnWindowLoaded;
            this.Closing += OnWindowClosing;
#if DEBUG
            this.AttachDevTools();
#endif
            _rows = new ObservableCollection<DataRow>();
            DataContext = this;

            idleStartTime = DateTime.UtcNow;
        }

        public DiscordRpcClient client;
        private DateTime? editStartTime;
        private DateTime? idleStartTime;

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _notificationManager = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 1
            };

            // Discord RPC
            client = new DiscordRpcClient("1251663992162619472");

            client.OnReady += (sender, e) =>
            {
                Console.WriteLine("Received Ready from user {0}", e.User.Username);
            };

            client.OnPresenceUpdate += (sender, e) =>
            {
                Console.WriteLine("Received Update! {0}", e.Presence);
            };

            client.Initialize();
            UpdatePresence(DiscordRPCEnabled);
        }

        private void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            client?.ClearPresence();
            client?.Dispose();
        }

        private void UpdatePresence(bool enabled)
        {
            if (enabled)
            {
                // Restart the client if it's disposed
                if (client == null || client.IsDisposed)
                {
                    client = new DiscordRpcClient("1251663992162619472");

                    client.OnReady += (sender, e) =>
                    {
                        Console.WriteLine("Received Ready from user {0}", e.User.Username);
                    };

                    client.OnPresenceUpdate += (sender, e) =>
                    {
                        Console.WriteLine("Received Update! {0}", e.Presence);
                    };

                    client.Initialize();
                }

                var presence = new RichPresence();

                if (_currentLocresFilePath == null)
                {
                    presence.Details = "Idling";
                    presence.Timestamps = idleStartTime.HasValue ? new Timestamps(idleStartTime.Value) : null;
                }
                else
                {
                    presence.Details = $"Editing file: {Path.GetFileName(_currentLocresFilePath)}";
                    presence.Timestamps = editStartTime.HasValue ? new Timestamps(editStartTime.Value) : null;
                }

                client.SetPresence(presence);
            }
            else
            {
                if (client != null && !client.IsDisposed)
                {
                    client.ClearPresence();
                    client.Dispose();
                    client = null;
                }
            }
        }

        private static string winePrefixDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wineprefix");

        private static bool IsLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        private static string GetExecutablePath()
        {
            return IsLinux() ? "wine" : "UnrealLocres.exe";
        }

        private static string GetArguments(string command, string locresFilePath, string csvFileName = null)
        {
            if (IsLinux())
            {
                return csvFileName == null
                    ? $"UnrealLocres.exe {command} \"{locresFilePath}\""
                    : $"UnrealLocres.exe {command} \"{locresFilePath}\" \"{csvFileName}\"";
            }
            else
            {
                return csvFileName == null
                    ? $"{command} \"{locresFilePath}\""
                    : $"{command} \"{locresFilePath}\" \"{csvFileName}\"";
            }
        }

        private static ProcessStartInfo GetProcessStartInfo(string command, string locresFilePath, string csvFileName = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(),
                Arguments = GetArguments(command, locresFilePath, csvFileName),
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            if (IsLinux())
            {
                startInfo.Environment["WINEPREFIX"] = winePrefixDirectory;
            }

            return startInfo;
        }

        private static void InitializeWinePrefix()
        {
            if (IsLinux() && !Directory.Exists(winePrefixDirectory))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wineboot",
                        Arguments = $"--init",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.StartInfo.Environment["WINEPREFIX"] = winePrefixDirectory;
                process.Start();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Error initializing wine prefix: {process.StandardError.ReadToEnd()}");
                }
            }
        }

        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                FileTypeFilter = new[] { new FilePickerFileType("Localization Files") { Patterns = new[] { "*.locres" } } },
                AllowMultiple = false
            });

            if (result != null && result.Count > 0)
            {
                _currentLocresFilePath = result[0].Path.LocalPath;

                // Update timers for RPC
                editStartTime = DateTime.UtcNow;
                idleStartTime = null;

                UpdatePresence(DiscordRPCEnabled); // Display opened file in Discord RPC
                var csvFileName = Path.GetFileNameWithoutExtension(_currentLocresFilePath) + ".csv";
                var csvFile = Path.Combine(Directory.GetCurrentDirectory(), csvFileName);

                // Check if UnrealLocres.exe exists
                var unrealLocresExePath = Path.Combine(Directory.GetCurrentDirectory(), "UnrealLocres.exe");
                if (!File.Exists(unrealLocresExePath))
                {
                    _notificationManager.Show(new Notification("Error", "UnrealLocres.exe not found. Please ensure it is in the application directory.", NotificationType.Error));
                    return;
                }

                // Run UnrealLocres.exe
                var process = new Process
                {
                    StartInfo = GetProcessStartInfo("export", _currentLocresFilePath)
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    try
                    {
                        var importedLocresDir = Path.Combine(Directory.GetCurrentDirectory(), "LocresFiles");

                        if (!Directory.Exists(importedLocresDir))
                        {
                            Directory.CreateDirectory(importedLocresDir);
                        }

                        var importedLocresPath = Path.Combine(importedLocresDir, Path.GetFileName(_currentLocresFilePath));
                        File.Copy(_currentLocresFilePath, importedLocresPath, true);

                        _currentLocresFilePath = importedLocresPath;
                        LoadCsv(csvFile);
                    }
                    finally
                    {
                        if (File.Exists(csvFile))
                        {
                            File.Delete(csvFile);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Error reading locres data: {process.StandardOutput.ReadToEnd()}");
                    _notificationManager.Show(new Notification("Error reading locres data:", $"{process.StandardOutput.ReadToEnd()}", NotificationType.Error));
                    if (File.Exists(csvFile))
                    {
                        File.Delete(csvFile); // Clean up if UnrealLocres failed
                    }
                }
            }
        }

        private void LoadCsv(string csvFile)
        {
            _rows.Clear();
            var columns = new List<DataGridColumn>();

            using (var reader = new StreamReader(csvFile))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                bool isFirstRow = true;
                while (csv.Read())
                {
                    string[] stringValues = new string[csv.Parser.Count];
                    for (int i = 0; i < csv.Parser.Count; i++)
                    {
                        stringValues[i] = csv.GetField(i);
                    }

                    if (isFirstRow)
                    {
                        for (int i = 0; i < stringValues.Length; i++)
                        {
                            columns.Add(new DataGridTextColumn { Header = stringValues[i], Binding = new Binding($"Values[{i}]"), IsReadOnly = false });
                        }
                        _dataGrid.Columns.Clear();
                        foreach (var column in columns)
                        {
                            _dataGrid.Columns.Add(column);
                        }
                        isFirstRow = false;
                    }
                    else
                    {
                        _rows.Add(new DataRow { Values = stringValues });
                    }
                }
            }
            _dataGrid.ItemsSource = _rows;
        }

        public class DataRow : INotifyPropertyChanged
        {
            private string[] _values;
            public string[] Values
            {
                get => _values;
                set
                {
                    if (_values != value)
                    {
                        _values = value;
                        OnPropertyChanged(nameof(Values));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLocresFilePath == null)
            {
                _notificationManager.Show(new Notification("No File Open", "Please open a locres file first.", NotificationType.Error));
                return;
            }
            if (_rows != null && _rows.Count > 0)
            {
                SaveEditedData();
            }
            else
            {
                _notificationManager.Show(new Notification("No Data", "There's no data to export.", NotificationType.Information));
            }
        }

        private void SaveEditedData()
        {
            var exeDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var csvFileName = Path.GetFileNameWithoutExtension(_currentLocresFilePath) + "_edited.csv";
            var csvFile = Path.Combine(exeDirectory, csvFileName);

            // Save edited data to CSV
            using (var writer = new StreamWriter(csvFile, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                for (int i = 0; i < _dataGrid.Columns.Count; i++)
                {
                    csv.WriteField(((DataGridTextColumn)_dataGrid.Columns[i]).Header);
                }
                csv.NextRecord();

                foreach (DataRow row in _rows)
                {
                    for (int i = 0; i < row.Values.Length; i++)
                    {
                        csv.WriteField(row.Values[i]);
                    }
                    csv.NextRecord();
                }
            }

            // Run UnrealLocres.exe to import edited CSV
            var process = new Process
            {
                StartInfo = GetProcessStartInfo("import", _currentLocresFilePath, csvFileName)
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                var modifiedLocres = _currentLocresFilePath + ".new";
                try
                {
                    _notificationManager.Show(new Notification("Success!", $"File saved as {Path.GetFileName(_currentLocresFilePath)}.new in {Path.Combine(Directory.GetCurrentDirectory(), "LocresFiles")}", NotificationType.Success));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error moving file: {ex.Message}");
                    _notificationManager.Show(new Notification("Error moving file:", $"{ex.Message}", NotificationType.Error));
                }
            }
            else
            {
                Console.WriteLine($"Error importing: {process.StandardOutput.ReadToEnd()}");
                _notificationManager.Show(new Notification("Error importing:", $"{process.StandardOutput.ReadToEnd()}", NotificationType.Error));
            }
            File.Delete(csvFile);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _dataGrid = this.FindControl<DataGrid>("uiDataGrid");

            _searchTextBox = this.FindControl<TextBox>("uiSearchTextBox");

            var saveMenuItem = this.FindControl<MenuItem>("uiSaveMenuItem");
            saveMenuItem.Click += SaveMenuItem_Click;

            var openMenuItem = this.FindControl<MenuItem>("uiOpenMenuItem");
            openMenuItem.Click += OpenMenuItem_Click;

            var winePrefixMenuItem = this.FindControl<MenuItem>("uiWinePrefix");
            winePrefixMenuItem.Click += WinePrefix_Click;
            winePrefixMenuItem.IsVisible = IsLinux();

            var uiDiscordRPCMenuItem = this.FindControl<MenuItem>("uiDiscordRPCItem");
            var uiDiscordActivityCheckBox = this.FindControl<CheckBox>("uiDiscordActivityCheckBox");
            uiDiscordActivityCheckBox.IsChecked = true;
            uiDiscordActivityCheckBox.Click += DiscordRPC_Click;
            DiscordRPCEnabled = uiDiscordActivityCheckBox.IsChecked ?? false;
        }

        private void DiscordRPC_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox != null)
            {
                DiscordRPCEnabled = checkBox.IsChecked ?? false;
                if (DiscordRPCEnabled)
                {
                    // Update timers
                    if (_currentLocresFilePath != null)
                    {
                        editStartTime = DateTime.UtcNow;
                        idleStartTime = null;
                    }
                    else
                    {
                        editStartTime = null;
                        idleStartTime = DateTime.UtcNow;
                    }
                }
                UpdatePresence(DiscordRPCEnabled);
            }
        }


        private void WinePrefix_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                InitializeWinePrefix();
                _notificationManager.Show(new Notification("Success", "Success. Make sure to install Wine MONO and set to 32 bit.", NotificationType.Success));
            }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification("Error", $"Failed to initialize Wine prefix: {ex.Message}", NotificationType.Error));
            }
        }
    }
}

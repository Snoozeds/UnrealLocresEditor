using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CsvHelper;
using CsvHelper.Configuration;
using DiscordRPC;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using UnrealLocresEditor.Config;

#nullable disable

namespace UnrealLocresEditor.Views
{
    public partial class MainWindow : Window
    {
        // Main
        public DataGrid _dataGrid;
        private TextBox _searchTextBox;
        public ObservableCollection<DataRow> _rows;
        private string _currentLocresFilePath;
        private WindowNotificationManager _notificationManager;

        // Auto saving
        private System.Timers.Timer _autoSaveTimer;
        private bool _hasUnsavedChanges = false;
        private const int AUTO_SAVE_INTERVAL = 5 * 60 * 1000; // 5 minute

        // Settings
        private AppConfig _appConfig;
        public bool DiscordRPCEnabled
        {
            get => _appConfig.DiscordRPCEnabled;
            set
            {
                _appConfig.DiscordRPCEnabled = value;
                _appConfig.Save();
            }
        }

        public bool UseWine
        {
            get => _appConfig.UseWine;
            set
            {
                _appConfig.UseWine = value;
                _appConfig.Save();
            }
        }

        private void SaveConfig()
        {
            _appConfig.Save();
        }

        // Misc
        public string csvFile = "";
        public bool shownSourceWarningDialog = false;

        public MainWindow()
        {
            _appConfig = AppConfig.Load();
            InitializeComponent();
            InitializeAutoSave();

            // Clear temp directory at startup
            GetOrCreateTempDirectory();

            this.Loaded += OnWindowLoaded;
            this.Closing += OnWindowClosing;
            this.KeyDown += MainWindow_KeyDown; // Keybinds
#if DEBUG
            this.AttachDevTools();
#endif
            _rows = new ObservableCollection<DataRow>();
            DataContext = this;

            idleStartTime = DateTime.UtcNow;

            // For displaying warning upon clicking cell in second (source) column
            _dataGrid.CellPointerPressed += DataGrid_CellPointerPressed;
        }

        // Initialize auto saving
        private void InitializeAutoSave()
        {
            _autoSaveTimer = new System.Timers.Timer(AUTO_SAVE_INTERVAL);
            _autoSaveTimer.Elapsed += AutoSave_Elapsed;
            _autoSaveTimer.Start();

            _dataGrid.CellEditEnded += DataGrid_CellEditEnded;
        }

        private void DataGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
        {
            _hasUnsavedChanges = true;
        }

        private void AutoSave_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_hasUnsavedChanges && _currentLocresFilePath != null)
            {
                // Dispatch to UI thread since we're in a timer callback
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        SaveEditedData();
                        _hasUnsavedChanges = false;
                        _notificationManager.Show(new Notification(
                            "Auto-save",
                            "Your changes have been automatically saved.",
                            NotificationType.Information));
                    }
                    catch (Exception ex)
                    {
                        _notificationManager.Show(new Notification(
                            "Auto-save Error",
                            $"Failed to auto-save: {ex.Message}",
                            NotificationType.Error));
                    }
                });
            }
        }


        // Start Discord RPC
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

        // Save things when window closes
        private void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            if (_hasUnsavedChanges)
            {
                try
                {
                    SaveEditedData();
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(new Notification(
                        "Save Error",
                        $"Failed to save changes before closing: {ex.Message}",
                        NotificationType.Error));
                }
            }

            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Dispose();

            client?.ClearPresence();
            client?.Dispose();
            SaveConfig();
        }

    // Keybinds
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                switch (e.Key)
                {
                    case Key.F:
                        ShowFindDialog();
                        break;
                    case Key.H:
                        ShowFindReplaceDialog();
                        break;
                }
            }
        }

        private void ShowFindDialog()
        {
            if (findDialog == null)
            {
                findDialog = new FindDialog();
                findDialog.Closed += FindDialog_Closed;
                findDialog.MainWindow = this;
            }

            findDialog.Show();
            findDialog.Activate();
        }

        private void ShowFindReplaceDialog()
        {
            if (findReplaceDialog == null)
            {
                findReplaceDialog = new FindReplaceDialog();
                findReplaceDialog.Closed += FindReplaceDialog_Closed;
                findReplaceDialog.MainWindow = this;
            }

            findReplaceDialog.Show();
            findReplaceDialog.Activate();
        }

        private static string winePrefixDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wineprefix");

        private static bool IsLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        private static string GetExecutablePath(bool useWine)
        {
            if (IsLinux())
            {
                return useWine ? "wine UnrealLocres.exe" : "./UnrealLocres";
            }
            else // Windows
            {
                return "UnrealLocres.exe";
            }
        }

        private static string GetArguments(string command, string locresFilePath, bool useWine, string csvFileName = null)
        {
            if (IsLinux())
            {
                if (useWine)
                {
                    return csvFileName == null
                        ? $"UnrealLocres.exe {command} \"{locresFilePath}\""
                        : $"UnrealLocres.exe {command} \"{locresFilePath}\" \"{csvFileName}\"";
                }
                else
                {
                    return csvFileName == null
                        ? $"./UnrealLocres {command} \"{locresFilePath}\""
                        : $"./UnrealLocres {command} \"{locresFilePath}\" \"{csvFileName}\"";
                }
            }
            else
            {
                return csvFileName == null
                    ? $"{command} \"{locresFilePath}\""
                    : $"{command} \"{locresFilePath}\" \"{csvFileName}\"";
            }
        }

        private ProcessStartInfo GetProcessStartInfo(string command, string locresFilePath, string csvFileName = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(UseWine),
                Arguments = GetArguments(command, locresFilePath, UseWine, csvFileName),
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            if (IsLinux() && UseWine)
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

        private static string GetOrCreateTempDirectory()
        {
            var exeDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var tempDirectoryName = ".temp-UnrealLocresEditor";
            var tempDirectoryPath = Path.Combine(exeDirectory, tempDirectoryName);

            if (Directory.Exists(tempDirectoryPath))
            {
                // Temp file cleanup
                DirectoryInfo dirInfo = new DirectoryInfo(tempDirectoryPath);
                foreach (FileInfo file in dirInfo.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in dirInfo.GetDirectories())
                {
                    dir.Delete(true);
                }
            }
            else
            {
                Directory.CreateDirectory(tempDirectoryPath);

                // Set folder to hidden on Windows
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(tempDirectoryPath, FileAttributes.Directory | FileAttributes.Hidden);
                }
            }

            return tempDirectoryPath;
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
                csvFile = Path.Combine(Directory.GetCurrentDirectory(), csvFileName);

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
                    StartInfo = GetProcessStartInfo("export", _currentLocresFilePath, csvFileName)
                };

                process.Start();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    try
                    {
                        var importedLocresDir = GetOrCreateTempDirectory();
                        var importedLocresPath = Path.Combine(importedLocresDir, Path.GetFileName(_currentLocresFilePath));

                        if (File.Exists(importedLocresPath))
                        {
                            // If file exists, just use it
                            _currentLocresFilePath = importedLocresPath;
                        }
                        else
                        {
                            // Otherwise copy
                            File.Copy(_currentLocresFilePath, importedLocresPath, true);
                            _currentLocresFilePath = importedLocresPath;
                        }

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
                            var column = new DataGridTextColumn
                            {
                                Header = stringValues[i],
                                Binding = new Binding($"Values[{i}]"),
                                IsReadOnly = false,
                                Width = new DataGridLength(300)
                            };
                            columns.Add(column);
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

            public virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void DataGrid_CellPointerPressed(object sender, DataGridCellPointerPressedEventArgs e)
        {
            if (e.Column?.Header?.ToString() == "source")
            {
                if (shownSourceWarningDialog == false)
                {
                    ShowWarningDialog();
                    shownSourceWarningDialog = true;
                }
            }
        }

        private void ShowWarningDialog()
        {
            var messageBox = new Window
            {
                Title = "Warning",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new StackPanel
                {
                    Children =
            {
                new TextBlock
                {
                    Text = "This is the source column, this is the original text, but you should not edit it to replace the text - instead write the text you want to replace this with in the target column next to it.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10)
                },
                new Avalonia.Controls.Button
                {
                    Content = "OK",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10)
                }
            }
                }
            };

            var button = (Avalonia.Controls.Button)((StackPanel)messageBox.Content).Children[1];
            button.Click += (s, e) => messageBox.Close();

            messageBox.ShowDialog(this);
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

            if (_currentLocresFilePath == null)
            {
                return;
            }

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

                // Create export directory with current date and time
                var exportDirectory = Path.Combine(exeDirectory, "export");
                var dateTimeFolder = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var destinationDirectory = Path.Combine(exportDirectory, dateTimeFolder);

                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                var newFileName = Path.GetFileNameWithoutExtension(modifiedLocres);
                var destinationFile = Path.Combine(destinationDirectory, newFileName);

                try
                {
                    // Move and rename
                    File.Move(modifiedLocres, destinationFile);

                    _notificationManager.Show(new Notification("Success!", $"File saved as {Path.GetFileName(destinationFile)} in {destinationDirectory}", NotificationType.Success));
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

            // Clean up CSV file
            File.Delete(csvFile);
        }

        private async void OpenSpreadsheetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                FileTypeFilter = new[]
                {
            new FilePickerFileType("Spreadsheet Files") { Patterns = new[] { "*.csv" } }
        },
                AllowMultiple = false
            });

            if (result != null && result.Count > 0)
            {
                string filePath = result[0].Path.LocalPath;

                // Load the CSV file
                LoadCsv(filePath);

                csvFile = filePath; // Update to the newly opened CSV file
                editStartTime = DateTime.UtcNow;
                idleStartTime = null;

                UpdatePresence(DiscordRPCEnabled); // Update Discord presence
            }
        }

        private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_rows != null && _rows.Count > 0)
            {
                var saveOptions = new FilePickerSaveOptions
                {
                    SuggestedFileName = Path.GetFileNameWithoutExtension(_currentLocresFilePath),
                    FileTypeChoices = new[]
                    {
                new FilePickerFileType("CSV file") { Patterns = new[] { "*.csv" } },
            }
                };

                var storageFile = await StorageProvider.SaveFilePickerAsync(saveOptions);

                if (storageFile != null)
                {
                    var filePath = storageFile.Path.LocalPath;
                    SaveAsCsv(filePath);

                    _notificationManager.Show(new Notification("Success", $"File saved as {Path.GetFileName(filePath)}", NotificationType.Success));
                }
            }
            else
            {
                _notificationManager.Show(new Notification("No Data", "There's no data to export.", NotificationType.Information));
            }
        }

        private void SaveAsCsv(string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                // Headers
                for (int i = 0; i < _dataGrid.Columns.Count; i++)
                {
                    csv.WriteField(((DataGridTextColumn)_dataGrid.Columns[i]).Header);
                }
                csv.NextRecord();

                // Rows
                foreach (DataRow row in _rows)
                {
                    for (int i = 0; i < row.Values.Length; i++)
                    {
                        csv.WriteField(row.Values[i]);
                    }
                    csv.NextRecord();
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _dataGrid = this.FindControl<DataGrid>("uiDataGrid");

            _searchTextBox = this.FindControl<TextBox>("uiSearchTextBox");

            var saveMenuItem = this.FindControl<MenuItem>("uiSaveMenuItem");
            saveMenuItem.Click += SaveMenuItem_Click;

            var uiOpenSpreadsheetMenuItem = this.FindControl<MenuItem>("uiOpenSpreadsheetMenuItem");
            uiOpenSpreadsheetMenuItem.Click += OpenSpreadsheetMenuItem_Click;

            var saveAsMenuItem = this.FindControl<MenuItem>("uiSaveAsMenuItem");
            saveAsMenuItem.Click += SaveAsMenuItem_Click;

            var openMenuItem = this.FindControl<MenuItem>("uiOpenMenuItem");
            openMenuItem.Click += OpenMenuItem_Click;

            var linuxMenuItem = this.FindControl<MenuItem>("uiLinuxHeader");
            linuxMenuItem.IsVisible = IsLinux();

            var findMenuItem = this.FindControl<MenuItem>("uiFindMenuItem");
            findMenuItem.Click += FindMenuItem_Click;

            var findReplaceMenuItem = this.FindControl<MenuItem>("uiFindReplaceMenuItem");
            findReplaceMenuItem.Click += FindReplaceMenuItem_Click;

            var winePrefixMenuItem = this.FindControl<MenuItem>("uiWinePrefix");
            winePrefixMenuItem.Click += WinePrefix_Click;
            winePrefixMenuItem.IsVisible = IsLinux();

            var useWineMenuItem = this.FindControl<MenuItem>("uiUseWineMenuItem");
            var useWineCheckBox = this.FindControl<CheckBox>("uiUseWineCheckBox");
            useWineMenuItem.IsVisible = IsLinux();
            if (_appConfig != null)
            {
                useWineCheckBox.IsChecked = _appConfig.UseWine;
            }
            UseWine = useWineCheckBox.IsChecked ?? true;

            var uiDiscordRPCMenuItem = this.FindControl<MenuItem>("uiDiscordRPCItem");
            var uiDiscordActivityCheckBox = this.FindControl<CheckBox>("uiDiscordActivityCheckBox");
            if (_appConfig != null)
            {
                uiDiscordActivityCheckBox.IsChecked = _appConfig.DiscordRPCEnabled;
            }
            uiDiscordActivityCheckBox.Click += DiscordRPC_Click;
            DiscordRPCEnabled = uiDiscordActivityCheckBox.IsChecked ?? true;
        }

        private FindDialog findDialog;
        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (findDialog == null)
            {
                findDialog = new FindDialog();
                findDialog.Closed += FindDialog_Closed;
                findDialog.MainWindow = this;
            }

            findDialog.Show();
            findDialog.Activate();
        }

        private void FindDialog_Closed(object sender, EventArgs e)
        {
            findDialog = null;
        }

        private FindReplaceDialog findReplaceDialog;
        private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (findReplaceDialog == null)
            {
                findReplaceDialog = new FindReplaceDialog();
                findReplaceDialog.Closed += FindReplaceDialog_Closed;
                findReplaceDialog.MainWindow = this;
            }

            findReplaceDialog.Show();
            findReplaceDialog.Activate();
        }

        private void FindReplaceDialog_Closed(object sender, EventArgs e)
        {
            findReplaceDialog = null;
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
                    _appConfig.DiscordRPCEnabled = true;
                }
                else if (!DiscordRPCEnabled)
                {
                    _appConfig.DiscordRPCEnabled = false;
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

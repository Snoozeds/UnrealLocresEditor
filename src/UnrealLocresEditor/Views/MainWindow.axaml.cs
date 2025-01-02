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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using UnrealLocresEditor.Utils;

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
        public bool _hasUnsavedChanges = false;

        // Settings
        private AppConfig _appConfig;
        public bool DiscordRPCEnabled;
        public bool UseWine;

        // Misc
        public string csvFile = "";
        public bool shownSourceWarningDialog = false;

        public MainWindow()
        {
            _appConfig = AppConfig.Load();
            InitializeComponent();
            InitializeAutoSave();

            UseWine = _appConfig.UseWine;
            DiscordRPCEnabled = _appConfig.DiscordRPCEnabled;

            // Set theme and accent
            ApplyTheme(_appConfig.IsDarkTheme);
            ApplyAccent(Color.Parse(_appConfig.AccentColor));

            // Clear temp directory at startup
            GetOrCreateTempDirectory();

            this.Loaded += OnWindowLoaded;
            this.Closing += OnWindowClosing;
            this.KeyDown += MainWindow_KeyDown; // Keybinds

            _rows = new ObservableCollection<DataRow>();
            DataContext = this;

            idleStartTime = DateTime.UtcNow;

            // For displaying warning upon clicking cell in second (source) column
            _dataGrid.CellPointerPressed += DataGrid_CellPointerPressed;

            // For preventing shutdown if the work is unsaved
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                AppDomain.CurrentDomain.ProcessExit += OnSystemShutdown;
            }
        }

        // Initialize auto saving
        private void InitializeAutoSave()
        {
            if (_appConfig.AutoSaveEnabled)
            {
                int autoSaveIntervalMs = (int)_appConfig.AutoSaveInterval.TotalMilliseconds;
                _autoSaveTimer = new System.Timers.Timer();
                _autoSaveTimer.Interval = _appConfig.AutoSaveInterval.TotalMilliseconds;
                _autoSaveTimer.Elapsed += AutoSave_Elapsed;
                _autoSaveTimer.Start();
            }
            else
            {
                _autoSaveTimer = null;
            }

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

        // Apply theme based off of config
        private void ApplyTheme(bool isDarkTheme)
        {
            Application.Current.RequestedThemeVariant = isDarkTheme
               ? Avalonia.Styling.ThemeVariant.Dark
               : Avalonia.Styling.ThemeVariant.Light;
        }


        // Apply accent based off of config
        // https://github.com/AvaloniaUI/Avalonia/issues/10746
        private void ApplyAccent(Color accentColor)
        {
            Application.Current!.Resources["SystemAccentColor"] = accentColor;
            Application.Current.Resources["SystemAccentColorDark1"] = ColorUtils.ChangeColorLuminosity(accentColor, -0.3);
            Application.Current.Resources["SystemAccentColorDark2"] = ColorUtils.ChangeColorLuminosity(accentColor, -0.5);
            Application.Current.Resources["SystemAccentColorDark3"] = ColorUtils.ChangeColorLuminosity(accentColor, -0.7);
            Application.Current.Resources["SystemAccentColorLight1"] = ColorUtils.ChangeColorLuminosity(accentColor, 0.3);
            Application.Current.Resources["SystemAccentColorLight2"] = ColorUtils.ChangeColorLuminosity(accentColor, 0.5);
            Application.Current.Resources["SystemAccentColorLight3"] = ColorUtils.ChangeColorLuminosity(accentColor, 0.7);
        }


        // Start Discord RPC
        public DiscordRpcClient client;
        private DateTime? editStartTime;
        private DateTime? idleStartTime;

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _notificationManager = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 1
            };

            // Check for updates
            if (_appConfig.AutoUpdateEnabled == false)
            {
                Console.WriteLine("Skipping update check - auto update disabled.");
                return;
            }
            else
            {
                AutoUpdater updater = new AutoUpdater(_notificationManager, this);
                try
                {
                    await updater.CheckForUpdates();
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(new Notification(
                        "Update Check Failed",
                        $"Could not check for updates: {ex.Message}",
                        NotificationType.Error));
                }
            }

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

        public void UpdatePresence(bool enabled)
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

                var presence = new RichPresence
                {
                    // Set the presence details based on the config privacy setting
                    Details = _appConfig.DiscordRPCPrivacy ? _appConfig.DiscordRPCPrivacyString : (_currentLocresFilePath == null ? "Idling" : $"Editing file: {Path.GetFileName(_currentLocresFilePath)}"),
                    Timestamps = editStartTime.HasValue ? new Timestamps(editStartTime.Value) : null,
                    Assets = new Assets()
                    {
                        LargeImageKey = "ule-logo",
                    }
                };

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

        // Ask if user wants to save when window closes + has unsaved changes
        private bool _closingHandled = false;
        private bool _isSystemShutdown = false;

        private void OnSystemShutdown(object? sender, EventArgs e)
        {
            _isSystemShutdown = true;
        }
        private async void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            if (_closingHandled)
                return;

            if (_hasUnsavedChanges)
            {
                // Cancel close event
                e.Cancel = true;

                // Display prompt to save changes
                var dialog = new Window
                {
                    Title = _isSystemShutdown ? "System Shutdown - Unsaved Changes" : "Unsaved Changes",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 20,
                        Children =
                {
                    new TextBlock
                    {
                        Text = _isSystemShutdown
                            ? "The system is shutting down. Do you want to save changes before exiting?"
                            : "You have unsaved changes. Do you want to save before closing?",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new Avalonia.Controls.Button { Content = "Save" },
                            new Avalonia.Controls.Button { Content = "Don't Save" },
                            new Avalonia.Controls.Button { Content = "Cancel" }
                        }
                    }
                }
                    }
                };

                var result = await ShowCustomDialog(dialog);

                switch (result)
                {
                    case "Save":
                        try
                        {
                            SaveEditedData();
                            CompleteClosing(e);
                        }
                        catch (Exception ex)
                        {
                            _notificationManager.Show(new Notification(
                                "Save Error",
                                $"Failed to save changes: {ex.Message}",
                                NotificationType.Error));
                        }
                        break;

                    case "Don't Save":
                        CompleteClosing(e);
                        break;

                    case "Cancel":
                        break;
                }
            }
            else
            {
                CompleteClosing(e);
            }
        }

        private void CompleteClosing(WindowClosingEventArgs e)
        {
            _closingHandled = true;

            if (!_isSystemShutdown)
            {
                Closing -= OnWindowClosing;
            }

            e.Cancel = false;
            CloseApplication();
        }

        private void CloseApplication()
        {
            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Dispose();
            client?.ClearPresence();
            client?.Dispose();

            var window = (Window)this;
            window.Close();
        }

        private TaskCompletionSource<string> _dialogResult;

        private async Task<string> ShowCustomDialog(Window dialog)
        {
            _dialogResult = new TaskCompletionSource<string>();

            var buttons = ((StackPanel)((StackPanel)dialog.Content).Children[1]).Children.OfType<Avalonia.Controls.Button>();

            foreach (var button in buttons)
            {
                button.Click += (s, e) =>
                {
                    _dialogResult.SetResult(((Avalonia.Controls.Button)s).Content.ToString());
                    dialog.Close();
                };
            }

            await dialog.ShowDialog(this);
            return await _dialogResult.Task;
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

        // Allow pressing shift+enter for multiline text.
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Shift)
            {
                if (sender is DataGrid grid && e.Source is TextBox textBox)
                {
                    int caretIndex = textBox.CaretIndex;
                    string currentText = textBox.Text ?? string.Empty;
                    textBox.Text = currentText.Insert(caretIndex, Environment.NewLine);
                    textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                    e.Handled = true;
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

            findDialog.Show(this);
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

            findReplaceDialog.Show(this);
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
            var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);
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
                var downloader = new UnrealLocresDownloader(this, _notificationManager);
                if (!await downloader.CheckAndDownloadUnrealLocres())
                {
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

        public void SaveEditedData()
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
            _dataGrid.AddHandler(KeyDownEvent, DataGrid_PreviewKeyDown, RoutingStrategies.Tunnel);

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

            var preferencesMenuItem = this.FindControl<MenuItem>("uiPreferencesMenuItem");
            preferencesMenuItem.Click += PreferencesMenuItem_Click;

            var winePrefixMenuItem = this.FindControl<MenuItem>("uiWinePrefix");
            winePrefixMenuItem.Click += WinePrefix_Click;
            winePrefixMenuItem.IsVisible = IsLinux();

            var reportIssueMenuItem = this.FindControl<MenuItem>("reportIssueMenuItem");
            reportIssueMenuItem.Click += ReportIssueMenuItem_Click;

            var aboutMenuItem = this.FindControl<MenuItem>("uiAboutMenuItem");
            aboutMenuItem.Click += AboutMenuItem_Click;
        }

        // Find dialog
        private FindDialog findDialog;
        private void FindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (findDialog == null)
            {
                findDialog = new FindDialog();
                findDialog.Closed += FindDialog_Closed;
                findDialog.MainWindow = this;
            }

            findDialog.Show(this);
            findDialog.Activate();
        }

        private void FindDialog_Closed(object sender, EventArgs e)
        {
            findDialog = null;
        }

        // Find and replace dialog
        private FindReplaceDialog findReplaceDialog;
        private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (findReplaceDialog == null)
            {
                findReplaceDialog = new FindReplaceDialog();
                findReplaceDialog.Closed += FindReplaceDialog_Closed;
                findReplaceDialog.MainWindow = this;
            }

            findReplaceDialog.Show(this);
            findReplaceDialog.Activate();
        }

        private void FindReplaceDialog_Closed(object sender, EventArgs e)
        {
            findReplaceDialog = null;
        }

        // Preferences
        private PreferencesWindow preferencesWindow;
        private void PreferencesMenuItem_Click(Object sender, RoutedEventArgs e)
        {
            if (preferencesWindow == null)
            {
                preferencesWindow = new PreferencesWindow(this);
                preferencesWindow.Closed += PreferencesWindow_Closed;
            }

            preferencesWindow.Show(this);
            preferencesWindow.Activate();
        }

        private void PreferencesWindow_Closed(object sender, EventArgs e)
        {

            preferencesWindow = null;
        }

        // Attempt wine prefix (Linux)
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

        // Report issue
        private const string GitHubIssueUrl = "https://github.com/Snoozeds/UnrealLocresEditor/issues/new";
        private void ReportIssueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = GitHubIssueUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception)
            {
            }
        }

        // About
        private AboutWindow aboutWindow;
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (aboutWindow == null)
            {
                aboutWindow = new AboutWindow(_notificationManager, this);
                aboutWindow.Closed += AboutWindow_Closed;
            }

            aboutWindow.Show(this);
            aboutWindow.Activate();
        }

        private void AboutWindow_Closed(Object sender, EventArgs e)
        {
            aboutWindow = null;
        }

    }
}

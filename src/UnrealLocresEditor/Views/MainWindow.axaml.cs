using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
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
using UnrealLocresEditor.Models;
using UnrealLocresEditor.Utils;

#nullable disable

namespace UnrealLocresEditor.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Main
        public DataGrid _dataGrid;
        private TextBox _searchTextBox;
        public ObservableCollection<DataRow> _rows;
        public string _currentLocresFilePath;
        private WindowNotificationManager _notificationManager;

        // Auto saving
        private System.Timers.Timer _autoSaveTimer;
        public bool _hasUnsavedChanges = false;

        // Settings
        private AppConfig _appConfig;
        private DiscordRPC _discordRPC;
        public bool UseWine;

        // Misc
        public string csvFile = "";
        public bool shownAddRowWarningDialog = false;

        private readonly ObservableCollection<LocresDocument> _documents = new();
        private LocresDocument _selectedDocument;

        public ObservableCollection<LocresDocument> Documents => _documents;

        public LocresDocument SelectedDocument
        {
            get => _selectedDocument;
            set
            {
                if (_selectedDocument != value)
                {
                    SaveSelectedDocumentState();
                    _selectedDocument = value;
                    RaisePropertyChanged(nameof(SelectedDocument));
                    ApplySelectedDocumentState();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void RaisePropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public MainWindow()
        {
            _appConfig = AppConfig.Load();
            InitializeComponent();
            _dataGrid.CellEditEnded += DataGrid_CellEditEnded;

            UseWine = _appConfig.UseWine;
            _discordRPC = new DiscordRPC();

            // Set theme and accent
            ApplyTheme(_appConfig.IsDarkTheme);
            ApplyAccent(Color.Parse(_appConfig.AccentColor));

            // NEW: Clean up old junk from previous crashes
            CleanupStaleTempDirectories();

            // Clear temp directory at startup
            GetOrCreateTempDirectory();

            this.Loaded += OnWindowLoaded;
            this.Closing += OnWindowClosing;
            this.KeyDown += MainWindow_KeyDown; // Keybinds

            _rows = new ObservableCollection<DataRow>();
            DataContext = this;
            _dataGrid.ItemsSource = _rows;
            Documents.CollectionChanged += Documents_CollectionChanged;
            ConfigureAutoSaveTimer();

            _discordRPC.idleStartTime = DateTime.UtcNow;

            // For preventing shutdown if the work is unsaved
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                AppDomain.CurrentDomain.ProcessExit += OnSystemShutdown;
            }
        }

        // Initialize auto saving
        private void ConfigureAutoSaveTimer()
        {
            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Elapsed -= AutoSave_Elapsed;
                _autoSaveTimer.Dispose();
                _autoSaveTimer = null;
            }

            if (_appConfig.AutoSaveEnabled && HasAutoSaveCandidates())
            {
                _autoSaveTimer = new System.Timers.Timer
                {
                    Interval = _appConfig.AutoSaveInterval.TotalMilliseconds,
                    AutoReset = true,
                };
                _autoSaveTimer.Elapsed += AutoSave_Elapsed;
                _autoSaveTimer.Start();
            }
        }

        private bool HasAutoSaveCandidates() =>
            _documents.Any(
                d => !string.IsNullOrWhiteSpace(d.WorkingPath) && d.Rows.Count > 0
            );

        private void Documents_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_documents.Count == 0)
            {
                _rows.Clear();
                _dataGrid.ItemsSource = _rows;
                _currentLocresFilePath = null;
                csvFile = string.Empty;
                _hasUnsavedChanges = false;
                if (_selectedDocument != null)
                {
                    _selectedDocument = null;
                    RaisePropertyChanged(nameof(SelectedDocument));
                }
            }
            else if (_selectedDocument == null || !_documents.Contains(_selectedDocument))
            {
                SelectedDocument = _documents.Last();
            }

            RefreshUnsavedChangesFlag();
            ConfigureAutoSaveTimer();
        }

        // Cleanup-After-Crash
        private void CleanupStaleTempDirectories()
        {
            try
            {
                var exeDirectory = AppContext.BaseDirectory;
                var currentInstanceId = Process.GetCurrentProcess().Id.ToString();

                // Find all folders that look like ".temp-UnrealLocresEditor-XXXX"
                var directories = Directory.GetDirectories(exeDirectory, ".temp-UnrealLocresEditor-*");

                foreach (var dir in directories)
                {
                    // IMPORTANT: Don't delete the folder we just created for THIS session!
                    if (dir.EndsWith(currentInstanceId))
                        continue;

                    try
                    {
                        // Try to delete the folder.
                        // If another instance of the app is currently running, Windows will lock the files
                        // and throw an exception. We catch that exception and simply skip it.
                        // This ensures we only delete folders from closed/crashed instances.
                        Directory.Delete(dir, true);
                        Console.WriteLine($"Cleaned up stale directory: {dir}");
                    }
                    catch
                    {
                        // Folder is locked (another app instance is running). Leave it alone.
                    }
                }
            }
            catch (Exception ex)
            {
                // General error (permissions, etc). Just ignore.
                Console.WriteLine($"Cleanup warning: {ex.Message}");
            }
        }
        private void SaveSelectedDocumentState()
        {
            if (_selectedDocument == null)
            {
                return;
            }

            _selectedDocument.ActiveCsvPath = string.IsNullOrWhiteSpace(csvFile)
                ? null
                : csvFile;
            _selectedDocument.HasUnsavedChanges = _hasUnsavedChanges;
        }

        private void ApplySelectedDocumentState()
        {
            if (_selectedDocument == null)
            {
                _rows = new ObservableCollection<DataRow>();
                _dataGrid.ItemsSource = _rows;
                _dataGrid.Columns.Clear();
                _currentLocresFilePath = null;
                csvFile = string.Empty;
                _hasUnsavedChanges = false;
                UpdateDiscordPresence(null);
                return;
            }

            _rows = _selectedDocument.Rows;
            _dataGrid.ItemsSource = _rows;
            ApplyColumnsForDocument(_selectedDocument);

            _currentLocresFilePath = string.IsNullOrWhiteSpace(_selectedDocument.WorkingPath)
                ? null
                : _selectedDocument.WorkingPath;
            csvFile = _selectedDocument.ActiveCsvPath ?? string.Empty;
            _hasUnsavedChanges = _selectedDocument.HasUnsavedChanges;
            UpdateDiscordPresence(_currentLocresFilePath);
        }

        private void ApplyColumnsForDocument(LocresDocument document)
        {
            if (_dataGrid == null)
            {
                return;
            }

            _dataGrid.Columns.Clear();

            for (int i = 0; i < document.ColumnHeaders.Count; i++)
            {
                var header = document.ColumnHeaders[i];
                var column = new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding($"Values[{i}]")
                    {
                        Mode = BindingMode.TwoWay,
                    },
                    IsReadOnly = header.Equals("source", StringComparison.OrdinalIgnoreCase),
                    Width = new DataGridLength(AppConfig.Instance.DefaultColumnWidth),
                };

                _dataGrid.Columns.Add(column);
            }
        }

        private void RefreshUnsavedChangesFlag()
        {
            _hasUnsavedChanges = _documents.Any(d => d.HasUnsavedChanges);
        }

        private void MarkDocumentDirty(LocresDocument document)
        {
            if (document == null)
            {
                return;
            }

            document.HasUnsavedChanges = true;
            _hasUnsavedChanges = true;
        }

        private void ClearDocumentDirty(LocresDocument document)
        {
            if (document == null)
            {
                return;
            }

            document.HasUnsavedChanges = false;
            RefreshUnsavedChangesFlag();
        }

        private void UpdateDiscordPresence(string? path)
        {
            _discordRPC.UpdatePresence(_appConfig.DiscordRPCEnabled, path);
        }

        private void DataGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
        {
            if (SelectedDocument != null)
            {
                MarkDocumentDirty(SelectedDocument);
            }
        }
        private void SaveDocument(LocresDocument document, bool openExplorer, bool isAutoSave)
        {
            if (document == null)
                return;

            var originalDocument = SelectedDocument;

            try
            {
                if (originalDocument != document)
                {
                    SaveSelectedDocumentState();

                    SelectedDocument = document;
                }

                SaveEditedData(openExplorer);

                ClearDocumentDirty(document);
            }
            finally
            {
                if (originalDocument != null && originalDocument != document)
                {
                    SelectedDocument = originalDocument;
                }

                RefreshUnsavedChangesFlag();
            }
        }


        private void AutoSave_Elapsed(object sender, ElapsedEventArgs e)
        {
            var documentsToSave = _documents
                .Where(
                    d =>
                        d.HasUnsavedChanges
                        && !string.IsNullOrWhiteSpace(d.WorkingPath)
                        && d.Rows.Count > 0
                )
                .ToList();

            if (documentsToSave.Count == 0)
            {
                return;
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var document in documentsToSave)
                {
                    try
                    {
                        SaveDocument(document, openExplorer: false, isAutoSave: true);
                        _notificationManager.Show(
                            new Notification(
                                "Auto-save",
                                $"Automatically saved {document.DisplayName}.",
                                NotificationType.Information
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        _notificationManager.Show(
                            new Notification(
                                "Auto-save Error",
                                $"Failed to auto-save {document.DisplayName}: {ex.Message}",
                                NotificationType.Error
                            )
                        );
                    }
                }
            });
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
            Application.Current.Resources["SystemAccentColorDark1"] =
                ColorUtils.ChangeColorLuminosity(accentColor, -0.3);
            Application.Current.Resources["SystemAccentColorDark2"] =
                ColorUtils.ChangeColorLuminosity(accentColor, -0.5);
            Application.Current.Resources["SystemAccentColorDark3"] =
                ColorUtils.ChangeColorLuminosity(accentColor, -0.7);
            Application.Current.Resources["SystemAccentColorLight1"] =
                ColorUtils.ChangeColorLuminosity(accentColor, 0.3);
            Application.Current.Resources["SystemAccentColorLight2"] =
                ColorUtils.ChangeColorLuminosity(accentColor, 0.5);
            Application.Current.Resources["SystemAccentColorLight3"] =
                ColorUtils.ChangeColorLuminosity(accentColor, 0.7);
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _notificationManager = new WindowNotificationManager(this)
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 1,
            };

            // Skip update check in Avalonia Designer
            if (Design.IsDesignMode)
            {
                Console.WriteLine("In designer - skipping update check.");
                return;
            }

#if DEBUG
            Console.WriteLine("Skipping update check - DEBUG mode.");
#else
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
                    _notificationManager.Show(
                        new Notification(
                            "Update Check Failed",
                            $"Could not check for updates: {ex.Message}",
                            NotificationType.Error
                        )
                    );
                }
            }
#endif

            _discordRPC.Initialize(_currentLocresFilePath);
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
                    Title = _isSystemShutdown
                        ? "System Shutdown - Unsaved Changes"
                        : "Unsaved Changes",
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
                                TextWrapping = TextWrapping.Wrap,
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
                                    new Avalonia.Controls.Button { Content = "Cancel" },
                                },
                            },
                        },
                    },
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
                            _notificationManager.Show(
                                new Notification(
                                    "Save Error",
                                    $"Failed to save changes: {ex.Message}",
                                    NotificationType.Error
                                )
                            );
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
            _discordRPC.client?.ClearPresence();
            _discordRPC.client?.Dispose();

            // Clean up the temp directory for this instance
            try
            {
                var instanceId = Process.GetCurrentProcess().Id.ToString();
                var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath);
                var tempDirectoryName = $".temp-UnrealLocresEditor-{instanceId}";
                var tempDirectoryPath = Path.Combine(exeDirectory, tempDirectoryName);

                if (Directory.Exists(tempDirectoryPath))
                {
                    Directory.Delete(tempDirectoryPath, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up temp directory: {ex.Message}");
            }

            var window = (Window)this;
            window.Close();
        }

        private TaskCompletionSource<string> _dialogResult;

        private async Task<string> ShowCustomDialog(Window dialog)
        {
            _dialogResult = new TaskCompletionSource<string>();

            var buttons = (
                (StackPanel)((StackPanel)dialog.Content).Children[1]
            ).Children.OfType<Avalonia.Controls.Button>();

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
                    case Key.T:
                        CopySourceToTarget(sender, null);
                        e.Handled = true;
                        break;
                }
            }
            else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                switch (e.Key)
                {
                    case Key.T:
                        CopySourceToTargetMultiple(sender, null);
                        e.Handled = true;
                        break;
                }
            }
        }

        private async void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Allow pressing shift+enter for multiline text.
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

            // Handle Delete key to clear cell content
            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
            {
                var focusedControl = FocusManager.GetFocusedElement() as TextBox;
                if (focusedControl != null)
                {
                    // If we're already editing a cell, let the default behavior handle it
                    return;
                }

                // If we have a selected cell but aren't editing it, clear the cell content
                if (_dataGrid.SelectedItem is DataRow selectedRow)
                {
                    int selectedColumnIndex = _dataGrid.Columns.IndexOf(_dataGrid.CurrentColumn);
                    if (selectedColumnIndex >= 0)
                    {
                        // Check if the column is read-only or the "key" column (some users may accidentally hit delete on the key column as it is selected by default, and key names can be very long, so.)
                        var column = _dataGrid.CurrentColumn as DataGridTextColumn;
                        var columnHeader = column?.Header?.ToString();

                        if (
                            column != null
                            && !column.IsReadOnly
                            && !columnHeader.Equals("key", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            // Clear the cell content
                            var newValues = (string[])selectedRow.Values.Clone();
                            newValues[selectedColumnIndex] = string.Empty;
                            selectedRow.Values = newValues;

                            // Mark as having unsaved changes
                            _hasUnsavedChanges = true;

                            e.Handled = true;
                        }
                    }
                }
            }

            // Handle Ctrl+C / Ctrl+V for copy-pasting when a cell is focused but not being directly edited.
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                var focusedControl = FocusManager.GetFocusedElement() as TextBox;
                if (focusedControl != null)
                {
                    if (e.Key == Key.C)
                    {
                        if (!string.IsNullOrEmpty(focusedControl.SelectedText))
                        {
                            await this.Clipboard.SetTextAsync(focusedControl.SelectedText);
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.V)
                    {
                        var clipboardText = await this.Clipboard.GetTextAsync();
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            int caretIndex = focusedControl.CaretIndex;
                            focusedControl.Text = focusedControl.Text.Insert(
                                caretIndex,
                                clipboardText
                            );
                            focusedControl.CaretIndex = caretIndex + clipboardText.Length;
                            e.Handled = true;
                        }
                    }
                }
                else if (_dataGrid.SelectedItem is DataRow selectedRow)
                {
                    int selectedColumnIndex = _dataGrid.Columns.IndexOf(_dataGrid.CurrentColumn);
                    if (selectedColumnIndex < 0)
                        return;

                    if (e.Key == Key.C)
                    {
                        // Copy cell content from the underlying data.
                        string cellValue = selectedRow.Values[selectedColumnIndex];
                        await this.Clipboard.SetTextAsync(cellValue);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.V)
                    {
                        // Begin editing if not already editing.
                        _dataGrid.BeginEdit();

                        // Defer the paste operation until the editing control (TextBox) is available.
                        Dispatcher.UIThread.Post(
                            async () =>
                            {
                                var editTextBox = FocusManager.GetFocusedElement() as TextBox;
                                if (editTextBox != null)
                                {
                                    var clipboardText = await this.Clipboard.GetTextAsync();
                                    if (!string.IsNullOrEmpty(clipboardText))
                                    {
                                        // If user has selected text, replace that
                                        if (!string.IsNullOrEmpty(editTextBox.SelectedText))
                                        {
                                            int selectionStart = editTextBox.SelectionStart;
                                            editTextBox.Text = editTextBox
                                                .Text.Remove(
                                                    selectionStart,
                                                    editTextBox.SelectionEnd - selectionStart
                                                )
                                                .Insert(selectionStart, clipboardText);
                                            editTextBox.CaretIndex =
                                                selectionStart + clipboardText.Length;
                                        }
                                        // Otherwise, replace entire cell
                                        else
                                        {
                                            editTextBox.Text = clipboardText;
                                            editTextBox.CaretIndex = clipboardText.Length;
                                        }
                                    }
                                }
                            },
                            DispatcherPriority.Background
                        );

                        e.Handled = true;
                    }
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

        #region Copy Source to Target operation
        private void CopySourceToTarget(object sender, RoutedEventArgs e)
        {
            if (_dataGrid.SelectedItem is not DataRow selectedRow)
            {
                _notificationManager.Show(
                    new Notification(
                        "No Selection",
                        "Please select a row to copy from source to target.",
                        NotificationType.Information
                    )
                );
                return;
            }

            // Find the source and target column indices
            int sourceColumnIndex = -1;
            int targetColumnIndex = -1;

            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                var header = ((DataGridTextColumn)_dataGrid.Columns[i]).Header?.ToString();
                if (header?.Equals("source", StringComparison.OrdinalIgnoreCase) == true)
                {
                    sourceColumnIndex = i;
                }
                else if (header?.Equals("target", StringComparison.OrdinalIgnoreCase) == true)
                {
                    targetColumnIndex = i;
                }
            }

            if (sourceColumnIndex == -1)
            {
                _notificationManager.Show(
                    new Notification(
                        "Column Not Found",
                        "Source column not found.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            if (targetColumnIndex == -1)
            {
                _notificationManager.Show(
                    new Notification(
                        "Column Not Found",
                        "Target column not found.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            // Copy the text from source to target
            string sourceText = selectedRow.Values[sourceColumnIndex];
            var newValues = (string[])selectedRow.Values.Clone();
            newValues[targetColumnIndex] = sourceText;
            selectedRow.Values = newValues;

            // Mark as having unsaved changes
            _hasUnsavedChanges = true;

            _notificationManager.Show(
                new Notification(
                    "Text Copied",
                    "Source text copied to target column.",
                    NotificationType.Success
                )
            );
        }

        private void CopySourceToTargetMultiple(object sender, RoutedEventArgs e)
        {
            var selectedRows = _dataGrid.SelectedItems?.Cast<DataRow>().ToList();

            if (selectedRows == null || !selectedRows.Any())
            {
                _notificationManager.Show(
                    new Notification(
                        "No Selection",
                        "Please select one or more rows to copy from source to target.",
                        NotificationType.Information
                    )
                );
                return;
            }

            // Find the source and target column indices
            int sourceColumnIndex = -1;
            int targetColumnIndex = -1;

            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                var header = ((DataGridTextColumn)_dataGrid.Columns[i]).Header?.ToString();
                if (header?.Equals("source", StringComparison.OrdinalIgnoreCase) == true)
                {
                    sourceColumnIndex = i;
                }
                else if (header?.Equals("target", StringComparison.OrdinalIgnoreCase) == true)
                {
                    targetColumnIndex = i;
                }
            }

            if (sourceColumnIndex == -1 || targetColumnIndex == -1)
            {
                _notificationManager.Show(
                    new Notification(
                        "Columns Not Found",
                        "Source or target column not found.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            int copiedCount = 0;
            foreach (var row in selectedRows)
            {
                string sourceText = row.Values[sourceColumnIndex];
                if (!string.IsNullOrEmpty(sourceText))
                {
                    // Copy the text from source to target
                    var newValues = (string[])row.Values.Clone();
                    newValues[targetColumnIndex] = sourceText;
                    row.Values = newValues;
                    copiedCount++;
                }
            }

            // Mark as having unsaved changes
            _hasUnsavedChanges = true;

            _notificationManager.Show(
                new Notification(
                    "Text Copied",
                    $"Source text copied to target column for {copiedCount} row(s).",
                    NotificationType.Success
                )
            );
        }
        #endregion

        private static string GetOrCreateTempDirectory()
        {
            var exeDirectory = AppContext.BaseDirectory;
            // Create a unique instance ID so that if multiple instances are open, they don't overwrite eachother.
            var instanceId = Process.GetCurrentProcess().Id.ToString();
            var tempDirectoryName = $".temp-UnrealLocresEditor-{instanceId}";
            var tempDirectoryPath = Path.Combine(exeDirectory, tempDirectoryName);

            // Create folder if it does not exist
            if (!Directory.Exists(tempDirectoryPath))
            {
                Directory.CreateDirectory(tempDirectoryPath);

                // Set folder to hidden on Windows
                if (OperatingSystem.IsWindows())
                {
                    File.SetAttributes(
                        tempDirectoryPath,
                        FileAttributes.Directory | FileAttributes.Hidden
                    );
                }
            }

            return tempDirectoryPath;
        }

        // Unique file name so multiple instances don't overwrite eachother
        private string GetUniqueFileName(string baseName, string extension)
        {
            var instanceId = Process.GetCurrentProcess().Id.ToString();
            return $"{baseName}_{instanceId}{extension}";
        }

        private void CloseMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (SelectedDocument == null)
                return;

            // TODO: better unsaved changes.
            _documents.Remove(SelectedDocument);
        }
        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                new FilePickerFileType("Localization Files")
                {
                    Patterns = new[] { "*.locres" },
                },
                    },
                    AllowMultiple = false,
                }
            );

            if (result != null && result.Count > 0)
            {
                // 1. Get the path the user selected (Local Variable)
                string originalFilePath = result[0].Path.LocalPath;

                // Update Discord RPC
                _discordRPC.editStartTime = DateTime.UtcNow;
                _discordRPC.idleStartTime = null;
                _discordRPC.UpdatePresence(_appConfig.DiscordRPCEnabled, originalFilePath);

                // 2. Prepare unique CSV filename
                var instanceId = Process.GetCurrentProcess().Id;
                var csvFileName = $"{Path.GetFileNameWithoutExtension(originalFilePath)}_{instanceId}.csv";
                csvFile = Path.Combine(Directory.GetCurrentDirectory(), csvFileName);

                // Safety: Delete any old CSV with this name
                if (File.Exists(csvFile))
                {
                    File.Delete(csvFile);
                }

                // Check if UnrealLocres.exe exists
                var downloader = new UnrealLocresDownloader(this, _notificationManager);
                if (!await downloader.CheckAndDownloadUnrealLocres())
                {
                    return;
                }

                // 3. Run UnrealLocres.exe
                var process = new Process
                {
                    StartInfo = ProcessUtils.GetProcessStartInfo(
                        command: "export",
                        locresFilePath: originalFilePath,
                        useWine: this.UseWine,
                        csvFileName: csvFileName
                    ),
                };

                process.Start();
                await process.WaitForExitAsync(); // Non-blocking wait

                if (process.ExitCode == 0)
                {
                    try
                    {
                        // 4. Verify CSV creation (Handle tool ignoring custom name)
                        if (!File.Exists(csvFile))
                        {
                            // Check for the default name (e.g., Game.csv)
                            var defaultCsvFile = Path.Combine(
                                Directory.GetCurrentDirectory(),
                                $"{Path.GetFileNameWithoutExtension(originalFilePath)}.csv"
                            );

                            if (File.Exists(defaultCsvFile))
                            {
                                // Rename it to our unique name
                                File.Move(defaultCsvFile, csvFile, overwrite: true);
                            }
                            else
                            {
                                _notificationManager.Show(new Notification("Error", "CSV file not found after export.", NotificationType.Error));
                                return;
                            }
                        }

                        // 5. Create a specific working copy of the .locres file
                        // This ensures every opened file has a unique path in the temp folder,
                        // preventing tabs from overwriting each other.
                        var importedLocresDir = GetOrCreateTempDirectory();
                        var uniqueLocresFileName = $"{Path.GetFileNameWithoutExtension(originalFilePath)}_{instanceId}{Path.GetExtension(originalFilePath)}";
                        var importedLocresPath = Path.Combine(importedLocresDir, uniqueLocresFileName);

                        File.Copy(originalFilePath, importedLocresPath, true);

                        // Update global tracking (optional, but LoadCsv handles the critical part now)
                        _currentLocresFilePath = importedLocresPath;

                        // 6. Load data into the tab
                        LoadCsv(csvFile, importedLocresPath, originalFilePath);
                    }
                    catch (Exception ex)
                    {
                        _notificationManager.Show(new Notification("Error Opening File", ex.Message, NotificationType.Error));
                    }
                    finally
                    {
                        // Cleanup temp CSV
                        if (File.Exists(csvFile))
                        {
                            File.Delete(csvFile);
                        }
                    }
                }
                else
                {
                    // Handle Process Failure
                    var output = await process.StandardOutput.ReadToEndAsync();
                    Console.WriteLine($"Error reading locres data: {output}");
                    _notificationManager.Show(
                        new Notification(
                            "Error reading locres data:",
                            output,
                            NotificationType.Error
                        )
                    );

                    if (File.Exists(csvFile))
                    {
                        File.Delete(csvFile);
                    }
                }
            }
        }

        // Change signature to accept locresPath
        // Change signature to accept locresPath
        private void LoadCsv(string csvFilePath, string locresPath, string originalUserPath = null)
        {
            try
            {
                // 1. PREPARE DATA
                var tempRows = new ObservableCollection<DataRow>();
                var tempHeaders = new List<string>();

                using (var reader = new StreamReader(csvFilePath))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)))
                {
                    bool isFirstRow = true;
                    while (csv.Read())
                    {
                        string[] stringValues = new string[csv.Parser.Count];
                        for (int i = 0; i < csv.Parser.Count; i++) stringValues[i] = csv.GetField(i);

                        if (isFirstRow)
                        {
                            for (int i = 0; i < stringValues.Length; i++) tempHeaders.Add(stringValues[i]);
                            isFirstRow = false;
                        }
                        else
                        {
                            var key = stringValues[0];
                            var isNew = _newKeySet != null && _newKeySet.Contains(key);
                            tempRows.Add(new DataRow { Values = stringValues, IsNewKey = isNew });
                        }
                    }
                }

                // 2. FIND OR CREATE DOCUMENT
                // We identify documents by their WORKING path (the temp file)
                var doc = _documents.FirstOrDefault(d =>
                        string.Equals(d.WorkingPath, locresPath, StringComparison.OrdinalIgnoreCase));

                if (doc == null)
                {
                    // FIX: Initialize with the "Pretty" path (originalUserPath) if we have it.
                    // This sets the 'OriginalPath' property in the model, which DisplayName uses.
                    string pathForDisplay = !string.IsNullOrEmpty(originalUserPath) ? originalUserPath : locresPath;

                    doc = new LocresDocument(pathForDisplay);

                    // CRITICAL: Ensure WorkingPath points to the actual temp file we are editing
                    doc.WorkingPath = locresPath;

                    _documents.Add(doc);
                }

                // 3. UPDATE DOCUMENT DATA
                doc.ActiveCsvPath = csvFilePath;

                doc.Rows.Clear();
                foreach (var row in tempRows) doc.Rows.Add(row);

                doc.ColumnHeaders.Clear();
                foreach (var header in tempHeaders) doc.ColumnHeaders.Add(header);

                doc.HasUnsavedChanges = _hasUnsavedChanges;

                // 4. UPDATE UI
                if (SelectedDocument == doc)
                {
                    ApplySelectedDocumentState();
                }
                else
                {
                    SelectedDocument = doc;
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(new Notification("Error Loading CSV", ex.Message, NotificationType.Error));
            }
        }

        private void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLocresFilePath == null)
            {
                _notificationManager.Show(
                    new Notification(
                        "No File Open",
                        "Please open a locres file first.",
                        NotificationType.Error
                    )
                );
                return;
            }
            if (_rows != null && _rows.Count > 0)
            {
                SaveEditedData(true); // Saves, and opens save location in file explorer with 'true'.
            }
            else
            {
                _notificationManager.Show(
                    new Notification(
                        "No Data",
                        "There's no data to export.",
                        NotificationType.Information
                    )
                );
            }
        }

        public void SaveEditedData(bool openExplorer = false)
        {
            if (string.IsNullOrEmpty(_currentLocresFilePath))
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        "No file is currently open to save.",
                        NotificationType.Error
                    )
                );
                return;
            }

            var exeDirectory = AppContext.BaseDirectory;
            var csvFileName = GetUniqueFileName(
                Path.GetFileNameWithoutExtension(_currentLocresFilePath) + "_edited",
                ".csv"
            );
            var csvFile = Path.Combine(exeDirectory, csvFileName);

            // Save edited data to CSV
            using (var writer = new StreamWriter(csvFile, false, Encoding.UTF8))
            using (
                var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture))
            )
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
                StartInfo = ProcessUtils.GetProcessStartInfo(
                    command: "import",
                    locresFilePath: _currentLocresFilePath,
                    useWine: this.UseWine,
                    csvFileName: csvFileName
                ),
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                var instanceId = Process.GetCurrentProcess().Id.ToString();
                var modifiedLocres = _currentLocresFilePath + ".new";

                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(modifiedLocres);
                var baseFileName = fileNameWithoutExtension.Split('_')[0];

                // Create export directory with current date and time
                var exportDirectory = Path.Combine(exeDirectory, "export");
                var dateTimeFolder = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var destinationDirectory = Path.Combine(exportDirectory, dateTimeFolder);

                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                var newFileName = baseFileName + ".locres";
                var destinationFile = Path.Combine(destinationDirectory, newFileName);

                try
                {
                    // Move and rename
                    File.Move(modifiedLocres, destinationFile);

                    // Open file explorer/equivalent window to where locres has been saved.
                    if (openExplorer)
                    {
                        OpenDirectoryInExplorer(destinationDirectory);
                    }

                    _notificationManager.Show(
                        new Notification(
                            "Success!",
                            $"File saved as {Path.GetFileName(destinationFile)} in {destinationDirectory}",
                            NotificationType.Success
                        )
                    );
                    _hasUnsavedChanges = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error moving file: {ex.Message}");
                    _notificationManager.Show(
                        new Notification(
                            "Error moving file:",
                            $"{ex.Message}",
                            NotificationType.Error
                        )
                    );
                }
            }
            else
            {
                Console.WriteLine($"Error importing: {process.StandardOutput.ReadToEnd()}");
                _notificationManager.Show(
                    new Notification(
                        "Error importing:",
                        $"{process.StandardOutput.ReadToEnd()}",
                        NotificationType.Error
                    )
                );
            }

            // Clean up CSV file
            File.Delete(csvFile);
        }

        private async void OpenSpreadsheetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Safety check: We can only import a CSV if we already have a Locres file open
            if (string.IsNullOrEmpty(_currentLocresFilePath))
            {
                _notificationManager.Show(new Notification("Error", "Please open a .locres file first before importing a spreadsheet.", NotificationType.Warning));
                return;
            }

            var storageProvider = StorageProvider;
            var result = await storageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                new FilePickerFileType("Spreadsheet Files")
                {
                    Patterns = new[] { "*.csv" },
                },
                    },
                    AllowMultiple = false,
                }
            );

            if (result != null && result.Count > 0)
            {
                string filePath = result[0].Path.LocalPath;

                // Pass BOTH the CSV path and the current Locres path
                LoadCsv(filePath, _currentLocresFilePath);

                csvFile = filePath;
                _discordRPC.editStartTime = DateTime.UtcNow;
                _discordRPC.idleStartTime = null;

                _discordRPC.UpdatePresence(_appConfig.DiscordRPCEnabled, _currentLocresFilePath);
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
                    },
                };

                var storageFile = await StorageProvider.SaveFilePickerAsync(saveOptions);

                if (storageFile != null)
                {
                    var filePath = storageFile.Path.LocalPath;
                    SaveAsCsv(filePath);

                    _notificationManager.Show(
                        new Notification(
                            "Success",
                            $"File saved as {Path.GetFileName(filePath)}",
                            NotificationType.Success
                        )
                    );
                }
            }
            else
            {
                _notificationManager.Show(
                    new Notification(
                        "No Data",
                        "There's no data to export.",
                        NotificationType.Information
                    )
                );
            }
        }

        private void SaveAsCsv(string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            using (
                var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture))
            )
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

        #region Merge Operation

        /***
         * Merging Files:
         *
         * usage: UnrealLocres.exe merge target_locres_path source_locres_path [-o output_path]
         *
         * positional arguments:
         * target_locres_path      Merge target locres file path, the file you want to translate
         * source_locres_path      Merge source locres file path, the file that has additional lines
         *
         * optional arguments:
         * -o                      Output locres file path (default: {target_locres_path}.new)
         *
         * Merge two locres files into one, adding strings that are present in source but not in target file.
         ***/

        private HashSet<string> _newKeySet = new();

        private async void MergeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are unsaved changes before mergin g
            if (!await MergeSaveChanges())
                return;

            try
            {
                // Pick TARGET file (base file to update)
                var targetFileResult = await StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Select BASE Locres File (to be updated)",
                        FileTypeFilter = new List<FilePickerFileType>
                        {
                            new FilePickerFileType("Locres Files")
                            {
                                Patterns = new[] { "*.locres" },
                            },
                        },
                        AllowMultiple = false,
                    }
                );
                if (targetFileResult.Count == 0)
                    return;
                var targetFile = targetFileResult[0].Path.LocalPath;

                // Pick SOURCE file (with additional keys)
                var sourceFileResult = await StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title = "Select ADDITIONAL Locres File (with new keys)",
                        FileTypeFilter = new List<FilePickerFileType>
                        {
                            new FilePickerFileType("Locres Files")
                            {
                                Patterns = new[] { "*.locres" },
                            },
                        },
                        AllowMultiple = false,
                    }
                );
                if (sourceFileResult.Count == 0)
                    return;
                var sourceFile = sourceFileResult[0].Path.LocalPath;

                // Set output path to a temp file
                var tempDir = GetOrCreateTempDirectory();
                var mergedFileName =
                    $"{Path.GetFileNameWithoutExtension(targetFile)}_merged_{Process.GetCurrentProcess().Id}{Path.GetExtension(targetFile)}";
                var outputPath = Path.Combine(tempDir, mergedFileName);

                // Check if UnrealLocres exists
                var downloader = new UnrealLocresDownloader(this, _notificationManager);
                if (!await downloader.CheckAndDownloadUnrealLocres())
                    return;

                // Run merge command
                using (var process = new Process())
                {
                    process.StartInfo = ProcessUtils.GetMergeProcessStartInfo(
                        targetLocresPath: targetFile,
                        sourceLocresPath: sourceFile,
                        useWine: UseWine,
                        outputPath: outputPath
                    );

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender2, args) =>
                    {
                        if (args.Data != null)
                            outputBuilder.AppendLine(args.Data);
                    };
                    process.ErrorDataReceived += (sender2, args) =>
                    {
                        if (args.Data != null)
                            errorBuilder.AppendLine(args.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());

                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();

                    if (process.ExitCode == 0)
                    {
                        _notificationManager.Show(
                            new Notification(
                                "Merge Successful",
                                $"Files merged successfully!\nOpening merged file...",
                                NotificationType.Success
                            )
                        );

                        // Highlight new keys
                        await HighlightNewKeysAndOpen(targetFile, outputPath);
                    }
                    else
                    {
                        _notificationManager.Show(
                            new Notification(
                                "Merge Failed",
                                $"Error merging files:\nExit Code: {process.ExitCode}\nOutput: {output}\nError: {error}",
                                NotificationType.Error
                            )
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Merge Error",
                        $"Unexpected error: {ex.Message}",
                        NotificationType.Error
                    )
                );

                Console.WriteLine($"Error during merge operation: {ex}");
            }
        }

        private async Task HighlightNewKeysAndOpen(string targetLocresPath, string mergedLocresPath)
        {
            try
            {
                // Export both files to CSV
                var tempDir = GetOrCreateTempDirectory();

                // Get list of existing CSV files before export
                var existingCsvFiles = Directory.GetFiles(tempDir, "*.csv").ToHashSet();

                // Export target file
                var exportTarget = new Process
                {
                    StartInfo = ProcessUtils.GetProcessStartInfo(
                        command: "export",
                        locresFilePath: targetLocresPath,
                        useWine: this.UseWine,
                        csvFileName: "target.csv"
                    ),
                };

                exportTarget.StartInfo.WorkingDirectory = tempDir;
                exportTarget.StartInfo.RedirectStandardError = true;

                var targetOutputBuilder = new StringBuilder();
                var targetErrorBuilder = new StringBuilder();

                exportTarget.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        targetOutputBuilder.AppendLine(args.Data);
                };
                exportTarget.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        targetErrorBuilder.AppendLine(args.Data);
                };

                exportTarget.Start();
                exportTarget.BeginOutputReadLine();
                exportTarget.BeginErrorReadLine();
                await Task.Run(() => exportTarget.WaitForExit());

                // Check if target export succeeded
                if (exportTarget.ExitCode != 0)
                {
                    var errorMessage =
                        $"Failed to export target file.\n"
                        + $"Exit code: {exportTarget.ExitCode}\n"
                        + $"Output: {targetOutputBuilder}\n"
                        + $"Error: {targetErrorBuilder}";

                    _notificationManager.Show(
                        new Notification("Export Error", errorMessage, NotificationType.Error)
                    );
                    return;
                }

                // Find the new CSV file created for target
                var csvFilesAfterTarget = Directory.GetFiles(tempDir, "*.csv").ToHashSet();
                var targetCsvFiles = csvFilesAfterTarget.Except(existingCsvFiles).ToList();

                if (targetCsvFiles.Count == 0)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Export Error",
                            "No CSV file was created for target locres file",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                var targetCsv = targetCsvFiles.First();

                // Update existing files list
                existingCsvFiles = csvFilesAfterTarget;

                // Export merged file
                var exportMerged = new Process
                {
                    StartInfo = ProcessUtils.GetProcessStartInfo(
                        command: "export",
                        locresFilePath: mergedLocresPath,
                        useWine: this.UseWine,
                        csvFileName: "merged.csv"
                    ),
                };

                exportMerged.StartInfo.WorkingDirectory = tempDir;
                exportMerged.StartInfo.RedirectStandardError = true;

                var mergedOutputBuilder = new StringBuilder();
                var mergedErrorBuilder = new StringBuilder();

                exportMerged.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        mergedOutputBuilder.AppendLine(args.Data);
                };
                exportMerged.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        mergedErrorBuilder.AppendLine(args.Data);
                };

                exportMerged.Start();
                exportMerged.BeginOutputReadLine();
                exportMerged.BeginErrorReadLine();
                await Task.Run(() => exportMerged.WaitForExit());

                // Check if merged export succeeded
                if (exportMerged.ExitCode != 0)
                {
                    var errorMessage =
                        $"Failed to export merged file.\n"
                        + $"Exit code: {exportMerged.ExitCode}\n"
                        + $"Output: {mergedOutputBuilder}\n"
                        + $"Error: {mergedErrorBuilder}";

                    _notificationManager.Show(
                        new Notification("Export Error", errorMessage, NotificationType.Error)
                    );
                    return;
                }

                // Find the new CSV file created for merged
                var csvFilesAfterMerged = Directory.GetFiles(tempDir, "*.csv").ToHashSet();
                var mergedCsvFiles = csvFilesAfterMerged.Except(existingCsvFiles).ToList();

                if (mergedCsvFiles.Count == 0)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Export Error",
                            "No CSV file was created for merged locres file",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                var mergedCsv = mergedCsvFiles.First();

                // Read keys from target CSV (assume first column is the key)
                var targetKeys = new HashSet<string>();
                try
                {
                    using (var reader = new StreamReader(targetCsv))
                    using (
                        var csv = new CsvHelper.CsvReader(
                            reader,
                            new CsvHelper.Configuration.CsvConfiguration(
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        )
                    )
                    {
                        if (csv.Read() && csv.ReadHeader()) // skip header if it exists
                        {
                            while (csv.Read())
                            {
                                var key = csv.GetField(0);
                                if (!string.IsNullOrEmpty(key))
                                {
                                    targetKeys.Add(key);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "CSV Parse Error",
                            $"Failed to parse target CSV ({Path.GetFileName(targetCsv)}): {ex.Message}",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                // Read keys from merged CSV
                var mergedKeys = new List<string>();
                try
                {
                    using (var reader = new StreamReader(mergedCsv))
                    using (
                        var csv = new CsvHelper.CsvReader(
                            reader,
                            new CsvHelper.Configuration.CsvConfiguration(
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        )
                    )
                    {
                        if (csv.Read() && csv.ReadHeader()) // skip header if it exists
                        {
                            while (csv.Read())
                            {
                                var key = csv.GetField(0);
                                if (!string.IsNullOrEmpty(key))
                                {
                                    mergedKeys.Add(key);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "CSV Parse Error",
                            $"Failed to parse merged CSV ({Path.GetFileName(mergedCsv)}): {ex.Message}",
                            NotificationType.Error
                        )
                    );
                    return;
                }

                // Find new keys (keys that are in merged but not in target)
                _newKeySet = new HashSet<string>(mergedKeys.Where(k => !targetKeys.Contains(k)));

                // Show info about new keys found
                if (_newKeySet.Count > 0)
                {
                    _notificationManager.Show(
                        new Notification(
                            "New Keys Found",
                            $"Found {_newKeySet.Count} new keys.",
                            NotificationType.Success
                        )
                    );
                }
                else
                {
                    _notificationManager.Show(
                        new Notification(
                            "No New Keys",
                            "No new keys found in the merge operation.",
                            NotificationType.Information
                        )
                    );
                }

                // Open merged file in editor
                _currentLocresFilePath = mergedLocresPath;
                LoadCsv(mergedCsv, mergedLocresPath, null);


                // Clean up temp CSV files
                try
                {
                    if (File.Exists(targetCsv))
                        File.Delete(targetCsv);
                    if (File.Exists(mergedCsv))
                        File.Delete(mergedCsv);
                }
                catch (Exception ex)
                {
                    // Non-critical error, just log it
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to clean up temp files: {ex.Message}"
                    );
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Highlight Error",
                        $"Error during highlight operation: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }

        #endregion


        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _dataGrid = this.FindControl<DataGrid>("uiDataGrid");
            _dataGrid.AddHandler(KeyDownEvent, DataGrid_PreviewKeyDown, RoutingStrategies.Tunnel);

            var linuxMenuItem = this.FindControl<MenuItem>("uiLinuxHeader");
            linuxMenuItem.IsVisible = PlatformUtils.IsLinux();

            var preferencesMenuItem = this.FindControl<MenuItem>("uiPreferencesMenuItem");
            preferencesMenuItem.Click += PreferencesMenuItem_Click;

            var winePrefixMenuItem = this.FindControl<MenuItem>("uiWinePrefix");
            winePrefixMenuItem.IsVisible = PlatformUtils.IsLinux();
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
                WineUtils.InitializeWinePrefix();
                _notificationManager.Show(
                    new Notification(
                        "Success",
                        "Success. Make sure to install Wine MONO and set to 32 bit.",
                        NotificationType.Success
                    )
                );
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        $"Failed to initialize Wine prefix: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }

        // Report issue
        private const string GitHubIssueUrl =
            "https://github.com/Snoozeds/UnrealLocresEditor/issues/new";

        private void ReportIssueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = GitHubIssueUrl,
                    UseShellExecute = true,
                };
                Process.Start(psi);
            }
            catch (Exception) { }
        }

        // About
        private AboutWindow aboutWindow;

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (aboutWindow == null)
            {
                aboutWindow = new AboutWindow();
                aboutWindow.Initialize(_notificationManager, this);
                aboutWindow.Closed += AboutWindow_Closed;
            }

            aboutWindow.Show(this);
            aboutWindow.Activate();
        }

        private void AboutWindow_Closed(Object sender, EventArgs e)
        {
            aboutWindow = null;
        }

        private async Task<bool> MergeSaveChanges()
        {
            if (!_hasUnsavedChanges)
                return true;

            var dialog = new Window
            {
                Title = "Unsaved Changes",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock
                        {
                            Text =
                                "You have unsaved changes. Would you like to save before merging?",
                            TextWrapping = TextWrapping.Wrap,
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
                                new Avalonia.Controls.Button { Content = "Cancel" },
                            },
                        },
                    },
                },
            };

            var tcs = new TaskCompletionSource<string>();
            var buttons = ((StackPanel)((StackPanel)dialog.Content).Children[1]).Children;
            ((Avalonia.Controls.Button)buttons[0]).Click += (s, e) =>
            {
                try
                {
                    SaveEditedData();
                    tcs.SetResult("Save");
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    _notificationManager.Show(
                        new Notification(
                            "Save Error",
                            $"Failed to save: {ex.Message}",
                            NotificationType.Error
                        )
                    );
                    tcs.SetResult("Cancel");
                    dialog.Close();
                }
            };
            ((Avalonia.Controls.Button)buttons[1]).Click += (s, e) =>
            {
                tcs.SetResult("Don't Save");
                dialog.Close();
            };
            ((Avalonia.Controls.Button)buttons[2]).Click += (s, e) =>
            {
                tcs.SetResult("Cancel");
                dialog.Close();
            };

            await dialog.ShowDialog(this);
            var result = await tcs.Task;
            return result == "Save" || result == "Don't Save";
        }

        private void OpenDirectoryInExplorer(string directoryPath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(
                        new ProcessStartInfo("explorer.exe", $"\"{directoryPath}\"")
                        {
                            UseShellExecute = true,
                        }
                    );
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start(
                        new ProcessStartInfo("xdg-open", $"\"{directoryPath}\"")
                        {
                            UseShellExecute = true,
                        }
                    );
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(
                        new ProcessStartInfo("open", $"\"{directoryPath}\"")
                        {
                            UseShellExecute = true,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification(
                        "Error",
                        $"Failed to open directory: {ex.Message}",
                        NotificationType.Error
                    )
                );
            }
        }
    }
}
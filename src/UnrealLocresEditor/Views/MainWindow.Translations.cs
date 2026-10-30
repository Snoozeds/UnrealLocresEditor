using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnrealLocresEditor.Models;
using UnrealLocresEditor.Utils;

#nullable disable

namespace UnrealLocresEditor.Views
{
    // Import/export of translations as a plain text file (see TranslationText).
    public partial class MainWindow
    {
        private static readonly FilePickerFileType TranslationFileType = new("Translation text file")
        {
            Patterns = new[] { "*.txt", "*.loctxt" },
        };

        private int GetColumnIndex(string header)
        {
            for (int i = 0; i < _dataGrid.Columns.Count; i++)
            {
                if (((DataGridTextColumn)_dataGrid.Columns[i]).Header?.ToString()
                    .Equals(header, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return i;
                }
            }
            return -1;
        }

        private bool CheckTranslationColumns(out int keyIndex, out int sourceIndex, out int targetIndex)
        {
            keyIndex = GetColumnIndex("key");
            sourceIndex = GetColumnIndex("source");
            targetIndex = GetColumnIndex("target");

            if (_rows == null || _rows.Count == 0 || keyIndex < 0 || sourceIndex < 0 || targetIndex < 0)
            {
                _notificationManager.Show(
                    new Notification(
                        "No File Open",
                        "Please open a locres file first.",
                        NotificationType.Error
                    )
                );
                return false;
            }
            return true;
        }

        private async void ImportTranslationsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckTranslationColumns(out int keyIndex, out int sourceIndex, out int targetIndex))
                return;

            var result = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Import translations",
                    FileTypeFilter = new[] { TranslationFileType, FilePickerFileTypes.All },
                    AllowMultiple = false,
                }
            );
            if (result == null || result.Count == 0)
                return;

            string content;
            try
            {
                content = File.ReadAllText(result[0].Path.LocalPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification("Import Error", ex.Message, NotificationType.Error)
                );
                return;
            }

            var errors = new List<string>();
            var entries = TranslationText.Parse(content, errors);

            var found = TranslationText.Match(entries, _rows.Select(r => r.Values[keyIndex]).ToList(), errors);

            // Line break used when neither the source nor the target text has one.
            var defaultNewLine = _rows
                .Select(r => TranslationText.DetectNewLine(r.Values[sourceIndex]))
                .Where(n => n != null)
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? Environment.NewLine;

            var matches = new List<(DataRow Row, string Text)>();
            foreach (var (rowIndex, entry) in found)
            {
                var row = _rows[rowIndex];
                var newLine = TranslationText.DetectNewLine(row.Values[targetIndex])
                    ?? TranslationText.DetectNewLine(row.Values[sourceIndex])
                    ?? defaultNewLine;
                matches.Add((row, TranslationText.NormalizeNewLines(entry.Text, newLine)));
            }

            if (errors.Count > 0)
            {
                var message =
                    $"{errors.Count} problem(s) found in {Path.GetFileName(result[0].Path.LocalPath)}:";
                var buttons = matches.Count > 0
                    ? new[] { $"Import the {matches.Count} valid entries", "Cancel" }
                    : new[] { "Close" };

                var choice = await ShowTranslationReport("Import translations", message, errors, buttons);
                if (matches.Count == 0 || choice != buttons[0])
                    return;
            }

            int changed = 0;
            foreach (var (row, text) in matches)
            {
                if (row.Values[targetIndex] == text)
                    continue;

                var newValues = (string[])row.Values.Clone();
                newValues[targetIndex] = text;
                row.Values = newValues;
                changed++;
            }

            if (changed > 0)
                _hasUnsavedChanges = true;

            _notificationManager.Show(
                new Notification(
                    "Translations Imported",
                    $"{changed} text(s) replaced, {matches.Count - changed} already identical.",
                    NotificationType.Success
                )
            );
        }

        private async void ExportTranslationsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckTranslationColumns(out int keyIndex, out int sourceIndex, out int targetIndex))
                return;

            var file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Export translations",
                    FileTypeChoices = new[] { TranslationFileType },
                    DefaultExtension = "txt",
                    SuggestedFileName = Path.GetFileNameWithoutExtension(_currentLocresFilePath) + ".txt",
                }
            );
            if (file == null)
                return;

            // The text the game shows: the target when there is one, otherwise the source.
            var items = _rows
                .Select(r => new KeyValuePair<string, string>(
                    r.Values[keyIndex],
                    string.IsNullOrEmpty(r.Values[targetIndex]) ? r.Values[sourceIndex] : r.Values[targetIndex]
                ))
                .ToList();

            try
            {
                File.WriteAllText(
                    file.Path.LocalPath,
                    TranslationText.Format(items, "\r\n"),
                    new UTF8Encoding(false)
                );
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification("Export Error", ex.Message, NotificationType.Error)
                );
                return;
            }

            var unsafeKeys = items.Where(i => TranslationText.HasKeyMarkerLine(i.Value)).Select(i => i.Key).ToList();
            if (unsafeKeys.Count > 0)
            {
                await ShowTranslationReport(
                    "Export translations",
                    $"These texts contain a line starting with \"{TranslationText.KeyMarker}\" and will not import back correctly:",
                    unsafeKeys,
                    new[] { "OK" }
                );
            }

            _notificationManager.Show(
                new Notification(
                    "Translations Exported",
                    $"{items.Count} text(s) saved to {Path.GetFileName(file.Path.LocalPath)}.",
                    NotificationType.Success
                )
            );
        }

        private Task<string> ShowTranslationReport(string title, string message, IEnumerable<string> lines, string[] buttons)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 650,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Background,
                Foreground = Foreground,
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var label in buttons)
            {
                var button = new Button { Content = label };
                button.Click += (_, _) => dialog.Close(label);
                buttonPanel.Children.Add(button);
            }

            var layout = new DockPanel { Margin = new Thickness(15) };
            var header = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            buttonPanel.Margin = new Thickness(0, 10, 0, 0);
            layout.Children.Add(header);
            layout.Children.Add(buttonPanel);
            layout.Children.Add(
                new ScrollViewer
                {
                    Content = new SelectableTextBlock
                    {
                        Text = string.Join(Environment.NewLine, lines),
                        TextWrapping = TextWrapping.Wrap,
                    },
                }
            );

            dialog.Content = layout;
            return dialog.ShowDialog<string>(this);
        }
    }
}

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
        private const int MaxReportLines = 500;

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

        // A row read from a ragged CSV can be shorter than the header.
        private static string Cell(DataRow row, int index)
        {
            return index < row.Values.Length ? row.Values[index] ?? string.Empty : string.Empty;
        }

        private bool CheckTranslationColumns(out int keyIndex, out int sourceIndex, out int targetIndex)
        {
            keyIndex = GetColumnIndex("key");
            sourceIndex = GetColumnIndex("source");
            targetIndex = GetColumnIndex("target");

            if (_rows == null || _rows.Count == 0)
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
            if (keyIndex < 0 || sourceIndex < 0 || targetIndex < 0)
            {
                _notificationManager.Show(
                    new Notification(
                        "Missing Columns",
                        "The open file needs key, source and target columns.",
                        NotificationType.Error
                    )
                );
                return false;
            }
            return true;
        }

        // Line break used when neither the source nor the target text has one.
        private string DominantSourceNewLine(int sourceIndex)
        {
            return _rows
                .Select(r => TranslationText.DetectNewLine(Cell(r, sourceIndex)))
                .Where(n => n != null)
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? Environment.NewLine;
        }

        private async void ImportTranslationsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ImportTranslations();
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification("Import Error", ex.Message, NotificationType.Error)
                );
            }
        }

        private async Task ImportTranslations()
        {
            _dataGrid.CommitEdit();
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

            var path = result[0].Path.LocalPath;
            var fileName = Path.GetFileName(path);
            string content;
            try
            {
                content = File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException)
            {
                _notificationManager.Show(
                    new Notification(
                        "Import Error",
                        $"{fileName} is not a UTF-8 file. Save it as UTF-8 and try again.",
                        NotificationType.Error
                    )
                );
                return;
            }

            var errors = new List<string>();
            var entries = TranslationText.Parse(content, errors);
            if (entries.Count == 0)
            {
                _notificationManager.Show(
                    new Notification(
                        "Import Translations",
                        $"No \"{TranslationText.KeyMarker}key\" block found in {fileName}.",
                        NotificationType.Warning
                    )
                );
                return;
            }

            var found = TranslationText.Match(entries, _rows.Select(r => Cell(r, keyIndex)).ToList(), errors);

            string defaultNewLine = null;
            int identical = 0;
            var matches = new List<(DataRow Row, string Text)>();
            foreach (var (rowIndex, entry) in found)
            {
                var row = _rows[rowIndex];
                var target = Cell(row, targetIndex);
                var source = Cell(row, sourceIndex);

                // Same text: the row is left as it is, line breaks included. An empty
                // target already shows the source, so the source text counts as the same.
                if (TranslationText.NormalizeNewLines(target, "\n") == entry.Text
                    || (target.Length == 0 && TranslationText.NormalizeNewLines(source, "\n") == entry.Text))
                {
                    identical++;
                    continue;
                }

                var newLine = TranslationText.DetectNewLine(target)
                    ?? TranslationText.DetectNewLine(source)
                    ?? (defaultNewLine ??= DominantSourceNewLine(sourceIndex));
                matches.Add((row, TranslationText.NormalizeNewLines(entry.Text, newLine)));
            }

            if (errors.Count > 0)
            {
                var message = $"{errors.Count} problem(s) found in {fileName}:";
                var buttons = matches.Count > 0
                    ? new[] { $"Import the {matches.Count} changed text(s)", "Cancel" }
                    : new[] { "Close" };

                var choice = await ShowTranslationReport("Import translations", message, errors, buttons);
                if (matches.Count == 0 || choice != buttons[0])
                    return;
            }

            foreach (var (row, text) in matches)
            {
                var newValues = (string[])row.Values.Clone();
                if (newValues.Length <= targetIndex)
                    Array.Resize(ref newValues, targetIndex + 1);
                newValues[targetIndex] = text;
                row.Values = newValues;
            }

            if (matches.Count > 0)
                _hasUnsavedChanges = true;

            _notificationManager.Show(
                new Notification(
                    "Translations Imported",
                    $"{matches.Count} text(s) replaced, {identical} already identical.",
                    NotificationType.Success
                )
            );
        }

        private async void ExportTranslationsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExportTranslations();
            }
            catch (Exception ex)
            {
                _notificationManager.Show(
                    new Notification("Export Error", ex.Message, NotificationType.Error)
                );
            }
        }

        private async Task ExportTranslations()
        {
            _dataGrid.CommitEdit();
            if (!CheckTranslationColumns(out int keyIndex, out int sourceIndex, out int targetIndex))
                return;

            // The text the game shows: the target when there is one, otherwise the source.
            var items = _rows
                .Select(r =>
                {
                    var target = Cell(r, targetIndex);
                    return new KeyValuePair<string, string>(
                        Cell(r, keyIndex),
                        target.Length == 0 ? Cell(r, sourceIndex) : target
                    );
                })
                .ToList();

            var unsafeKeys = items.Where(i => TranslationText.HasKeyMarkerLine(i.Value)).Select(i => i.Key).ToList();
            if (unsafeKeys.Count > 0)
            {
                var buttons = new[] { "Export anyway", "Cancel" };
                var choice = await ShowTranslationReport(
                    "Export translations",
                    $"These texts contain a line starting with \"{TranslationText.KeyMarker}\" or \"{TranslationText.CommentMarker}\" and will not import back correctly:",
                    unsafeKeys,
                    buttons
                );
                if (choice != buttons[0])
                    return;
            }

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

            File.WriteAllText(
                file.Path.LocalPath,
                TranslationText.Format(items, "\r\n"),
                new UTF8Encoding(false)
            );

            _notificationManager.Show(
                new Notification(
                    "Translations Exported",
                    $"{items.Count} text(s) saved to {Path.GetFileName(file.Path.LocalPath)}.",
                    NotificationType.Success
                )
            );
        }

        private Task<string> ShowTranslationReport(string title, string message, IList<string> lines, string[] buttons)
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

            // Thousands of lines (e.g. a file for another game) would freeze the text block.
            var text = string.Join(Environment.NewLine, lines.Take(MaxReportLines));
            if (lines.Count > MaxReportLines)
                text += Environment.NewLine + $"... and {lines.Count - MaxReportLines} more.";

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
                        Text = text,
                        TextWrapping = TextWrapping.Wrap,
                    },
                }
            );

            dialog.Content = layout;
            return dialog.ShowDialog<string>(this);
        }
    }
}

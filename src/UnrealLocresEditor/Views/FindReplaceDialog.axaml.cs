using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UnrealLocresEditor.Models;

namespace UnrealLocresEditor.Views
{
    public partial class FindReplaceDialog : Window
    {

        public FindReplaceDialog()
        {
            AvaloniaXamlLoader.Load(this);
            this.Opened += OnWindowOpened;
        }

        private void OnWindowOpened(object? sender, EventArgs e)
        {
            InitializeComponent();

            uiFindButton = this.FindControl<Button>("uiFindButton");
            uiReplaceButton = this.FindControl<Button>("uiReplaceButton");
            uiReplaceAllButton = this.FindControl<Button>("uiReplaceAllButton");
            uiSearchTextBox = this.FindControl<TextBox>("uiSearchTextBox");
            uiReplaceTextBox = this.FindControl<TextBox>("uiReplaceTextBox");

            uiMatchCaseCheckBox = this.FindControl<CheckBox>("uiMatchCaseCheckBox");
            uiMatchCellCheckBox = this.FindControl<CheckBox>("uiMatchCellCheckBox");
            uiMatchWholeWordCheckBox = this.FindControl<CheckBox>("uiMatchWholeWordCheckBox");
            uiLocresModeCheckBox = this.FindControl<CheckBox>("uiLocresModeCheckBox");

            uiFindButton.Click += FindButton_Click;
            uiReplaceButton.Click += ReplaceButton_Click;
            uiReplaceAllButton.Click += ReplaceAllButton_Click;
        }

        public MainWindow? MainWindow { get; set; }
        private int _currentRowIndex = -1;
        private int _currentMatchIndex = -1;
        private int _totalMatches = 0;
        private string _lastSearchTerm = "";

        private void FindButton_Click(object? sender, RoutedEventArgs e)
        {
            string currentSearchTerm = uiSearchTextBox.Text;
            if (currentSearchTerm != _lastSearchTerm)
            {
                _currentMatchIndex = -1;
                _currentRowIndex = -1;
                _lastSearchTerm = string.Empty;
            }

            FindText(currentSearchTerm);
        }

        private async Task ScrollToSelectedRow(DataGrid dataGrid, int rowIndex, int colIndex)
        {
            if (
                rowIndex < 0
                || rowIndex >= MainWindow._rows.Count
                || colIndex < 0
                || colIndex >= dataGrid.Columns.Count
            )
            {
                return;
            }

            var row = MainWindow._rows[rowIndex];
            var column = dataGrid.Columns[colIndex];

            // Scroll to row and column
            dataGrid.ScrollIntoView(row, column);
        }

        public async void FindText(string searchTerm, bool forward = true)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return;

            var dataGrid = MainWindow._dataGrid;
            var items = MainWindow._rows;

            if (items == null || dataGrid.Columns.Count == 0)
                return;

            bool isMatchCase = uiMatchCaseCheckBox.IsChecked ?? false;
            bool isMatchWholeWord = uiMatchWholeWordCheckBox.IsChecked ?? false;
            bool isMatchEntireCell = uiMatchCellCheckBox.IsChecked ?? false;

            int rowCount = items.Count;
            int colCount = dataGrid.Columns.Count;

            int startRow = _currentRowIndex;
            int startCol = _currentMatchIndex;

            if (searchTerm != _lastSearchTerm)
            {
                startRow = forward ? -1 : rowCount;
                startCol = forward ? -1 : colCount;
                _lastSearchTerm = searchTerm;
                _totalMatches = CountTotalMatches(searchTerm);
            }

            bool foundMatch = false;

            for (int i = 0; i < rowCount; i++)
            {
                int rowIndex = forward
                    ? (startRow + 1 + i) % rowCount
                    : (startRow - 1 - i + rowCount) % rowCount;
                var row = items[rowIndex];

                // Iterate through columns
                for (int j = 0; j < colCount; j++)
                {
                    int colIndex = forward ? j : (colCount - 1 - j);
                    var cellContent = row.Values[colIndex];

                    if (cellContent is string cellText && !string.IsNullOrEmpty(cellText))
                    {
                        bool matchFound = IsTextMatch(cellText, searchTerm, isMatchCase, isMatchWholeWord, isMatchEntireCell);

                        if (matchFound)
                        {
                            _currentRowIndex = rowIndex;
                            _currentMatchIndex = colIndex;
                            foundMatch = true;

                            await Dispatcher.UIThread.InvokeAsync(
                                async () =>
                                {
                                    dataGrid.SelectedItem = row;
                                    dataGrid.Focus();
                                    await ScrollToSelectedRow(dataGrid, rowIndex, colIndex);
                                },
                                DispatcherPriority.Background
                            );

                            return;
                        }
                    }
                }
            }

            if (!foundMatch)
            {
                _currentMatchIndex = -1;
                _currentRowIndex = -1;
            }
        }

        private bool IsTextMatch(string cellText, string searchTerm, bool isMatchCase, bool isMatchWholeWord, bool isMatchEntireCell)
        {
            if (string.IsNullOrEmpty(cellText) || string.IsNullOrEmpty(searchTerm))
                return false;

            StringComparison comparison = isMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            RegexOptions regexOptions = isMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

            if (isMatchEntireCell)
            {
                // For entire cell matching, we normalize line breaks and compare
                string normalizedCellText = NormalizeLineBreaks(cellText);
                string normalizedSearchTerm = NormalizeLineBreaks(searchTerm);
                return string.Equals(normalizedCellText, normalizedSearchTerm, comparison);
            }
            else if (isMatchWholeWord)
            {
                // For whole word matching across line breaks, we use regex
                string escapedSearchTerm = Regex.Escape(NormalizeLineBreaks(searchTerm));
                string pattern = @"\b" + escapedSearchTerm + @"\b";
                string normalizedCellText = NormalizeLineBreaks(cellText);

                return Regex.IsMatch(normalizedCellText, pattern, regexOptions);
            }
            else
            {
                // For regular substring matching across line breaks
                string normalizedCellText = NormalizeLineBreaks(cellText);
                string normalizedSearchTerm = NormalizeLineBreaks(searchTerm);
                return normalizedCellText.IndexOf(normalizedSearchTerm, comparison) >= 0;
            }
        }

        private string NormalizeLineBreaks(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Replace various line break patterns with single spaces
            // This allows searching across line breaks as if they were spaces
            string normalized = Regex.Replace(text, @"\r\n|\r|\n", " ", RegexOptions.Multiline);
            normalized = Regex.Replace(normalized, @"\s+", " "); // Collapse multiple spaces into single space
            return normalized.Trim();
        }

        private string ReplaceTextAcrossLineBreaks(string cellText, string searchTerm, string replaceText, bool isMatchCase, bool isMatchWholeWord, bool isMatchEntireCell)
        {
            if (string.IsNullOrEmpty(cellText) || string.IsNullOrEmpty(searchTerm))
                return cellText;

            RegexOptions regexOptions = isMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

            if (isMatchEntireCell)
            {
                // For entire cell replacement, normalize and check if it matches, then replace entirely
                string normalizedCellText = NormalizeLineBreaks(cellText);
                string normalizedSearchTerm = NormalizeLineBreaks(searchTerm);
                StringComparison comparison = isMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                if (string.Equals(normalizedCellText, normalizedSearchTerm, comparison))
                {
                    return replaceText;
                }
                return cellText;
            }
            else if (isMatchWholeWord)
            {
                // For whole word replacement across line breaks
                string normalizedSearchTerm = NormalizeLineBreaks(searchTerm);
                string escapedSearchTerm = Regex.Escape(normalizedSearchTerm);
                string pattern = @"\b" + escapedSearchTerm + @"\b";

                // Create a mapping of normalized positions back to original positions
                return ReplaceWithLineBreakPreservation(cellText, searchTerm, replaceText, pattern, regexOptions);
            }
            else
            {
                // For regular substring replacement across line breaks
                string normalizedSearchTerm = NormalizeLineBreaks(searchTerm);
                string escapedSearchTerm = Regex.Escape(normalizedSearchTerm);

                return ReplaceWithLineBreakPreservation(cellText, searchTerm, replaceText, escapedSearchTerm, regexOptions);
            }
        }

        private string ReplaceWithLineBreakPreservation(string originalText, string searchTerm, string replaceText, string pattern, RegexOptions options)
        {
            // Create a normalized version for pattern matching
            string normalizedText = NormalizeLineBreaks(originalText);
            string normalizedSearchTerm = NormalizeLineBreaks(searchTerm);

            // Find matches in the normalized text
            var matches = Regex.Matches(normalizedText, pattern, options);

            if (matches.Count == 0)
                return originalText;

            // Work backwards through matches to preserve indices
            string result = originalText;
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];

                // Map the normalized position back to the original text position
                var originalMatch = FindCorrespondingTextInOriginal(originalText, normalizedSearchTerm, match.Index, match.Length);

                if (originalMatch.HasValue)
                {
                    result = result.Remove(originalMatch.Value.Start, originalMatch.Value.Length)
                                  .Insert(originalMatch.Value.Start, replaceText);
                }
            }

            return result;
        }
        private (int Start, int Length)? FindCorrespondingTextInOriginal(string originalText, string searchTerm, int normalizedStart, int normalizedLength)
        {
            string normalizedOriginal = NormalizeLineBreaks(originalText);

            // If the match is found in the normalized version, try to find it in the original
            string matchedText = normalizedOriginal.Substring(normalizedStart, normalizedLength);

            // Look for this pattern in the original text, accounting for line breaks
            string patternWithLineBreaks = Regex.Escape(searchTerm);
            patternWithLineBreaks = patternWithLineBreaks.Replace("\\ ", @"[\s\r\n]+"); // Allow any whitespace/line breaks

            var match = Regex.Match(originalText, patternWithLineBreaks, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return (match.Index, match.Length);
            }

            return null;
        }

        private int CountTotalMatches(string searchTerm)
        {
            int matchCount = 0;
            var dataGrid = MainWindow._dataGrid;
            var items = MainWindow._rows;

            if (
                items == null
                || dataGrid.Columns.Count == 0
                || string.IsNullOrWhiteSpace(searchTerm)
            )
                return 0;

            bool isMatchCase = uiMatchCaseCheckBox.IsChecked ?? false;
            bool isMatchWholeWord = uiMatchWholeWordCheckBox.IsChecked ?? false;
            bool isMatchEntireCell = uiMatchCellCheckBox.IsChecked ?? false;

            foreach (var row in items)
            {
                for (int colIndex = 0; colIndex < dataGrid.Columns.Count; colIndex++)
                {
                    var cellContent = row.Values[colIndex];
                    if (cellContent is string cellText && !string.IsNullOrEmpty(cellText))
                    {
                        bool matchFound = IsTextMatch(cellText, searchTerm, isMatchCase, isMatchWholeWord, isMatchEntireCell);

                        if (matchFound)
                        {
                            matchCount++;
                        }
                    }
                }
            }
            return matchCount;
        }

        private bool IsWholeWordMatch(string text, string searchTerm, StringComparison comparison)
        {
            int index = text.IndexOf(searchTerm, comparison);
            while (index != -1)
            {
                bool isStartBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool isEndBoundary =
                    (index + searchTerm.Length) == text.Length
                    || !char.IsLetterOrDigit(text[index + searchTerm.Length]);

                if (isStartBoundary && isEndBoundary)
                    return true;

                index = text.IndexOf(searchTerm, index + 1, comparison);
            }

            return false;
        }

        private bool IsEntireCellMatch(string cellValue, string searchText, bool matchCase)
        {
            StringComparison comparison = matchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return string.Equals(cellValue, searchText, comparison);
        }

        private async void ReplaceButton_Click(object? sender, RoutedEventArgs e)
        {
            if (MainWindow?._dataGrid?.SelectedItem != null)
            {
                var row = MainWindow._dataGrid.SelectedItem as DataRow;
                if (row != null)
                {
                    var searchText = uiSearchTextBox.Text;
                    var replaceText = uiReplaceTextBox.Text;
                    bool matchCase = uiMatchCaseCheckBox.IsChecked == true;
                    bool matchWholeWord = uiMatchWholeWordCheckBox.IsChecked == true;
                    bool matchCell = uiMatchCellCheckBox.IsChecked == true;
                    bool locresMode = uiLocresModeCheckBox.IsChecked == true;

                    if (locresMode)
                    {
                        ReplaceTextInLocresMode(
                            row,
                            searchText,
                            replaceText,
                            matchCase,
                            matchWholeWord,
                            matchCell
                        );
                    }
                    else
                    {
                        ReplaceTextInRow(
                            row,
                            searchText,
                            replaceText,
                            matchCase,
                            matchWholeWord,
                            matchCell
                        );
                    }

                    MainWindow._hasUnsavedChanges = true; // Mark as having unsaved changes
                    MainWindow._dataGrid.ItemsSource = new ObservableCollection<DataRow>(
                        MainWindow._dataGrid.ItemsSource.Cast<DataRow>().ToList()
                    );
                    FindText(searchText, true);
                }
            }
        }

        private async void ReplaceAllButton_Click(object? sender, RoutedEventArgs e)
        {
            if (MainWindow?._dataGrid?.ItemsSource == null)
                return;

            var searchText = uiSearchTextBox.Text;
            var replaceText = uiReplaceTextBox.Text;
            if (string.IsNullOrEmpty(searchText))
                return;

            var items = MainWindow._dataGrid.ItemsSource.Cast<DataRow>().ToList();
            bool matchCase = await Dispatcher.UIThread.InvokeAsync(
                () => uiMatchCaseCheckBox.IsChecked == true
            );
            bool matchWholeWord = await Dispatcher.UIThread.InvokeAsync(
                () => uiMatchWholeWordCheckBox.IsChecked == true
            );
            bool matchCell = await Dispatcher.UIThread.InvokeAsync(
                () => uiMatchCellCheckBox.IsChecked == true
            );
            bool locresMode = await Dispatcher.UIThread.InvokeAsync(
                () => uiLocresModeCheckBox.IsChecked == true
            );

            var tasks = items
                .Select(row =>
                    Task.Run(() =>
                    {
                        if (locresMode)
                        {
                            ReplaceTextInLocresMode(
                                row,
                                searchText,
                                replaceText,
                                matchCase,
                                matchWholeWord,
                                matchCell
                            );
                        }
                        else
                        {
                            ReplaceTextInRow(
                                row,
                                searchText,
                                replaceText,
                                matchCase,
                                matchWholeWord,
                                matchCell
                            );
                        }
                    })
                )
                .ToArray();

            await Task.WhenAll(tasks);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MainWindow._hasUnsavedChanges = true; // Mark as having unsaved changes
                MainWindow._dataGrid.ItemsSource = new ObservableCollection<DataRow>(items);
            });
        }

        private void ReplaceTextInRow(
            DataRow row,
            string searchText,
            string replaceText,
            bool matchCase,
            bool matchWholeWord,
            bool matchCell
        )
        {
            for (int i = 0; i < row.Values.Length; i++)
            {
                if (ShouldReplaceInCell(row.Values[i], searchText, matchCase, matchWholeWord, matchCell))
                {
                    row.Values[i] = ReplaceTextAcrossLineBreaks(
                        row.Values[i],
                        searchText,
                        replaceText,
                        matchCase,
                        matchWholeWord,
                        matchCell
                    );
                }
            }
            row.OnPropertyChanged(nameof(row.Values));
        }

        private void ReplaceTextInLocresMode(
            DataRow row,
            string searchText,
            string replaceText,
            bool matchCase,
            bool matchWholeWord,
            bool matchCell
        )
        {
            if (ShouldReplaceInCell(row.Values[1], searchText, matchCase, matchWholeWord, matchCell))
            {
                row.Values[2] = ReplaceTextAcrossLineBreaks(
                    row.Values[1],
                    searchText,
                    replaceText,
                    matchCase,
                    matchWholeWord,
                    matchCell
                );
            }
            row.OnPropertyChanged(nameof(row.Values));
        }

        private bool ShouldReplaceInCell(
            string cellValue,
            string searchText,
            bool matchCase,
            bool matchWholeWord,
            bool matchCell
        )
        {
            return IsTextMatch(cellValue, searchText, matchCase, matchWholeWord, matchCell);
        }

        private async Task ReplaceTextInRowAsync(
            DataRow row,
            string searchText,
            string replaceText,
            bool matchCase,
            bool matchWholeWord,
            bool matchCell
        )
        {
            for (int i = 0; i < row.Values.Length; i++)
            {
                if (ShouldReplaceInCell(row.Values[i], searchText, matchCase, matchWholeWord, matchCell))
                {
                    row.Values[i] = ReplaceTextAcrossLineBreaks(
                        row.Values[i],
                        searchText,
                        replaceText,
                        matchCase,
                        matchWholeWord,
                        matchCell
                    );
                }

                await Dispatcher.UIThread.InvokeAsync(
                    () => row.OnPropertyChanged(nameof(row.Values))
                );
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
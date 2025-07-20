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

            StringComparison comparison = isMatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

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
                        bool matchFound = false;

                        if (isMatchEntireCell)
                        {
                            matchFound = string.Equals(cellText, searchTerm, comparison);
                        }
                        else if (isMatchWholeWord)
                        {
                            matchFound = IsWholeWordMatch(cellText, searchTerm, comparison);
                        }
                        else
                        {
                            matchFound = cellText.IndexOf(searchTerm, comparison) >= 0;
                        }

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

            StringComparison comparison = isMatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            foreach (var row in items)
            {
                for (int colIndex = 0; colIndex < dataGrid.Columns.Count; colIndex++)
                {
                    var cellContent = row.Values[colIndex];
                    if (cellContent is string cellText && !string.IsNullOrEmpty(cellText))
                    {
                        bool matchFound = false;

                        if (isMatchEntireCell)
                        {
                            matchFound = string.Equals(cellText, searchTerm, comparison);
                        }
                        else if (isMatchWholeWord)
                        {
                            matchFound = IsWholeWordMatch(cellText, searchTerm, comparison);
                        }
                        else
                        {
                            matchFound = cellText.IndexOf(searchTerm, comparison) >= 0;
                        }

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
            if (string.IsNullOrEmpty(searchText) || string.IsNullOrEmpty(replaceText))
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
                if (
                    ShouldReplaceInCell(
                        row.Values[i],
                        searchText,
                        matchCase,
                        matchWholeWord,
                        matchCell
                    )
                )
                {
                    row.Values[i] = row.Values[i]
                        .Replace(
                            searchText,
                            replaceText,
                            matchCase
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase
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
            if (
                ShouldReplaceInCell(row.Values[1], searchText, matchCase, matchWholeWord, matchCell)
            )
            {
                row.Values[2] = row.Values[1]
                    .Replace(searchText, replaceText, StringComparison.OrdinalIgnoreCase);
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
            StringComparison comparison = matchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (matchCase)
            {
                if (!cellValue.Contains(searchText, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            else
            {
                if (!cellValue.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (matchWholeWord)
            {
                if (!IsWholeWordMatch(cellValue, searchText, comparison))
                {
                    return false;
                }
            }

            if (matchCell)
            {
                if (!IsEntireCellMatch(cellValue, searchText, matchCase))
                {
                    return false;
                }
            }

            return true;
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
                if (
                    ShouldReplaceInCell(
                        row.Values[i],
                        searchText,
                        matchCase,
                        matchWholeWord,
                        matchCell
                    )
                )
                {
                    row.Values[i] = row.Values[i]
                        .Replace(
                            searchText,
                            replaceText,
                            matchCase
                                ? StringComparison.Ordinal
                                : StringComparison.OrdinalIgnoreCase
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

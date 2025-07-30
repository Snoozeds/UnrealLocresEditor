using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

#nullable disable

namespace UnrealLocresEditor.Views
{
    public partial class FindDialog : Window
    {
        private string _lastSearchTerm = string.Empty;
        private int _currentMatchIndex = -1;
        private int _currentRowIndex = -1;
        private int _totalMatches = 0;

        public string SearchTerm
        {
            get => uiSearchTextBox.Text;
            set => uiSearchTextBox.Text = value;
        }

        public MainWindow MainWindow
        {
            get { return _mainWindow; }
            set { _mainWindow = value; }
        }

        private MainWindow _mainWindow;

        public FindDialog()
        {
            InitializeComponent();
            uiFindButton.Click += FindButton_Click;
            uiFindNextButton.Click += FindNextButton_Click;
            uiFindPreviousButton.Click += FindPreviousButton_Click;
            uiMatchWholeWordCheckBox.Click += MatchWholeWordCheckBox_Click;
            uiMatchCaseCheckBox.Click += MatchCaseCheckBox_Click;
            uiMatchCellCheckBox.Click += MatchCellCheckBox_Click;
            uiMatchCountTextBlock.Text = "";
        }

        private async void FindButton_Click(object sender, RoutedEventArgs e)
        {
            _currentMatchIndex = -1;
            _currentRowIndex = -1;
            _lastSearchTerm = string.Empty;
            uiMatchCountTextBlock.Text = "";

            await FindTextAsync(uiSearchTextBox.Text, forward: true);
        }

        private async void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            await FindTextAsync(uiSearchTextBox.Text, forward: true);
        }

        private async void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            await FindTextAsync(uiSearchTextBox.Text, forward: false);
        }

        private async void MatchCaseCheckBox_Click(object sender, RoutedEventArgs e)
        {
            await FindTextAsync(uiSearchTextBox.Text, forward: true);
        }

        private async void MatchWholeWordCheckBox_Click(object sender, RoutedEventArgs e)
        {
            await FindTextAsync(uiSearchTextBox.Text, forward: true);
        }

        private async void MatchCellCheckBox_Click(object sender, RoutedEventArgs e)
        {
            await FindTextAsync(uiSearchTextBox.Text, forward: true);
        }

        public async Task FindTextAsync(string searchTerm, bool forward = true)
        {
            bool isMatchCase = uiMatchCaseCheckBox.IsChecked ?? false;
            bool isMatchWholeWord = uiMatchWholeWordCheckBox.IsChecked ?? false;
            bool isMatchEntireCell = uiMatchCellCheckBox.IsChecked ?? false;
            bool searchNewKeysOnly = uiSearchNewKeysOnlyCheckBox.IsChecked ?? false;

            // Allow empty search term when searching new keys only
            if (string.IsNullOrWhiteSpace(searchTerm) && !searchNewKeysOnly)
            {
                uiMatchCountTextBlock.Text = "Please enter a search term.";
                return;
            }

            var dataGrid = MainWindow._dataGrid;
            var items = MainWindow._rows;

            if (items == null || dataGrid.Columns.Count == 0)
                return;

            // Early exit if searching new keys only but no new keys exist
            if (searchNewKeysOnly && !items.Any(row => row.IsNewKey))
            {
                _currentMatchIndex = -1;
                _currentRowIndex = -1;
                uiMatchCountTextBlock.Text = string.IsNullOrWhiteSpace(searchTerm)
                    ? "No new keys found."
                    : "No matches found.";
                return;
            }

            StringComparison comparison = isMatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int rowCount = items.Count;
            int colCount = dataGrid.Columns.Count;

            // Determine the starting point
            int startRow = _currentRowIndex;
            int startCol = _currentMatchIndex;

            // Reset search indices if the search term has changed
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

                // Bounds checking
                if (rowIndex < 0 || rowIndex >= items.Count)
                    continue;

                var row = items[rowIndex];

                // Skip this row if "search new keys only" is enabled and this row is not a new key
                if (searchNewKeysOnly && !row.IsNewKey)
                {
                    continue;
                }

                // If we're only searching new keys and have no search term, just select the first new key
                if (searchNewKeysOnly && string.IsNullOrWhiteSpace(searchTerm))
                {
                    _currentRowIndex = rowIndex;
                    _currentMatchIndex = 0; // Select first column
                    foundMatch = true;

                    await Dispatcher.UIThread.InvokeAsync(
                        async () =>
                        {
                            dataGrid.SelectedItem = row;
                            dataGrid.Focus();
                            await ScrollToSelectedRow(dataGrid, rowIndex, 0);
                            UpdateMatchCount();
                        },
                        DispatcherPriority.Background
                    );

                    return;
                }

                // Only search in cells if we have a search term
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    // Iterate through columns
                    for (int j = 0; j < colCount; j++)
                    {
                        int colIndex = forward ? j : (colCount - 1 - j);

                        // Bounds checking for column access
                        if (colIndex < 0 || colIndex >= row.Values.Length)
                            continue;

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
                                        UpdateMatchCount();
                                    },
                                    DispatcherPriority.Background
                                );

                                return;
                            }
                        }
                    }
                }
            }

            if (!foundMatch)
            {
                // Reset UI and match data if no matches found
                _currentMatchIndex = -1;
                _currentRowIndex = -1;
                uiMatchCountTextBlock.Text =
                    searchNewKeysOnly && string.IsNullOrWhiteSpace(searchTerm)
                        ? "No new keys found."
                        : "No matches found.";
            }
            else
            {
                uiMatchCountTextBlock.Text = $"Matches found: {_totalMatches}";
            }
        }

        private int CountTotalMatches(string searchTerm)
        {
            int matchCount = 0;
            var dataGrid = MainWindow._dataGrid;
            var items = MainWindow._rows;

            if (items == null || dataGrid.Columns.Count == 0)
                return 0;

            bool isMatchCase = uiMatchCaseCheckBox.IsChecked ?? false;
            bool isMatchWholeWord = uiMatchWholeWordCheckBox.IsChecked ?? false;
            bool isMatchEntireCell = uiMatchCellCheckBox.IsChecked ?? false;
            bool searchNewKeysOnly = uiSearchNewKeysOnlyCheckBox.IsChecked ?? false;

            // If searching new keys only without a search term, count all new keys
            if (searchNewKeysOnly && string.IsNullOrWhiteSpace(searchTerm))
            {
                return items.Count(row => row.IsNewKey);
            }

            // If we don't have a search term and not searching new keys only, return 0
            if (string.IsNullOrWhiteSpace(searchTerm))
                return 0;

            StringComparison comparison = isMatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            foreach (var row in items)
            {
                // Skip this row if "search new keys only" is enabled and this row is not a new key
                if (searchNewKeysOnly && !row.IsNewKey)
                {
                    continue;
                }

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

        private async Task ScrollToSelectedRow(DataGrid dataGrid, int rowIndex, int colIndex)
        {
            if (rowIndex < 0 || rowIndex >= MainWindow._rows.Count)
                return;

            var row = MainWindow._rows[rowIndex];
            var column = dataGrid.Columns[colIndex];

            dataGrid.ScrollIntoView(row, column);

            await Task.Delay(100);
        }

        private void UpdateMatchCount()
        {
            uiMatchCountTextBlock.Text = $"Matches found: {_totalMatches}";
        }
    }
}

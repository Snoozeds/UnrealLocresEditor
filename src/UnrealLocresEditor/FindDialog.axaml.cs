using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

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

        private void FindButton_Click(object sender, RoutedEventArgs e)
        {
            FindText(uiSearchTextBox.Text);
        }

        private async void ScrollToSelectedRow(DataGrid dataGrid, int selectedRowIndex)
        {
            var row = MainWindow._rows[selectedRowIndex];
            var column = dataGrid.Columns[_currentMatchIndex];
            var columnplus = dataGrid.Columns[_currentMatchIndex + 1];

            // Fix bug where ScrollIntoView does not scroll the cell into view if the cell has a different height to other cells.
            // This is fucking stupid and it is 1AM.

            // Jump a few rows ahead first
            int jumpRowIndex = Math.Min(selectedRowIndex + 5, MainWindow._rows.Count - 1);
            var jumpRow = MainWindow._rows[jumpRowIndex];

            dataGrid.ScrollIntoView(jumpRow, columnplus);
            dataGrid.ScrollIntoView(jumpRow, column);

            await Task.Delay(1);

            // Scroll back to the actual row
            dataGrid.ScrollIntoView(row, columnplus);
            dataGrid.ScrollIntoView(row, column);

        }

        public async void FindText(string searchTerm, bool forward = true)
        {
            if (string.IsNullOrEmpty(searchTerm)) return;

            var dataGrid = MainWindow._dataGrid;
            var items = MainWindow._rows;

            if (items == null || dataGrid.Columns.Count == 0) return;

            bool isMatchCaseChecked = uiMatchCaseCheckBox.IsChecked ?? false;
            bool isMatchWholeWordChecked = uiMatchWholeWordCheckBox.IsChecked ?? false;
            bool isMatchCellChecked = uiMatchCellCheckBox.IsChecked ?? false;

            int increment = forward ? 1 : -1;
            int startRowIndex = (_currentRowIndex + increment + items.Count) % items.Count;

            StringComparison comparison = isMatchCaseChecked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            bool foundMatch = false;

            for (int rowIndex = startRowIndex; ; rowIndex = (rowIndex + increment + items.Count) % items.Count)
            {
                var row = items[rowIndex];

                int startColIndex = (rowIndex == _currentRowIndex ? _currentMatchIndex + increment : (forward ? 0 : dataGrid.Columns.Count - 1));

                for (int colIndex = startColIndex; colIndex >= 0 && colIndex < dataGrid.Columns.Count; colIndex += increment)
                {
                    var column = dataGrid.Columns[colIndex];
                    var cellContent = row.Values[colIndex];

                    if (cellContent is string cellText)
                    {
                        bool matchFound = false;

                        if (isMatchCellChecked)
                        {
                            matchFound = string.Equals(cellText, searchTerm, comparison);
                        }
                        else if (isMatchWholeWordChecked)
                        {
                            matchFound = IsWholeWordMatch(cellText, searchTerm, comparison);
                        }
                        else
                        {
                            matchFound = cellText.IndexOf(searchTerm, comparison) >= 0;
                        }

                        if (matchFound)
                        {
                            _currentMatchIndex = colIndex;
                            _currentRowIndex = rowIndex;

                            if (searchTerm != _lastSearchTerm)
                            {
                                _totalMatches = 1;
                                _lastSearchTerm = searchTerm;
                            }
                            else
                            {
                                _totalMatches++;
                            }

                            foundMatch = true;

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                dataGrid.InvalidateMeasure();
                                dataGrid.InvalidateArrange();
                                dataGrid.UpdateLayout();
                                dataGrid.SelectedItem = row;
                                dataGrid.Focus();

                                ScrollToSelectedRow(dataGrid, _currentRowIndex);
                            }, DispatcherPriority.Background);

                            break;
                        }
                    }
                }

                // Break the loop if a match is found or if we have completed a full circle
                if (foundMatch || rowIndex == startRowIndex)
                    break;
            }

            if (!foundMatch)
            {
                _currentMatchIndex = -1;
                _currentRowIndex = -1;
                _totalMatches = 0;
                _lastSearchTerm = searchTerm;
            }
        }

        private bool IsWholeWordMatch(string text, string searchTerm, StringComparison comparison)
        {
            int index = text.IndexOf(searchTerm, comparison);
            while (index != -1)
            {
                bool isWholeWord = (index == 0 || !char.IsLetterOrDigit(text[index - 1])) &&
                                  (index + searchTerm.Length == text.Length || !char.IsLetterOrDigit(text[index + searchTerm.Length]));

                if (isWholeWord)
                    return true;

                index = text.IndexOf(searchTerm, index + 1, comparison);
            }

            return false;
        }

        private async void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Delay(1);
            _currentMatchIndex++;
            FindText(uiSearchTextBox.Text, forward: true);
        }

        private async void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Delay(1);
            _currentMatchIndex--;
            FindText(uiSearchTextBox.Text, forward: false);
        }

        private void MatchCaseCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool isMatchCaseChecked = (sender as CheckBox)?.IsChecked ?? false;
            FindText(uiSearchTextBox.Text);
        }

        private void MatchWholeWordCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool isMatchWholeWordChecked = (sender as CheckBox)?.IsChecked ?? false;
            FindText(uiSearchTextBox.Text);
        }

        // Match entire cell contents
        private void MatchCellCheckBox_Click(object sender, RoutedEventArgs e)
        {
            FindText(uiSearchTextBox.Text);
        }
        public FindDialog()
        {
            InitializeComponent();
            uiFindNextButton.Click += FindNextButton_Click;
            uiFindPreviousButton.Click += FindPreviousButton_Click;
            uiMatchWholeWordCheckBox.Click += MatchWholeWordCheckBox_Click;
            uiMatchCaseCheckBox.Click += MatchCaseCheckBox_Click;
            uiMatchCellCheckBox.Click += MatchCellCheckBox_Click;
            uiMatchCountTextBlock.Text = "";
        }

    }
}

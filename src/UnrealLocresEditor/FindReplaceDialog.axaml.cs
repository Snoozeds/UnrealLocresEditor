using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        private int currentFindIndex = -1;
        private int _currentRowIndex = -1;
        private int _currentMatchIndex = -1;
        private int _totalMatches = 0;
        private string _lastSearchTerm = "";

        private void FindButton_Click(object? sender, RoutedEventArgs e)
        {
            FindText(uiSearchTextBox.Text);
        }

        private async void ScrollToSelectedRow(DataGrid dataGrid, MainWindow.DataRow row)
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= dataGrid.Columns.Count)
            {
                if (dataGrid.Columns.Count > 0)
                {
                    var fixedcolumn = dataGrid.Columns[0];
                    dataGrid.ScrollIntoView(row, fixedcolumn);
                }
                return;
            }

            var column = dataGrid.Columns[_currentMatchIndex];
            var columnplus = dataGrid.Columns[_currentMatchIndex + 1];

            int jumpRowIndex = Math.Min(_currentRowIndex + 5, MainWindow._rows.Count - 1);
            var jumpRow = MainWindow._rows[jumpRowIndex];

            dataGrid.ScrollIntoView(jumpRow, columnplus);
            dataGrid.ScrollIntoView(jumpRow, column);

            await Task.Delay(1);

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
                                ScrollToSelectedRow(dataGrid, row);
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

        private bool IsWholeWordMatch(string cellValue, string searchText, StringComparison comparison)
        {
            string pattern = $@"\b{Regex.Escape(searchText)}\b";
            var options = comparison == StringComparison.Ordinal ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(cellValue, pattern, options);
        }

        private bool IsEntireCellMatch(string cellValue, string searchText, bool matchCase)
        {
            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(cellValue, searchText, comparison);
        }

        private void ReplaceButton_Click(object? sender, RoutedEventArgs e)
        {
            if (MainWindow?._dataGrid?.SelectedItem != null)
            {
                var row = MainWindow._dataGrid.SelectedItem as MainWindow.DataRow;
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
                        ReplaceTextInLocresMode(row, searchText, replaceText, matchCase, matchWholeWord, matchCell);
                    }
                    else
                    {
                        ReplaceTextInRow(row, searchText, replaceText, matchCase, matchWholeWord, matchCell);
                    }
                    MainWindow._dataGrid.ItemsSource = new ObservableCollection<MainWindow.DataRow>(MainWindow._dataGrid.ItemsSource.Cast<MainWindow.DataRow>().ToList());
                    FindText(searchText, true);
                }
            }
        }

        private async void ReplaceAllButton_Click(object? sender, RoutedEventArgs e)
        {
            if (MainWindow?._dataGrid?.ItemsSource == null) return;

            var searchText = uiSearchTextBox.Text;
            var replaceText = uiReplaceTextBox.Text;
            if (string.IsNullOrEmpty(searchText) || string.IsNullOrEmpty(replaceText)) return;

            var items = MainWindow._dataGrid.ItemsSource.Cast<MainWindow.DataRow>().ToList();


            bool matchCase = await Dispatcher.UIThread.InvokeAsync(() => uiMatchCaseCheckBox.IsChecked == true);
            bool matchWholeWord = await Dispatcher.UIThread.InvokeAsync(() => uiMatchWholeWordCheckBox.IsChecked == true);
            bool matchCell = await Dispatcher.UIThread.InvokeAsync(() => uiMatchCellCheckBox.IsChecked == true);
            bool locresMode = await Dispatcher.UIThread.InvokeAsync(() => uiLocresModeCheckBox.IsChecked == true);

            var tasks = items.Select(row => Task.Run(() =>
            {
                if (locresMode)
                {
                    ReplaceTextInLocresMode(row, searchText, replaceText, matchCase, matchWholeWord, matchCell);
                }
                else
                {
                    ReplaceTextInRow(row, searchText, replaceText, matchCase, matchWholeWord, matchCell);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MainWindow._dataGrid.ItemsSource = new ObservableCollection<MainWindow.DataRow>(items);
            });
        }


        private void ReplaceTextInRow(MainWindow.DataRow row, string searchText, string replaceText, bool matchCase, bool matchWholeWord, bool matchCell)
        {
            for (int i = 0; i < row.Values.Length; i++)
            {
                if (ShouldReplaceInCell(row.Values[i], searchText, matchCase, matchWholeWord, matchCell))
                {
                    row.Values[i] = row.Values[i].Replace(searchText, replaceText, StringComparison.OrdinalIgnoreCase);
                }
            }
            row.OnPropertyChanged(nameof(row.Values));
        }

        private void ReplaceTextInLocresMode(MainWindow.DataRow row, string searchText, string replaceText, bool matchCase, bool matchWholeWord, bool matchCell)
        {
            if (ShouldReplaceInCell(row.Values[1], searchText, matchCase, matchWholeWord, matchCell))
            {
                row.Values[2] = row.Values[1].Replace(searchText, replaceText, StringComparison.OrdinalIgnoreCase);
            }
            row.OnPropertyChanged(nameof(row.Values));
        }

        private bool ShouldReplaceInCell(string cellValue, string searchText, bool matchCase, bool matchWholeWord, bool matchCell)
        {
            StringComparison comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

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


        private async Task ReplaceTextInRowAsync(MainWindow.DataRow row, string searchText, string replaceText, bool matchCase, bool matchWholeWord, bool matchCell)
        {
            for (int i = 0; i < row.Values.Length; i++)
            {
                if (ShouldReplaceInCell(row.Values[i], searchText, matchCase, matchWholeWord, matchCell))
                {
                    row.Values[i] = row.Values[i].Replace(searchText, replaceText, StringComparison.OrdinalIgnoreCase);
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => row.OnPropertyChanged(nameof(row.Values)));
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

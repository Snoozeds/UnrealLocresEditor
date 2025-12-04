using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace UnrealLocresEditor.Models
{
    public class LocresDocument : INotifyPropertyChanged
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly ObservableCollection<DataRow> _rows = new();
        private readonly List<string> _columnHeaders = new();
        private bool _hasUnsavedChanges;
        private string _workingPath = string.Empty;
        private string? _activeCsvPath;
        private string? _displayNameOverride;

        public LocresDocument(string originalPath)
        {
            OriginalPath = originalPath;
            _workingPath = originalPath;
        }

        public Guid Id => _id;

        public string OriginalPath { get; }

        public string WorkingPath
        {
            get => _workingPath;
            set
            {
                if (_workingPath != value)
                {
                    _workingPath = value ?? string.Empty;
                    OnPropertyChanged(nameof(WorkingPath));
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(TabHeader));
                }
            }
        }

        public ObservableCollection<DataRow> Rows => _rows;

        public IList<string> ColumnHeaders => _columnHeaders;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    OnPropertyChanged(nameof(TabHeader));
                }
            }
        }

        public string? ActiveCsvPath
        {
            get => _activeCsvPath;
            set
            {
                if (_activeCsvPath != value)
                {
                    _activeCsvPath = value;
                    OnPropertyChanged(nameof(ActiveCsvPath));
                }
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_displayNameOverride))
                {
                    return _displayNameOverride!;
                }

                var referencePath = !string.IsNullOrWhiteSpace(OriginalPath)
                    ? OriginalPath
                    : WorkingPath;

                return string.IsNullOrWhiteSpace(referencePath)
                    ? "Untitled"
                    : Path.GetFileName(referencePath);
            }
        }

        public string TabHeader => HasUnsavedChanges ? $"{DisplayName} *" : DisplayName;

        public void SetDisplayName(string? displayName)
        {
            if (_displayNameOverride != displayName)
            {
                _displayNameOverride = displayName;
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(TabHeader));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
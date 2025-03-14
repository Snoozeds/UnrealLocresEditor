using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UnrealLocresEditor.ViewModels;

namespace UnrealLocresEditor.Views
{
    public partial class PreferencesWindow : Window
    {
        private readonly MainWindow? _mainWindow;

        public PreferencesWindow()
        {
            InitializeComponent();
            DataContext = new PreferencesWindowViewModel(this, null);
        }

        public PreferencesWindow(MainWindow mainWindow)
            : this()
        {
            _mainWindow = mainWindow;
            DataContext = new PreferencesWindowViewModel(this, _mainWindow);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

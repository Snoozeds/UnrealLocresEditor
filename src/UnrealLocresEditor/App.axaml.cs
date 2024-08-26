using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UnrealLocresEditor.ViewModels;
using UnrealLocresEditor.Views;

namespace UnrealLocresEditor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogException(e.ExceptionObject as Exception, "Unhandled Exception");
    }

    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "Unobserved Task Exception");
        e.SetObserved();
    }

    private void LogException(Exception ex, string exceptionType)
    {
        try
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDirectory = Path.Combine(appDataPath, "UnrealLocresEditor", "Logs");
            Directory.CreateDirectory(logDirectory);

            string logFilePath = Path.Combine(logDirectory, "crashlog.txt");
            string logMessage = $"{DateTime.Now}: {exceptionType} - {ex?.Message}\n{ex?.StackTrace}\n\n";

            File.AppendAllText(logFilePath, logMessage);
        }
        catch (Exception loggingEx)
        {
            return;
        }
    }
}

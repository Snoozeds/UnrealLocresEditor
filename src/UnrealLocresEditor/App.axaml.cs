using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UnrealLocresEditor.Views;
// Removed "using UnrealLocresEditor.ViewModels;" because we don't use it anymore!

namespace UnrealLocresEditor
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private bool _consoleAllocated = false;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // Check for command-line arguments
            if (Environment.GetCommandLineArgs().Contains("-console"))
            {
                if (AllocConsole())
                {
                    _consoleAllocated = true;
                    Console.WriteLine("Console initialized due to -console argument.");
                }
            }

            // Exception handlers
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // FIX: Removed "{ DataContext = new MainViewModel() }"
                // Now MainWindow keeps the DataContext it set for itself.
                desktop.MainWindow = new MainWindow();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                // FIX: Same here for mobile/browser support
                singleViewPlatform.MainView = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();

            if (_consoleAllocated)
            {
                AppDomain.CurrentDomain.ProcessExit += (sender, args) => FreeConsole();
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogException(e.ExceptionObject as Exception, "Unhandled Exception");
            Console.Error.WriteLine($"Unhandled Exception: {e.ExceptionObject}");
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException(e.Exception, "Unobserved Task Exception");
            Console.Error.WriteLine($"Unobserved Task Exception: {e.Exception}");
            e.SetObserved();
        }

        private void LogException(Exception ex, string exceptionType)
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData
                );
                string logDirectory = Path.Combine(appDataPath, "UnrealLocresEditor", "Logs");
                Directory.CreateDirectory(logDirectory);

                string logFilePath = Path.Combine(logDirectory, "crashlog.txt");
                string logMessage =
                    $"{DateTime.Now}: {exceptionType} - {ex?.Message}\n{ex?.StackTrace}\n\n";

                File.AppendAllText(logFilePath, logMessage);

                Console.Error.WriteLine(logMessage);
            }
            catch
            {
                return;
            }
        }
    }
}
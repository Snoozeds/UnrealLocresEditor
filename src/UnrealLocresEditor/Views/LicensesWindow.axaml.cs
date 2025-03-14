using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UnrealLocresEditor;

public partial class LicensesWindow : Window
{
    private readonly Dictionary<string, string> urlMappings = new()
    {
        { "Avalonia", "https://github.com/AvaloniaUI/Avalonia" },
        { "ClosedXML", "https://github.com/ClosedXML/ClosedXML" },
        { "CsvHelper", "https://github.com/JoshClose/CsvHelper" },
        { "DiscordRichPresence", "https://github.com/Lachee/discord-rpc-csharp" },
        { "SystemTextJson", "https://github.com/dotnet/runtime" },
    };

    public LicensesWindow()
    {
        InitializeComponent();
    }

    private void LaunchUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open URL: {url}. Error: {ex.Message}");
        }
    }

    private void OpenURL(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url)
        {
            LaunchUrl(url);
        }
    }

    private void HandleUrlClick(object sender, RoutedEventArgs e)
    {
        if (
            sender is Button button
            && button.Name is string name
            && urlMappings.TryGetValue(name, out var url)
        )
        {
            LaunchUrl(url);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}

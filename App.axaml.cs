using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Joktan.Telemetry;
using Joktan.ViewModels;
using Joktan.Views;

namespace Joktan;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var monitor = new MachineMonitor();
            var telemetry = new TelemetryHost(monitor);
            var main = new MainViewModel(monitor);
            desktop.Exit += (_, _) =>
            {
                main.SaveState();
                telemetry.Dispose();
                monitor.Dispose();
            };

            // Tool windows such as the topic explorer must not keep the app alive on their own.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow
            {
                DataContext = main,
            };
        }

        MacDockIcon.Apply();
        base.OnFrameworkInitializationCompleted();
    }

    private void OnAboutClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;

        new AboutWindow().ShowDialog(owner);
    }
}
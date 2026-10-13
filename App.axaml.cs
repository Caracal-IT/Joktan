using Avalonia;
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
            desktop.Exit += (_, _) =>
            {
                telemetry.Dispose();
                monitor.Dispose();
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(monitor),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Joktan.Certificates;
using Joktan.Mqtt;
using Joktan.Notes;
using Joktan.Passwords;
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
            // Tool windows such as the topic explorer must not keep the app alive on their own.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            Dispatcher.UIThread.Post(() => _ = StartAsync(desktop, splash), DispatcherPriority.Background);
        }

        MacDockIcon.Apply();
        base.OnFrameworkInitializationCompleted();
    }

    // How long the splash stays at least, so it does not just flash when start-up is quick.
    private static readonly TimeSpan MinimumSplashTime = TimeSpan.FromSeconds(1.5);

    /// <summary>Loads everything behind the splash, then swaps it for the main window.</summary>
    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        var shown = Stopwatch.StartNew();

        // Telemetry is set up and the settings files are read on background threads, all at once,
        // so the splash keeps drawing. Only the view models are made on the UI thread.
        splash.Status = "Loading telemetry, certificates, MQTT clients and notes…";
        var monitor = new MachineMonitor();
        var telemetryTask = Task.Run(() => new TelemetryHost(monitor));
        var certificatesTask = Task.Run(() => CertificateStore.Load());
        var mqttSettingsTask = Task.Run(MqttSettings.Load);
        var notesTask = Task.Run(NoteStore.Load);
        var passwordSettingsTask = Task.Run(PasswordSettings.Load);
        await Task.WhenAll(telemetryTask, certificatesTask, mqttSettingsTask, notesTask, passwordSettingsTask);
        var telemetry = telemetryTask.Result;

        splash.Status = "Opening…";
        await Rendered();
        var main = new MainViewModel(monitor, certificatesTask.Result, mqttSettingsTask.Result, notesTask.Result, passwordSettingsTask.Result);
        desktop.Exit += (_, _) =>
        {
            main.SaveState();
            telemetry.Dispose();
            monitor.Dispose();
        };

        var window = new MainWindow { DataContext = main };
        if (shown.Elapsed < MinimumSplashTime)
            await Task.Delay(MinimumSplashTime - shown.Elapsed);

        // The main window takes over first, so closing the splash does not end the app.
        desktop.MainWindow = window;
        window.Show();
        window.Activate();
        splash.Close();
    }

    // Lets the splash draw its new status before the work that follows holds up the UI thread.
    private static Task Rendered() => Task.Delay(50);

    private void OnAboutClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;

        new AboutWindow().ShowDialog(owner);
    }
}
using System.Reflection;
using Avalonia.Controls;

namespace Joktan.Views;

/// <summary>Shown while the app starts, until the main window is ready.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "" : $"Version {version.ToString(3)}";
    }

    /// <summary>What is being loaded, such as "Loading MQTT clients".</summary>
    public string Status
    {
        get => StatusText.Text ?? "";
        set => StatusText.Text = value;
    }
}

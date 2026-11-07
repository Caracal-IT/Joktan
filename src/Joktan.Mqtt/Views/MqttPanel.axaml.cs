using Avalonia.Controls;

namespace Joktan.Mqtt.Views;

/// <summary>
/// The MQTT clients themselves, with a tab per client. They are hosted by the page in the main window
/// or by their own window.
/// </summary>
public partial class MqttPanel : UserControl
{
    public MqttPanel()
    {
        InitializeComponent();
    }
}

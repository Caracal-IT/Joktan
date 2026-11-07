using Avalonia;
using Avalonia.Controls;
using Joktan.Users.ViewModels;

namespace Joktan.Users.Views;

public partial class UserSettingsView : UserControl
{
    public UserSettingsView()
    {
        InitializeComponent();
    }

    // Databases may have been added or removed on Data / Database since the page was last shown.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as UserSettingsViewModel)?.Refresh();
    }
}

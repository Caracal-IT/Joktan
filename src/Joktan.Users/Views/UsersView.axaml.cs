using Avalonia;
using Avalonia.Controls;
using Joktan.Users.ViewModels;

namespace Joktan.Users.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
    }

    // The users are read again each time the page is shown, as they may have been changed by another copy of Joktan.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is UsersViewModel vm)
            _ = vm.ActivateAsync();
    }
}

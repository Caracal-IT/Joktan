using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Joktan.Core;

namespace Joktan.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeIcon.Data = maximized ? Icons.WindowRestore : Icons.WindowMaximize;
            ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximise");
        }
    }

    // The top bar stands in for the title bar, except over its buttons and search box.
    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null
            || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
            return;

        if (e.ClickCount == 2)
            ToggleMaximized();
        else
            BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}

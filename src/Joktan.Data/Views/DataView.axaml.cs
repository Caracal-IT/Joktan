using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Joktan.Data.ViewModels;

namespace Joktan.Data.Views;

public partial class DataView : UserControl
{
    private DataViewModel? _vm;

    public DataView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as DataViewModel;
        if (_vm is not null && this.IsAttachedToVisualTree())
            _vm.Databases.PickFolder = PickFolderAsync;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_vm is not null)
            _vm.Databases.PickFolder = PickFolderAsync;
        _vm?.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm?.Deactivate();
    }

    // The column whose heading's edge is being dragged, where the drag started and how wide it was.
    private ColumnSize? _resizing;
    private double _resizeFrom, _resizeWidth;

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BrowserCell cell } grip || _vm is null || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _vm.Browser.FitColumn(cell.Size);
            return;
        }

        _resizing = cell.Size;
        _resizeFrom = e.GetPosition(this).X;
        _resizeWidth = cell.Size.Width;
        e.Pointer.Capture(grip);
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_resizing is not null && _vm is not null)
            _vm.Browser.SetColumnWidth(_resizing, _resizeWidth + e.GetPosition(this).X - _resizeFrom);
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizing is null)
            return;
        _resizing = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnGripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _resizing = null;

    private async Task<string?> PickFolderAsync(string title)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}

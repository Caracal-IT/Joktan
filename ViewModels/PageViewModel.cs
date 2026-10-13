namespace Joktan.ViewModels;

/// <summary>
/// A page shown in the shell's content area. Used directly, it is the placeholder for menu
/// entries that have no page of their own yet.
/// </summary>
public class PageViewModel : ViewModelBase
{
    public PageViewModel(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }

    public string Subtitle { get; }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.ViewModels;

/// <summary>A type of page item on the builder's palette.</summary>
public sealed record PageItemTypeChoice(PageItemType Type, string Text, bool IsInput);

/// <summary>
/// Workflows / Pages: builds the pages that user input steps show on the website, from headings, text,
/// values from the run and the fields the person fills in. Every change is saved at once.
/// </summary>
public sealed partial class PageBuilderViewModel : PageViewModel
{
    private readonly PageLibrary _library;
    private bool _loading;

    public PageBuilderViewModel(PageLibrary library) : base(Strings.Pages_Title, Strings.Pages_Subtitle)
    {
        _library = library;
        library.Changed += ListChanged;
        Pages = new ObservableCollection<WorkflowPage>(library.All);
        SelectedPage = Pages.FirstOrDefault();
        ShowReadError();
    }

    public ObservableCollection<WorkflowPage> Pages { get; }

    public bool HasNoPages => Pages.Count == 0;

    /// <summary>What can be put on a page: what it shows, then what is filled in.</summary>
    public IReadOnlyList<PageItemTypeChoice> ShownTypes { get; } =
        Enum.GetValues<PageItemType>().Where(t => !t.IsInput()).Select(t => new PageItemTypeChoice(t, t.Text(), false)).ToList();

    public IReadOnlyList<PageItemTypeChoice> InputTypes { get; } =
        Enum.GetValues<PageItemType>().Where(t => t.IsInput()).Select(t => new PageItemTypeChoice(t, t.Text(), true)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage), nameof(PageName), nameof(PageTitle), nameof(PageDescription), nameof(SubmitText), nameof(Problem), nameof(PreviewUrlText))]
    public partial WorkflowPage? SelectedPage { get; set; }

    public bool HasPage => SelectedPage is not null;

    public ObservableCollection<PageItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RemoveItemCommand))]
    public partial PageItemViewModel? SelectedItem { get; set; }

    public bool HasItem => SelectedItem is not null;

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>The website's address while it is on, such as http://localhost:47822; null while it is off. Set by the app.</summary>
    public Func<string?> WebsiteUrl { get; set; } = () => null;

    /// <summary>Opens a web address in the browser. Set by the view.</summary>
    public Func<string, Task>? OpenUrl { get; set; }

    partial void OnSelectedPageChanged(WorkflowPage? value)
    {
        IsConfirmingDelete = false;
        _loading = true;
        try
        {
            Items.Clear();
            foreach (var item in value?.Items ?? [])
                Items.Add(new PageItemViewModel(item, Changed));
            SelectedItem = Items.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }
    }

    public string PageName
    {
        get => SelectedPage?.Name ?? "";
        set => Edit(p =>
        {
            // A page needs a name to be found by; an empty one is not kept.
            if (value.Trim().Length > 0)
                p.Name = value.Trim();
        });
    }

    public string PageTitle
    {
        get => SelectedPage?.Title ?? "";
        set => Edit(p => p.Title = value);
    }

    public string PageDescription
    {
        get => SelectedPage?.Description ?? "";
        set => Edit(p => p.Description = value);
    }

    public string SubmitText
    {
        get => SelectedPage?.SubmitText ?? "";
        set => Edit(p => p.SubmitText = value);
    }

    /// <summary>What stops the page from being filled in as it is, or null.</summary>
    public string? Problem => SelectedPage is { } page ? ProblemOf(page) : null;

    /// <summary>What is wrong with a page: fields without a name or with the same name, or drop-downs with nothing to choose.</summary>
    public static string? ProblemOf(WorkflowPage page)
    {
        var inputs = page.Items.Where(i => i.IsInput).ToList();
        if (inputs.FirstOrDefault(i => i.Name.Trim().Length == 0) is { } unnamed)
            return string.Format(Strings.Pages_NameTheField, unnamed.Type.Text());
        if (inputs.FirstOrDefault(i => !WorkflowCheck.IsName(i.Name.Trim())) is { } badName)
            return string.Format(Strings.Pages_FieldNameNotValid, badName.Name.Trim());
        if (inputs.GroupBy(i => i.Name.Trim()).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return string.Format(Strings.Pages_FieldTwice, twice.Key);
        if (inputs.FirstOrDefault(i => i.Type == PageItemType.DropDown && i.Options.All(o => o.Trim().Length == 0)) is { } empty)
            return string.Format(Strings.Pages_NoOptions, empty.Caption);
        return null;
    }

    /// <summary>Where the page can be seen on the website, or what to do for that.</summary>
    public string PreviewUrlText => WebsiteUrl() is { } url && SelectedPage is { } page ? $"{url}/preview/{page.Id}" : Strings.Pages_WebsiteOff;

    [RelayCommand]
    private async Task PreviewAsync()
    {
        OnPropertyChanged(nameof(PreviewUrlText));
        if (WebsiteUrl() is { } url && SelectedPage is { } page && OpenUrl is not null)
            await OpenUrl($"{url}/preview/{page.Id}");
    }

    [RelayCommand]
    private void NewPage()
    {
        var page = new WorkflowPage
        {
            Id = WorkflowLibrary.NewId(),
            Name = _library.FreeName(Strings.Pages_NewPageName),
            Items =
            [
                new PageItem { Type = PageItemType.Text, Text = "{{input}}" },
                new PageItem { Type = PageItemType.CheckBox, Name = "approved", Label = Strings.Pages_SampleCheckBox },
                new PageItem { Type = PageItemType.TextArea, Name = "comment", Label = Strings.Pages_SampleComment },
            ],
        };
        Save(page);
        SelectedPage = Pages.FirstOrDefault(p => p.Id == page.Id);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedPage is not { } page)
            return;
        var copy = page.Clone();
        copy.Id = WorkflowLibrary.NewId();
        copy.Name = _library.FreeName(string.Format(Strings.Pages_CopyName, page.Name));
        foreach (var item in copy.Items)
            item.Id = WorkflowLibrary.NewId();
        Save(copy);
        SelectedPage = Pages.FirstOrDefault(p => p.Id == copy.Id);
    }

    [RelayCommand]
    private void AskDelete() => IsConfirmingDelete = SelectedPage is not null;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        IsConfirmingDelete = false;
        if (SelectedPage is not { } page)
            return;
        try
        {
            _library.Delete(page.Id);
            Error = null;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Pages_CouldNotDelete, e.Message);
        }

        SelectedPage = Pages.FirstOrDefault();
    }

    [RelayCommand]
    private void AddItem(PageItemTypeChoice? choice)
    {
        if (choice is null || SelectedPage is not { } page)
            return;
        var item = new PageItem { Type = choice.Type };
        if (choice.IsInput)
            item.Name = FreeFieldName(page, choice.Type == PageItemType.CheckBox ? "approved" : "field");
        if (choice.Type == PageItemType.DropDown)
            item.Options = [Strings.Pages_SampleOption1, Strings.Pages_SampleOption2];

        // Under the item selected, or at the end.
        var at = SelectedItem is { } selected ? Items.IndexOf(selected) + 1 : Items.Count;
        page.Items.Insert(at, item);
        var row = new PageItemViewModel(item, Changed);
        Items.Insert(at, row);
        SelectedItem = row;
        Changed();
    }

    private static string FreeFieldName(WorkflowPage page, string name)
    {
        var candidate = name;
        for (var n = 2; page.Items.Any(i => i.Name.Trim() == candidate); n++)
            candidate = $"{name}{n}";
        return candidate;
    }

    private bool CanMoveUp => SelectedItem is { } item && Items.IndexOf(item) > 0;

    private bool CanMoveDown => SelectedItem is { } item && Items.IndexOf(item) < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private void Move(int by)
    {
        if (SelectedItem is not { } row || SelectedPage is not { } page)
            return;
        var from = Items.IndexOf(row);
        var to = from + by;
        if (to < 0 || to >= Items.Count)
            return;
        page.Items.RemoveAt(from);
        page.Items.Insert(to, row.Item);
        Items.Move(from, to);
        SelectedItem = row;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        Changed();
    }

    [RelayCommand(CanExecute = nameof(HasItem))]
    private void RemoveItem()
    {
        if (SelectedItem is not { } row || SelectedPage is not { } page)
            return;
        var at = Items.IndexOf(row);
        page.Items.Remove(row.Item);
        Items.Remove(row);
        SelectedItem = Items.Count == 0 ? null : Items[Math.Min(at, Items.Count - 1)];
        Changed();
    }

    private void Edit(Action<WorkflowPage> change, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (SelectedPage is not { } page)
            return;
        change(page);
        OnPropertyChanged(name);
        Changed();
    }

    // Each change is written to the page's file at once.
    private void Changed()
    {
        if (_loading || SelectedPage is not { } page)
            return;
        Save(page);
        OnPropertyChanged(nameof(Problem));
    }

    private void Save(WorkflowPage page)
    {
        try
        {
            _library.Save(page);
            Error = null;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.Pages_CouldNotSave, e.Message);
        }
    }

    // The list follows the library, keeping the page selected; the same objects stay, so nothing is lost while editing.
    private void ListChanged()
    {
        var all = _library.All;
        for (var i = Pages.Count - 1; i >= 0; i--)
        {
            if (all.All(p => p.Id != Pages[i].Id))
                Pages.RemoveAt(i);
        }

        for (var i = 0; i < all.Count; i++)
        {
            var at = Pages.IndexOf(all[i]);
            if (at < 0)
                Pages.Insert(i, all[i]);
            else if (at != i)
                Pages.Move(at, i);
        }

        OnPropertyChanged(nameof(HasNoPages));
        ShowReadError();
    }

    // Why the folder could not be read, which would otherwise look like there being no pages.
    private void ShowReadError()
    {
        if (_library.Error is { } error)
            Error = string.Format(Strings.Pages_CouldNotRead, error);
    }
}

/// <summary>One item of the page being built, as the builder shows it and changes it.</summary>
public sealed class PageItemViewModel(PageItem item, Action changed) : ViewModelBase
{
    public PageItem Item { get; } = item;

    public PageItemType Type => Item.Type;

    public string TypeText => Item.Type.Text();

    public bool IsHeading => Type == PageItemType.Heading;
    public bool IsText => Type == PageItemType.Text;
    public bool IsValue => Type == PageItemType.Value;
    public bool IsDivider => Type == PageItemType.Divider;
    public bool IsInput => Item.IsInput;
    public bool IsTextBox => Type is PageItemType.TextBox or PageItemType.Number or PageItemType.Date;
    public bool IsTextArea => Type == PageItemType.TextArea;
    public bool IsCheckBox => Type == PageItemType.CheckBox;
    public bool IsDropDown => Type == PageItemType.DropDown;

    /// <summary>Whether the item has text of its own: a heading's, a paragraph's, or the value a value shows.</summary>
    public bool HasText => IsHeading || IsText || IsValue;

    public bool HasLabel => IsInput || IsValue;

    public bool HasPlaceholder => Type is PageItemType.TextBox or PageItemType.TextArea or PageItemType.Number;

    public string TextLabel => IsValue ? Strings.Pages_ValueText : Strings.Pages_Text;

    public string Name
    {
        get => Item.Name;
        set => Set(() => Item.Name = value.Trim());
    }

    public string Label
    {
        get => Item.Label;
        set => Set(() => Item.Label = value);
    }

    public string Text
    {
        get => Item.Text;
        set => Set(() => Item.Text = value);
    }

    public string Default
    {
        get => Item.Default;
        set => Set(() => Item.Default = value);
    }

    public string Placeholder
    {
        get => Item.Placeholder;
        set => Set(() => Item.Placeholder = value);
    }

    public string Help
    {
        get => Item.Help;
        set => Set(() => Item.Help = value);
    }

    public bool Required
    {
        get => Item.Required;
        set => Set(() => Item.Required = value);
    }

    /// <summary>A drop-down's options, one to a line.</summary>
    public string OptionsText
    {
        // As typed, so that a line just started is not taken away again.
        get => _optionsText ??= string.Join(Environment.NewLine, Item.Options);
        set => Set(() =>
        {
            _optionsText = value;
            Item.Options = value.Split('\n').Select(o => o.TrimEnd('\r')).Where(o => o.Trim().Length > 0).ToList();
        });
    }

    private string? _optionsText;

    public IReadOnlyList<string> Options => Item.Options;

    /// <summary>The label as the page shows it, with a star when it must be filled in.</summary>
    public string Caption => (Item.Caption.Length > 0 ? Item.Caption : Strings.Pages_Unnamed) + (Item.Required ? " *" : "");

    /// <summary>What a heading, paragraph or value shows, or a word for it while there is nothing yet.</summary>
    public string Summary => Item.Text.Trim().Length > 0 ? Item.Text : TypeText;

    /// <summary>What a field shows in the builder: its starting value, or its first option.</summary>
    public string Sample => Item.Default.Length > 0 ? Item.Default : IsDropDown ? Item.Options.FirstOrDefault() ?? "" : "";

    public bool IsTicked => Item.Default.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    public bool HasHelp => Item.Help.Trim().Length > 0;

    // Anything the item shows may follow from what changed, so all of it is shown again.
    private void Set(Action apply)
    {
        apply();
        OnPropertyChanged(string.Empty);
        changed();
    }
}

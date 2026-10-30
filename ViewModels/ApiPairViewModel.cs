using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Api;

namespace Joktan.ViewModels;

/// <summary>One header, query parameter, form field, metadata entry or variable in an editable list.</summary>
public partial class ApiPairViewModel : ViewModelBase
{
    private readonly ApiPairList _list;

    public ApiPairViewModel(ApiPairList list, ApiPair pair)
    {
        _list = list;
        Enabled = pair.Enabled;
        Name = pair.Name;
        Value = pair.Value;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; }

    partial void OnEnabledChanged(bool value) => _list.OnChanged();

    partial void OnNameChanged(string value) => _list.OnChanged();

    partial void OnValueChanged(string value) => _list.OnChanged();

    [RelayCommand]
    private void Remove() => _list.Remove(this);

    public ApiPair ToPair() => new() { Enabled = Enabled, Name = Name, Value = Value };
}

/// <summary>An editable list of names and values that reports every change.</summary>
public sealed class ApiPairList : ObservableCollection<ApiPairViewModel>
{
    private readonly Action _changed;

    public ApiPairList(IEnumerable<ApiPair> pairs, Action changed)
    {
        foreach (var pair in pairs)
            Add(new ApiPairViewModel(this, pair));
        _changed = changed;
        AddCommand = new RelayCommand(() => Add(new ApiPairViewModel(this, new ApiPair())));
        CollectionChanged += (_, _) => _changed();
    }

    public RelayCommand AddCommand { get; }

    public void OnChanged() => _changed?.Invoke();

    public List<ApiPair> ToPairs() => this.Select(p => p.ToPair()).ToList();
}

/// <summary>A file or folder path in an editable list, such as the .proto files of a request.</summary>
public sealed partial class ApiPathViewModel(ObservableCollection<ApiPathViewModel> list, string path) : ViewModelBase
{
    public string Path { get; } = path;

    [RelayCommand]
    private void Remove() => list.Remove(this);
}

/// <summary>A folder or request in the API client's tree.</summary>
public partial class ApiNodeViewModel : ViewModelBase
{
    public ApiNodeViewModel(string path, bool isFolder, ApiNodeViewModel? parent)
    {
        Path = path;
        IsFolder = isFolder;
        Parent = parent;
    }

    public string Path { get; }

    public bool IsFolder { get; }

    public bool IsRequest => !IsFolder;

    public ApiNodeViewModel? Parent { get; }

    public ObservableCollection<ApiNodeViewModel> Children { get; } = [];

    public string Name => IsFolder ? System.IO.Path.GetFileName(Path) : ApiWorkspace.NameOf(Path);

    /// <summary>The method, or gRPC, shown before a request's name.</summary>
    [ObservableProperty]
    public partial string Badge { get; set; } = "";

    /// <summary>The whole method, or gRPC, which sets the badge's colour.</summary>
    [ObservableProperty]
    public partial string Method { get; set; } = "";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>The folders above it, outermost first, without the workspace itself.</summary>
    public IEnumerable<ApiNodeViewModel> Ancestors()
    {
        var path = new List<ApiNodeViewModel>();
        for (var node = Parent; node?.Parent is not null; node = node.Parent)
            path.Insert(0, node);
        return path;
    }

    public IEnumerable<ApiNodeViewModel> SelfAndDescendants()
        => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    public void Describe(ApiRequest request)
    {
        Method = request.Kind == ApiKind.Grpc ? "gRPC" : request.Method.Trim().ToUpperInvariant();
        Badge = Method switch
        {
            "DELETE" => "DEL",
            "OPTIONS" => "OPT",
            var method => method,
        };
    }
}

/// <summary>A folder offered as the place for a request or folder, indented by depth.</summary>
public sealed record ApiFolderChoice(ApiNodeViewModel Node, string Label)
{
    public override string ToString() => Label;
}

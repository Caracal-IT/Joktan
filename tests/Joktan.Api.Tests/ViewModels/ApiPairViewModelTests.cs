using System.IO;
using Joktan.Api.ViewModels;
using Xunit;

namespace Joktan.Api.Tests.ViewModels;

public class ApiPairListTests
{
    private int _changes;

    private ApiPairList List(params ApiPair[] pairs) => new(pairs, () => _changes++);

    [Fact]
    public void Starts_with_the_pairs_without_reporting_a_change()
    {
        var list = List(new ApiPair { Name = "a", Value = "b", Enabled = false });

        var pair = Assert.Single(list);
        Assert.Equal(("a", "b", false), (pair.Name, pair.Value, pair.Enabled));
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Reports_every_change()
    {
        var list = List(new ApiPair { Name = "a" });
        var pair = list[0];

        pair.Name = "b";
        pair.Value = "c";
        pair.Enabled = false;
        Assert.Equal(3, _changes);

        var beforeAdding = _changes;
        list.AddCommand.Execute(null);
        Assert.True(_changes > beforeAdding);
        Assert.Equal(2, list.Count);

        var beforeRemoving = _changes;
        pair.RemoveCommand.Execute(null);
        Assert.True(_changes > beforeRemoving);
        Assert.Single(list);
    }

    [Fact]
    public void Gives_the_pairs_back()
    {
        var list = List(new ApiPair { Name = "a", Value = "b", Enabled = false });
        list[0].Value = "c";

        var pair = Assert.Single(list.ToPairs());

        Assert.Equal(("a", "c", false), (pair.Name, pair.Value, pair.Enabled));
    }
}

public class ApiNodeViewModelTests
{
    [Fact]
    public void Names_a_folder_and_a_request()
    {
        var folder = new ApiNodeViewModel(Path.Combine("w", "folder"), true, null);
        var request = new ApiNodeViewModel(Path.Combine("w", "get" + ApiWorkspace.RequestExtension), false, folder);

        Assert.Equal("folder", folder.Name);
        Assert.Equal("get", request.Name);
        Assert.True(folder.IsFolder);
        Assert.True(request.IsRequest);
    }

    [Theory]
    [InlineData("get", "GET", "GET")]
    [InlineData(" post ", "POST", "POST")]
    [InlineData("DELETE", "DELETE", "DEL")]
    [InlineData("options", "OPTIONS", "OPT")]
    public void Shows_the_method_briefly(string method, string expectedMethod, string expectedBadge)
    {
        var node = new ApiNodeViewModel("r", false, null);

        node.Describe(new ApiRequest { Method = method });

        Assert.Equal((expectedMethod, expectedBadge), (node.Method, node.Badge));
    }

    [Fact]
    public void A_gRPC_request_says_so_whatever_its_method()
    {
        var node = new ApiNodeViewModel("r", false, null);

        node.Describe(new ApiRequest { Kind = ApiKind.Grpc, Method = "POST" });

        Assert.Equal(("gRPC", "gRPC"), (node.Method, node.Badge));
    }

    [Fact]
    public void Follows_the_tree_leaving_out_the_workspace_itself()
    {
        var workspace = new ApiNodeViewModel("w", true, null);
        var outer = new ApiNodeViewModel("w/o", true, workspace);
        var inner = new ApiNodeViewModel("w/o/i", true, outer);
        var request = new ApiNodeViewModel("w/o/i/r", false, inner);
        workspace.Children.Add(outer);
        outer.Children.Add(inner);
        inner.Children.Add(request);

        Assert.Equal([outer, inner], request.Ancestors());
        Assert.Empty(outer.Ancestors());
        Assert.Equal([outer, inner, request], outer.SelfAndDescendants());
    }

    [Fact]
    public void A_folder_choice_shows_its_label()
    {
        Assert.Equal("  label", new ApiFolderChoice(new ApiNodeViewModel("w", true, null), "  label").ToString());
    }

    [Fact]
    public void A_path_in_a_list_can_remove_itself()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<ApiPathViewModel>();
        var item = new ApiPathViewModel(list, "a.proto");
        list.Add(item);

        item.RemoveCommand.Execute(null);

        Assert.Empty(list);
        Assert.Equal("a.proto", item.Path);
    }
}

using System;
using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests;

public class ApiWorkspaceFileTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly ApiWorkspace _workspace;

    public ApiWorkspaceFileTests()
    {
        _workspace = new ApiWorkspace(_folder.Path);
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_request_is_written_and_read_back()
    {
        var path = _folder.At("nested", "folder", "get" + ApiWorkspace.RequestExtension);
        var request = new ApiRequest
        {
            Kind = ApiKind.Grpc,
            Method = "POST",
            Url = "http://x",
            Headers = [new ApiPair { Name = "a", Value = "b", Enabled = false }],
            BodyKind = ApiBodyKind.Json,
            AuthKind = ApiAuthKind.Bearer,
            Schema = GrpcSchemaSource.ProtoFiles,
            ProtoFiles = ["a.proto"],
            TimeoutSeconds = 5,
        };

        ApiWorkspace.Write(path, request);
        var read = ApiWorkspace.Read(path);

        Assert.Equal(ApiKind.Grpc, read.Kind);
        Assert.Equal("POST", read.Method);
        Assert.False(read.Headers[0].Enabled);
        Assert.Equal(ApiBodyKind.Json, read.BodyKind);
        Assert.Equal(ApiAuthKind.Bearer, read.AuthKind);
        Assert.Equal(GrpcSchemaSource.ProtoFiles, read.Schema);
        Assert.Equal(["a.proto"], read.ProtoFiles);
        Assert.Equal(5, read.TimeoutSeconds);
    }

    [Fact]
    public void Kinds_are_written_as_words_so_files_read_well()
    {
        var path = _folder.At("r" + ApiWorkspace.RequestExtension);

        ApiWorkspace.Write(path, new ApiRequest { Kind = ApiKind.Grpc });

        Assert.Contains("\"Grpc\"", File.ReadAllText(path));
    }

    [Fact]
    public void A_request_that_cannot_be_read_is_a_new_one()
    {
        var bad = _folder.Write("bad.request.json", "not json");

        Assert.Equal("GET", ApiWorkspace.Read(bad).Method);
        Assert.Equal("GET", ApiWorkspace.Read(_folder.At("missing.request.json")).Method);
    }

    [Fact]
    public void Lists_folders_without_hidden_ones_by_name()
    {
        Directory.CreateDirectory(_folder.At("b"));
        Directory.CreateDirectory(_folder.At("A"));
        Directory.CreateDirectory(_folder.At(".git"));

        Assert.Equal(["A", "b"], _workspace.Folders(_folder.Path).Select(Path.GetFileName));
        Assert.Empty(_workspace.Folders(_folder.At("missing")));
    }

    [Fact]
    public void Lists_requests_by_name_leaving_other_files()
    {
        _folder.Write("b.request.json", "{}");
        _folder.Write("A.request.json", "{}");
        _folder.Write("other.json", "{}");

        Assert.Equal(["A", "b"], _workspace.Requests(_folder.Path).Select(ApiWorkspace.NameOf));
        Assert.Empty(_workspace.Requests(_folder.At("missing")));
    }

    [Fact]
    public void Knows_a_folder_with_content()
    {
        Assert.False(ApiWorkspace.HasContent(_folder.At("missing")));
        Assert.False(ApiWorkspace.HasContent(_folder.Path));

        _folder.Write("deep/down/r.request.json", "{}");

        Assert.True(ApiWorkspace.HasContent(_folder.Path));
    }

    [Fact]
    public void Environments_are_written_and_read_back()
    {
        var environments = new ApiEnvironments
        {
            Selected = "dev",
            Environments = [new ApiEnvironment { Name = "dev", Variables = [new ApiPair { Name = "a", Value = "b" }] }],
        };

        _workspace.WriteEnvironments(environments);
        var read = _workspace.ReadEnvironments();

        Assert.Equal("dev", read.Selected);
        Assert.Equal("b", read.Environments[0].Variables[0].Value);
    }

    [Fact]
    public void Without_a_readable_file_there_are_no_environments()
    {
        Assert.Empty(_workspace.ReadEnvironments().Environments);

        _folder.Write(ApiWorkspace.EnvironmentsFile, "not json");

        Assert.Empty(_workspace.ReadEnvironments().Environments);
    }

    [Theory]
    [InlineData("name", "name")]
    [InlineData("  name  ", "name")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a-b-c-d-e-f-g-h-i-j")]
    [InlineData("a\tb", "a-b")]
    [InlineData("..name..", "name")]
    [InlineData("", "Untitled")]
    [InlineData("...", "Untitled")]
    [InlineData("  ", "Untitled")]
    public void Makes_a_name_safe_for_a_file(string name, string expected)
        => Assert.Equal(expected, ApiWorkspace.SafeName(name));

    [Fact]
    public void Numbers_a_new_path_when_the_name_is_taken()
    {
        const string extension = ApiWorkspace.RequestExtension;
        Assert.Equal(_folder.At("a" + extension), ApiWorkspace.FreePath(_folder.Path, "a", extension));

        _folder.Write("a" + extension, "{}");
        Assert.Equal(_folder.At("a 2" + extension), ApiWorkspace.FreePath(_folder.Path, "a", extension));

        _folder.Write("a 2" + extension, "{}");
        Assert.Equal(_folder.At("a 3" + extension), ApiWorkspace.FreePath(_folder.Path, "a", extension));
    }

    [Fact]
    public void A_folder_in_the_way_counts_as_taken()
    {
        Directory.CreateDirectory(_folder.At("a"));

        Assert.Equal(_folder.At("a 2"), ApiWorkspace.FreePath(_folder.Path, "a", ""));
    }

    [Fact]
    public void Copying_keeps_what_is_there_and_leaves_hidden_files_behind()
    {
        _folder.Write("a.request.json", "new");
        _folder.Write("sub/b.request.json", "new");
        _folder.Write(".git/config", "hidden");
        _folder.Write(".hidden", "hidden");
        using var target = new TempFolder();
        target.Write("a.request.json", "old");

        _workspace.CopyTo(target.Path);

        Assert.Equal("old", File.ReadAllText(target.At("a.request.json")));
        Assert.Equal("new", File.ReadAllText(target.At("sub", "b.request.json")));
        Assert.False(Directory.Exists(target.At(".git")));
        Assert.False(File.Exists(target.At(".hidden")));
    }

    [Fact]
    public void Compares_folders_whatever_their_ending()
    {
        Assert.True(ApiWorkspace.SameFolder(_folder.Path, _folder.Path + Path.DirectorySeparatorChar));
        Assert.False(ApiWorkspace.SameFolder(_folder.Path, _folder.At("other")));
    }

    [Fact]
    public void Keeps_the_folder_as_a_full_path()
    {
        Assert.Equal(Path.GetFullPath("relative"), new ApiWorkspace("relative").Folder);
    }
}

[Collection(ProcessStateCollection.Name)]
public class ApiWorkspaceLocationTests : DataFolderTest
{
    [Fact]
    public void The_default_folder_is_in_the_data_folder()
    {
        Assert.Equal(Data.At("api"), ApiWorkspace.DefaultFolder);
        Assert.Equal(Data.At("api"), ApiWorkspace.ChosenFolder);
    }

    [Fact]
    public void Remembers_the_folder_chosen()
    {
        ApiWorkspace.SaveChosenFolder("/some/where");

        Assert.Equal("/some/where", ApiWorkspace.ChosenFolder);
    }

    [Fact]
    public void Choosing_the_default_forgets_the_choice()
    {
        ApiWorkspace.SaveChosenFolder("/some/where");

        ApiWorkspace.SaveChosenFolder(ApiWorkspace.DefaultFolder);

        Assert.Equal(ApiWorkspace.DefaultFolder, ApiWorkspace.ChosenFolder);
        Assert.False(File.Exists(Data.At("api-location.json")));
    }

    [Fact]
    public void A_choice_that_cannot_be_read_falls_back_to_the_default()
    {
        File.WriteAllText(Data.At("api-location.json"), "not json");

        Assert.Equal(ApiWorkspace.DefaultFolder, ApiWorkspace.ChosenFolder);
    }
}

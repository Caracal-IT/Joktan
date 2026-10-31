using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Api.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class ApiRequestViewModelTests : DataFolderTest
{
    private readonly List<IReadOnlyList<ApiPair>> _saved = [];
    private readonly List<ApiRequestViewModel> _described = [];
    private ApiVariables _variables = ApiVariables.None;
    private string? _environment = "Dev";

    private ApiRequestViewModel New(ApiRequest? request = null)
    {
        var path = Data.At("a.request.json");
        ApiWorkspace.Write(path, request ?? new ApiRequest());
        return new ApiRequestViewModel(path, () => _variables, values =>
        {
            _saved.Add(values);
            return _environment;
        }, _described.Add);
    }

    private ApiRequest Saved() => ApiWorkspace.Read(Data.At("a.request.json"));

    private ApiRequestViewModel NewGrpc(string address, GrpcSchemaSource schema = GrpcSchemaSource.Reflection, params string[] protoFiles) => New(new ApiRequest
    {
        Kind = ApiKind.Grpc,
        Address = address,
        Schema = schema,
        ProtoFiles = [.. protoFiles],
        Message = """{"name":"me"}""",
        TimeoutSeconds = 10,
    });

    [Fact]
    public Task A_request_shows_what_its_file_holds_and_saves_changes_when_asked() => Headless.Run(() =>
    {
        var vm = New(new ApiRequest
        {
            Method = "POST",
            Url = "http://x",
            BodyKind = ApiBodyKind.Json,
            Body = "{}",
            AuthKind = ApiAuthKind.Bearer,
            Token = "t",
            Username = "u",
            Password = "p",
            Query = [new ApiPair { Name = "q", Value = "1" }],
            ImportPaths = ["/imports"],
            ProtoFiles = ["/a.proto"],
            AcceptAnyCertificate = true,
            TimeoutSeconds = 7,
        });

        Assert.True(vm.IsRest);
        Assert.False(vm.IsGrpc);
        Assert.Equal("POST", vm.Method);
        Assert.Equal("http://x", vm.Url);
        Assert.True(vm.IsBodyText);
        Assert.False(vm.IsBodyForm);
        Assert.True(vm.IsBearer);
        Assert.False(vm.IsBasic);
        Assert.Equal("q", vm.Query[0].Name);
        Assert.Equal("/imports", vm.ImportPaths[0].Path);
        Assert.Equal("/a.proto", vm.ProtoFiles[0].Path);
        Assert.Equal(RestSender.Methods, vm.MethodChoices);
        Assert.Contains(ApiBodyKind.Form, vm.BodyKinds);
        Assert.Contains(ApiAuthKind.Basic, vm.AuthKinds);

        vm.Url = "http://changed";
        Assert.Equal("http://x", Saved().Url);

        vm.Save();
        Assert.Equal("http://changed", Saved().Url);

        vm.BodyKind = ApiBodyKind.Form;
        vm.AuthKind = ApiAuthKind.Basic;
        Assert.True(vm.IsBodyForm);
        Assert.True(vm.IsBasic);
    });

    [Fact]
    public Task Changes_are_saved_after_a_pause() => Headless.Run(async () =>
    {
        var vm = New();

        vm.Headers.AddCommand.Execute(null);
        vm.Headers[0].Name = "H";

        await Headless.Until(() => Saved().Headers.Count == 1 && Saved().Headers[0].Name == "H");
    });

    [Fact]
    public Task A_save_that_fails_is_reported() => Headless.Run(() =>
    {
        var vm = New();
        File.Delete(vm.Path);
        Directory.CreateDirectory(vm.Path);

        vm.Url = "changed";
        vm.Save();

        Assert.StartsWith("Could not save ", vm.Error);

        // With nothing waiting, nothing is tried.
        vm.Error = null;
        vm.Save();
        Assert.Null(vm.Error);
    });

    [Fact]
    public Task Changing_the_method_tells_the_tree() => Headless.Run(() =>
    {
        var vm = New();

        vm.Method = "PUT";

        Assert.Same(vm, Assert.Single(_described));
    });

    [Fact]
    public Task The_body_and_message_can_be_tidied_if_they_are_json() => Headless.Run(() =>
    {
        var vm = New();

        vm.Body = """{"a":1}""";
        vm.FormatBodyCommand.Execute(null);
        Assert.Contains("\n", vm.Body);

        vm.Body = "{ not json";
        vm.FormatBodyCommand.Execute(null);
        Assert.Equal("The body is not valid JSON.", vm.Error);

        vm.Message = """{"a":1}""";
        vm.FormatMessageCommand.Execute(null);
        Assert.Contains("\n", vm.Message);

        vm.Message = "{ not json";
        vm.FormatMessageCommand.Execute(null);
        Assert.Equal("The message is not valid JSON.", vm.Error);
    });

    [Fact]
    public Task A_rest_request_is_sent_and_its_response_shown() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Method = "POST", Url = server.RestAddress + "/ok", BodyKind = ApiBodyKind.Text, Body = "hello" });
        vm.Url += " ";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.HasResponse);
        var response = vm.Response!;
        Assert.True(response.IsSuccess);
        Assert.False(response.IsFailure);
        Assert.False(response.HasTrailers);
        Assert.Contains("from the server", response.Body);
        Assert.NotEmpty(response.Headers);
        Assert.Empty(response.Trailers);
        Assert.EndsWith(" ms", response.Elapsed);
        Assert.EndsWith(" B", response.Size);
        Assert.NotEmpty(response.Status);
        Assert.Equal("hello", server.LastRestBody);
        Assert.True(vm.HasLastSent);
        Assert.True(vm.CanResend);
        Assert.StartsWith("Sent ", vm.LastSentTime);
        Assert.Contains("POST", vm.LastSentText);
        Assert.False(vm.IsSending);
        Assert.True(vm.IsNotSending);
    });

    [Fact]
    public Task Sending_says_which_variables_were_not_defined_and_saves_captures() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest
        {
            Url = server.RestAddress + "/ok?x={{missing}}",
            Captures = [new ApiPair { Name = "token", Value = "token" }, new ApiPair { Name = "nope", Value = "not.there" }],
        });

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("{{missing}}", vm.Error);
        Assert.Contains("nope", vm.Error);
        Assert.Equal("token", _saved.Single().Single().Name);
        Assert.Equal("from the server", _saved.Single().Single().Value);
        Assert.Equal("Saved to Dev: token", vm.SavedVariables);
    });

    [Fact]
    public Task Captures_without_an_environment_ask_for_one() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        _environment = null;
        var vm = New(new ApiRequest { Url = server.RestAddress + "/ok", Captures = [new ApiPair { Name = "token", Value = "token" }] });

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Choose an environment at the top to save token.", vm.Error);
        Assert.Null(vm.SavedVariables);
    });

    [Fact]
    public Task A_failed_response_saves_no_captures() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Url = server.RestAddress + "/missing", Captures = [new ApiPair { Name = "token", Value = "token" }] });

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.Response!.IsFailure);
        Assert.Empty(_saved);
    });

    [Fact]
    public Task A_request_being_sent_can_be_cancelled_and_is_not_sent_twice() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Url = server.RestAddress + "/hang" });
        vm.CancelCommand.Execute(null);

        var sending = vm.SendCommand.ExecuteAsync(null);
        await Headless.Until(() => vm.IsSending);
        Assert.False(vm.CanResend);
        await vm.ResendCommand.ExecuteAsync(null);

        vm.CancelCommand.Execute(null);
        await sending;

        Assert.False(vm.IsSending);
        Assert.False(vm.Response!.IsSuccess);
    });

    [Fact]
    public Task Resending_uses_what_was_sent_whatever_has_been_edited_since() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Method = "POST", Url = server.RestAddress + "/ok", Body = "first", BodyKind = ApiBodyKind.Text });
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.False(vm.HasResponse);

        await vm.SendCommand.ExecuteAsync(null);
        vm.Body = "second";

        await vm.ResendCommand.ExecuteAsync(null);

        Assert.Equal("first", server.LastRestBody);
    });

    [Fact]
    public Task What_was_last_sent_is_kept_between_runs() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Url = server.RestAddress + "/ok" });
        await vm.SendCommand.ExecuteAsync(null);

        var again = new ApiRequestViewModel(vm.Path, () => ApiVariables.None, _ => null, _ => { });

        Assert.True(again.HasLastSent);
    });

    [Fact]
    public Task Copying_gives_the_body_to_the_view_if_there_is_one() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Url = server.RestAddress + "/ok" });
        var copied = new List<string>();
        await vm.CopyBodyCommand.ExecuteAsync(null);

        await vm.SendCommand.ExecuteAsync(null);
        await vm.CopyBodyCommand.ExecuteAsync(null);
        Assert.Empty(copied);

        vm.CopyText = text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        };
        await vm.CopyBodyCommand.ExecuteAsync(null);
        Assert.Contains("from the server", Assert.Single(copied));
    });

    [Theory]
    [InlineData(100, "100 B")]
    [InlineData(2048, "2")]
    [InlineData(3 * 1024 * 1024, "3")]
    public void A_response_says_its_size_in_a_fitting_unit(long size, string starts)
    {
        var response = new ApiResponseViewModel(new ApiResponse { Size = size, Elapsed = TimeSpan.FromSeconds(2) }, isGrpc: true);

        Assert.StartsWith(starts, response.Size);
        Assert.EndsWith(" s", response.Elapsed);
        Assert.True(response.HasTrailers);
    }

    // ---- gRPC ----

    [Fact]
    public Task Starts_by_saying_how_to_find_the_services_and_keeps_what_was_chosen() => Headless.Run(() =>
    {
        var vm = New(new ApiRequest { Kind = ApiKind.Grpc, Address = "http://unused-address-" + Guid.NewGuid(), Service = "demo.Greeter", Rpc = "One" });

        Assert.True(vm.IsGrpc);
        Assert.True(vm.UsesReflection);
        Assert.False(vm.UsesProtoFiles);
        Assert.Contains("ask the server", vm.SchemaStatus);
        Assert.Equal(["demo.Greeter"], vm.Services);
        Assert.Equal(["One"], vm.Methods);
        Assert.Equal("", vm.MethodText);

        vm.UsesProtoFiles = true;
        Assert.Contains(".proto files", vm.SchemaStatus);
        Assert.True(vm.UsesProtoFiles);
        vm.UsesProtoFiles = false;
        Assert.True(vm.UsesProtoFiles);
        vm.UsesReflection = true;
        Assert.True(vm.UsesReflection);
        vm.UsesReflection = false;
        Assert.True(vm.UsesReflection);
    });

    [Fact]
    public Task Services_are_read_from_proto_files_and_each_method_described() => Headless.Run(async () =>
    {
        var proto = FakeServer.WriteProtos(Data);
        var vm = NewGrpc("http://unused", GrpcSchemaSource.ProtoFiles, proto);

        await vm.LoadServicesCommand.ExecuteAsync(null);

        Assert.Equal("1 service, 6 methods", vm.SchemaStatus);
        Assert.Equal("demo.Greeter", vm.Service);
        Assert.Equal("One", vm.Rpc);
        Assert.Equal("Unary · Named → Named", vm.MethodText);

        vm.RpcChoice = "Many";
        Assert.Equal("Server streaming · Named → stream Named", vm.MethodText);
        vm.RpcChoice = "Gather";
        Assert.StartsWith("Client streaming; write the messages as a JSON array · stream Named", vm.MethodText);
        vm.RpcChoice = "Both";
        Assert.StartsWith("Two-way streaming", vm.MethodText);

        vm.RpcChoice = "";
        vm.ServiceChoice = null;
        Assert.Equal("Both", vm.RpcChoice);
        Assert.Equal("demo.Greeter", vm.ServiceChoice);

        // A request opened later on the same files finds them already read.
        vm.Save();
        Assert.StartsWith("1 service", new ApiRequestViewModel(vm.Path, () => ApiVariables.None, _ => null, _ => { }).SchemaStatus);
    });

    [Fact]
    public Task Choosing_a_service_that_is_not_there_changes_nothing_but_gives_no_method_text() => Headless.Run(async () =>
    {
        var proto = FakeServer.WriteProtos(Data);
        var vm = NewGrpc("http://unused", GrpcSchemaSource.ProtoFiles, proto);
        await vm.LoadServicesCommand.ExecuteAsync(null);

        vm.Service = "demo.Other";

        Assert.Equal("", vm.MethodText);
        vm.TemplateCommand.Execute(null);
        Assert.Equal("Load the services and choose a method first.", vm.Error);
    });

    [Fact]
    public Task A_template_is_an_example_of_the_request_message() => Headless.Run(async () =>
    {
        var proto = FakeServer.WriteProtos(Data);
        var vm = NewGrpc("http://unused", GrpcSchemaSource.ProtoFiles, proto);
        await vm.LoadServicesCommand.ExecuteAsync(null);

        vm.TemplateCommand.Execute(null);
        Assert.StartsWith("{", vm.Message);
        Assert.Contains("name", vm.Message);

        vm.RpcChoice = "Gather";
        vm.TemplateCommand.Execute(null);
        Assert.StartsWith("[", vm.Message);
    });

    [Fact]
    public Task Proto_files_that_cannot_be_read_say_why() => Headless.Run(async () =>
    {
        var vm = NewGrpc("http://unused", GrpcSchemaSource.ProtoFiles, Data.At("gone.proto"));

        await vm.LoadServicesCommand.ExecuteAsync(null);

        Assert.Contains("does not exist", vm.SchemaStatus);
    });

    [Fact]
    public Task Services_are_asked_of_the_server() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = NewGrpc(server.GrpcAddress);

        await vm.LoadServicesCommand.ExecuteAsync(null);

        Assert.Equal("1 service, 6 methods", vm.SchemaStatus);

        vm.Address = "http://another.example";
        Assert.Contains("ask the server", vm.SchemaStatus);
    });

    [Fact]
    public Task A_server_that_is_not_there_is_reported() => Headless.Run(async () =>
    {
        var vm = NewGrpc("");

        await vm.LoadServicesCommand.ExecuteAsync(null);

        Assert.Contains("Enter the server's address", vm.SchemaStatus);
    });

    [Fact]
    public Task A_grpc_request_finds_its_method_and_is_sent() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = NewGrpc(server.GrpcAddress);

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.Response!.IsSuccess);
        Assert.True(vm.Response.HasTrailers);
        Assert.Contains("hello me", vm.Response.Body);
        Assert.Equal("One", vm.LastSent!.Request.Rpc);

        // Sent again, from what was remembered.
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.True(vm.Response!.IsSuccess);
    });

    [Fact]
    public Task A_grpc_request_with_no_way_to_find_its_method_says_why() => Headless.Run(async () =>
    {
        var vm = NewGrpc("");

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("Enter the server's address", vm.Error);
        Assert.False(vm.HasResponse);
    });

    [Fact]
    public Task A_request_for_a_method_that_has_gone_moves_to_one_there_is() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New(new ApiRequest { Kind = ApiKind.Grpc, Address = server.GrpcAddress, Service = "demo.Greeter", Rpc = "Gone", TimeoutSeconds = 10 });

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("One", vm.Rpc);
        Assert.True(vm.HasResponse);
    });

    [Fact]
    public Task Resending_a_method_that_has_gone_or_a_server_that_has_says_so() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = NewGrpc(server.GrpcAddress);
        await vm.SendCommand.ExecuteAsync(null);

        var sent = vm.LastSent!;
        sent.Request.Rpc = "Gone";
        vm.LastSent = sent;
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Equal("demo.Greeter has no method Gone.", vm.Error);

        sent.Request.Address = "";
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Contains("Enter the server's address", vm.Error);
    });

    [Fact]
    public Task Adding_proto_files_uses_them_and_leaves_out_repeats() => Headless.Run(async () =>
    {
        var vm = New(new ApiRequest { Kind = ApiKind.Grpc, ProtoFiles = ["/a.proto"], ImportPaths = ["/imports"] });
        await vm.AddProtoFilesCommand.ExecuteAsync(null);
        await vm.AddImportPathCommand.ExecuteAsync(null);
        Assert.Single(vm.ProtoFiles);

        vm.PickFiles = folder => Task.FromResult<IReadOnlyList<string>>(folder ? ["/imports", "/more"] : ["/a.proto", "/b.proto"]);
        await vm.AddProtoFilesCommand.ExecuteAsync(null);
        await vm.AddImportPathCommand.ExecuteAsync(null);

        Assert.True(vm.UsesProtoFiles);
        Assert.Equal(["/a.proto", "/b.proto"], vm.ProtoFiles.Select(p => p.Path));
        Assert.Equal(["/imports", "/more"], vm.ImportPaths.Select(p => p.Path));

        vm.ProtoFiles[0].RemoveCommand.Execute(null);
        Assert.Equal(["/b.proto"], vm.ProtoFiles.Select(p => p.Path));
    });
}

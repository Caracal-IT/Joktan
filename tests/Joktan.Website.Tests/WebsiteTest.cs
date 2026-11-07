using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Rest;
using Joktan.Tests.TestSupport;
using Joktan.Workflows;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests;
using Xunit;

namespace Joktan.Website.Tests;

/// <summary>Users who live in the test, with their passwords.</summary>
public sealed class FakeWebsiteUsers : IWebsiteUsers
{
    public Dictionary<int, (WebsiteUser User, string Password)> All { get; } = [];

    /// <summary>Why the users cannot be read, as when their database is down; null when they can.</summary>
    public string? Unavailable { get; set; }

    public WebsiteUser Add(int id, string username, string role, bool administrator = false, string password = "pw")
    {
        var user = new WebsiteUser(id, username, char.ToUpperInvariant(username[0]) + username[1..], role, administrator);
        All[id] = (user, password);
        return user;
    }

    public Task<WebsiteUser?> SignInAsync(string username, string password)
    {
        if (Unavailable is not null)
            throw new UsersUnavailableException(Unavailable);
        return Task.FromResult(All.Values.FirstOrDefault(u => string.Equals(u.User.Username, username, StringComparison.OrdinalIgnoreCase) && u.Password == password).User);
    }

    public WebsiteUser? Find(int id) => All.TryGetValue(id, out var found) ? found.User : null;
}

/// <summary>A workflow engine with runs waiting for answers, and the website over it, called as a browser or program would.</summary>
public abstract class WebsiteTest : DataFolderTest
{
    protected WebsiteTest()
    {
        Library = new WorkflowLibrary(Data.At("definitions"));
        Engine = new WorkflowEngine(Library, new WorkflowSettings { ShortTimeLimitSeconds = 60 }, new FakeWorkflowServices(), new RunStore(Data.At("runs")));
        Admin = Users.Add(1, "admin", "Administrator", administrator: true);
        Ann = Users.Add(2, "ann", "Approver");
        Bob = Users.Add(3, "bob", "Operator");
        Site = new Website(Settings, Engine, Users);
    }

    protected override void Dispose(bool disposing)
    {
        Engine.Dispose();
        base.Dispose(disposing);
    }

    protected WebsiteSettings Settings { get; } = new() { Enabled = true };

    protected FakeWebsiteUsers Users { get; } = new();

    protected WorkflowLibrary Library { get; }

    protected WorkflowEngine Engine { get; }

    protected Website Site { get; }

    protected WebsiteUser Admin { get; }

    protected WebsiteUser Ann { get; }

    protected WebsiteUser Bob { get; }

    protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public static Assignee Role(string name) => new() { Kind = AssigneeKind.Role, Id = name, Name = name };

    public static Assignee User(WebsiteUser user) => new() { Kind = AssigneeKind.User, Id = user.IdText, Name = user.Name };

    /// <summary>Starts a long workflow whose user input step asks someone, and waits for it to ask.</summary>
    protected async Task<WorkflowRun> AskAsync(Assignee to, string name = "Orders", string pageId = "", string input = "{}", params InputField[] fields)
    {
        var flow = new Flow(name, WorkflowKind.Long).Then(new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Approve order?", AssignedTo = to, PageId = pageId, Variable = "answer",
            Fields = fields.ToList(),
        }).Then(Flow.End());
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, JsonNode.Parse(input), RunTrigger.Manual);
        var deadline = DateTime.Now + Patience;
        while (run.PendingInput is null)
        {
            Assert.True(DateTime.Now < deadline, $"The run did not ask; it is {run.Status}: {run.Error}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return run;
    }

    /// <summary>A request as the server would read it, from this computer.</summary>
    protected static RestRequest Request(string method, string path, JsonNode? body = null, string? token = null, Dictionary<string, string>? headers = null)
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = "localhost:47822" };
        if (token is not null)
            all["Authorization"] = $"Bearer {token}";
        foreach (var (name, value) in headers ?? [])
            all[name] = value;
        var question = path.IndexOf('?');
        return new RestRequest(method, question < 0 ? path : path[..question], question < 0 ? "" : path[(question + 1)..], all, body?.ToJsonString() ?? "");
    }

    protected Task<RestReply> CallAsync(string method, string path, JsonNode? body = null, string? token = null, Dictionary<string, string>? headers = null)
        => Site.AnswerAsync(Request(method, path, body, token, headers));

    /// <summary>Signs in, giving the token.</summary>
    protected async Task<string> SignInAsync(WebsiteUser user, string password = "pw")
    {
        var reply = await CallAsync("POST", "/api/login", new JsonObject { ["username"] = user.Username, ["password"] = password });
        Assert.Equal(200, reply.Status);
        return reply.Body!["token"]!.GetValue<string>();
    }
}

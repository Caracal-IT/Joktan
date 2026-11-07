using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Rest;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Website.Tests;

[Collection(ProcessStateCollection.Name)]
public class WebsiteTests : WebsiteTest
{
    [Fact]
    public async Task Signing_in_gives_a_token_and_a_cookie_that_only_this_site_can_use()
    {
        var reply = await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "ANN", ["password"] = "pw" });
        Assert.Equal(200, reply.Status);
        Assert.Equal("ann", reply.Body!["user"]!["username"]!.GetValue<string>());
        Assert.False(reply.Body["user"]!["administrator"]!.GetValue<bool>());
        var token = reply.Body["token"]!.GetValue<string>();
        Assert.True(token.Length >= 40);
        var cookie = reply.Headers!["Set-Cookie"];
        Assert.StartsWith($"{Website.CookieName}={token};", cookie);
        Assert.Contains("HttpOnly", cookie);
        Assert.Contains("SameSite=Strict", cookie);

        // The cookie does as well as the token, as it does for the browser.
        var me = await CallAsync("GET", "/api/me", headers: new() { ["Cookie"] = $"other=1; {Website.CookieName}={token}" });
        Assert.Equal("Approver", me.Body!["role"]!.GetValue<string>());

        var signedOut = await CallAsync("POST", "/api/logout", new JsonObject(), token);
        Assert.Contains("Max-Age=0", signedOut.Headers!["Set-Cookie"]);
        Assert.Equal(401, (await CallAsync("GET", "/api/me", token: token)).Status);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_too_many_lock_the_username_out_for_a_while()
    {
        var wrong = await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "ann", ["password"] = "nope" });
        Assert.Equal(401, wrong.Status);
        Assert.Equal("The username or password is wrong.", wrong.Body!["error"]!.GetValue<string>());
        Assert.Equal(400, (await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "ann" })).Status);

        for (var i = 1; i < WebsiteSessions.MaxFailures; i++)
            await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "ann", ["password"] = "nope" });
        var locked = await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "ann", ["password"] = "pw" });
        Assert.Equal(429, locked.Status);

        // Others are not held back.
        await SignInAsync(Bob);

        Users.Unavailable = "The users database is not running.";
        var down = await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "bob", ["password"] = "pw" });
        Assert.Equal(503, down.Status);
        Assert.Equal("The users database is not running.", down.Body!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Calls_need_someone_signed_in_and_a_user_removed_since_is_signed_out()
    {
        var reply = await CallAsync("GET", "/api/items");
        Assert.Equal(401, reply.Status);
        Assert.Equal("Bearer", reply.Headers!["WWW-Authenticate"]);
        Assert.Equal(401, (await CallAsync("GET", "/api/items", token: "made-up")).Status);

        var token = await SignInAsync(Bob);
        Assert.Equal(200, (await CallAsync("GET", "/api/items", token: token)).Status);
        Users.All.Remove(Bob.Id);
        Assert.Equal(401, (await CallAsync("GET", "/api/items", token: token)).Status);
    }

    [Fact]
    public async Task A_user_sees_only_their_own_work_items_and_an_administrator_sees_them_all()
    {
        var forApprovers = await AskAsync(Role("Approver"), "Approve");
        var forBob = await AskAsync(User(Bob), "Pick");
        var forManagers = await AskAsync(Role("Manager"), "Escalated");

        async Task<List<string>> ItemsOf(WebsiteUser user)
        {
            var reply = await CallAsync("GET", "/api/items", token: await SignInAsync(user));
            Assert.Equal(200, reply.Status);
            return reply.Body!["items"]!.AsArray().Select(i => i!["workflow"]!.GetValue<string>()).ToList();
        }

        Assert.Equal(["Approve"], await ItemsOf(Ann));
        Assert.Equal(["Pick"], await ItemsOf(Bob));
        Assert.Equal(["Approve", "Pick", "Escalated"], await ItemsOf(Admin));

        var all = await CallAsync("GET", "/api/items", token: await SignInAsync(Admin));
        Assert.True(all.Body!["all"]!.GetValue<bool>());
        var first = all.Body["items"]![0]!;
        Assert.Equal(forApprovers.PendingInput!.Id, first["id"]!.GetValue<string>());
        Assert.Equal(forApprovers.Id, first["runId"]!.GetValue<string>());
        Assert.False(first["assignedToYou"]!.GetValue<bool>());
        Assert.Equal("role", first["assignedTo"]!["kind"]!.GetValue<string>());
        Assert.Equal("Approve order?", first["title"]!.GetValue<string>());

        // Another's item is not there for a user, as if it did not exist.
        var annToken = await SignInAsync(Ann);
        Assert.Equal(404, (await CallAsync("GET", $"/api/items/{forBob.PendingInput!.Id}", token: annToken)).Status);
        Assert.Equal(404, (await CallAsync("POST", $"/api/items/{forManagers.PendingInput!.Id}", new JsonObject { ["values"] = new JsonObject() }, annToken)).Status);
        Assert.Equal(RunStatus.WaitingForInput, forManagers.Status);
    }

    [Fact]
    public async Task A_work_item_comes_with_its_page_and_is_answered_with_its_values()
    {
        Engine.Pages.Save(new WorkflowPage
        {
            Id = "approve", Name = "Approve", Title = "Order {{input.order}}",
            Items =
            [
                new PageItem { Type = PageItemType.Value, Label = "Total", Text = "{{input.total}}" },
                new PageItem { Type = PageItemType.CheckBox, Name = "approved", Required = true },
                new PageItem { Type = PageItemType.Number, Name = "quantity", Default = "1" },
                new PageItem { Type = PageItemType.DropDown, Name = "speed", Options = ["Normal", "Fast"] },
            ],
        });
        var run = await AskAsync(Role("Approver"), pageId: "approve", input: """{"order":"A-7","total":"12.50"}""");
        var token = await SignInAsync(Ann);
        var id = run.PendingInput!.Id;

        var item = (await CallAsync("GET", $"/api/items/{id}", token: token)).Body!;
        Assert.Equal("Order A-7", item["title"]!.GetValue<string>());
        Assert.Equal("approve", item["pageId"]!.GetValue<string>());
        var page = item["page"]!;
        Assert.Equal("Order A-7", page["title"]!.GetValue<string>());
        Assert.Equal("Value", page["items"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("12.50", page["items"]![0]!["text"]!.GetValue<string>());
        Assert.True(page["items"]![1]!["required"]!.GetValue<bool>());
        Assert.Equal(["approved", "quantity", "speed"], item["fields"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()));

        var wrong = await CallAsync("POST", $"/api/items/{id}", new JsonObject
        {
            ["values"] = new JsonObject { ["approved"] = false, ["quantity"] = "lots", ["speed"] = "Slow", ["extra"] = 1 },
        }, token);
        Assert.Equal(422, wrong.Status);
        var problems = wrong.Body!["fields"]!.AsObject();
        Assert.Equal(["approved", "extra", "quantity", "speed"], problems.Select(p => p.Key).Order());
        Assert.Equal("This must be ticked.", problems["approved"]!.GetValue<string>());
        Assert.Equal(RunStatus.WaitingForInput, run.Status);

        var answered = await CallAsync("POST", $"/api/items/{id}", new JsonObject
        {
            ["values"] = new JsonObject { ["approved"] = true, ["quantity"] = 3, ["speed"] = "Fast" },
        }, token);
        Assert.Equal(200, answered.Status);
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
        Assert.Equal("""{"approved":true,"quantity":3,"speed":"Fast"}""", Workflows.Engine.JsonText.Of(finished.Steps.Single(s => s.Type == StepType.UserInput).Result));
        Assert.Contains(finished.Log, l => l.Text == "Answered by Ann (ann) on the website.");

        // Answered once only.
        Assert.Equal(404, (await CallAsync("POST", $"/api/items/{id}", new JsonObject { ["values"] = new JsonObject() }, token)).Status);
    }

    [Fact]
    public async Task A_step_without_a_page_is_shown_with_its_message_and_fields()
    {
        var run = await AskAsync(Role("Approver"), fields: [new InputField { Name = "reason", Label = "Reason", Default = "ok" }]);
        var token = await SignInAsync(Ann);
        var item = (await CallAsync("GET", $"/api/items/{run.PendingInput!.Id}", token: token)).Body!;
        Assert.Null(item["pageId"]);
        Assert.Equal("Approve order?", item["page"]!["title"]!.GetValue<string>());
        var box = item["page"]!["items"]![0]!;
        Assert.Equal(("TextBox", "reason", "ok"), (box["type"]!.GetValue<string>(), box["name"]!.GetValue<string>(), box["default"]!.GetValue<string>()));

        Assert.Equal(200, (await CallAsync("POST", $"/api/items/{run.PendingInput.Id}", new JsonObject { ["values"] = new JsonObject() }, token)).Status);
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("""{"reason":"ok"}""", Workflows.Engine.JsonText.Of(finished.Steps.Single(s => s.Type == StepType.UserInput).Result));
    }

    [Fact]
    public async Task Only_an_administrator_can_preview_a_page()
    {
        Engine.Pages.Save(new WorkflowPage { Id = "p1", Name = "Approve", Title = "Order {{input.order}}" });
        Assert.Equal(403, (await CallAsync("GET", "/api/pages/p1", token: await SignInAsync(Ann))).Status);
        var admin = await SignInAsync(Admin);
        var page = await CallAsync("GET", "/api/pages/p1", token: admin);
        Assert.Equal("Order {{input.order}}", page.Body!["page"]!["title"]!.GetValue<string>());
        Assert.Equal(404, (await CallAsync("GET", "/api/pages/nope", token: admin)).Status);
        Assert.Equal("Approve", (await CallAsync("GET", "/api/pages", token: admin)).Body!["pages"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_pages_of_the_site_are_its_own_and_are_not_framed_or_called_from_elsewhere()
    {
        foreach (var path in new[] { "/", "/login", "/mine", "/items/abc", "/preview/p1" })
        {
            var page = await CallAsync("GET", path);
            Assert.Equal(200, page.Status);
            Assert.StartsWith("text/html", page.ContentType);
            Assert.Contains("/assets/app.js", Encoding.UTF8.GetString(page.Content!));
            Assert.Equal("DENY", page.Headers!["X-Frame-Options"]);
            Assert.Contains("default-src 'self'", page.Headers["Content-Security-Policy"]);
        }

        Assert.StartsWith("text/javascript", (await CallAsync("GET", "/assets/app.js")).ContentType);
        Assert.StartsWith("text/css", (await CallAsync("GET", "/assets/app.css")).ContentType);
        var logo = await CallAsync("GET", "/assets/icon.png");
        Assert.Equal("image/png", logo.ContentType);
        Assert.True(logo.Content!.Length > 1000);
        var strings = JsonNode.Parse((await CallAsync("GET", "/assets/strings.json")).Content)!;
        Assert.Equal("Sign in", strings["SignIn"]!.GetValue<string>());
        Assert.Null(strings["WebsiteSettings_Title"]);
        Assert.Equal(404, (await CallAsync("GET", "/assets/../Joktan.dll")).Status);
        Assert.Equal(404, (await CallAsync("GET", "/nothing")).Status);

        var elsewhere = await CallAsync("POST", "/api/login", new JsonObject { ["username"] = "ann", ["password"] = "pw" },
            headers: new() { ["Origin"] = "https://evil.example" });
        Assert.Equal(403, elsewhere.Status);
        var rebound = await Site.AnswerAsync(new RestRequest("GET", "/", "", new Dictionary<string, string> { ["Host"] = "evil.example" }, ""));
        Assert.Equal(403, rebound.Status);

        // Answering other computers, it answers whatever name it is called by.
        Settings.FromOtherComputers = true;
        Assert.Equal(200, (await Site.AnswerAsync(new RestRequest("GET", "/", "", new Dictionary<string, string> { ["Host"] = "office-mac:47822" }, ""))).Status);
    }

    [Fact]
    public async Task A_program_does_it_all_over_http()
    {
        var run = await AskAsync(User(Bob), fields: [new InputField { Name = "ok", Default = "false" }]);
        using var server = new RestServer(false, 0, Site.AnswerAsync);
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{server.Port}") };
        var cancel = TestContext.Current.CancellationToken;

        var login = await http.PostAsync("/api/login", new StringContent("""{"username":"bob","password":"pw"}""", Encoding.UTF8, "application/json"), cancel);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = JsonNode.Parse(await login.Content.ReadAsStringAsync(cancel))!["token"]!.GetValue<string>();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var items = JsonNode.Parse(await http.GetStringAsync("/api/items", cancel))!["items"]!.AsArray();
        var id = Assert.Single(items)!["id"]!.GetValue<string>();
        var answer = await http.PostAsync($"/api/items/{id}", new StringContent("""{"values":{"ok":"true"}}""", Encoding.UTF8, "application/json"), cancel);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, cancel);
        Assert.Equal(RunStatus.Completed, finished.Status);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Rest;
using Joktan.Core;
using Joktan.Website.Resources;
using Joktan.Workflows.Engine;
using Microsoft.Extensions.Logging;

namespace Joktan.Website;

/// <summary>
/// The website where people sign in and answer the questions workflows ask them, each on its page from
/// Workflows / Pages. The pages a browser shows are one app at <c>/</c>, drawn from the JSON calls under
/// <c>/api</c>, which other programs can make too: sign in with <c>POST /api/login</c>, then send the
/// token it gives as <c>Authorization: Bearer &lt;token&gt;</c>. A user is given only the questions for
/// their role or for them; an administrator is given every one. Runs where the engine runs, on the UI thread.
/// </summary>
public sealed class Website(WebsiteSettings settings, WorkflowEngine engine, IWebsiteUsers users, WebsiteSessions? sessions = null)
{
    public const string CookieName = "joktan_session";

    private const string Html = "text/html; charset=utf-8";

    private readonly ILogger _log = AppLog.For("Website");

    public WebsiteSessions Sessions { get; } = sessions ?? new WebsiteSessions();

    // A page cannot be shown in a frame elsewhere, and loads nothing but its own files.
    private static readonly Dictionary<string, string> Secure = new()
    {
        ["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'",
        ["X-Content-Type-Options"] = "nosniff",
        ["X-Frame-Options"] = "DENY",
        ["Referrer-Policy"] = "no-referrer",
    };

    public async Task<RestReply> AnswerAsync(RestRequest request)
    {
        request.Headers.TryGetValue("Host", out var host);

        // On this computer alone, a page elsewhere whose name was pointed at this computer would name itself, which stops DNS rebinding.
        if (!settings.FromOtherComputers && !IsLoopbackHost(host))
            return Problem(403, "Open the website as localhost or 127.0.0.1.");

        // Pages of other sites may not make calls as whoever is signed in here.
        if (request.Headers.TryGetValue("Origin", out var origin) && origin.Length > 0 && !IsOwnOrigin(origin, host))
            return Problem(403, "Requests from other web pages are not accepted.");

        var path = request.Path.Length > 1 ? request.Path.TrimEnd('/') : request.Path;
        if (path == "/api" || path.StartsWith("/api/", StringComparison.Ordinal))
            return await ApiAsync(request, path);
        if (request.Method != "GET")
            return NotAllowed("GET");
        if (path.StartsWith("/assets/", StringComparison.Ordinal))
            return Asset(path["/assets/".Length..]);

        // The app draws each of its pages itself, from the address.
        return path is "/" or "/login" or "/mine" || path.StartsWith("/items/", StringComparison.Ordinal) || path.StartsWith("/preview/", StringComparison.Ordinal)
            ? Page("index.html", Html)
            : Problem(404, "There is no such page.");
    }

    private async Task<RestReply> ApiAsync(RestRequest request, string path)
    {
        var route = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[1..];
        if (route is ["login"])
            return request.Method == "POST" ? await SignInAsync(request) : NotAllowed("POST");

        var (user, token) = SignedIn(request);
        if (user is null)
            return Problem(401, "Sign in first, with POST /api/login, and send the token it gives as Authorization: Bearer <token>.",
                new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer" });

        switch (route)
        {
            case ["logout"] when request.Method == "POST":
                Sessions.End(token!);
                return Reply(200, new JsonObject { ["signedOut"] = true }, new Dictionary<string, string> { ["Set-Cookie"] = Cookie("", TimeSpan.Zero) });
            case ["me"] when request.Method == "GET":
                return Reply(200, UserJson(user));
            case ["items"] when request.Method == "GET":
                return Reply(200, new JsonObject
                {
                    ["user"] = UserJson(user),
                    ["all"] = user.IsAdministrator,
                    ["items"] = new JsonArray(WorkItems.For(engine.Runs, user).Select(r => (JsonNode)WorkItems.Summary(r, user)).ToArray()),
                });
            case ["items", var id] when request.Method == "GET":
                return WorkItems.Find(engine.Runs, id, user) is { } run
                    ? Reply(200, WorkItems.Detail(run, user))
                    : Problem(404, "There is no such work item for you; it may have been answered already.");
            case ["items", var id] when request.Method == "POST":
                return Answer(request, id, user);
            case ["pages"] when request.Method == "GET":
                return !user.IsAdministrator
                    ? Problem(403, "Only an administrator can see the pages.")
                    : Reply(200, new JsonObject
                    {
                        ["pages"] = new JsonArray(engine.Pages.All.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name }).ToArray()),
                    });
            case ["pages", var id] when request.Method == "GET":
                if (!user.IsAdministrator)
                    return Problem(403, "Only an administrator can preview pages.");
                return engine.Pages.Find(Uri.UnescapeDataString(id)) is { } page
                    ? Reply(200, new JsonObject { ["page"] = WorkItems.PageJsonOf(page) })
                    : Problem(404, "There is no such page on Workflows / Pages.");
            case ["logout"] or ["items"] or ["items", _]:
                return NotAllowed(route is ["items", _] ? "GET, POST" : route is ["logout"] ? "POST" : "GET");
            default:
                return Problem(404, "There is no such call.");
        }
    }

    private async Task<RestReply> SignInAsync(RestRequest request)
    {
        if (Parse(request.Body) is not { } body || body["username"]?.GetValueKind() != JsonValueKind.String || body["password"]?.GetValueKind() != JsonValueKind.String)
            return Problem(400, "Send a JSON object with a username and a password.");
        var username = body["username"]!.GetValue<string>().Trim();
        var password = body["password"]!.GetValue<string>();

        if (Sessions.LockedOut(username) is { } wait)
            return Problem(429, $"Too many wrong passwords. Try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes.");

        WebsiteUser? user;
        try
        {
            user = await users.SignInAsync(username, password);
        }
        catch (UsersUnavailableException e)
        {
            return Problem(503, e.Message);
        }

        if (user is null)
        {
            Sessions.Failed(username);
            _log.LogWarning("A sign-in as {Username} was refused", username);
            return Problem(401, "The username or password is wrong.");
        }

        Sessions.Succeeded(username);
        var (token, expires) = Sessions.Start(user.Id, settings.SessionLength);
        _log.LogInformation("{Username} signed in", user.Username);
        return Reply(200, new JsonObject
        {
            ["token"] = token,
            ["expires"] = expires.ToString("O", CultureInfo.InvariantCulture),
            ["user"] = UserJson(user),
        }, new Dictionary<string, string> { ["Set-Cookie"] = Cookie(token, settings.SessionLength) });
    }

    private RestReply Answer(RestRequest request, string id, WebsiteUser user)
    {
        if (Parse(request.Body) is not { } body || body["values"] is not (JsonObject or null))
            return Problem(400, "Send a JSON object with the values, as {\"values\": {\"field\": \"value\"}}.");
        if (WorkItems.Find(engine.Runs, id, user) is not { PendingInput: { } question } run)
            return Problem(404, "There is no such work item for you; it may have been answered already.");

        var (values, problems) = WorkItems.Check(question, body["values"] as JsonObject);
        if (problems is not null)
            return Reply(422, new JsonObject { ["error"] = "Some of the values are not right.", ["fields"] = problems });

        try
        {
            engine.Answer(run.Id, values!, $"{user.Name} ({user.Username}) on the website");
        }
        catch (WorkflowException e)
        {
            return Problem(409, e.Message);
        }

        _log.LogInformation("{Username} answered run #{Serial} of {Workflow}", user.Username, run.Serial, run.WorkflowName);
        return Reply(200, new JsonObject { ["answered"] = true, ["runId"] = run.Id, ["serial"] = run.Serial });
    }

    // Whoever the bearer token or the cookie stands for, if they are still a user.
    private (WebsiteUser? User, string? Token) SignedIn(RestRequest request)
    {
        if (Token(request) is not { } token || Sessions.UserOf(token, settings.SessionLength) is not { } id)
            return (null, null);
        if (users.Find(id) is { } user)
            return (user, token);
        Sessions.End(token);
        return (null, null);
    }

    private static string? Token(RestRequest request)
    {
        const string bearer = "Bearer ";
        if (request.Headers.TryGetValue("Authorization", out var authorization) && authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            return authorization[bearer.Length..].Trim() is { Length: > 0 } given ? given : null;
        if (!request.Headers.TryGetValue("Cookie", out var cookies))
            return null;
        foreach (var cookie in cookies.Split(';'))
        {
            var equals = cookie.IndexOf('=');
            if (equals > 0 && cookie[..equals].Trim() == CookieName && cookie[(equals + 1)..].Trim() is { Length: > 0 } value)
                return value;
        }

        return null;
    }

    // Kept from scripts, and sent only to this site: a page elsewhere cannot use it.
    private static string Cookie(string token, TimeSpan length)
        => $"{CookieName}={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age={(long)length.TotalSeconds}";

    private static JsonObject UserJson(WebsiteUser user) => new()
    {
        ["id"] = user.Id,
        ["username"] = user.Username,
        ["name"] = user.Name,
        ["role"] = user.Role,
        ["administrator"] = user.IsAdministrator,
    };

    private static JsonObject? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body.Trim().Length == 0 ? "{}" : body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RestReply Reply(int status, JsonNode body, IReadOnlyDictionary<string, string>? headers = null)
        => new(status, body, With(headers));

    private static RestReply Problem(int status, string message, IReadOnlyDictionary<string, string>? headers = null)
        => RestReply.Problem(status, message, With(headers));

    private static RestReply NotAllowed(string allowed)
        => Problem(405, $"Use {allowed}.", new Dictionary<string, string> { ["Allow"] = allowed });

    private static Dictionary<string, string> With(IReadOnlyDictionary<string, string>? headers)
    {
        var all = new Dictionary<string, string>(Secure);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            all[name] = value;
        return all;
    }

    private static RestReply Asset(string name) => name switch
    {
        "app.js" => Page(name, "text/javascript; charset=utf-8"),
        "app.css" => Page(name, "text/css; charset=utf-8"),
        "icon.png" => Page(name, "image/png"),
        "strings.json" => new RestReply(200, null, Secure) { Content = StringsJson(), ContentType = RestReply.Json },
        _ => Problem(404, "There is no such file."),
    };

    private static RestReply Page(string name, string contentType)
        => File(name) is { } content ? new RestReply(200, null, Secure) { Content = content, ContentType = contentType } : Problem(404, "There is no such file.");

    private static byte[]? File(string name)
    {
        using var stream = typeof(Website).Assembly.GetManifestResourceStream($"Joktan.Website.wwwroot.{name}");
        if (stream is null)
            return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>The text the website's pages show, from the strings named Web_*, without that start.</summary>
    public static byte[] StringsJson()
    {
        var strings = new JsonObject();
        var set = Strings.ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
        var entries = set?.Cast<System.Collections.DictionaryEntry>() ?? Enumerable.Empty<System.Collections.DictionaryEntry>();
        foreach (var entry in entries.OrderBy(e => (string)e.Key, StringComparer.Ordinal))
        {
            if (entry.Key is string key && key.StartsWith("Web_", StringComparison.Ordinal))
                strings[key["Web_".Length..]] = entry.Value as string;
        }

        return Encoding.UTF8.GetBytes(strings.ToJsonString());
    }

    private static bool IsLoopbackHost(string? host)
        => host is not null && Uri.TryCreate($"http://{host}/", UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static bool IsOwnOrigin(string origin, string? host)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && host is not null && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);
}

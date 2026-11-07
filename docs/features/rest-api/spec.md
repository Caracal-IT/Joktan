# REST API

Lets scripts and programs that are not MCP clients do what the agent can, over plain HTTP, with API keys.

| Item      | Value                      |
|-----------|----------------------------|
| Menu path | Settings / REST API        |
| Status    | Implemented                |
| Code      | `src/Joktan.Agent/Rest/`, `ViewModels/RestApiSettingsViewModel.cs`, `ViewModels/RestApiItems.cs`, `Views/RestApiSettingsView.axaml` |
| Settings  | `settings/rest-api.json`   |

## Calls

- Every tool of the [MCP server](../agent/spec.md) is a call at `/api/<tool name>`, so the API grows with the tools and there is one set of checks. Tools of risk Secret (`passwords_reveal`) are never calls, whatever the settings say.
- `POST /api/<name>` takes a JSON object of the tool's arguments; an empty body is `{}`. Tools of risk Read also take `GET`, with the arguments in the query, each as text, which `ToolArgs` reads as numbers and flags where needed. A bare name in the query is `true`.
- `GET /api` lists the calls that are on, with their description, path, methods and the tool's input schema.
- Replies are JSON: `{"result": …}` with 200, where a tool's text is a string and anything else is serialized as it is; otherwise `{"error": "…"}` with 400 (body not an object), 401 (no key, unknown, off or expired, with `WWW-Authenticate: Bearer`), 403 (call off, or an `Origin` that is not loopback), 404, 405 (with `Allow`), 422 (`ToolFailure`) or 500 (the exceptions the MCP server reports as "That did not work").
- Each call is switched on or off on the page. Read calls start on and the rest start off; `Calls` in the settings holds only those switched away from how they start, so a new tool takes its default.
- REST calls do not go through the approval gate: the call's switch is the approval, because a script cannot answer a question in the panel. The page says so above the list, and marks Risky calls.
- Calls run on the UI thread, like the MCP server's, so they drive the same view models and the pages update. Authorization and routing run there too, so the keys and switches are read on the thread that changes them.
- The tools are made the first time a call or the Calls card needs them.
- Each call is logged under "REST API" with the key's name, the call and the status.

## Categories

- Each tool has a `Category`, the menu area of its `Tools/*Tools.cs`, set in `AgentTools.All`: App, Notes, Passwords, Pomodoro, MQTT, Alerts and Messages, Topic Explorer, MQTT Dashboards, Certificates, API Client, Containers, Errors and Settings. They are English in code, like the tools' descriptions, as the docs and API Client folders use them.
- The Calls card is a `TreeView`: a node per category with "n of m on" and a switch, which is on when every call listed is on, off when none is, and neither in between; setting it switches the calls listed. Below it, a node per call with its switch.
- **Filter** matches a call's name or description. Categories without a match are hidden, and the rest open. A category's switch and **Turn listed on/off** change the calls listed only.

## Swagger

- `GET /api/openapi.json` is an OpenAPI 3.0.3 document of the calls that are on: a tag per category, POST for every call with the tool's arguments as a JSON body, and GET for Read calls whose arguments are not objects, with them in the query. Security schemes are bearer and the `X-Api-Key` header. An empty `required` list is left out, as 3.0 does not allow one. Operation ids are the tool's name, with `_get` for GET.
- `GET /api/docs` is Swagger UI on that document, with tags listed and operations closed, a filter, and Try it out on. Its files are under `/api/docs/`, and only `swagger-ui-bundle.js`, `swagger-ui.css`, `LICENSE` and `NOTICE` are served from there.
- Both need no key, as a browser cannot send one to open a page; calls from them do. **Swagger docs** on the page, on by default, serves them; off, they are not there.
- Swagger UI 5.17.14 (Apache 2.0) is fetched from jsDelivr at build time into `.swagger-ui/`, which git ignores, its SHA-256 hashes checked against those in `Joktan.Agent.csproj`, and embedded in the assembly. The app downloads nothing, and the docs work offline. A build without it serves a page that links to the document, and the page says so.
- The docs page's own requests carry an `Origin` that names this server, which is let in. Other origins are still turned away.
- While the API answers this computer only, the `Host` must be a loopback name, which stops DNS rebinding.

## API Client

- **Add to API Client** writes every offered call, on or off, to `Joktan REST API/<category>/<name>.request.json` in the API Client's workspace, through `ApiClientViewModel.AddRequests`. A request already there is kept as it is, but gets the description if its own is empty.
- Each request's `Description`, Markdown shown on its **Docs** tab, is made by `ApiClientExport.Description`: the tool's description, how to send it (Params for a GET, Body for a POST), what it may change, the key variable, and each argument with its type, whether it is required, and its choices.
- Requests use `{{joktanUrl}}/<name>` and bearer `{{joktanKey}}`. Read calls that `OpenApi.TakesGet` are GET with every argument in the query, the optional ones switched off; others are POST with a JSON body of the required arguments, with empty text, 0, false, `{}` or the first choice.
- The **Joktan REST API** environment gets `joktanUrl`, set each time to `http://localhost:<port>/api`, and `joktanKey`, added empty and never overwritten. It is put in use only when none is. No key is written: Joktan does not have it, and environments are plain text.

## Keys

- A key is `jok_` and 64 hex characters from a cryptographic random generator. Only its SHA-256 is kept, with a hint of its first characters. It is shown once, with **Copy** and **Done**, and cannot be shown again.
- Keys are compared by hash in constant time. Sent as `Authorization: Bearer <key>` or `X-Api-Key: <key>`.
- Each key has a name, unique ignoring case, the date it was made, an optional last day, and a switch. Without a date it never expires. With one, it works to the end of that day in the computer's time zone, and is turned away from the next day. A date before today cannot be given.
- Turning a key off, or removing it, takes effect on the next request.

## Server

- Off by default. Switched on, it listens at once and again whenever the app starts. Port 47821 by default, from 1024 to 65535. A port in use is reported on the page.
- On loopback only, unless **Answer other computers** is on, when it listens on every address. It is plain HTTP, which the page says.
- A `TcpListener` with a small HTTP/1.1 reader rather than `HttpListener`, which on Windows needs an administrator to listen beyond `localhost`, as with the metrics endpoint. One request per connection; headers up to 16 KB (431), bodies up to 1 MB (413) and only with `Content-Length` (411 for chunked). `Expect: 100-continue` is answered. A request must be whole within 30 seconds.
- No CORS: any request with an `Origin` that is neither loopback nor this server is turned away, so another web page cannot use a key a browser might hold.

## Not covered

HTTPS, rate limiting, per-key choice of calls, a record of when each key was last used, and removing API Client requests for calls that no longer exist.

## Testing

`tests/Joktan.Agent.Tests/Rest/` covers keys, settings, routing and authorization without sockets, and the listener with real connections. `ViewModels/` and `Views/` cover the page, including a real call through the UI thread.

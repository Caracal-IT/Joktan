# API client

A developer tool for sending REST and gRPC requests and reading what comes back, in the manner of
Postman or Insomnia. Requests are files in a folder the user chooses, so they can live in a
project and in version control.

| Item      | Value                   |
|-----------|-------------------------|
| Menu path | Dev Tools / API Client  |
| Status    | Implemented             |

## Page layout

1. Page header: title "API Client", subtitle "Dev Tools / API Client", and on the right the
   environment in use, **Environments**, **Folder**, **New folder**, **New gRPC** and **New REST**.
2. While open, a band for the requests folder or for the environments.
3. On the left, **Requests**: the folders and requests as a tree, folders first, then requests,
   each by name, with the method (or gRPC) before a request's name, in the method's colour;
   **Reload** reads the folder again; the folder's path at the bottom.
4. On the right, for the selected request or folder: its name, the folder it is in,
   **Duplicate** (requests) and **Delete**. Below that, for a request, the request, and under it,
   the response. The line between the two can be dragged.

A new request or folder goes in the selected folder, or the folder of the selected request.
Typing a new name renames the file or folder on Enter or on leaving the box; choosing another
folder moves it. **Delete** deletes the file, or the folder and everything in it, once confirmed.
Changes to a request are written to its file half a second after the last one, before it is
sent, and when the app closes. The request open last is opened again next time.

## REST requests

| Part     | What it holds |
|----------|---------------|
| Address  | Method (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS) and URL; `http://` is added when no scheme is given |
| Params   | Query parameters, added to the URL's own, each switchable |
| Headers  | Request headers, each switchable; a content header such as Content-Type replaces the body's |
| Body     | None, JSON, Text, XML (sent with that content type) or Form (URL-encoded fields); **Format JSON** indents it |
| Auth     | None, Bearer token or Basic (username and password) |
| Save to variables | Environment variables to set from the response; see [Saving a response to variables](#saving-a-response-to-variables) |
| Last sent | What was last sent from this request; see [Resending](#resending) |
| Settings | Accept any TLS certificate, and how long to wait (100 seconds by default) |

Each method has its own colour, in the tree and in the method list: GET green, POST orange, PUT
blue, PATCH violet, DELETE red, HEAD grey and OPTIONS pink. gRPC requests are teal in the tree.

## gRPC requests

The address is the server, such as `https://localhost:5001`, or `http://localhost:5000` for plain
HTTP/2. **Load services** finds the services and methods, either:

- from the server, through gRPC server reflection (v1, else v1alpha), or
- from `.proto` files, looking up imports beside each file and in the import folders added.

Well-known types (Timestamp, Duration, wrappers, Struct and so on) are known without being sent
or found. The services found are kept while the app runs, so other requests to the same server
or files do not ask again. Before they are loaded, the service and method saved with the request
are shown and kept.

The message is JSON in the protobuf JSON form: fields by their JSON or proto name, enums by name
or number, 64-bit integers as numbers or strings, bytes as base64, well-known types in their own
forms. A client or two-way stream takes a JSON array of messages. **Example message** fills in
every field of the request message, one field of each oneof. **Metadata** are sent as headers;
names ending in `-bin` take base64 values. **Schema** is where the services are loaded from, and
**Settings** has accepting any TLS certificate and the deadline. All four kinds of method are supported. A
`google.protobuf.Any` is written with its type in `@type`, such as
`{"@type": "type.googleapis.com/demo.Named", "name": "a"}`, beside the fields of what it holds, or
beside `value` for a well-known type with its own form. The type must be among the services loaded;
**Example message** leaves an Any empty. An answer holding a type that is not loaded is shown as its
type URL and base64 bytes.

## Responses

The status (green for 2xx or gRPC OK, red otherwise, or what went wrong, such as a refused
connection), how long it took and the size of the body. **Body** shows JSON and XML indented,
cut short after 2 MB; a gRPC answer is shown as JSON, and a server or two-way stream as an array
of messages. **Headers** lists the response headers, and **Trailers** the gRPC trailers.
**Copy body** copies it. A request being sent can be cancelled.

## Resending

Each request remembers what was last sent from it, between runs. **Last sent** shows when, and
the method and address or the service and method, its parameters, headers or metadata, and its
body or message; tokens and passwords are not shown.

**Resend**, beside **Send** once something has been sent, sends that again exactly as it was,
even if the request has been edited since, and without changing what is being edited. Its
`{{variables}}` are remembered as written and filled in from the environment in use, so a token
that has been renewed is picked up. It also saves to variables as that request did. A request
that failed, such as on a refused connection, can be resent too; a gRPC request is only remembered
once its method has been found.

What was sent stays with a request that is renamed or moved, and is forgotten when it is deleted.
It is not copied when the requests folder changes to a new one.

## Environments

An environment is a named set of variables. `{{name}}` in an address, parameter, header, body,
token, username, password, message or metadata is replaced with the value from the environment
chosen in the page header. Names the environment does not define are sent as written, and listed
after sending. A new environment starts with `baseUrl` and `grpcAddress`, which new requests use.

## Saving a response to variables

**Save to variables**, on REST and gRPC requests, lists variables to set from the response, each
switchable, with where its value is in the body:

| Path | Value |
|------|-------|
| (empty) or `$` | The whole body |
| `token` or `$.token` | A field |
| `data.items[0].id` | A field further in; array positions count from 0 |
| `["odd.name"]` | A field whose name has dots or spaces |

A gRPC server or two-way stream's body is an array of messages, so its paths start with a
position, such as `[0].id`. Text is saved without its quotes, and numbers, true or false, objects
and arrays as their JSON.

Values are saved only from a successful response (2xx or gRPC OK), into the environment chosen in
the page header; a variable it lacks is added, and one switched off is switched on. The variables
saved are named above the response. Nothing changes for a value that cannot be found, is null, or
comes from a body cut short; these are listed after sending, as is having no environment chosen.

## Files

| File | Holds |
|------|-------|
| `<folder>/<name>.request.json` | One request, as indented JSON |
| `environments.json` | The environments and the one in use |

Folders and files whose names start with a dot are left out. Requests and environments are saved
as written, tokens and passwords included; secrets kept in an environment variable stay out of
shared request files. Values saved from responses, which may be tokens, are written to
`environments.json` too.

The requests folder is the `api` folder of Joktan's data folder unless another is chosen with
**Folder**: a folder that already holds requests is opened as it is; otherwise the requests open
now are copied into it, and the originals are left where they were. The choice is kept in
`api-location.json` in the data folder, and the request open last in `api-state.json`.

What each request last sent is in `api-last-sent.json` in the data folder, not beside the requests,
so sending does not change what is in version control. A token or password typed into a request
is in that copy as written, and stays there until the request is next sent or deleted.

## Not built yet

- Importing from Postman, Insomnia or cURL, and copying a request as cURL
- A history of sent requests
- Scripts and tests run on responses
- Sending files and multipart bodies

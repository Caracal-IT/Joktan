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
   each by name, with the method (or gRPC) before a request's name; **Reload** reads the folder
   again; the folder's path at the bottom.
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
| Settings | Accept any TLS certificate, and how long to wait (100 seconds by default) |

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
names ending in `-bin` take base64 values. All four kinds of method are supported. Messages of
type `google.protobuf.Any` cannot be sent yet.

## Responses

The status (green for 2xx or gRPC OK, red otherwise, or what went wrong, such as a refused
connection), how long it took and the size of the body. **Body** shows JSON and XML indented,
cut short after 2 MB; a gRPC answer is shown as JSON, and a server or two-way stream as an array
of messages. **Headers** lists the response headers, and **Trailers** the gRPC trailers.
**Copy body** copies it. A request being sent can be cancelled.

## Environments

An environment is a named set of variables. `{{name}}` in an address, parameter, header, body,
token, username, password, message or metadata is replaced with the value from the environment
chosen in the page header. Names the environment does not define are sent as written, and listed
after sending. A new environment starts with `baseUrl` and `grpcAddress`, which new requests use.

## Files

| File | Holds |
|------|-------|
| `<folder>/<name>.request.json` | One request, as indented JSON |
| `environments.json` | The environments and the one in use |

Folders and files whose names start with a dot are left out. Requests and environments are saved
as written, tokens and passwords included; secrets kept in an environment variable stay out of
shared request files.

The requests folder is the `api` folder of Joktan's data folder unless another is chosen with
**Folder**: a folder that already holds requests is opened as it is; otherwise the requests open
now are copied into it, and the originals are left where they were. The choice is kept in
`api-location.json` in the data folder, and the request open last in `api-state.json`.

## Not built yet

- Importing from Postman, Insomnia or cURL, and copying a request as cURL
- A history of sent requests
- Scripts and tests run on responses
- Sending files and multipart bodies

# API Client

Sends REST and [gRPC](help:api-client-grpc) requests and shows what comes back. Requests are files in a folder
you choose, so they can live in a project and in version control.

## Send your first request

1. Press **New REST**.
2. Choose the method, and type the address, such as `https://api.example.com/items`. `http://` is added when
   no scheme is given.
3. Press **Send**. The response appears below. Drag the line between the two to resize them.

There is nothing to save by hand: changes are written to the request's file half a second after you stop,
before it is sent, and when the app closes.

## Organising requests

- **New folder**, **New REST** and **New gRPC** add to the selected folder, or to the folder of the selected request.
- Type a new name to rename a request or folder, on Enter or on leaving the box. Choose another folder to move it.
- **Duplicate** copies a request. **Delete** deletes a request, or a folder with everything in it, once you confirm.
- **Reload** reads the folder again, after files were changed outside Joktan.

## Building a REST request

- **Params**: query parameters added to the address, each with a switch.
- **Headers**: request headers, each with a switch.
- **Body**: None, JSON, Text, XML or Form. **Format JSON** indents it.
- **Auth**: None, Bearer token, or Basic with a username and password.
- **Save to variables**: see [Environments and variables](help:api-client-variables).
- **Settings**: **Accept any TLS certificate**, for local servers with self-signed ones, and how long to wait
  in **Give up after**, which is 100 seconds by default.

## Reading the response

The status shows green for 2xx and red otherwise, or what went wrong, such as a refused connection, with how
long it took and the size of the body.

- **Body**: JSON and XML are indented. Bodies are cut short after 2 MB. **Copy body** copies it.
- **Headers**: the response headers.

A request being sent can be cancelled.

## Resending

**Last sent** shows what was last sent from a request, kept between runs, with tokens and passwords hidden.

**Resend**, beside **Send**, sends that again exactly as it was, even if the request has been edited since,
without changing what is being edited. Its variables are filled in from the environment in use, so a renewed token
is picked up.

## Choosing where requests are kept

Requests are kept in the `api` folder of the [data folder](help:settings-and-data) until you choose another.

1. Press **Folder**.
2. Type a path or use **Browse**. **Default** fills in Joktan's own folder.
3. Press **Use folder**.

A folder that already holds requests is opened as it is. Otherwise the requests open now are copied into it, and
the originals stay where they were.

Each request is a `<name>.request.json` file, and each folder a folder. The environments are in `environments.json`
at the top of the requests folder. What was last sent is kept in the data folder instead, so sending does not change
what is in version control.

**Requests and environments are saved as written, tokens and passwords included.** Put secrets in an
[environment variable](help:api-client-variables) to keep them out of shared request files, and keep
`environments.json` out of version control if it holds any.

# gRPC requests

Press **New gRPC** to add a request. All four kinds of method are supported: unary, server streaming, client
streaming and two-way.

## Sending one

1. Type the server's address: `https://localhost:5001`, or `http://localhost:5000` for plain HTTP/2.
2. Press **Load services**, which finds the services and methods. See **Schema** below.
3. Choose the **Service** and **Method**.
4. Press **Example message** to fill in every field of the request message, and change what you need.
5. Press **Send**.

## Schema

The **Schema** tab sets where the services are loaded from:

- **Ask the server** uses gRPC server reflection, which the server must have switched on.
- **Read .proto files** uses files you add with **Add .proto files**. Imports are looked for beside each
  file, and in folders you add with **Add import folder**.

Well-known types such as Timestamp and Duration are known without being found. The services found are kept
while the app runs, so other requests to the same server or files do not ask again.

## The message

The message is JSON, in the protobuf JSON form:

- Fields go by their JSON or proto name, and enums by name or number.
- 64-bit integers are numbers or strings, and bytes are base64.
- Well-known types take their own forms, such as text for a Timestamp.
- A client or two-way stream takes a JSON array of messages.

A `google.protobuf.Any` takes its type in `@type`, beside the fields of what it holds, and is left empty in **Example message**:

```json
{ "@type": "type.googleapis.com/demo.Named", "name": "a" }
```

A well-known type with its own form goes in `value`, such as `{ "@type": "type.googleapis.com/google.protobuf.Duration", "value": "1s" }`.
The type must be among the services loaded. With .proto files, add the file that defines it. An answer holding a type that is not loaded shows its type URL and base64 bytes.

## The other tabs

- **Metadata**: sent as headers, such as `authorization: Bearer {{token}}`. Names ending in `-bin` take base64 values.
- **Save to variables**: see [Environments and variables](help:api-client-variables). A stream's answer is an
  array of messages, so start the path with `[0]`.
- **Last sent**: see [Resending](help:api-client).
- **Settings**: **Accept any TLS certificate**, and the **Deadline**.

## The response

The answer is shown as JSON, and a server or two-way stream as an array of messages. **Headers** lists the response
headers, and **Trailers** the gRPC trailers. The status is green for OK.

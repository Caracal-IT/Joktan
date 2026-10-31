# Environments and variables

An environment is a named set of variables, such as one per server. Write `{{name}}` in a request, and it is
replaced with the value from the environment chosen at the top of the page.

## Setting them up

1. Press **Environments**, then **New environment**. It starts with `baseUrl` and `grpcAddress`, which new
   requests use.
2. Fill in their values, and press **Add variable** for any others.
3. Press **Close**, and choose the environment at the top of the page.

**Delete environment** deletes one.

## Using them

`{{name}}` works in an address, parameter, header, body, token, username, password, message or metadata. For
example: `{{baseUrl}}/items`, or a Bearer token of `{{token}}`.

A name the environment does not define is sent as written, and listed after sending.

## Saving a response to variables

**Save to variables** sets variables from a response, such as a login's token for the requests after it. Press
**Add variable** on the tab, then give each one a variable name and where its value is in the body:

| Path | Value |
| --- | --- |
| (empty) or `$` | The whole body |
| `token` or `$.token` | A field |
| `data.items[0].id` | A field further in; positions count from 0 |
| `["odd.name"]` | A field with dots or spaces in its name |

Text is saved without its quotes. Numbers, true or false, objects and arrays are saved as their JSON.

Values are saved only from a successful response (2xx or gRPC OK), into the environment chosen at the top. A
variable it lacks is added, and one switched off is switched on. The variables saved are named above the response.

Nothing changes for a value that cannot be found, is null, or comes from a body cut short. These are listed after
sending, as is having no environment chosen.

## Where they are kept

Environments are in `environments.json` at the top of the requests folder, values saved from responses included,
so that file may hold tokens. Keep it out of version control if it does.

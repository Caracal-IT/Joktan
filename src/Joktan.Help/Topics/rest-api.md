# REST API

The REST API lets your own scripts and programs do what the [agent](help:agent) can do in Joktan, over HTTP: list
notes, read a topic, publish an MQTT message, run a container and so on. Every call needs an API key, and each call
can be switched on or off. It is set up on the Settings / REST API page, and is off to begin with.

## Turning it on

Switch on **Answer REST calls**. Joktan listens on port 47821 unless you give another **Port**, and the page shows the
address, such as `http://localhost:47821/api`. It starts again by itself the next time Joktan opens.

**Answer other computers** is off to begin with, so only programs on this computer can call. Switched on, anyone on
your network who holds a key can. The calls go over plain HTTP, so keys and answers can be read on the way: use it on
networks you trust.

## Swagger docs

While the API is on, **Swagger docs** (on to begin with) serves Swagger UI at `/api/docs`, such as
`http://localhost:47821/api/docs`. Press **Open Swagger** to open it in your browser. It lists the calls that are on,
grouped by the part of the app they belong to, with their arguments and answers.

To try a call there, press **Authorize**, give an API key, and use **Try it out** on the call. The docs open without a
key, but every call made from them needs one. The same description is at `/api/openapi.json`, as OpenAPI 3, for other
tools such as code generators. Swagger UI is built into Joktan, so the docs work without the internet.

Switch **Swagger docs** off to stop serving both.

## API keys

Every call needs a key. Give the key a **Name**, saying who or what will use it, and press **Add key**.

- **Works until** is optional. Leave it empty for a key that never expires. A key works to the end of the day you
  give, and is turned away from the next day on. A key past its date is shown in red.
- The key is shown once, when it is made. Press **Copy** and keep it somewhere safe, then **Done**. Joktan keeps only
  a fingerprint of it, so it cannot be shown again. The list shows how each key starts, so you can tell them apart.
- The switch beside a key turns it off and on again. A key that is off is turned away at once.
- **Remove** deletes a key for good. Anyone still holding it is shut out.

Send the key in a header, either way:

```sh
curl -H "Authorization: Bearer jok_…" http://localhost:47821/api
curl -H "X-Api-Key: jok_…" http://localhost:47821/api
```

## Calls

Each call does what one of the agent's tools does, and has the same name. The **Calls** card shows them as a tree, in
categories that follow the menu, such as Notes, MQTT and Containers. Open a category to see its calls, each with a
switch. Calls that only look start on, and the rest start off.

- A category says how many of its calls are on, and its own switch turns them all on or off. It shows as neither
  when only some are on.
- **Filter** narrows the tree to the calls whose name or description holds what you type, and opens the categories
  they are in. A category's switch, **Turn listed on** and **Turn listed off** then change only the calls listed.

- `GET /api` lists the calls that are on, with the arguments each takes.
- `POST /api/<name>` makes a call, with a JSON object of its arguments as the body. An empty body is no arguments.
- A call that only looks also takes `GET`, with its arguments in the query, such as
  `GET /api/notes_list`, or `GET /api/notes_read?id=…` with an id from it.

```sh
curl -X POST -H "Authorization: Bearer jok_…" -H "Content-Type: application/json" \
     -d '{"client":"Local","topic":"test/hello","body":"hi"}' \
     http://localhost:47821/api/mqtt_publish
```

The answer is JSON. A call that worked gives `{"result": …}`. Otherwise there is `{"error": "…"}` saying why:

| Status | Meaning |
|---|---|
| 200 | It worked |
| 400 | The body is not a JSON object |
| 401 | No key, a key Joktan does not know, or one that is off or past its date |
| 403 | The call is off, or the request came from a web page |
| 404 | There is no call by that name |
| 405 | That call does not take GET |
| 422 | The call could not do what was asked, such as a note that does not exist |
| 500 | Something failed on the way, such as a file that could not be written |

## In the API Client

**Add to API Client**, below the calls, puts every call in a **Joktan REST API** folder on the
[API Client](help:api-client) page, in a folder for each category. A call that only looks is a GET with its
arguments in the query, the optional ones switched off; any other is a POST with a body of its required arguments to
fill in. Each opens on its **Docs** tab, which says what the call does, where its arguments go, whether it changes
anything, and what each argument is.

The requests reach Joktan through two variables of a **Joktan REST API** environment, which is put in use if you had
none: `joktanUrl`, the address, and `joktanKey`, which is empty. Paste a key into `joktanKey` there, once, and every
request uses it. Environments are kept as plain text in the requests folder, so keep a key there only if that folder
stays private.

Pressing it again adds any calls that are new, and keeps the requests already there as they are, with your changes;
one with nothing on its **Docs** tab is given the call's description.
It sets `joktanUrl` again, in case the port changed, and leaves `joktanKey` alone. A call that is off answers 403,
so switch it on here first.

## Good to know

- Calls that change something run straight away. Unlike the agent, nobody is asked first, so switch on only the
  calls you need, and the risky ones (marked **risky**) with care.
- Revealing a password is never a call, and the REST API cannot change its own settings or the agent's.
- Other web pages cannot use the REST API: a request from a browser page is turned away, unless it is the docs page
  served by Joktan itself.
- While it answers this computer only, a request must name it as `localhost` or `127.0.0.1`.
- Calls run on the same pages you see, so the pages update as they work.
- The settings are kept in `settings/rest-api.json`, with only fingerprints of the keys.

See also [Agent](help:agent) and [Settings and data](help:settings-and-data).

# MQTT Proxy

A broker on this computer that devices connect to instead of the real one. It forwards what they
publish to a target broker, and what the target sends for their subscriptions back to them, and
can intercept chosen messages on the way.

| Item      | Value                            |
|-----------|----------------------------------|
| Menu path | Dev Tools / Brokers / MQTT Proxy |
| Status    | Implemented                      |
| Help      | `mqtt-proxy`                     |

## Purpose

Test how a system behaves when a device sends something else, without changing the device:
change some fields of what it sends, send a fixed message on a schedule in its place, or move its
messages to another topic, while everything else passes through untouched.

## Page layout

1. Page header: title "MQTT Proxy", subtitle "Dev Tools / Brokers / MQTT Proxy", **Add proxy**.
2. With no proxies, a line saying what one does. Otherwise a tab per proxy, named after it, with a
   dot in the colour of its connection to the target (as a client's tab).
3. On each tab, the cards below.

### Proxy

| Control | Default | Meaning |
|---------|---------|---------|
| Name | `Proxy 1`, `Proxy 2`… | Unique, as for a client's name |
| Plain MQTT port | The first from 1885 no proxy uses | Empty for none |
| TLS port | Empty | Filled in, from 8885, when a server certificate is chosen; emptied by **None** |
| Mutual TLS port | Empty | Filled in, from 8886, when whose client certificates to accept is chosen; emptied by **None: no mutual TLS** |
| Server certificate | None | A server certificate from Dev Tools / Certificates, shown on both TLS ports |
| Client certificates signed by | None: no mutual TLS | The CAs that signed the server certificate, or any CA there |
| Devices sign in | With a login the broker accepts | How devices sign in to the proxy, apart from how the proxy signs in to the target, and can be changed while running. **Anyone can connect**: any login or none. **With the proxy's own login**: **Username for devices** and **Password for devices** (kept in the credential store under `Joktan MQTT`, account `proxy:<id>`, and forgotten when the proxy is removed), compared exactly, the password in fixed time; starting without both is refused. **With a login the broker accepts**: each device's username and password, or none, are tried at the target with a connection of their own (with the target's TLS settings) as the device connects, dropped at once. A device refused is told "bad username or password", and **Refused** is logged with the reason |
| Let devices on other computers connect | Off | A toggle switch. Off listens on the loopback addresses only; on, on every address, with no login asked for |
| **Start** / **Stop** | | Listens, connects to the target and starts the scheduled messages; stops them all |
| **Remove proxy** | | Stops it and removes it. Its folder is left as it is |

Ports are 1 to 65535, at least one, all different, and used by no other proxy; a free port is one
no proxy uses and that is not typed in the others. A typed port is never replaced. Starting with
a TLS port and no server certificate, a certificate whose files cannot be read, or a mutual TLS
port with no CA, is refused with the reason. The server certificate and its key are read from its
files when the proxy starts. On the mutual TLS port a device must show a certificate that chains
to the chosen CAs (an intermediate chosen must be in its chain), valid now; revocation is not
checked. TLS 1.2 and 1.3 are offered.

Below, where devices connect, and counts: devices connected, messages forwarded, modified, blocked,
and sent to devices. While running, the name, port, switch and target cannot be changed, and a
line says to stop it first. A port that cannot be listened on, such as one in use, shows why and
the proxy stays stopped.

### Forward to

The target's host and port, login and security, and **Edit broker**, which opens the client
editor (as **Edit client** on the MQTT page, including **From a broker on Dev Tools /
Containers**). A target linked to a broker there follows it as linked clients do, reconnecting if
running, and says which it follows; when that broker goes, it is unlinked and keeps its settings.
Passwords are kept in the credential store as for clients, so a target and a client of the same
broker share them; a target that follows a broker takes its password too, when it is added, when
the broker changes, and when the page is made. Then the connection's state, with the reason when it failed. A new proxy
forwards to the first broker port from Data / MQTT, or to `localhost:1883`.

### Intercepted messages

A folder of messages in the same form as a client's folder messages, chosen with **Browse** (which
switches every message off), read again with **Read again**. **New message** shows a form for a
new one (as on a client's Folder messages card). Then a row for each message: a switch, its topic,
its file, what intercepting it does, its schedule's progress while sent, and **Edit** for its
settings.

| The message has | Its topic is intercepted by |
|-----------------|-----------------------------|
| No schedule | Merging it into each message a device sends there: objects merge at every depth, anything else in the message replaces the device's, then its array and field rules are applied, with counters kept across sends. A payload that is not JSON is replaced by the message. A gzipped payload is unzipped first and zipped again; so is one whose message says gzip |
| A schedule | Sending it on its schedule, from when the proxy starts, and blocking what devices send there |

A topic matches a message's when they have as many levels and each is the same, or the message's
is a field. Invalid messages cannot be switched on. Which are switched on is kept with the proxy
by their path in the folder, not in their settings files, and the editor is given the file's own
settings, so a folder shared with a client is not changed.

A switched-on message has **Topic at the broker**: empty for its own, or another with no `+` or
`#`. Each `{field}` in it takes the level the same field of the message's own topic matched, or
else the value at that path in the payload sent (strings without quotes, anything else as JSON).
A field with neither stops the message, with a failure in the activity. What the target sends on
that topic is given to devices on the message's own, and a device's filter that can take in the
message's topic is also subscribed as that topic with its fields as `+` — both only when every
field of the message's own topic is in it.

### Activity

Newest first, at most 500 lines: listening and stopping, devices connecting and disconnecting,
subscribing and unsubscribing (with the extra filters at the target), forwarded, modified (with the
body, shown when selected), blocked, scheduled sends, and failures. **Clear** empties it.

## How it works

The proxy is an MQTTnet server for each of its ports, as one server has at most one TLS
endpoint. Devices are told apart by port and client ID, and the activity names the port for TLS
and mutual TLS devices, such as `sensor (TLS)`. Every device publish is taken by the proxy rather than delivered
locally, and published to the target through one MQTT connection of the proxy's own, with the
device's QoS and retain flag. Each device's subscriptions are subscribed at the target under its
own name, and dropped when it unsubscribes or disconnects; what arrives is injected into every
server, each of which delivers it to the devices whose subscriptions match. A device subscribed to the
topic it publishes on gets its message once, from the target.

Settings are kept in `mqtt/proxies.json` in the data folder: per proxy, its name, ports, certificates, switch,
folder, the switched-on messages and their topics at the broker, and the target, without passwords.

## Out of scope

- Devices' own logins, client IDs and last-will messages, which the target does not see.
- Agent tools for the proxy.

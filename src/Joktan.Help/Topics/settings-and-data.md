# Settings and data

Settings is a group in the menu. Its Menu page arranges the menu, its [Search](help:search) page chooses
what the search box looks through, its Themes, Colours, Fonts and
Text Sizes pages set how the app looks, its [Alerts and Messages](help:alerts-and-messages) page sets
up their MQTT server and how you appear in messages, its [Certificate Defaults](help:certificates) page sets what a
new certificate is filled in with, its [Container Settings](help:containers) page chooses the compose files, the engine
and where the Mosquitto brokers and PostgreSQL databases are written and the databases keep their data, its Telemetry page sets where logs go and how metrics are read, its [Folders and Backup](help:folders-and-backup) page shows
where everything is kept, and backs it up and restores it, its [Agent](help:agent) page chooses the command-line
client the agent panel runs, and its [REST API](help:rest-api) page lets your own programs call Joktan with an API key. Everything else is set up on the page it
belongs to, and is saved as it changes. Nothing here needs setting up before you start.

## The menu

On the Settings / Menu page, each entry of the menu has a switch and **Up** and **Down** buttons. Changes
apply at once and are remembered.

- **The switch** shows or hides the entry. Hiding a group hides everything in it; its entries keep
  their own switches for when the group comes back. A group with every entry hidden leaves the menu
  as well.
- **Up** and **Down** move the entry among those beside it, so a group's entries stay in that group.
- **Settings** and its **Menu** page cannot be hidden, so you can always get back here. They can be
  moved.

Hiding a feature only takes it off the menu. What it has running keeps running, such as an MQTT
client that is connected or a vault waiting to lock, and its data is kept. Its help stays available.

Joktan opens on the first page of the menu.

## Appearance

By default Joktan follows the light or dark setting of your operating system. To choose another look,
or make your own, see [Themes](help:themes).

## The data folder

Everything Joktan keeps goes in a `Joktan` folder in your documents, such as `~/Documents/Joktan`, with a folder for
each purpose.

To keep it somewhere else, change the **App settings** row on the
[Folders and Backup](help:folders-and-backup) page. Starting Joktan with the `JOKTAN_DATA_DIR` environment
variable set to another folder overrides that, which is useful for testing, or for running a second,
separate copy.

## What is in it

| File or folder | Holds |
| --- | --- |
| `mqtt/mqtt.json` | MQTT clients, Topic Explorer filters, and the alerts and messages server with your display name |
| `messaging/messaging-avatar.png` | Your picture for messages, if you chose one |
| `messaging/messaging-history.json` | The latest messages and alerts |
| `dashboards/dashboards.json` | MQTT dashboards, their graphs, format and tab colours, unless another folder was chosen |
| `settings/dashboards-location.json` | The dashboards folder that was chosen |
| `settings/backup.json` | Where backups go, how many are kept, and what they hold |
| `data-location.json` | The folder chosen for all of this. It stays in the default folder |
| `settings/menu.json` | The order of the menu, and what it shows |
| `settings/search.json` | What the search box looks through |
| `settings/themes.json` | The theme in use, and the themes you saved |
| `certificates/mqtt-certificates/` | Certificates copied for MQTT clients |
| `certificates/certificates.json` | The list of certificates, and their defaults |
| `certificates/certificates/` | Certificates, unless another folder was chosen |
| `notes/` | Notes, unless another folder was chosen |
| `settings/notes-location.json` | The notes folder that was chosen |
| `passwords/passwords.vault` | The default vault file |
| `passwords/passwords.json` | Entries without their passwords, with the keychain |
| `passwords/passwords-settings.json` | Where the passwords are, and the lock time |
| `pomodoro/pomodoro.json` | The Pomodoro settings, and how many focus periods were finished today |
| `workflows/definitions/` | Workflows, a `.workflow.json` file each, unless another folder was chosen |
| `workflows/runs/` | Runs of workflows: long ones as they go, and every one when it finishes |
| `workflows/go/` | Go scripts, built once each |
| `settings/workflows.json` | Settings / Workflow Settings, and the workflows folder chosen |
| `mqtt/recordings/` | Recorder recordings, unless another folder was chosen |
| `settings/recordings-location.json` | The recordings folder that was chosen |
| `api/requests/` | API requests, unless another folder was chosen |
| `settings/api-location.json` | The requests folder that was chosen |
| `api/api-state.json` | The request open last |
| `api/api-last-sent.json` | What each request last sent |
| `agent/agent.json` | The agent's client, speech settings and prompts, and where its panel was left |
| `settings/rest-api.json` | Whether the REST API is on, its port, which calls are on, and its keys, as fingerprints only |
| `settings/telemetry.json` | Where logs go, Elasticsearch, the collector and the metrics endpoint. API keys and passwords are in the credential store |
| `logs/` | Log files, unless another folder was chosen |
| `settings/containers.json` | Compose files, the engine, whether the logs are collapsed, and the Mosquitto brokers made on Data, without their passwords |
| `containers/mosquitto/` | The compose file Joktan writes for those brokers, with a folder for each holding its configuration, password hash and certificate |
| `settings/log-sources.json` | Where [Errors](help:errors) reads logs from, and where each app's code is. API keys and passwords are in the credential store |

Some things can be kept elsewhere, such as in a synced folder or in version control. See
[Notes](help:notes), [Passwords](help:passwords), [API Client](help:api-client),
[Certificates](help:certificates) and [MQTT Dashboards](help:topic-dashboards).

## Passwords and secrets

- MQTT broker passwords and key passwords are kept in the operating system's credential store: the
  macOS Keychain, Windows Credential Manager, or the Secret Service on Linux through `secret-tool`.
  They are never written to `mqtt/mqtt.json`. Where there is no credential store they are not saved, and
  have to be typed in again each run.
- Private keys made on the [Certificates](help:certificates) page are not encrypted.
- API requests and environments are saved as written, tokens and passwords included.

## Telemetry and logs

On **Settings / Telemetry**, logs can go to any of these, or all of them. Each log names the app, `joktan`, and its
module, such as `mqtt`. Passwords, topics and message bodies are never logged.

- **File**: a file for each day in `logs/` in the data folder, or a folder you choose. Old files are deleted after the
  number of days you set.
- **Elasticsearch**: straight from Joktan, to one on this computer or in the cloud. Sign in with an API key or a username
  and password, which are kept in the system's credential store. **Test connection** tries it.
- **Collector**: over OTLP to an OpenTelemetry collector. Metrics and traces always go there.

The collector's address is `http://localhost:4317` to begin with, used from the next start. It wins over
`OTEL_EXPORTER_OTLP_ENDPOINT`, which an IDE such as Rider may set for what it runs. Leave it empty to use that variable.

To read these logs back, and other apps', look for errors on [Errors](help:errors); where it reads from is set on
**Settings / Log Sources**.

**Metrics endpoint**: when turned on, Joktan also answers at `/metrics` for Prometheus to scrape directly, on this computer
only unless you allow others. It has no login.

## Telemetry

Joktan exports metrics, traces and logs using OpenTelemetry, by default to a collector at
`http://localhost:4317`. If nothing is listening there, the data is dropped and the app runs as
normal. Topics, message bodies and the like are never included. The machine's name and operating system are.

Without an address on Settings / Telemetry, it is set with the standard environment variables, when starting Joktan:

- `OTEL_EXPORTER_OTLP_ENDPOINT`: where to send, such as `http://localhost:4318`.
- `OTEL_EXPORTER_OTLP_PROTOCOL`: `grpc`, or `http/protobuf`.
- `OTEL_SDK_DISABLED=true`: turns telemetry off.

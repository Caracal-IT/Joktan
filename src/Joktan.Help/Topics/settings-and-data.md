# Settings and data

The Settings page on the menu is a placeholder. Each page keeps its own settings, on the page
itself, and saves them as they change. Nothing here needs setting up before you start.

## Appearance

Joktan follows the light or dark setting of your operating system. There is no switch for it in the
app.

## The data folder

Everything Joktan keeps goes in a `Joktan` folder under your application data folder:

- Windows: `%APPDATA%\Joktan`
- macOS: `~/Library/Application Support/Joktan`
- Linux: `~/.config/Joktan`

To keep it somewhere else, start Joktan with the `JOKTAN_DATA_DIR` environment variable set to
another folder. This is useful for testing, or for running a second, separate copy.

## What is in it

| File or folder | Holds |
| --- | --- |
| `mqtt.json` | MQTT clients and Topic Explorer filters |
| `dashboards.json` | Topic dashboards, their graphs and format |
| `mqtt-certificates/` | Certificates copied for MQTT clients |
| `certificates.json` | The list of certificates, and their defaults |
| `certificates/` | Certificates, unless another folder was chosen |
| `notes/` | Notes, unless another folder was chosen |
| `notes-location.json` | The notes folder that was chosen |
| `passwords.vault` | The default vault file |
| `passwords.json` | Entries without their passwords, with the keychain |
| `passwords-settings.json` | Where the passwords are, and the lock time |
| `api/` | API requests, unless another folder was chosen |
| `api-location.json` | The requests folder that was chosen |
| `api-state.json` | The request open last |
| `api-last-sent.json` | What each request last sent |
| `containers.json` | Compose files and the engine |

Some things can be kept elsewhere, such as in a synced folder or in version control. See
[Notes](help:notes), [Passwords](help:passwords), [API Client](help:api-client) and
[Certificates](help:certificates).

## Passwords and secrets

- MQTT broker passwords and key passwords are kept in the operating system's credential store: the
  macOS Keychain, Windows Credential Manager, or the Secret Service on Linux through `secret-tool`.
  They are never written to `mqtt.json`. Where there is no credential store they are not saved, and
  have to be typed in again each run.
- Private keys made on the [Certificates](help:certificates) page are not encrypted.
- API requests and environments are saved as written, tokens and passwords included.

## Telemetry

Joktan exports metrics and traces using OpenTelemetry, by default to a collector at
`http://localhost:4317`. If nothing is listening there, the data is dropped and the app runs as
normal. Topics, message bodies and the like are never included.

It is set with the standard environment variables, when starting Joktan:

- `OTEL_EXPORTER_OTLP_ENDPOINT`: where to send, such as `http://localhost:4318`.
- `OTEL_EXPORTER_OTLP_PROTOCOL`: `grpc`, or `http/protobuf`.
- `OTEL_SDK_DISABLED=true`: turns telemetry off.

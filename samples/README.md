# Samples

## mqtt-messages

A folder of messages for Dev Tools / Connectivity / MQTT. Choose `samples/mqtt-messages` with
**Choose folder** on that page.

| File                                     | Topic                   | Schedule                          | Varying fields                          |
|------------------------------------------|-------------------------|-----------------------------------|-----------------------------------------|
| `plant/line1/telemetry/temperature.json` | `plant/line1/telemetry` | Every 5 seconds                   | Unix time, GUID, counter, random number |
| `plant/line1/telemetry/pressure.json`    | `plant/line1/telemetry` | Daily at 08:00 and 12:30, Mon–Fri | ISO timestamp                           |
| `plant/line1/status/online.json`         | `plant/line1/status`    | Manual                            | None                                    |

The `.send.json` file beside a message holds its schedule and varying fields. The format is
described in [the MQTT spec](../docs/features/dev-tools/connectivity/mqtt/spec.md).

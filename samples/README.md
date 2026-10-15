# Samples

## mqtt-messages

A folder of demo messages for Dev Tools / Connectivity / MQTT. Choose `samples/mqtt-messages`
with **Choose folder** on that page and connect. Every message is switched off to begin with:
turn on a message, or a whole group, with the switch in the **On** column. Open the Topic
Explorer to watch them arrive.

Between them the messages use every kind of schedule and every kind of varying field. Line 1 and
Line 2 are sub-groups of Plant, and Alarms is a sub-group of Line 1. Two messages change their
topic on every send: the fleet heartbeat sets its own topic in its settings and cycles through
three devices, and the alarm adds a severity level below its folder.

| Group               | File                                     | Topic                           | Schedule                          | Varying fields                                       | Notes                                                        |
|---------------------|------------------------------------------|---------------------------------|-----------------------------------|------------------------------------------------------|--------------------------------------------------------------|
| Plant/Line 1        | `plant/line1/telemetry/temperature.json` | `plant/line1/telemetry`         | Every 5 seconds                   | Unix seconds, GUID, counter, random number           | QoS 1                                                        |
| Plant/Line 1        | `plant/line1/telemetry/pressure.json`    | `plant/line1/telemetry`         | Daily at 08:00 and 12:30, Mon–Fri | ISO timestamp                                        |                                                              |
| Plant/Line 1        | `plant/line1/status/online.json`         | `plant/line1/status`            | Manual                            | None                                                 | Sent as written                                              |
| Plant/Line 1/Alarms | `plant/line1/alarms/alarm.json`          | `plant/line1/alarms/{severity}` | Every 20 seconds                  | GUID, ISO timestamp, one of a list                   | Topic field: severity picked at random                       |
| Plant/Line 2        | `plant/line2/telemetry/vibration.json`   | `plant/line2/telemetry`         | Every 2 seconds                   | Unix milliseconds, one of a list, two random numbers | The fastest message                                          |
| Plant/Line 2        | `plant/line2/telemetry/production.json`  | `plant/line2/telemetry`         | Every 10 seconds                  | ISO timestamp, GUID, counter, random integer         | Counter starts at 100, step 25                               |
| Plant/Line 2        | `plant/line2/status/state.json`          | `plant/line2/status`            | Every 15 seconds                  | One of a list, Unix seconds                          | Retained                                                     |
| Building            | `building/hvac/zone1/climate.json`       | `building/hvac/zone1`           | Every 30 seconds                  | ISO timestamps and random values inside an array     | Paths such as `readings[0].value`                            |
| Building            | `building/energy/meter.json`             | `building/energy`               | Every minute, 10 times            | Unix seconds, two counters                           | Finishes after 10 sends                                      |
| Devices             | `devices/sensor-01/heartbeat.json`       | `devices/sensor-01`             | Every 5 seconds                   | Unix seconds, counter, random integer                |                                                              |
| Devices             | `devices/sensor-01/config.json`          | `devices/sensor-01`             | Manual                            | None                                                 | Retained, QoS 1                                              |
| Devices/Fleet       | `devices/fleet/heartbeat.json`           | `devices/{device}/heartbeat`    | Every 2 seconds                   | Unix seconds, two random integers                    | Topic set in settings; cycles through sensor-02 to sensor-04 |
| Alerts              | `alerts/maintenance/reminder.json`       | `alerts/maintenance`            | Daily at 09:00, every day         | GUID, ISO timestamp                                  |                                                              |
| Alerts              | `alerts/maintenance/shutdown.json`       | `alerts/maintenance`            | Once, at 2027-01-04 06:00         | GUID                                                 | QoS 2; shows "Missed" after that date                        |

The `.send.json` file beside a message holds its topic, group, schedule and varying fields. The format is
described in [the MQTT spec](../docs/features/dev-tools/connectivity/mqtt/spec.md).

# Message settings

**Edit**, on a row under [Folder messages](help:mqtt-folder-messages), opens a message's settings.
**Preview** shows the topic and body as they would be sent now. **Save** writes them to a file beside the
message: `temperature.send.json` for `temperature.json`.

## Delivery

- **Topic**: leave it empty to send to the folder path. See below.
- **Group**: the group it is listed under. `Plant/Line 1` makes Line 1 a sub-group of Plant.
- **Switched on**: the same as the switch in the list.
- **QoS**: 0, 1 or 2.
- **Retain**: whether the broker keeps the message for new subscribers.
- **Gzip**: compresses the body before it is sent, after varying fields are applied.

## Topic and topic fields

A topic set here replaces the folder path.

- `{folder}` stands for the folder path, so `{folder}/heartbeat` adds a level below it.
- Any other `{name}` is a **topic field**, a part that gets a new value on every send. Press **Add topic
  field**, and give it the same name.

For example, a topic of `{folder}/{device}/heartbeat`, with a field called `device` of type **Each in
turn** and the values `sensor-01, sensor-02`, sends to `sensor-01` and then `sensor-02`, over and over.

Topic fields take the same types as [varying fields](help:mqtt-varying-fields). Every `{name}` needs a
field and every field must be used, or the message is invalid. A send whose topic would contain `+` or
`#` is not made, and is logged as failed.

## Schedule

Under **When to send**:

- **Manual: only when I press Send now.** The message cannot be switched on.
- **Once: at a date and time.** Sent one time. If the time has already passed when it starts, it shows
  "Missed".
- **Recurring: at a fixed interval.** **Every** *N* seconds, minutes or hours; 1 second is the shortest.
  **Start**, **End** and **Maximum sends** are optional. At the end it shows "Finished".
- **Recurring: daily at set times.** Times of day separated by commas, such as `08:00, 12:30`, and the
  days of the week.

Dates and times are in the computer's local time, written like `2026-11-02 08:30:00`.

A schedule starts when the message is switched on while connected, or when its client connects while it
is on. Its sent count and any counters start again then. If the connection drops and comes back by
itself, the schedule carries on where it was.

## Array sizes

Send an array with a set number of items. Press **Add array** and pick one from the body.

- **Items** is how many to send. Add **Up to** for a random size on every send.
- The items in the file are the templates, repeated in turn to fill the array. A smaller size keeps the
  first items, and 0 sends an empty array.
- The file must have at least one item to repeat. The most is 10,000.

Array sizes are applied before [varying fields](help:mqtt-varying-fields), so `[*]` reaches the added
items too.

## Varying fields

Fields of the body that get a new value on every send. See [Varying fields](help:mqtt-varying-fields).

## The settings file

The file can be edited by hand as well, and the list picks up the change. If it is not valid, the
message shows why, and **Edit** starts from the defaults.

```json
{
  "enabled": true,
  "group": "Plant/Line 1",
  "qos": 1,
  "retain": false,
  "schedule": { "type": "interval", "every": 5, "unit": "seconds" },
  "fields": {
    "ts": { "type": "unixSeconds" },
    "sensor.value": { "type": "randomNumber", "min": 18, "max": 26, "decimals": 1 }
  }
}
```

The other schedules are written like this:

```json
{ "type": "manual" }
{ "type": "once", "at": "2026-11-02T08:30:00" }
{ "type": "interval", "every": 5, "unit": "seconds",
  "start": "2026-11-02T08:00:00", "end": "2026-11-02T17:00:00",
  "maxSends": 1000 }
{ "type": "daily", "times": ["08:00", "12:30"], "days": ["mon", "tue", "wed", "thu", "fri"] }
```

Topics and array sizes are written like this:

```json
"topic": "{folder}/{device}/heartbeat",
"topicFields": {
  "device": { "type": "cycle", "values": ["sensor-01", "sensor-02"] }
},
"arrays": {
  "readings": { "min": 3, "max": 6 },
  "lines[*].points": { "count": 10 }
}
```

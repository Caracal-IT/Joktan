# Folder messages

Point a client at a folder of JSON files, and each file becomes a message. Press **Choose folder** on
the **Folder messages** card. Each client has its own folder, remembered between runs. The list updates
when files change; **Rescan** reads the folder again on demand.

## How files become messages

Every `.json` file below the folder is one message, except settings files ending in `.send.json`.

- The **body** is the content of the file.
- The **topic** is the path of the folder that holds the file. The file name is not part of it.
- Several files in one folder are several messages on the same topic.

```
mqtt-samples/
├── plant/
│   └── line1/
│       ├── telemetry/
│       │   ├── temperature.json        topic: plant/line1/telemetry
│       │   └── temperature.send.json   settings for temperature.json
│       └── status/
│           └── online.json             topic: plant/line1/status
└── readme.json                         no topic: it is in no folder
```

The topic can be set to something else in the [message's settings](help:mqtt-message-settings).

A message is listed but never sent if it has no topic, if its topic is not valid, if the file is not
valid JSON or repeats a key in an object, or if its settings are not valid. The reason is shown in its row.

## Sending messages

- **Send now** publishes a message once, whether it is switched on or off.
- **The switch** in the **On** column sends it on its schedule. There is no separate start button: a
  message is sent while it is switched on **and** its client is connected.
- **Edit** opens its [settings](help:mqtt-message-settings), where the schedule is set.

Every message starts switched off, and one with no schedule cannot be switched on: its switch stays
grey until you give it one with **Edit**.

Switches are saved, so a message left on is sent again as soon as its client connects, even after a
restart. **Switch a group off before connecting to a broker you do not want to send to.**

## The list

- **Topic**: where it goes, with any `{name}` parts as written.
- **Schedule**: a summary, such as "Every 5 seconds".
- **Next send** and **Last sent**: when.
- **Sent**: how many times since it was switched on or its client connected.
- **State**: see below.

| State | Meaning |
| --- | --- |
| No schedule | Sent only with **Send now** |
| Off | It has a schedule but is switched off |
| Waiting for connection | Switched on; starts when the client connects |
| Scheduled | Being sent; **Next send** shows when |
| Finished | Reached its end time or maximum sends |
| Missed | Its one send time passed before it could be sent |
| Invalid | It cannot be sent; the reason follows |

A send that falls due while the message is off, the client is disconnected or the app is closed is
skipped, never sent late. To start a Finished message again, switch it off and on.

## Groups

Groups keep related messages together and switch them as one. Set a message's group in **Edit**, by
typing a new name or picking one in use.

- Write sub-groups with `/`: `Plant/Line 1` is "Line 1" inside "Plant".
- The switch in a group's header turns on or off every message with a schedule in it and its sub-groups.
- Click a group's name to collapse it. Its messages carry on being sent.
- A group exists while a message names it, so there is nothing to create or delete.

## Sharing a folder

Settings live beside the messages, so a folder can be copied or kept in version control complete with
its schedules. Two clients can use the same folder, and each then sends every message in it. A message's
switch is in its settings file, so switching it on one tab does the same on the other.

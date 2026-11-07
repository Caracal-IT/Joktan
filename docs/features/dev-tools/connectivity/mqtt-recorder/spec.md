# Recorder

Records what arrives on an MQTT broker, with the time between messages, and plays it back later
through any MQTT client, faster or slower, with fields of the payloads changed as on folder messages.

| Item      | Value                          |
|-----------|--------------------------------|
| Menu path | Dev Tools / Brokers / Recorder |
| Status    | Implemented                    |
| Help      | `mqtt-recorder`                |

## Purpose

Capture real traffic once, from a device or a whole plant, and send it again as often as needed to
test what consumes it, without the devices. Playback can be sped up to test load or slowed down to
watch closely, and timestamps or IDs can be made fresh on every send.

## Page layout

1. Page header: title "Recorder", subtitle "Dev Tools / Brokers / Recorder", and on the right **Folder**
   and **Open recording**. **Folder** opens a band to choose the recordings folder: path, **Browse**,
   **Default**, **Cancel** and **Use folder**.
2. **Record** card: **Client**, **Topics** (filters separated by commas, `#` at first) and one button,
   **Record**, which becomes **Stop recording**. Below it a status line: a hint, then the time and count
   while recording, then what was saved.
3. **Recordings** card: every recording, newest first, with its length, messages and topics.
4. For the selected recording:
   - **Play** card: **Name**, **Send through**, **Play**/**Pause**/**Resume**, **Stop**, **Speed** as
     **−**, the speed, **+**, and **Loop**; a progress bar and "0:34.2 / 2:00.0 · 412 of 1,203 sent".
     **Delete** asks once more inline.
   - **Vary payloads** card: varying fields, edited as in message settings.
   - **Messages** card: when each message came after the start, its topic and the start of its payload.

## Behaviour

| Rule | Detail |
|------|--------|
| Record and Play connect the client | When it is not connected; a failure is shown with the reason |
| Subscriptions | The recorder subscribes as its own owner, `recorder`, so it leaves those of other pages alone |
| What is kept | The payload exactly as it arrived (gzipped if it was), topic, QoS, retain flag, and time from the start |
| Limit | 100,000 messages per recording; the status says when it is full |
| Nothing arrived | Nothing is saved |
| Timing | Gaps between messages are kept, divided by the speed. A change of speed or a pause takes effect within 50 ms |
| Speeds | 0.1×, 0.25×, 0.5×, 1×, 2×, 5×, 10×, 25×, 50×, 100×. Clicking the speed goes back to 1× |
| Loop | Waits out the time from the last message to the end of the recording, then starts again. A loop lasts at least 100 ms |
| Stop | Stops; the next Play starts at the beginning |
| Varying fields | Applied to every JSON payload that has the field; others are sent byte for byte as recorded. Gzipped payloads are unzipped, changed and zipped again. Counters start again with each Play |
| Saving | Name and fields are saved when played, when another recording is selected, and when the app closes |
| Closing the app | A recording under way is stopped and saved; playback stops |
| Choosing a folder | A folder that holds recordings is opened as it is; otherwise the recordings are copied there and the originals stay. Playback stops and the list is read again. The choice is kept in `settings/recordings-location.json`, and is also a row on Settings / Folders and Backup (`mqtt-recordings`), backed up on its own and left out of the app settings |
| Open recording | Reads a recording file from anywhere and copies it into the folder under an unused name, then lists and selects it. A file in the folder, or the same as one there, is only selected. One that is not a recording is refused with the reason |

## Storage

One file per recording, `<recordings folder>/<yyyy-MM-dd-HHmmss>.json`; the folder is `<data folder>/mqtt/recordings`
unless another was chosen:

```json
{
  "name": "local 2026-11-07 09:30",
  "created": "2026-11-07T09:30:12",
  "client": "local",
  "filters": "plant/#",
  "length": 123.456,
  "fields": { "ts": { "type": "unixSeconds" } },
  "messages": [
    { "at": 0.012, "topic": "plant/a", "qos": 0, "retain": false, "text": "{\"ts\":1}" },
    { "at": 0.5, "topic": "plant/b", "qos": 1, "retain": false, "base64": "H4sIAAAA..." }
  ]
}
```

`fields` is written as in a message's settings file. A payload that is UTF-8 text and not gzipped is
written as `text`; anything else as `base64`. A file that cannot be read is left out of the list and logged.

## Code

| Part | Where |
|------|-------|
| Recording file, varying a payload | `src/Joktan.Mqtt/Recording.cs` |
| Recording from a connection | `src/Joktan.Mqtt/MessageRecorder.cs` |
| Playback timing | `src/Joktan.Mqtt/RecordingPlayer.cs` |
| Page | `src/Joktan.Mqtt/ViewModels/RecorderViewModel.cs`, `RecordingViewModel.cs`, `Views/RecorderView.axaml` |

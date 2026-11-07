# Recorder

Records what arrives on an MQTT broker, and sends it again later, at the pace it arrived or faster or
slower. Fields of the payloads can be changed as they are sent, as on [folder messages](help:mqtt-varying-fields),
so that a recording of yesterday goes out with today's timestamps.

It records and sends through the clients on the [MQTT](help:mqtt) page.

## Record

1. Choose a **Client**.
2. Leave **Topics** at `#` to record everything, or give as many topic filters as you need, such as
   `plant/+/telemetry` and `plant/alarms/#`. Press Enter or type a comma after each to add the next;
   **×** on a filter takes it away.
3. Press **Record**. The client connects by itself if it is not connected yet.
4. Press **Stop recording**. The recording is saved and selected, ready to play.

Retained values the broker holds are recorded as they arrive, right at the start. A recording keeps at most
100,000 messages; messages after that are not kept, and the status says so.

## Play

Select a recording under **Recordings**.

- **Send through** is the client it is sent through. It starts as the client it was recorded through.
- **Play** sends the messages with the same time between them as when they arrived. It connects the
  client first if need be. While playing it becomes **Pause**, and then **Resume**.
- **Stop** stops, and the next **Play** starts from the beginning.
- **Speed**: **−** and **+** go from 0.1× to 100×, and work while it plays. Click the speed itself to go
  back to 1×.
- **Loop** starts again from the beginning after the end, until **Stop**.

Every message is sent with the QoS and retain flag it arrived with.

## Vary payloads

**Add field** names a field of the JSON payloads, and how it gets a new value on every send. The types are
those of [varying fields](help:mqtt-varying-fields), such as Unix time or a counter, and the list offers the
fields that are in the recording.

- A message that has the field gets the new value. A message without it, or one that is not JSON, is sent
  exactly as recorded.
- Gzipped payloads are unzipped, changed and zipped again.
- Counters start again with each **Play**, and carry on across loops.

## Recordings

The name can be changed in **Name**. **Delete** asks once more, then removes the recording for good.

Each recording is one JSON file. Payloads that are text are kept as text, so the file can be read and edited by hand.

## Where recordings are kept

They are kept in the `mqtt/recordings` folder of the app's data folder until you choose another.

1. Press **Folder** at the top of the page.
2. Type a path or use **Browse**. **Default** fills in Joktan's own folder.
3. Press **Use folder**.

If the folder already holds recordings, they are opened as they are. If not, the recordings you have are copied
there, and the originals stay where they were. New recordings, and changes to names and fields, are saved in
the folder in use. The same can be done on Settings / [Folders and Backup](help:folders-and-backup).

## Play an old recording

Press **Open recording** at the top of the page and choose a recording file, kept anywhere, such as one copied
from another computer or from an older recordings folder. A copy is kept in the recordings folder, and it is
listed and selected, ready to play. Opening the same file again selects the copy already there.

## In its own window

**Open in own window** at the top of the page moves the recorder into a separate window, which can go on
another screen. Recording and playback keep going while it moves. **Back to main window**, or closing the
window, returns it to the page.

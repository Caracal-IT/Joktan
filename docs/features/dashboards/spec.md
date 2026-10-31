# Dashboards

Dashboards are the pages under **Dashboard** in the main menu. Each one gives an at-a-glance
view of one subject.

| Dashboard | Menu path | Status |
| --- | --- | --- |
| Overview | Dashboard / Overview | Implemented; specified below |
| Topic Dashboards | Dashboard / Topic Dashboards | Implemented; specified [below](#topic-dashboards) |
| Analytics | Dashboard / Analytics | Placeholder page, not specified yet |
| Reports | Dashboard / Reports | Placeholder page, not specified yet |

## Overview dashboard

![Overview dashboard](../../images/dashboard-dark.png)

### Purpose

Show the current state of the machine Joktan is running on: how busy the CPU is, how much memory
and disk are in use, and how long the machine has been up. It is the page shown when the app
starts.

### Behaviour

- Readings are taken every 2 seconds, starting when the app starts, whether or not the page is
  open. Returning to the page shows current values and the trend collected so far.
- CPU usage is measured between two consecutive readings, so it shows "–" for the first
  2 seconds.
- A reading that cannot be obtained on the current platform shows "–" (or "Not available" for a
  usage line). The rest of the page is unaffected.
- The page has no controls. Nothing on it can be clicked, filtered or configured.

### Layout

From top to bottom:

1. Page header: title "Overview", subtitle "Live metrics for this machine".
2. A row of four stat tiles of equal width.
3. Two cards side by side: "System" and "OpenTelemetry metrics".

The content scrolls vertically when it does not fit.

### Stat tiles

Each tile shows an icon, the current value, the tile's name, and one supporting line.

| Tile        | Value                                                            | Supporting line           | Meter | Trend |
|-------------|------------------------------------------------------------------|---------------------------|-------|-------|
| CPU usage   | Share of CPU time in use across all cores, as a whole percentage | "*N* logical processors"  | Yes   | Yes   |
| Memory used | Share of physical memory in use, as a whole percentage           | "*used* of *total*"       | Yes   | Yes   |
| Disk used   | Share of the system drive in use, as a whole percentage          | "*used* of *total*"       | Yes   | No    |
| Uptime      | Time since the machine started                                   | "Started *date and time*" | No    | No    |

**Meter.** A horizontal bar filled to the tile's percentage. Its colour reflects the level:

| Level   | Range            | Colour  |
|---------|------------------|---------|
| Normal  | Below 75%        | Accent  |
| Warning | 75% to below 90% | Warning |
| Danger  | 90% and above    | Danger  |

The percentage is always shown as text, so the level never depends on colour alone.

**Trend.** A sparkline along the bottom edge of the tile showing the last 30 readings (one
minute), on a fixed 0–100% scale with the newest reading at the right. It needs at least two
readings to draw, and fills from the right until a full minute has been collected.

### System card

Facts about the machine, read once when the app starts:

| Row                | Example          |
|--------------------|------------------|
| Machine name       | `workstation-01` |
| Operating system   | `macOS 27.0.1`   |
| Architecture       | `Arm64`          |
| Logical processors | `18`             |
| .NET runtime       | `10.0.12`        |

### OpenTelemetry metrics card

Lists every instrument of the `Joktan.Machine` meter with its latest value, and states the OTLP
endpoint the app exports to. The values come from the same readings as the tiles, so the card and
the exported data always agree.

| Instrument                      | Unit    | Shown as   |
|---------------------------------|---------|------------|
| `system.cpu.utilization`        | `1`     | Percentage |
| `system.cpu.logical.count`      | `{cpu}` | Count      |
| `system.memory.usage`           | `By`    | Size       |
| `system.memory.limit`           | `By`    | Size       |
| `system.memory.utilization`     | `1`     | Percentage |
| `system.filesystem.usage`       | `By`    | Size       |
| `system.filesystem.limit`       | `By`    | Size       |
| `system.filesystem.utilization` | `1`     | Percentage |
| `system.uptime`                 | `s`     | Seconds    |
| `process.memory.usage`          | `By`    | Size       |

Export settings are described in the [README](../../../README.md#telemetry).

### Formatting

| Kind                    | Rule                     | Example   |
|-------------------------|--------------------------|-----------|
| Percentage              | Whole number             | `68%`     |
| Size, 1 GB or more      | One decimal place, in GB | `32.7 GB` |
| Size, under 1 GB        | Whole number, in MB      | `165 MB`  |
| Uptime, 1 day or more   | Days and hours           | `9d 0h`   |
| Uptime, 1 hour to 1 day | Hours and minutes        | `5h 12m`  |
| Uptime, under 1 hour    | Minutes                  | `42m`     |
| Missing value           | En dash                  | `–`       |

Sizes use 1 GB = 1024 MB = 1024³ bytes. Numbers and dates follow the user's regional settings.

### Data sources

| Reading  | Windows                                                     | Linux           | macOS                             |
|----------|-------------------------------------------------------------|-----------------|-----------------------------------|
| CPU time | `GetSystemTimes`                                            | `/proc/stat`    | `host_statistics`                 |
| Memory   | `GlobalMemoryStatusEx`                                      | `/proc/meminfo` | `host_statistics64`, `hw.memsize` |
| Disk     | `DriveInfo` for the drive that holds the user's home folder | same            | same                              |
| Uptime   | `Environment.TickCount64`                                   | same            | same                              |

Definitions:

- **CPU usage** is 1 − (idle time ÷ total time) between two readings, across all cores. On Linux,
  time waiting for I/O counts as idle.
- **Memory used** is total minus available memory on Windows and Linux. On macOS it is app
  memory + wired + compressed, the figure Activity Monitor calls "Memory Used".
- **Disk used** is capacity minus the space available to the current user.

Only macOS has been run and checked against system tools. The Windows and Linux readers are
implemented but untested.

### Implementation

| Part          | File                                                                                                      |
| ------------- | --------------------------------------------------------------------------------------------------------- |
| Page          | `src/Joktan.Dashboard/Views/DashboardView.axaml`, `src/Joktan.Dashboard/ViewModels/DashboardViewModel.cs` |
| Stat tile     | `src/Joktan.Dashboard/ViewModels/MetricTileViewModel.cs`                                                  |
| Sparkline     | `src/Joktan.Core/Controls/Sparkline.cs`                                                                   |
| Sampling      | `src/Joktan.Dashboard/Telemetry/MachineMonitor.cs`, `src/Joktan.Dashboard/Telemetry/SystemReader.cs`      |
| Instruments   | `src/Joktan.Dashboard/Telemetry/MachineMetrics.cs`                                                        |

Styles and colour tokens are covered in [design.md](../../design.md#dashboard).

### Not included

- Per-core CPU, network, GPU, battery and temperature readings
- More than one drive
- Choosing the refresh interval or the length of the trend
- History beyond the last minute, or any history kept between runs
- Alerts or notifications when a level is reached

## Topic Dashboards

### Purpose

Keep an eye on the numbers on MQTT topics: subscribe to a topic, choose how to draw it, and put the
graph on a named dashboard, laid out as wanted. The [topic explorer](../dev-tools/connectivity/topic-explorer/spec.md)
shows everything on a broker for a moment; a dashboard shows chosen things for as long as the app runs.

### Dashboards

- The page has one tab per dashboard, and **Add dashboard** adds another. There is always at least one,
  called "Main" to begin with, and the last cannot be removed.
- Removing a dashboard asks first, and says how many graphs go with it.
- The bar at the top of a dashboard lists the clients its graphs use, each with its state, and
  **Connect** for one that is disconnected or failed. The connection is the client's own, so this
  connects it on the MQTT page too.

**Format** opens these, which apply at once:

| Setting               | Default | Rules                                                     |
|-----------------------|---------|-----------------------------------------------------------|
| Dashboard name        | -       | Shown on the tab; "Untitled" while empty; need not be unique |
| Columns               | 3       | 1 to 6. Fewer are used when a column would be under 180 wide |
| Row height            | 240     | 120 to 480, in steps of 20                                |
| Space between graphs  | 16      | 0 to 32, in steps of 4                                    |
| Background            | Theme's | One of the colours offered for a client's page, or Default |

### Graph editor

**Add graph** on a dashboard, **Edit** on a graph, and **Add to dashboard** in the topic explorer open the
same dialog. It subscribes to the topic for as long as it is open, lists the numbers that arrive, and
shows the graph as it will look.

| Field           | Default                  | Rules                                                 |
|-----------------|--------------------------|-------------------------------------------------------|
| Client          | The MQTT page's selected | A client on the MQTT page                             |
| Topic           | Empty                    | One exact topic: not empty, no `+` or `#`             |
| Name            | The topic's last level   | Required                                              |
| Type            | Line                     | Line, Area, Bars, Gauge or Number                     |
| Shows the last  | 5 minutes                | 1, 5, 15 or 30 minutes; 1, 6 or 24 hours              |
| Unit            | Empty                    | Shown after each value                                |
| Decimal places  | Automatic                | Automatic (up to three as needed), or 0 to 3          |
| Fixed scale     | Off                      | With a minimum below its maximum                      |
| Numbers to draw | The first to arrive      | 1 to 8, each with a name and one of 8 colours         |
| Dashboard       | The one on show          | Or a new one, named here. Not offered when editing    |
| Columns wide    | 1                        | 1 to 6; no wider than the dashboard's columns when drawn |
| Rows high       | 1                        | 1 to 6                                                |

- Numbers are found as in the topic explorer's [Choose values](../dev-tools/connectivity/topic-explorer/spec.md#selected-topic),
  and listed in the same order. A path can also be typed, for a topic with nothing arriving yet.
- From the topic explorer, the numbers it is drawing start ticked, and the messages in the topic's
  history, up to 100, start on the graph.
- Editing starts from a copy of the graph's readings, so **Cancel** changes nothing.
- Changing the client or topic clears what was received. Ticked numbers stay.
- What was received in the dialog stays on the graph when it is saved.

### Graph types

| Type   | Draws                                                                                |
|--------|--------------------------------------------------------------------------------------|
| Line   | A line for each number over the time shown, with a dot on the latest reading          |
| Area   | The same, filled down to zero. The scale includes zero                                |
| Bars   | One number: its recent readings, as many as fit, newest at the right. Several: the latest of each, named below. Bars start at zero |
| Gauge  | The latest value on a half dial for each number, with the scale's ends labelled      |
| Number | The latest value in large type for each number, with a trend line if the tile is over 90 high |

- All the numbers on a graph share one scale. There is never a second axis.
- Without a fixed scale, the scale is fitted to what is in view and rounded out to whole steps.
- A value outside a fixed scale is held at its edge on bars and gauges, and clipped on lines and areas.
- A graph with several numbers on Line or Area has a legend with each name and latest value. One number
  on Line, Area or Bars has its latest value beside the graph's name instead.
- Pointing at a line or area shows a crosshair and the nearest reading of each number, within 48 of the
  pointer. Pointing at a bar shows its value.
- The scale shortens numbers from a thousand up, as 1.2K, 3M and 4G, and leaves out the unit.
- A number's colour stays with it when others are ticked or cleared. Colours are `Chart1Brush` to
  `Chart8Brush`, and each new number takes the first one not in use.

### Layout

**Edit layout** adds to each graph a bar with **‹**, **›**, **Edit** and **Remove**, and a handle in its
bottom corner.

- Dragging the bar onto another graph puts it in that graph's position. The graph being dragged fades, and
  the place it would go is outlined. Nothing moves until the pointer is released.
- Dragging the handle resizes the graph to whole columns and rows, changing once past halfway. It goes no
  wider than the dashboard's columns, and no higher than 6 rows.
- Removing a graph asks first.
- Graphs are placed in order, each in the first free place it fits, so a gap can be filled by a later graph.

### Receiving

- Graphs subscribe to their exact topics through their clients when the app starts, whether or not the
  page or the topic explorer has been opened, and again after a reconnect.
- They subscribe separately from the topic explorer, so **Unsubscribe** there does not stop them. See
  [Subscriptions](../dev-tools/connectivity/topic-explorer/spec.md#subscriptions) there for how the two share
  a connection.
- Graphs are updated about seven times a second as messages arrive, and move along once a second.
- A number holding several values, for `[*]`, has them spread over the time since its last reading, at
  most 10 seconds, so a batch is drawn as a run of readings rather than one upright line.
- A retained message is a stored value and not a new reading, so it only counts when the number has no
  readings yet.
- Time is when the message arrived. A time inside the payload is not used.

### Data

Dashboards, their format and their graphs are saved to `dashboards.json` in the data folder, as they
change. Readings are not saved.

- A graph knows its client by name and follows it when it is renamed. If the client is removed, the graph
  stays and says so, and comes back if a client of that name is added.
- A `dashboards.json` that cannot be understood is copied to `dashboards.json.unreadable`, as saving would
  otherwise replace it.
- Each number keeps what is in the time shown, and at most 4,000 readings. Past that every other reading is
  dropped, so a fast topic loses detail rather than time. Spikes can be lost this way.

### Implementation

| Part                 | File                                                                                                   |
| -------------------- | ------------------------------------------------------------------------------------------------------ |
| Page, routing        | `src/Joktan.TopicDashboards/Views/TopicDashboardsView.axaml`, `src/Joktan.TopicDashboards/ViewModels/TopicDashboardsViewModel.cs` |
| One dashboard        | `src/Joktan.TopicDashboards/Views/DashboardTabView.axaml`, `src/Joktan.TopicDashboards/ViewModels/DashboardTabViewModel.cs` |
| Grid                 | `src/Joktan.TopicDashboards/Controls/DashboardGrid.cs`                                                 |
| One graph            | `src/Joktan.TopicDashboards/Views/GraphCardView.axaml`, `src/Joktan.TopicDashboards/ViewModels/GraphCardViewModel.cs` |
| Drawing              | `src/Joktan.TopicDashboards/Controls/GraphView.cs`                                                     |
| Editor               | `src/Joktan.TopicDashboards/Views/GraphEditorWindow.axaml`, `src/Joktan.TopicDashboards/ViewModels/GraphEditorViewModel.cs` |
| Readings             | `src/Joktan.TopicDashboards/GraphData.cs`, `src/Joktan.TopicDashboards/ViewModels/ClientFeed.cs`       |
| Saving               | `src/Joktan.TopicDashboards/DashboardStore.cs`                                                         |

Checked on macOS against Mosquitto 2 running under Podman.

### Not included

- One graph drawing several topics, or a topic filter with wildcards
- Text, true or false, and other values that are not numbers
- Keeping readings between runs, or exporting them
- Warning colours or alerts when a value passes a limit
- Placing graphs freely, with empty places left between them
- A time taken from inside the payload

### Decisions to confirm

These were not stated in the request and are the choices this spec makes:

1. **Dashboards are a new page under Dashboard**, and Overview stays as it is, since Overview shows this
   machine and has no MQTT client.
2. **A graph draws one exact topic**, with up to 8 of its numbers.
3. **Graphs start empty each run**, as the topic explorer does, rather than storing readings.
4. **Graphs subscribe when the app starts but do not connect the client**, so opening the app never
   connects to a broker by itself.

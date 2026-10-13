# Dashboards

Dashboards are the pages under **Dashboard** in the main menu. Each one gives an at-a-glance
view of one subject.

| Dashboard | Menu path | Status |
| --- | --- | --- |
| Overview | Dashboard / Overview | Implemented; specified below |
| Analytics | Dashboard / Analytics | Placeholder page, not specified yet |
| Reports | Dashboard / Reports | Placeholder page, not specified yet |

## Overview dashboard

![Overview dashboard](../../images/dashboard-light.png)

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

| Part        | File                                                            |
|-------------|-----------------------------------------------------------------|
| Page        | `Views/DashboardView.axaml`, `ViewModels/DashboardViewModel.cs` |
| Stat tile   | `ViewModels/MetricTileViewModel.cs`                             |
| Sparkline   | `Controls/Sparkline.cs`                                         |
| Sampling    | `Telemetry/MachineMonitor.cs`, `Telemetry/SystemReader.cs`      |
| Instruments | `Telemetry/MachineMetrics.cs`                                   |

Styles and colour tokens are covered in [design.md](../../design.md#dashboard).

### Not included

- Per-core CPU, network, GPU, battery and temperature readings
- More than one drive
- Choosing the refresh interval or the length of the trend
- History beyond the last minute, or any history kept between runs
- Alerts or notifications when a level is reached

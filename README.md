# Joktan

A cross-platform desktop admin application built with [Avalonia UI](https://avaloniaui.net/) and
.NET. It runs on Windows, Linux and macOS from one codebase.

![Joktan dashboard](docs/images/dashboard-light.png)

The project is at an early stage: the application shell and a dashboard of local machine metrics
are in place, and the other pages are placeholders.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Run

```sh
dotnet run
```

## Publish

Build a self-contained app for a given platform by passing its runtime identifier:

```sh
dotnet publish -c Release -r win-x64 --self-contained -o bin/publish/win-x64
dotnet publish -c Release -r linux-x64 --self-contained -o bin/publish/linux-x64
dotnet publish -c Release -r osx-arm64 --self-contained -o bin/publish/osx-arm64
```

Other identifiers such as `win-arm64`, `linux-arm64` and `osx-x64` work the same way.

## Telemetry

Joktan uses [OpenTelemetry](https://opentelemetry.io/) and exports metrics and traces over OTLP.
By default it sends to a collector at `http://localhost:4317` using gRPC. If nothing is listening
there, the app runs normally and the data is dropped.

The standard environment variables change where it goes:

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318 OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf dotnet run
```

Set `OTEL_SDK_DISABLED=true` to turn telemetry off.

The `Joktan.Machine` meter reports the local machine every two seconds. The same readings drive
the dashboard.

| Instrument | Unit | Meaning |
| --- | --- | --- |
| `system.cpu.utilization` | `1` | Share of CPU time in use across all cores |
| `system.cpu.logical.count` | `{cpu}` | Number of logical processors |
| `system.memory.usage` | `By` | Physical memory in use |
| `system.memory.limit` | `By` | Total physical memory |
| `system.memory.utilization` | `1` | Share of physical memory in use |
| `system.filesystem.usage` | `By` | Space in use on the system drive |
| `system.filesystem.limit` | `By` | Capacity of the system drive |
| `system.filesystem.utilization` | `1` | Share of the system drive in use |
| `system.uptime` | `s` | Time since the machine started |
| `process.memory.usage` | `By` | Physical memory used by Joktan |

The filesystem instruments carry a `system.filesystem.mountpoint` attribute. The built-in .NET
`System.Runtime` meter is exported as well, and each page navigation is recorded as a `navigate`
span from the `Joktan` activity source.

CPU and memory are read from the operating system directly: `GetSystemTimes` and
`GlobalMemoryStatusEx` on Windows, `/proc/stat` and `/proc/meminfo` on Linux, and
`host_statistics` on macOS.

## Project layout

| Path | Contents |
| --- | --- |
| `Program.cs`, `App.axaml` | Entry point and application resources |
| `Views/` | Windows and pages (`MainWindow` is the shell) |
| `ViewModels/` | View models, using CommunityToolkit.Mvvm |
| `Telemetry/` | Machine sampling, OpenTelemetry instruments and SDK setup |
| `Controls/` | Custom controls (`Sparkline`) |
| `Styles/Tokens.axaml` | Colours and sizes for light and dark variants |
| `Styles/Shell.axaml` | Styles for the shell and shared components |
| `Icons.cs` | Icon geometries |
| `ViewLocator.cs` | Maps a view model to its view by name |
| `docs/design.md` | Visual design: layout, colours, typography, components |
| `docs/features/` | One folder per feature, each with its specification |

## Documentation

- [docs/design.md](docs/design.md): layout, colours, typography and components.
- [docs/features/](docs/features/README.md): feature specifications, starting with
  [dashboards](docs/features/dashboards/spec.md).

## License

[MIT](LICENSE)

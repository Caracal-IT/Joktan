# Containers

A developer tool for starting, stopping and taking down a compose project, such as the local
MQTT broker in `containers/docker-compose.yml`, with Docker or Podman, and for watching its
containers' logs.

| Item      | Value                   |
|-----------|-------------------------|
| Menu path | Dev Tools / Containers  |
| Status    | Implemented             |

## Purpose

Let a developer run the services Joktan's other tools talk to without a terminal: start the
broker, see whether it is running and on which ports, read what it logs, and stop it again.

## Page layout

1. Page header: title "Containers", subtitle "Dev Tools / Containers", and **Start**, **Stop**,
   **Restart**, **Down** and **More** on the right, for every service in the file.
2. While a command runs, a progress bar, what it is doing and **Cancel**; below that, any error.
3. On the left, **Services**: one row per service in the file, with a dot in the colour of its
   state, its image, container, status and published ports, buttons for that service alone and
   a **⋯** menu with more. The rows are in the order the services are written in the file, and
   stay in that order as their state changes; containers of services no longer in the file come
   last, by name.
4. On the right, **Compose file**: the file and the engine.
5. Below, **Logs** and **Command output** tabs.

## Commands

| Button  | Runs                          | What it does                                                        |
|---------|-------------------------------|---------------------------------------------------------------------|
| Start   | `compose up -d [service]`     | Creates the containers if needed, building images where the file says so, and starts them |
| Stop    | `compose stop [service]`      | Stops the containers and keeps them                                 |
| Restart | `compose restart [service]`   | Stops and starts the containers without recreating them             |
| Down    | `compose down`                | Stops and removes the containers and their network; volumes are kept |

Only one command runs at a time; the buttons are disabled meanwhile. **Cancel** stops the command
and everything it started; containers it already changed stay as they are. A command that
fails shows its exit code as an error, and its output stays on **Command output**.

**More** has:

| Item                     | Runs                              | What it does |
|--------------------------|-----------------------------------|--------------|
| Recreate                 | `compose up -d --force-recreate`  | Makes new containers even when nothing changed, and starts them |
| Rebuild and start        | `compose up -d --build`           | Builds the images again, then recreates and starts the containers |
| Pull images              | `compose pull --ignore-buildable` | Fetches the latest images; images built from the file are skipped |
| Build images             | `compose build`                   | Builds the images without starting anything |
| Pause                    | `compose pause`                   | Freezes the running containers |
| Resume                   | `<engine> unpause <containers>`   | Lets the paused containers run again. Run by container name, because `compose unpause` finds nothing paused under Podman |
| Kill                     | `compose kill`                    | Stops the containers at once, without letting them shut down |
| Down and remove volumes… | `compose down --volumes`          | As Down, and deletes the volumes. A bar below the header asks first, since what they hold, such as the broker's development certificates, is lost |

A row's **Start** shows when it has no container or its container is stopped, **Resume** when it
is paused, and **Stop** and **Restart** when it is running. Its **⋯** menu has:

| Item                       | Shown or enabled              | What it does |
|----------------------------|-------------------------------|--------------|
| Show its logs              | Always                        | Picks the service on the **Logs** tab, shows that tab, and starts the logs if they are not shown |
| Open a shell in a terminal | Enabled while running         | Opens Terminal on macOS, a command window on Windows, or the first of `x-terminal-emulator`, `gnome-terminal`, `konsole` and `xterm` on Linux, running `<engine> exec -it <container> sh` |
| Copy the container name    | Enabled with a container      | Copies it, for use on the command line |
| Recreate, Rebuild and start, Pull image | Always           | As in **More**, for this service |
| Pause, Kill                | While running                 | As in **More**, for this service |
| Resume                     | While paused                  | As in **More**, for this service |
| Remove container           | With a container              | `compose rm --stop --force`: stops and removes it; **Start** makes it again |

Every command in the menus names the service, and all are disabled while a command runs.

## Services

The list is `compose config --services`, joined with `compose ps --all --format json`. It is
read when the page is shown, every 5 seconds while it stays shown, after each command, and on
**Refresh**.

| Dot   | Container                      | Status shown            |
|-------|--------------------------------|-------------------------|
| Green | Running                        | The engine's, such as "Up 5 minutes", with its health check's result when it has one |
| Amber | Paused                         | The engine's, such as "Up 5 minutes (Paused)" |
| Red   | Created but not running        | The engine's, such as "Exited (0) 2 minutes ago" |
| Grey  | None                           | Not created             |

Ports are the published ones, as `host→container`. Above the table, how many of the services
are running.

## Compose file and engine

| Setting | Default | Meaning |
|---------|---------|---------|
| File    | Empty   | The compose file. Empty for `containers/docker-compose.yml` in the first folder above Joktan's own, or above the current folder, that has one; that path is shown as the placeholder |
| Engine  | Auto    | Docker, Podman, or Auto: Docker when its engine answers `docker info`, then Podman when it answers `podman info`, then whichever is installed |

Both are saved in `containers.json` in Joktan's data folder as soon as they change. Changing
either stops the logs and reads the services again. Below the engine, which command is used.

Commands run from the compose file's folder, so a `.env` beside it is read, as on the command
line.

### Finding Docker and Podman

An app started from the Finder gets a short `PATH`. The commands are looked for on `PATH`, then
in `/opt/homebrew/bin`, `/usr/local/bin`, `/opt/podman/bin`, Docker Desktop's
`Contents/Resources/bin` and `~/.docker/bin`, and commands are run with that `PATH`, so that
`podman compose` finds its compose provider.

On macOS, `podman compose` looks for the Podman machine's API socket in `$TMPDIR/podman/`, but
the socket is wherever `TMPDIR` pointed when the machine was started. Joktan finds the socket
from the arguments of the machine's `gvproxy` process, or from `podman machine inspect`, and
runs `podman compose` with the `TMPDIR` that matches it.

Commands run with `COMPOSE_ANSI=never`, `NO_COLOR=1` and plain progress output, and with
Podman's banner naming the compose provider turned off. Any colour codes still written are
removed.

## Logs

**Show logs** runs `compose logs --follow --no-color --tail <n>`, and the button becomes
**Stop**.

| Control            | Default       | Meaning |
|--------------------|---------------|---------|
| Service            | All services  | One service's logs, without the container name before each line |
| Last _n_ lines     | 200           | How many earlier lines of each container's log to show first |
| Timestamps         | Off           | Adds `--timestamps` |
| Keep newest in view | On           | Scrolls to each new line |
| Copy               |               | Copies the lines shown |
| Clear              |               | Empties the list; the logs keep coming |

Changing the service or timestamps starts the logs again. At most 5,000 lines are kept; older
ones are dropped.

A command run from the page pauses the logs, since a container it recreates would not be
followed, and they continue afterwards from the moment they paused, with `--since`, so nothing
is shown twice. When the logs end on their own because no container is running (Docker does
this; Podman keeps waiting), a line says so, and they continue after the next command run from
the page.

The logs keep coming while another page is shown. Leaving the app stops them, and any command
still running.

## Command output

Each command's line, such as `$ podman compose up -d`, followed by what it wrote to either
stream. At most 1,000 lines are kept. **Copy** and **Clear** as for the logs.

## Out of scope

- Several compose files or projects at once.
- Containers not in the compose file.
- Running commands inside a container.

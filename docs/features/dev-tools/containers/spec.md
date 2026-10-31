# Containers

A developer tool for starting, stopping and taking down compose projects, such as the local
MQTT brokers in `containers/docker-compose.yml`, with Docker or Podman, and for watching their
containers' logs. Several compose files can be on the page, each its own project.

| Item      | Value                   |
|-----------|-------------------------|
| Menu path | Dev Tools / Containers  |
| Status    | Implemented             |

## Purpose

Let a developer run the services Joktan's other tools talk to without a terminal: start the
broker, see whether it is running and on which ports, read what it logs, and stop it again.

## Page layout

1. Page header: title "Containers", subtitle "Dev Tools / Containers".
2. While a command runs, a progress bar, what it is doing and on which file, and **Cancel**;
   below that, any error that belongs to no one file, such as finding the engine.
3. On the left, **Services**, with how many are running across all the files. A group for each
   file: a button to show or hide its services, the project's name, how many of its services are
   running, its path, and **Start**, **Stop**, **Restart**, **Down** and **More** for every
   service in the file; below that, the file's own errors. Then one row per service in the file,
   with a dot in the colour of its state, its image, container, status and published ports,
   buttons for that service alone and a **⋯** menu with more. The rows are in the order the
   services are written in the file, and stay in that order as their state changes; containers
   of services no longer in the file come last, by name. The groups scroll when they outgrow the
   space.
4. On the right, **Compose files**: the files, and the engine.
5. Below, **Logs** and **Command output** tabs, which collapse. See [Collapsing](#collapsing).

## Commands

| Button  | Runs                        | What it does                                                                              |
|---------|-----------------------------|-------------------------------------------------------------------------------------------|
| Start   | `compose up -d [service]`   | Creates the containers if needed, building images where the file says so, and starts them |
| Stop    | `compose stop [service]`    | Stops the containers and keeps them                                                       |
| Restart | `compose restart [service]` | Stops and starts the containers without recreating them                                   |
| Down    | `compose down`              | Stops and removes the containers and their network; volumes are kept                      |

Each command runs on its own file only. Only one command runs at a time across the page, so the
output stays readable; the buttons are disabled meanwhile. **Cancel** stops the command
and everything it started; containers it already changed stay as they are. A command that
fails shows its exit code as an error, and its output stays on **Command output**.

**More** has:

| Item                     | Runs                              | What it does                                                                                                                                      |
|--------------------------|-----------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------|
| Recreate                 | `compose up -d --force-recreate`  | Makes new containers even when nothing changed, and starts them                                                                                   |
| Rebuild and start        | `compose up -d --build`           | Builds the images again, then recreates and starts the containers                                                                                 |
| Pull images              | `compose pull --ignore-buildable` | Fetches the latest images; images built from the file are skipped                                                                                 |
| Build images             | `compose build`                   | Builds the images without starting anything                                                                                                       |
| Pause                    | `compose pause`                   | Freezes the running containers                                                                                                                    |
| Resume                   | `<engine> unpause <containers>`   | Lets the paused containers run again. Run by container name, because `compose unpause` finds nothing paused under Podman                          |
| Kill                     | `compose kill`                    | Stops the containers at once, without letting them shut down                                                                                      |
| Down and remove volumes… | `compose down --volumes`          | As Down, and deletes the volumes. A bar below the header asks first, since what they hold, such as the broker's development certificates, is lost |

A row's **Start** shows when it has no container or its container is stopped, **Resume** when it
is paused, and **Stop** and **Restart** when it is running. Its **⋯** menu has:

| Item                                    | Shown or enabled         | What it does                                                                                                                                                                              |
|-----------------------------------------|--------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Show its logs                           | Always                   | Picks the service on the **Logs** tab, shows that tab, and starts the logs if they are not shown                                                                                          |
| Open a shell in a terminal              | Enabled while running    | Opens Terminal on macOS, a command window on Windows, or the first of `x-terminal-emulator`, `gnome-terminal`, `konsole` and `xterm` on Linux, running `<engine> exec -it <container> sh` |
| Copy the container name                 | Enabled with a container | Copies it, for use on the command line                                                                                                                                                    |
| Recreate, Rebuild and start, Pull image | Always                   | As in **More**, for this service                                                                                                                                                          |
| Pause, Kill                             | While running            | As in **More**, for this service                                                                                                                                                          |
| Resume                                  | While paused             | As in **More**, for this service                                                                                                                                                          |
| Remove container                        | With a container         | `compose rm --stop --force`: stops and removes it; **Start** makes it again                                                                                                               |

Every command in the menus names the service, and all are disabled while a command runs.

## Services

Each file's list is `compose config --services`, joined with `compose ps --all --format json`.
It is read when the page is shown, every 5 seconds while it stays shown, and on **Refresh**, and
after a command on that file. A file that cannot be read shows why in its group, and the others
carry on.

| Dot   | Container               | Status shown                                                                         |
|-------|-------------------------|--------------------------------------------------------------------------------------|
| Green | Running                 | The engine's, such as "Up 5 minutes", with its health check's result when it has one |
| Amber | Paused                  | The engine's, such as "Up 5 minutes (Paused)"                                        |
| Red   | Created but not running | The engine's, such as "Exited (0) 2 minutes ago"                                     |
| Grey  | None                    | Not created                                                                          |

Ports are the published ones, as `host→container`.

## Compose files and engine

| Setting | Default          | Meaning                                                                                                                                                    |
|---------|------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Files   | The repository's | The compose files. Until any are chosen, `containers/docker-compose.yml` in the first folder above Joktan's own, or above the current folder, that has one |
| Engine  | Auto             | Docker, Podman, or Auto: Docker when its engine answers `docker info`, then Podman when it answers `podman info`, then whichever is installed              |

Both are saved in `containers.json` in Joktan's data folder as soon as they change. The single
`ComposeFile` that earlier versions saved is read as a list of one. Changing the engine stops the
logs and reads the services again. Below the engine, which command is used; it is looked for once
for all the files.

**Add** takes the path typed and **Browse** one or more files. A file that does not exist, or is
on the list already, is refused. **Remove** only stops showing a file: its containers are left as
they are, and the logs stop if they were showing it. Once every file is removed, the list stays
empty.

A group is named after the project: the file's `name:`, or else its folder's name. Each file is a
project of its own, and commands run from its folder, so a `.env` beside it is read, as on the
command line. Two files with the same project name, such as two in one folder, would share
containers, so both groups say so; a `name:` line at the top of one of the files separates them.

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

| Control             | Default                   | Meaning                                                                                                              |
|---------------------|---------------------------|----------------------------------------------------------------------------------------------------------------------|
| File and service    | The first file's services | All of one file's services, or one service without the container name before each line. Logs show one file at a time |
| Last _n_ lines      | 200                       | How many earlier lines of each container's log to show first                                                         |
| Timestamps          | Off                       | Adds `--timestamps`                                                                                                  |
| Keep newest in view | On                        | Scrolls to each new line                                                                                             |
| Copy                |                           | Copies the lines shown                                                                                               |
| Clear               |                           | Empties the list; the logs keep coming                                                                               |

Changing the choice or timestamps starts the logs again. At most 5,000 lines are kept; older
ones are dropped.

A command run on the file whose logs are shown pauses them, since a container it recreates would
not be followed, and they continue afterwards from the moment they paused, with `--since`, so
nothing is shown twice. Commands on other files leave them alone. When the logs end on their own
because no container is running (Docker does this; Podman keeps waiting), a line says so, and
they continue after the next command run on that file.

The logs keep coming while another page is shown. Leaving the app stops them, and any command
still running.

## Command output

Each command's project and line, such as `joktan $ podman compose up -d`, followed by what it
wrote to either stream. At most 1,000 lines are kept. **Copy** and **Clear** as for the logs.

## Collapsing

**▾**, at the top right of the tabs, collapses the logs and command output to a bar with **▸**, which
brings them back. Open, the services scroll past 420 high and the tabs take the rest of the page.
Collapsed, the services and **Compose files** take all but the bar.

- Collapsing only hides them: logs keep coming, and the bar says so while they do.
- **Show its logs** on a service opens them.
- Whether they are collapsed is saved in `containers.json`. They start open.

## Out of scope

- Merging several files into one project, as `compose -f a.yml -f b.yml` does.
- Containers not in a compose file on the list.
- Running commands inside a container.

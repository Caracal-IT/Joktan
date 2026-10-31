# Containers

Runs compose files with Docker or Podman, without a terminal: start a service, see whether it is running and on
which ports, read what it logs, and stop it again.

You need Docker or Podman installed. Podman also needs a compose provider: `podman-compose` or Docker's
`docker-compose`.

## Starting and stopping

Each compose file is a project with its own group under **Services**. The buttons in a group's header act on every
service in that file; those on a row act on that service alone.

| Button | What it does |
| --- | --- |
| Start | Creates the containers if needed, and starts them |
| Stop | Stops the containers and keeps them |
| Restart | Stops and starts them, without recreating them |
| Down | Stops and removes them and their network; volumes are kept |

Only one command runs at a time across the page. **Cancel** stops it; containers it already changed stay as they are.
A command that fails shows its exit code, and what it wrote stays on **Command output**.

## More commands

**More**, on a group:

- **Recreate**: makes new containers, even when nothing changed.
- **Rebuild and start**: builds the images again, then recreates and starts the containers.
- **Pull images**: fetches the latest images.
- **Build images**: builds without starting anything.
- **Pause** and **Resume**: freeze the running containers, and let them run again.
- **Kill**: stops the containers at once, without letting them shut down.
- **Down and remove volumes…**: as Down, and deletes the volumes and what is in them. It asks first.

**⋯**, on a row, has most of the same for one service, and:

- **Show its logs**: shows the service on the **Logs** tab.
- **Open a shell in a terminal**: runs `sh` inside the running container.
- **Copy the container name**: for use on the command line.
- **Remove container**: stops and removes it. **Start** makes it again.

## Reading the list

The list is checked every 5 seconds while the page is shown, and on **Refresh**.

- Green: running, with its health check's result if it has one.
- Amber: paused.
- Red: created but not running.
- Grey: no container yet.

Ports are the published ones, shown as `host→container`.

## Logs

On the **Logs** tab, choose one file's services or one service, and press **Show logs**.

- **Last** *n* **lines**: how many earlier lines to show first; 200 by default.
- **Timestamps**: adds the time to each line.
- **Keep newest in view**: scrolls to each new line.
- **Copy** copies the lines shown, and **Clear** empties the list while the logs keep coming.

At most 5,000 lines are kept. Logs keep coming while another page is shown, and pause while a command runs on that file.

**Command output** shows what each command wrote, for up to 1,000 lines.

## Configuring it

Under **Compose files**, both of these are saved as you change them.

**Compose files.** **Add** takes the path typed, and **Browse** picks one or more files. **Remove** only stops showing a
file: its containers are left as they are. Until you choose any, Joktan uses `containers/docker-compose.yml` from the
first folder above it that has one, which is the local test brokers when running from the repository.

**Engine.** **Auto** uses Docker when its engine is running and Podman otherwise. Choose **Docker** or **Podman** to
insist on one. The command in use is shown below it.

Commands run from the file's folder, so a `.env` beside it is read, as on the command line.

## When something goes wrong

- **The engine is not found or not answering**: start Docker Desktop, or the Podman machine with
  `podman machine start`, then press **Refresh**.
- **Two groups say they share containers**: two files have the same project name, such as two in one folder. Add a
  `name:` line at the top of one of the files.
- **A file cannot be read**: the reason is shown in its group, and the other files carry on.

Containers that are not in a compose file on the list are not shown.

# Containers

Runs compose files with Docker or Podman, without a terminal: start a service, see whether it is running and on
which ports, read what it logs, and stop it again. It also makes Mosquitto brokers for you, with a login and TLS if
you want them.

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

## Mosquitto brokers

The **Mosquitto brokers** card makes brokers without writing a compose file. **Add broker**, fill in the form, and
press **Save**. Joktan writes each broker into a compose file of its own, which appears under **Services** as a group
of its own; start the broker there.

| Setting | What it does |
| --- | --- |
| Name | The service's name: lower-case letters, digits, dashes and underscores |
| Plain MQTT port | The port on this computer for plain MQTT. A new broker gets the first one free from 1883 |
| TLS port | The port for TLS. Filled in, with the first one free from 8883, when you choose a server certificate |
| Mutual TLS port | The port where clients must show a certificate too. Filled in, from 8884, when you choose whose client certificates to accept |
| Username and Password | Optional. With a username, clients must sign in with it; without one, anyone can connect. The password is kept in the system's credential store |
| Server certificate | A server certificate from [Certificates](help:certificates), which the broker shows on its TLS ports. **None** empties both TLS ports |
| Client certificates signed by | On the mutual TLS port, the CA clients' certificates must be signed by: the CAs that signed the server certificate, or another CA. **None: no mutual TLS** empties the port |

Leave a port empty to turn it off. Every port must differ from the other brokers' ports.

Each broker also gets a client on [MQTT](help:mqtt) with the same name, connecting to `localhost`: on the plain port if
it has one, or else with TLS, trusting the CA that signed its certificate. On a mutual TLS port, it shows the first
client certificate signed by the right CA. Any client can be linked to another of the broker's ports in its settings,
under **From a broker on Dev Tools / Containers**. Linked clients follow the broker and its certificates when they
change, and are removed with it. **Down** or **Remove container** leaves them, as **Start** brings the broker back.

Each broker's row shows its container's state, with a dot coloured as under **Services**, and how its MQTT client is
doing: connected, connecting, failed or disconnected. **Start** and **Stop** on the row start and stop that broker;
**Start** writes its files first, and puts the brokers' compose file on the page if it is not there.

**Edit** changes a broker. If it is running, it is started again with the change. **Remove** asks first, then stops
and removes its container and forgets its password.

The files are written again before the brokers are started or built, so a certificate made again on Certificates is
picked up. Don't edit them by hand: Joktan replaces them.

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

## Making room for the services

Press **▾**, at the top right of the **Logs** and **Command output** tabs, to collapse them to a bar. The services
then take the rest of the page. Press **▸** on the bar to bring the tabs back.

Logs keep coming while collapsed, and the bar says so. **Show its logs** on a service opens the tabs again. Joktan
remembers whether they were collapsed.

## Configuring it

On **Settings / Container Settings**, or with **Settings** beside **Refresh**. Each change is saved as you make it.

**Compose files.** **Add** takes the path typed, and **Browse** picks one or more files. **Remove** only stops showing a
file: its containers are left as they are.

The list starts empty. Joktan does not look for compose files by itself: it uses the ones saved in `settings/containers.json` in
its [data folder](help:settings-and-data). The repository has three compose files, under `profile/ettiene/containers/`, and
`profile/ettiene/settings/containers.json` lists them. Add them with **Add** or **Browse**, or copy that profile to the
data folder with `make -C profile/ettiene` while Joktan is closed.

| File | Services | Open |
| --- | --- | --- |
| `mqtt/docker-compose.yml` | MQTT brokers, which need a server certificate first; see [TLS](help:mqtt-tls) | `localhost:1883`, `localhost:1783` and `localhost:1983` |
| `elastic/docker-compose.yml` | Elasticsearch and Kibana | `http://localhost:9200` and `http://localhost:5601` |
| `monitoring/docker-compose.yml` | Prometheus and Grafana, whose login is `admin` and `admin` | `http://localhost:9090` and `http://localhost:3000` |

The README beside each file explains its settings. The brokers made on this page add their own file, which goes
when the last broker does.

**Engine.** **Auto** uses Docker when its engine is running and Podman otherwise. Choose **Docker** or **Podman** to
insist on one. The command in use is shown below it.

Commands run from the file's folder, so a `.env` beside it is read, as on the command line.

**Mosquitto brokers.** Where the brokers' compose file is written: by default `containers/mosquitto` in the data
folder, with a folder for each broker inside it. Change the **Folder** or **Project name** and press **Apply**. Take
the brokers down first, with **Down**, so that their containers are not left behind. The **Mosquitto image** they
are built from is used the next time a broker is started.

## When something goes wrong

- **The engine is not found or not answering**: start Docker Desktop, or the Podman machine with
  `podman machine start`, then press **Refresh**.
- **Two groups say they share containers**: two files have the same project name, such as two in one folder. Add a
  `name:` line at the top of one of the files.
- **A file cannot be read**: the reason is shown in its group, and the other files carry on.

Containers that are not in a compose file on the list are not shown.

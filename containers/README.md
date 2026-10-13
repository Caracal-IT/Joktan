# Containers

Services that Joktan's developer tools talk to, for running on your own machine. The compose
file works with both Docker and Podman.

| Service     | Image                                   | Port | Used by                                         |
|-------------|-----------------------------------------|------|-------------------------------------------------|
| `mosquitto` | `docker.io/library/eclipse-mosquitto:2` | 1883 | Dev Tools / Connectivity (MQTT, Topic Explorer) |

## Start and stop

Run these from the repository root.

With Docker:

```sh
docker compose -f containers/docker-compose.yml up -d
docker compose -f containers/docker-compose.yml down
```

With Podman:

```sh
podman compose -f containers/docker-compose.yml up -d
podman compose -f containers/docker-compose.yml down
```

`podman compose` hands the file to a compose provider, so one must be installed: either
`podman-compose` or Docker's `docker-compose`.

Then, in Joktan, open Dev Tools / Connectivity / MQTT and connect to host `localhost`, port `1883`.

## Settings

The broker accepts anonymous connections by default. To change that, copy `.env.example` to
`.env` in this folder and edit it. `.env` is not committed.

| Variable        | Default | Meaning                                             |
|-----------------|---------|-----------------------------------------------------|
| `MQTT_PORT`     | `1883`  | The port on your machine that the broker listens on |
| `MQTT_USERNAME` | Empty   | Set together with the password to require a login   |
| `MQTT_PASSWORD` | Empty   | The password for that user                          |

Run `up -d` again after changing `.env` so the container is recreated with the new values.

## Watching what arrives

To see the messages Joktan sends, subscribe from inside the container:

```sh
docker exec joktan-mosquitto mosquitto_sub -t '#' -v
podman exec joktan-mosquitto mosquitto_sub -t '#' -v
```

Add `-u <username> -P <password>` if you set a login.

## Notes

- The broker is for local development only. It has no TLS, so a username and password travel in
  clear text.
- Nothing is kept between restarts: retained messages are lost when the container stops.
- The broker's configuration is written inside the container when it starts, from the variables
  above, rather than mounted from this folder. A mounted file needs SELinux relabelling on some
  Podman hosts and fails on others; generating it avoids the difference.
- The image name is fully qualified because Podman does not assume Docker Hub for short names.

# MQTT brokers

The brokers that Joktan's developer tools talk to, for running on your own machine. The compose
file works with both Docker and Podman.

| Service      | Image                            | Plain MQTT | TLS  | Mutual TLS | Used by                                         |
|--------------|----------------------------------|------------|------|------------|-------------------------------------------------|
| `mosquitto`  | `localhost/joktan-brokers:latest` | 1883       | 8883 | 8884       | Dev Tools / Connectivity (MQTT, Topic Explorer) |
| `mosquitto2` | `localhost/joktan-brokers:latest` | 1783       | 8783 | 8784       | The same, for trying more than one broker       |
| `messaging`  | `localhost/joktan-brokers:latest` | 1983       |      |            | Joktan's Messages and Alerts buttons            |

The image is `docker.io/library/eclipse-mosquitto:2` with `brokers/MQTT/` and the certificate in
`certs/` copied in. See the [`Dockerfile`](Dockerfile). The project is called `joktan-brokers`.

`messaging` is kept apart from the other two so that messages and alerts do not mix with what you
are testing. In Joktan, on Settings / Alerts and Messages, press **Edit server** and use host
`localhost`, port `1983`.

## Before the first start

The image needs a server certificate, and certificates are not committed, so make them first, from the
repository root:

```sh
make -C profile/ettiene certs
```

It keeps any already there. Or make them in Joktan on Dev Tools / Connectivity / Certificates: a root CA
with the folder set to `profile/ettiene/containers/mqtt/certs/joktan`, then a **Server certificate** called
`localhost` and a **Client certificate** for each client that uses mutual TLS.

The brokers use `certs/joktan/localhost/`: `cert.pem`, `key.pem`, and `ca.pem` as the CA that
clients' certificates must be signed by.

## Start and stop

Run these from the repository root.

With Docker:

```sh
docker compose -f profile/ettiene/containers/mqtt/docker-compose.yml up -d
docker compose -f profile/ettiene/containers/mqtt/docker-compose.yml down
```

With Podman:

```sh
podman compose -f profile/ettiene/containers/mqtt/docker-compose.yml up -d
podman compose -f profile/ettiene/containers/mqtt/docker-compose.yml down
```

`podman compose` hands the file to a compose provider, so one must be installed: either
`podman-compose` or Docker's `docker-compose`.

Or, in Joktan, open Dev Tools / Containers, which runs these commands with whichever of Docker
and Podman is running, and shows the brokers' state and logs. See
[its spec](../../../../docs/features/dev-tools/containers/spec.md).

Then, in Joktan, open Dev Tools / Connectivity / MQTT and connect to host `localhost`, port `1883`.

## TLS and mutual TLS

In a client's settings in Joktan, turn on **Use TLS** and choose the CA under **Or a CA from
Certificates**. For port 8884 or 8784, also choose a client certificate under **Or one from
Certificates**.

## Changing the configuration or the certificate

Edit `brokers/MQTT/mosquitto.conf`, `brokers/MQTT/messaging.conf` or the files in `certs/`, then
run `up -d`. It builds the image every time, so the change takes effect.

The files are copied rather than mounted because Podman on macOS only shares some folders, such
as your home folder, with the virtual machine it runs containers in, and a mount from anywhere
else, such as another drive, fails.

## Watching what arrives

To see the messages Joktan sends, subscribe from inside the container:

```sh
docker compose -f profile/ettiene/containers/mqtt/docker-compose.yml exec mosquitto mosquitto_sub -t '#' -v
podman compose -f profile/ettiene/containers/mqtt/docker-compose.yml exec mosquitto mosquitto_sub -t '#' -v
```

## Notes

- The brokers are for local development only. They accept anonymous connections, and on the plain
  ports anything sent travels in clear text.
- The image stays on your machine, but it holds the broker's private key; do not push it to a
  registry.
- Nothing is kept between restarts unless the configuration turns on persistence.

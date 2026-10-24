# Containers

Services that Joktan's developer tools talk to, for running on your own machine. The compose
file works with both Docker and Podman.

| Service     | Image                                   | Ports            | Used by                                         |
|-------------|-----------------------------------------|------------------|-------------------------------------------------|
| `mosquitto` | `docker.io/library/eclipse-mosquitto:2` | 1883, 8883, 8884 | Dev Tools / Connectivity (MQTT, Topic Explorer) |

The broker has three listeners:

| Port | What it is                                                                     |
|------|--------------------------------------------------------------------------------|
| 1883 | Plain MQTT                                                                     |
| 8883 | TLS. The broker shows a certificate signed by the development CA               |
| 8884 | Mutual TLS. As 8883, and the client must show a certificate signed by that CA |

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

Or, in Joktan, open Dev Tools / Containers, which runs these commands with whichever of Docker
and Podman is running, and shows the broker's state and logs. See
[its spec](../docs/features/dev-tools/containers/spec.md).

Then, in Joktan, open Dev Tools / Connectivity / MQTT and connect to host `localhost`, port `1883`.

## TLS and mutual TLS

The broker's TLS listeners use the server certificate in `certs/` when it is there, and
development certificates otherwise.

### Certificates from `certs/`

`certs/` is copied into the broker's image when it is built, and `up -d` builds it every time,
so a change in `certs/` takes effect with:

```sh
docker compose -f containers/docker-compose.yml up -d
podman compose -f containers/docker-compose.yml up -d
```

The files are copied rather than mounted because Podman on macOS only shares some folders, such
as your home folder, with the virtual machine it runs containers in. The image stays on your
machine, but it holds the broker's private key; nothing in `certs/` except its README is
committed.

By default the broker uses what Dev Tools / Connectivity / Certificates in Joktan makes for a
server certificate called `localhost`, with `containers/certs/joktan` as the folder:

1. In Joktan, make a root CA with the folder set to `containers/certs/joktan`.
2. Select it, make a **Server certificate** (the common name and host names default to
   `localhost`), and a **Client certificate** for each client that uses mutual TLS.
3. Run `up -d`. The log says `TLS uses certs/joktan/localhost/chain.pem`.
4. In a client's settings in Joktan, choose the CA under **Or a CA from Certificates**, and for
   port 8884 a client certificate under **Or one from Certificates**.

| Variable        | Default                        | What it is                                                 |
|-----------------|--------------------------------|------------------------------------------------------------|
| `MQTT_TLS_CERT` | `joktan/localhost/chain.pem`   | The broker's certificate followed by any intermediate CAs  |
| `MQTT_TLS_KEY`  | `joktan/localhost/key.pem`     | Its private key, not encrypted                             |
| `MQTT_TLS_CA`   | `joktan/localhost/ca.pem`      | The CAs that clients' certificates must be signed by       |

The paths are relative to `certs/`; set them in `.env` (see [Settings](#settings)) to use other
files, such as ones exported from Joktan or made with OpenSSL. On port 8884 the broker accepts
client certificates signed by any CA in `MQTT_TLS_CA`.

### Development certificates

When any of those files is missing, the log says so, and the first start makes a development
CA, a certificate for the broker (valid for `localhost`, `127.0.0.1` and `::1`) and a client
certificate, all valid for 825 days. The image has no `openssl` command, so it installs one for
this, which needs a network connection. Without one the broker starts with plain MQTT only;
restart it once the network is back.

They are kept in the `certs` volume, so they survive restarts. Copy the client's files out of
the container:

```sh
docker cp joktan-mosquitto:/certs/client ./containers/certs/dev
podman cp joktan-mosquitto:/certs/client ./containers/certs/dev
```

That gives `containers/certs/dev/`:

| File         | What it is                                                   |
|--------------|--------------------------------------------------------------|
| `ca.crt`     | The development CA. Choose it as the client's CA certificate |
| `client.crt` | The client certificate, PEM                                  |
| `client.key` | Its private key, PEM, not encrypted                          |
| `client.p12` | The certificate and key together, password `joktan`          |

In Joktan, add a client with host `localhost` and **Use TLS** on:

- port `8883` with `ca.crt` as the CA certificate, for TLS;
- port `8884` with `ca.crt`, and either `client.crt` with `client.key`, or `client.p12` with the
  password `joktan`, for mutual TLS.

To make new development certificates, remove the volume: `down -v`, then `up -d`. Copy the client
files out again afterwards, because the old ones are no longer accepted.

## Settings

The broker accepts anonymous connections by default. To change that, copy `.env.example` to
`.env` in this folder and edit it. `.env` is not committed.

| Variable         | Default | Meaning                                             |
|------------------|---------|-----------------------------------------------------|
| `MQTT_PORT`      | `1883`  | The port on your machine for plain MQTT             |
| `MQTT_TLS_PORT`  | `8883`  | The port on your machine for TLS                    |
| `MQTT_MTLS_PORT` | `8884`  | The port on your machine for mutual TLS             |
| `MQTT_USERNAME`  | Empty   | Set together with the password to require a login   |
| `MQTT_PASSWORD`  | Empty   | The password for that user                          |
| `MQTT_TLS_CERT`, `MQTT_TLS_KEY`, `MQTT_TLS_CA` | See [above](#certificates-from-certs) | The broker's certificate files in `certs/` |

The login applies to all three listeners.

Run `up -d` again after changing `.env` so the container is recreated with the new values.

## Watching what arrives

To see the messages Joktan sends, subscribe from inside the container:

```sh
docker exec joktan-mosquitto mosquitto_sub -t '#' -v
podman exec joktan-mosquitto mosquitto_sub -t '#' -v
```

Add `-u <username> -P <password>` if you set a login.

## Notes

- The broker is for local development only. On port 1883 a username and password travel in clear
  text; use 8883 or 8884 to encrypt them. The development CA's key stays inside the container,
  but anyone who copies the volume can sign certificates the broker will accept. The broker's
  image holds the private key from `certs/`; do not push it to a registry.
- Nothing is kept between restarts: retained messages are lost when the container stops.
- The broker's configuration is written inside the container when it starts, from the variables
  above, and `certs/` is copied into its image, rather than mounted from this folder. A mount
  needs SELinux relabelling on some Podman hosts, and Podman on macOS cannot mount folders
  outside the ones it shares, such as other drives; copying avoids both.
- The image name is fully qualified because Podman does not assume Docker Hub for short names.

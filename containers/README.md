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

Then, in Joktan, open Dev Tools / Connectivity / MQTT and connect to host `localhost`, port `1883`.

## TLS and mutual TLS

The first time the container starts it makes a development CA, a certificate for the broker
(valid for `localhost`, `127.0.0.1` and `::1`) and a client certificate, all valid for 825 days.
The image has no `openssl` command, so it installs one for this, which needs a network
connection. Without one the broker starts with plain MQTT only; restart it once the network is
back.

The certificates are kept in the `certs` volume, so they survive restarts. Copy the client's
files out of the container:

```sh
docker cp joktan-mosquitto:/certs/client ./containers/certs
podman cp joktan-mosquitto:/certs/client ./containers/certs
```

That gives `containers/certs/`, which is not committed:

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

To make new certificates, remove the volume: `down -v`, then `up -d`. Copy the client files out
again afterwards, because the old ones are no longer accepted.

### Certificates made in Joktan

The broker can use certificates made on Dev Tools / Connectivity / Certificates instead:

1. Make a root CA there, then a server certificate signed by it whose host names include
   `localhost`, and a client certificate for each client that uses mutual TLS.
2. Export the server certificate with the file name `server`, its private key and its CA
   certificates. That gives `server.crt`, `server.key` and `ca.crt`.
3. Copy them into the container, in place of the development certificates, and restart it:

   ```sh
   docker cp server.crt joktan-mosquitto:/certs/server.crt
   docker cp server.key joktan-mosquitto:/certs/server.key
   docker cp ca.crt joktan-mosquitto:/certs/client/ca.crt
   docker restart joktan-mosquitto
   ```

   With Podman, use `podman cp` and `podman restart`.

4. In Joktan, choose the CA under **Or a CA from Certificates** in the client's settings, and for
   port 8884 a client certificate under **Or one from Certificates**.

The broker accepts client certificates signed by any CA in `ca.crt`. They stay in the volume
until it is removed; `down -v` and `up -d` bring back the development certificates.

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
  but anyone who copies the volume can sign certificates the broker will accept.
- Nothing is kept between restarts: retained messages are lost when the container stops.
- The broker's configuration is written inside the container when it starts, from the variables
  above, rather than mounted from this folder. A mounted file needs SELinux relabelling on some
  Podman hosts and fails on others; generating it avoids the difference.
- The image name is fully qualified because Podman does not assume Docker Hub for short names.

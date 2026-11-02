# Prometheus and Grafana

Prometheus and Grafana, for local development. Grafana has Prometheus set up as its data source,
and Prometheus collects its own metrics and Grafana's, so there is something to look at straight
away.

| Service      | Container           | Image                                 | Address                 |
|--------------|---------------------|---------------------------------------|-------------------------|
| `prometheus` | `joktan-prometheus` | `docker.io/prom/prometheus:v3.15.1`   | <http://localhost:9090> |
| `grafana`    | `joktan-grafana`    | `docker.io/grafana/grafana:13.3.0`    | <http://localhost:3000> |

Grafana's login is `admin` and `admin` unless changed in `.env`.

## Start and stop

Use Dev Tools / Containers in Joktan, or run these from the repository root:

```sh
docker compose -f profile/ettiene/containers/monitoring/docker-compose.yml up -d
docker compose -f profile/ettiene/containers/monitoring/docker-compose.yml down
```

With Podman, write `podman compose` instead.

## What Prometheus collects

Add jobs to [`prometheus/prometheus.yml`](prometheus/prometheus.yml), then run `up -d`. The file is
copied into the image when it is built, and `up -d` builds it every time. To reach something
running on your machine rather than in a container, use `host.docker.internal:<port>` with Docker
and `host.containers.internal:<port>` with Podman.

## Settings

Copy `.env.example` to `.env` in this folder and edit it. `.env` is not committed. Run `up -d`
again afterwards so the containers are recreated with the new values.

| Variable           | Default | Meaning                                 |
|--------------------|---------|-----------------------------------------|
| `PROMETHEUS_PORT`  | `9090`  | The port on your machine for Prometheus |
| `GRAFANA_PORT`     | `3000`  | The port on your machine for Grafana    |
| `GRAFANA_USER`     | `admin` | Grafana's administrator                 |
| `GRAFANA_PASSWORD` | `admin` | That administrator's password           |

Grafana only reads the user and password the first time, when the `grafana-data` volume is made.

## Notes

- For local development only. Prometheus has no login, and both ports are published on `127.0.0.1`
  only.
- Metrics, and any dashboards made in Grafana, are kept in the `prometheus-data` and `grafana-data`
  volumes. **Down** keeps them, and **Down and remove volumes…** deletes them.
- The configuration is copied into the images rather than mounted, because Podman on macOS can
  only mount folders it shares with its virtual machine, which leaves out other drives.

# Containers

Services to run on your own machine, one folder per compose project. They work with both Docker
and Podman. Add them to Joktan's Dev Tools / Containers page with **Add** or **Browse**, or with
[`settings/containers.json`](../settings/containers.json), which the [Makefile](../Makefile) copies to Joktan's data folder. See [its spec](../../../docs/features/dev-tools/containers/spec.md).

| Folder                       | Project             | Services                  | Open                                                                 |
|------------------------------|---------------------|---------------------------|----------------------------------------------------------------------|
| [`mqtt/`](mqtt/README.md)             | `joktan-brokers`    | Three Mosquitto brokers   | `localhost:1883`, in Dev Tools / Connectivity                        |
| [`elastic/`](elastic/README.md)       | `joktan-elastic`    | Elasticsearch and Kibana  | <http://localhost:9200>, <http://localhost:5601>                     |
| [`monitoring/`](monitoring/README.md) | `joktan-monitoring` | Prometheus, Grafana and the OpenTelemetry Collector | <http://localhost:9090>, <http://localhost:3000>, OTLP on `localhost:4317` |

Joktan exports to the collector in `monitoring/`. Prometheus scrapes the metrics from it, and it sends the
logs to Elasticsearch in `elastic/` over a network the two share, `joktan-telemetry`. Start both to see everything.

`elastic/` and `monitoring/` each have a `.env.example`. Copy it to `.env` in that folder to change
the defaults; `.env` is not committed.

`podman compose` hands the file to a compose provider, so one must be installed: either
`podman-compose` or Docker's `docker-compose`.

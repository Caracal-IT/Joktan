# Containers

Services to run on your own machine, one folder per compose project. They work with both Docker
and Podman. Joktan's Dev Tools / Containers page shows `mqtt/` until any files are chosen; add the
others with **Add** or **Browse**. See [its spec](../docs/features/dev-tools/containers/spec.md).

| Folder                       | Project             | Services                  | Open                                                                 |
|------------------------------|---------------------|---------------------------|----------------------------------------------------------------------|
| [`mqtt/`](mqtt/README.md)             | `joktan`            | Two Mosquitto brokers     | `localhost:1883`, in Dev Tools / Connectivity                        |
| [`elastic/`](elastic/README.md)       | `joktan-elastic`    | Elasticsearch and Kibana  | <http://localhost:9200>, <http://localhost:5601>                     |
| [`monitoring/`](monitoring/README.md) | `joktan-monitoring` | Prometheus and Grafana    | <http://localhost:9090>, <http://localhost:3000>                     |

Each folder has its own `.env.example`. Copy it to `.env` in that folder to change the defaults;
`.env` is not committed.

`podman compose` hands the file to a compose provider, so one must be installed: either
`podman-compose` or Docker's `docker-compose`.

# Elasticsearch and Kibana

A single Elasticsearch node and Kibana, for local development.

| Service         | Container              | Image                                   | Address                 |
|-----------------|------------------------|-----------------------------------------|-------------------------|
| `elasticsearch` | `joktan-elasticsearch` | `docker.io/library/elasticsearch:9.5.5` | <http://localhost:9200> |
| `kibana`        | `joktan-kibana`        | `docker.io/library/kibana:9.5.5`        | <http://localhost:5601> |

Kibana waits until Elasticsearch is healthy, and takes a minute or so to start itself.

## Start and stop

Use Dev Tools / Containers in Joktan, or run these from the repository root:

```sh
docker compose -f profile/ettiene/containers/elastic/docker-compose.yml up -d
docker compose -f profile/ettiene/containers/elastic/docker-compose.yml down
```

With Podman, write `podman compose` instead.

## Settings

Copy `.env.example` to `.env` in this folder and edit it. `.env` is not committed. Run `up -d`
again afterwards so the containers are recreated with the new values.

| Variable             | Default | Meaning                                    |
|----------------------|---------|--------------------------------------------|
| `ELASTICSEARCH_PORT` | `9200`  | The port on your machine for Elasticsearch |
| `KIBANA_PORT`        | `5601`  | The port on your machine for Kibana        |
| `ELASTICSEARCH_HEAP` | `1g`    | Elasticsearch's heap                       |

## Notes

- For local development only. Security is off, so there is no login and no TLS, and the ports are
  published on `127.0.0.1` only.
- The indices are kept in the `elasticsearch-data` volume. **Down** keeps it, and **Down and
  remove volumes…** deletes it.
- Elasticsearch and Kibana must stay on the same version, so change both image tags together.
- With Podman, the two need about 3.5 GB of the machine's memory. See the machine's with
  `podman machine list`.

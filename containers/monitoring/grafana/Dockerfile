# Grafana with Prometheus already set up as its data source.
# Fully qualified, because Podman does not assume Docker Hub for short names.
FROM docker.io/grafana/grafana:13.3.0
COPY datasources.yml /etc/grafana/provisioning/datasources/datasources.yml

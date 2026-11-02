# The broker for docker-compose.yml, with certs/ copied in so TLS can use the certificates there.
# Fully qualified, because Podman does not assume Docker Hub for short names.
FROM docker.io/library/eclipse-mosquitto:2
COPY --chown=mosquitto:mosquitto certs/ /joktan-certs/

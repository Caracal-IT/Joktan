# The brokers for docker-compose.yml, with their configuration and certificate copied in.
# Fully qualified, because Podman does not assume Docker Hub for short names.
FROM docker.io/library/eclipse-mosquitto:2
COPY --chown=mosquitto:mosquitto brokers/MQTT/ /mosquitto/config/
# The "localhost" server certificate from Joktan's Certificates page.
COPY --chown=mosquitto:mosquitto certs/joktan/localhost/ /mosquitto/certs/

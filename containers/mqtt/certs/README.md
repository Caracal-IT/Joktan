# Broker certificates

Put the broker's certificates here; this folder is copied into the broker image when it is
built, and nothing else in it is committed. See [../README.md](../README.md#tls-and-mutual-tls).

By default the broker uses the files Dev Tools / Connectivity / Certificates makes for a server
certificate called `localhost` when `certs/joktan` is chosen as its folder:

| File                         | Used as                                              |
|------------------------------|------------------------------------------------------|
| `joktan/localhost/chain.pem` | The broker's certificate and intermediate CAs        |
| `joktan/localhost/key.pem`   | Its private key                                      |
| `joktan/localhost/ca.pem`    | The CAs that clients' certificates must be signed by |

Other files can be named in `.env` with `MQTT_TLS_CERT`, `MQTT_TLS_KEY` and `MQTT_TLS_CA`.

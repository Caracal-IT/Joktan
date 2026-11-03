# Broker certificates

Put the broker's certificates here; this folder is copied into the broker image when it is
built, and nothing else in it is committed. See [../README.md](../README.md#tls-and-mutual-tls).

By default the broker uses the files Dev Tools / Connectivity / Certificates makes for a server
certificate called `localhost` when `certs/joktan` is chosen as its folder:

| File                         | Used as                                              |
|------------------------------|------------------------------------------------------|
| `joktan/localhost/cert.pem`  | The broker's certificate                             |
| `joktan/localhost/key.pem`   | Its private key                                      |
| `joktan/localhost/ca.pem`    | The CAs that clients' certificates must be signed by |

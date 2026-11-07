# Data

Where data is kept, with a tab for each kind at the top of the page: **MQTT** for Mosquitto brokers, and
**Database** for databases.

## MQTT

The **Mosquitto brokers** card makes brokers without writing a compose file. **Add broker**, fill in the form, and
press **Save**, then press **Start** on the broker's row. Joktan writes each broker into a compose file of its own,
which also appears on [Containers](help:containers) under **Services**, with its logs and command output.

| Setting | What it does |
| --- | --- |
| Name | The service's name: lower-case letters, digits, dashes and underscores |
| Plain MQTT port | The port on this computer for plain MQTT. A new broker gets the first one free from 1883 |
| TLS port | The port for TLS. Filled in, with the first one free from 8883, when you choose a server certificate |
| Mutual TLS port | The port where clients must show a certificate too. Filled in, from 8884, when you choose whose client certificates to accept |
| Username and Password | Optional. With a username, clients must sign in with it; without one, anyone can connect. The password is kept in the system's credential store |
| Server certificate | A server certificate from [Certificates](help:certificates), which the broker shows on its TLS ports. **None** empties both TLS ports |
| Client certificates signed by | On the mutual TLS port, the CA clients' certificates must be signed by: the CAs that signed the server certificate, or another CA. **None: no mutual TLS** empties the port |

Leave a port empty to turn it off. Every port must differ from the other brokers' ports.

Each broker also gets a client on [MQTT](help:mqtt) with the same name, connecting to `localhost`: on the plain port if
it has one, or else with TLS, trusting the CA that signed its certificate. On a mutual TLS port, it shows the first
client certificate signed by the right CA. Any client can be linked to another of the broker's ports in its settings,
under **From a broker on Data / MQTT**. Linked clients follow the broker and its certificates when they change, and
are removed with it. **Stop**, or **Down** on Containers, leaves them, as **Start** brings the broker back.

Each broker's row shows its container's state, with a dot: green running, amber paused, red stopped, grey not
created. Below it is how its MQTT client is doing: connected, connecting, failed or disconnected. **Start** and **Stop** on the row start and stop that broker;
**Start** writes its files first, and puts the brokers' compose file on Containers if it is not there.

Only one command runs at a time, here and on Containers. While one runs, the page says what it is doing, and
**Cancel** stops it.

**Edit** changes a broker. If it is running, it is started again with the change. **Remove** asks first, then stops
and removes its container and forgets its password.

The files are written again before the brokers are started or built, so a certificate made again on Certificates is
picked up. Don't edit them by hand: Joktan replaces them.

The brokers run with Docker or Podman, so you need one of them, as for [Containers](help:containers). Their state is
checked every 5 seconds while the page is shown.

Where the brokers' compose file is written, its project name and the Mosquitto image are set on **Settings /
Container Settings**.

## Database

Nothing here yet.

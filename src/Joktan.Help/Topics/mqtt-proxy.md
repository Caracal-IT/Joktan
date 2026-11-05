# MQTT Proxy

A broker on this computer that your devices connect to instead of the real one. It forwards everything to a broker
you choose, and can change chosen messages on the way: merge your own values into them, or replace them with
messages it sends on a schedule. Open it on **Dev Tools / Brokers / MQTT Proxy**.

## Setting one up

1. Press **Add proxy**. It gets a tab of its own, a port from 1885, and the first Mosquitto broker from
   [Containers](help:containers) to forward to, if there is one.
2. Under **Forward to**, press **Edit broker** to choose another. **From a broker on Dev Tools / Containers** picks
   one of those brokers' ports, which the proxy then follows; or set up any broker, as for a client on
   [MQTT](help:mqtt-clients), with its login and [TLS](help:mqtt-tls). A broker's username and password are filled
   in; type others to sign in with a login of your own, which is then kept while the host, port and certificates
   follow the broker.
3. Press **Start**, and point your devices at `localhost` and one of the proxy's ports. Switch on
   **Let devices on other computers connect** for devices elsewhere on the network; anything that can reach this
   computer can then connect, without a login.

## TLS and mutual TLS

Devices can connect to the proxy with plain MQTT, TLS, mutual TLS, or any of them, each on a port of its own;
leave a port empty to turn it off.

- **Server certificate** is what the proxy shows on its TLS ports: a server certificate from
  [Certificates](help:certificates). It must name the host devices connect to, such as `localhost`. Choosing one
  fills in a TLS port, the first free from 8885; **None** empties both TLS ports.
- **Client certificates signed by** is, for the mutual TLS port, the CA devices' certificates must be signed by:
  the CAs that signed the server certificate, or any CA there. Choosing one fills in a mutual TLS port, from 8886;
  **None: no mutual TLS** empties it. A device without such a certificate cannot connect.

Devices trust the proxy with the CA that signed its certificate, as they would a broker with that certificate.
Devices on every port share the proxy's one connection to the broker, so they get each other's messages as usual.

While it runs, its ports, certificates, name and broker cannot be changed; press **Stop** first. The dot on its tab
shows its connection to the broker.

## Logins

The proxy signs in to the broker with the login under **Forward to**. How devices sign in to the proxy is set
separately, under **Devices sign in**, so the two can differ:

| Choice | Devices |
| --- | --- |
| Anyone can connect | May connect with any login, or none |
| With the proxy's own login | Must give **Username for devices** and **Password for devices**, which need not be the broker's. The password is kept in the credential store |
| With a login the broker accepts | Must give a login the broker accepts, or none where the broker allows that. Each is tried at the broker as the device connects. This is the choice to start with |

A device that is refused is told its username or password is wrong, and the activity says why. A broker from
[Containers](help:containers) brings its username and password with it.

## What is passed on

- What devices publish goes to the broker, through one connection of the proxy's own.
- What devices subscribe to is subscribed to at the broker, and what the broker sends for it comes back to them.
- A message that is not intercepted goes as it is, on its own topic.

## Intercepting messages

Under **Intercepted messages**, choose a folder of messages, in the same form as a client's
[folder messages](help:mqtt-folder-messages): the topic is the folder a file is in, and a topic can have fields,
such as `plant/{line}/status`, each standing for one level. **New message** writes a new one there. Switch a
message on to intercept its topic:

| The message has | What happens to devices' messages on its topic |
| --- | --- |
| No schedule | Each one has the message merged into it, then goes on: its values replace the device's at every depth, its [varying fields](help:mqtt-varying-fields) are filled in, and the rest of the device's message is kept. One that is not JSON is replaced by the message |
| A schedule | They are blocked. The proxy sends the message itself on its schedule, as a client would |

**Edit** changes a message's schedule and fields, in its settings file. Whether it is switched on here is kept
with the proxy, so a folder shared with a client is not changed by it.

### Its topic at the broker

A message switched on has a **Topic at the broker**: empty to keep its own, or another. Its fields are filled in as
the payload is:

- A field of the message's own topic takes the level it matched, so `plant/{line}/status` sent as
  `lab/{line}/status` turns `plant/line1/status` into `lab/line1/status`.
- Any other field takes the value at that path in the payload sent, after the merge and varying fields, such as
  `{id}` or `{sensor.serial}`. A field with no value stops the message, and the activity says why.

What the broker sends on that topic comes back to devices on the message's own, as long as every field of its own
topic is in it; subscriptions are made there too. Otherwise devices get it as the broker sent it.

## Activity

Each tab shows what happened, newest first: devices connecting and leaving, subscriptions, messages forwarded,
modified, blocked and sent on schedule, and failures. Select a modified message to see the body that went on.
At most 500 lines are kept.

# MQTT

Sends test messages to MQTT brokers, so you can exercise something that consumes MQTT without writing
a script. Messages are typed in by hand, or read from a folder of JSON files and sent on a schedule
with fields that change on every send.

To see what arrives on a broker, use the [Topic Explorer](help:topic-explorer).

## The page

Each client has a tab: one connection to one broker, with its own folder, manual message and activity
log. Any number can be connected at once, and the dot on a tab shows that client's state. A message
always goes through the client whose tab it is on.

A tab has four cards. Click a card's title to collapse it, which only changes what is shown: messages
switched on keep being sent.

- **Broker**: where this client connects, and **Connect**.
- **Manual message**: send one message typed in by hand.
- **Folder messages**: the messages in the client's folder.
- **Activity**: what this client has sent, and what failed.

## Send your first message

1. On the **Broker** card, press **Connect**. To point at another broker first, press **Edit client**.
2. On **Manual message**, type a **Topic** such as `plant/line1/telemetry`, and a **Body**.
3. Press **Send**.
4. The send appears under **Activity**.

## Manual messages

- **Topic** is required, and cannot contain `+` or `#`.
- **Body** can be any text. It does not have to be JSON.
- **QoS** is 0, 1 or 2.
- **Retain** asks the broker to keep the message for new subscribers.
- **Gzip** compresses the body before it is sent.

**Send** is enabled while the client is connected and the topic is valid. The form keeps what is in it
between sends, and between runs.

## Activity

The most recent 500 events of this session, newest first: Sent, Failed, Skipped, Connected,
Disconnected and Reconnecting. Select a sent message to see the exact body that was published, before
any gzipping. **Clear** empties the list.

## Configuring it

- [Clients and brokers](help:mqtt-clients): hosts, ports, logins, colours, and connecting.
- [TLS and mutual TLS](help:mqtt-tls): encrypting the connection, and certificates.
- [Folder messages](help:mqtt-folder-messages): how files become messages, groups and switches.
- [Message settings](help:mqtt-message-settings): topics, schedules and array sizes.
- [Varying fields](help:mqtt-varying-fields): values that change on every send.

For a broker to test against, see [Containers](help:containers).

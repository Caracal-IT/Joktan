# TLS and mutual TLS

TLS is set per client, and is off by default. Open it with **Edit client**, in the **TLS** section.

- **TLS** encrypts the connection, and the client checks the broker's certificate.
- **Mutual TLS** adds a certificate for the client, which the broker checks in turn.

## Settings

- **Use TLS**: encrypts the connection. Switching it on changes port 1883 to 8883, and off changes it
  back. Any other port is left alone.
- **CA certificate**: the certificate that signed the broker's, as PEM or DER. Choose it for a broker
  with a certificate of its own making. Left empty, the authorities this computer trusts are used.
- **Accept any broker certificate**: skips every check of the broker's certificate, so the connection
  is encrypted but the broker is not verified. For testing only.
- **Client certificate**: for mutual TLS. A PEM file, or a PFX/P12 file that holds the key too.
- **Private key**: the key of a PEM client certificate. Leave it empty when the key is in the
  certificate file. Not shown for a PFX/P12 file.
- **Key password**: opens a PFX/P12 file or an encrypted key. It is kept in the system's credential
  store after a successful connection.

Each file has **Browse** and **Clear**. Only the paths are saved, and the files are read again on every
connect, so a renewed certificate takes effect without restarting.

## Using certificates made in Joktan

The easiest way, once the [Certificates](help:certificates) page has made some:

1. Under **Or a CA from Certificates**, choose the CA that signed the broker's certificate.
2. For mutual TLS, under **Or one from Certificates**, choose a client certificate. Its key is filled
   in, and its CA too if none is chosen yet.
3. Press **Save**.

The files are copied into a folder for this client, and the client uses the copies, so it never reads
the folders holding the CAs' keys, and keeps working if the originals are deleted.

## How the broker's certificate is checked

Unless any certificate is accepted:

- Its host name must match the **Host** you entered. A certificate for `localhost` fails if you connect
  to `127.0.0.1`, unless it also lists that address.
- With a CA certificate chosen, it must be signed by that CA, directly or through intermediates the
  broker sends or the CA file holds.
- Without one, the operating system must trust it.

Revocation is not checked.

## When it will not connect

- **"The broker's certificate was refused: …"**: the reason follows. Usually the wrong CA is chosen, or
  the host name does not match.
- **"The TLS handshake failed: …"**: often the broker rejected the client certificate. Check that it is
  signed by a CA the broker accepts.
- **"The … file … does not exist"**: a certificate's file was moved or deleted.
- **A plain port**: a TLS client pointed at a plain MQTT port fails, and so does a plain client pointed
  at a TLS port.

A problem with a file shows straight away in the settings window. It does not stop you saving, because
the file may be fixed before you connect.

## The local test broker

The brokers in the repository's compose file listen on three ports:

| `mosquitto` | `mosquitto2` | What it is |
| --- | --- | --- |
| 1883 | 1783 | Plain MQTT |
| 8883 | 8783 | TLS |
| 8884 | 8784 | Mutual TLS |

To use certificates of your own with them:

1. On [Certificates](help:certificates), make a root CA with its folder set to
   `containers/certs/joktan` in the repository.
2. Select it, and make a **Server certificate** with the defaults, which are for `localhost`. For mutual
   TLS, make a **Client certificate** as well.
3. On [Containers](help:containers), press **Start** on the project, which builds them into the broker.
4. Add a client with host `localhost`, **Use TLS** on and port `8883`, or `8884` for mutual TLS, and
   choose the CA and client certificate as above.

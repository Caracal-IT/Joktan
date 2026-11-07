# Data

Where data is kept, with a tab for each kind at the top of the page: **MQTT** for Mosquitto brokers, and
**Database** for PostgreSQL databases.

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
Container Settings**. So are the databases', with their image and the folder new databases keep their data in.

## Database

The **PostgreSQL databases** card makes databases the same way: **Add database**, fill in the form, **Save**, then
**Start**. Each runs as a container from a compose file of Joktan's own, but keeps its data in a folder on this
computer, so the data stays when the container is removed or the image changes.

| Setting | What it does |
| --- | --- |
| Name | The service's name: lower-case letters, digits, dashes and underscores |
| Port | The port on this computer. A new database gets the first one free from 5432 |
| Username | The superuser, `postgres` unless you change it |
| Password | Needed. Kept in the system's credential store |
| Database | The database made when it first starts. It follows the name until you type one |
| Data folder | Where its files are kept: a folder named after it in the data folder set on **Settings / Container Settings**, unless you choose another. With Podman or Docker Desktop, choose one under your home folder, which their virtual machine can reach |

The username, password and database are set up the first time the database starts, in an empty data folder. After
that they are kept in the data, so changing them in the form doesn't change them in the database.

Each row shows the container's state and how to connect, such as `postgres://postgres@localhost:5432/app`. **Remove**
asks first, then removes the container and forgets the password, but never deletes the data folder.

### Browse a database

Below the list, choose a database while it runs. Its schemas are listed, then the tables of the schema chosen, then
the table chosen: its columns and its rows, a page at a time. Each column of the rows is as wide as its longest
value on the page; point at a cut-off value to see all of it. Drag the line at the right of a column's heading to make
it wider or narrower, and double-click the line to fit it again. Under the rows, **First**, **Previous**, **Next** and
**Last** move between pages, and the box beside them sets how many rows a page has.

- **Schemas** and **Tables**: type a name and press **Create**. A new table starts with an `id` column, numbered for
  each row, unless you untick **With an id column**. **Drop** removes the one chosen, with everything in it.
- **Columns**: **Add column** with a name, a type such as `text` or `integer` (choose one or type any PostgreSQL
  type), an optional default written as SQL, such as `now()` or `'none'`, and **not null**. **Drop** on a column
  removes it with its values.
- **Rows**: **Insert a row** with a value for each column, typed as you would write it in SQL without quotes.
  Leave a box empty for the column's default, or tick **NULL**. **Edit** on a row puts its values in the form; change
  them and press **Save**, and only what you changed is saved. **Delete** removes the row.

Every drop asks first, as it can't be undone. Each change is made in the database at once, and if PostgreSQL refuses
it, what it said is shown. **Refresh** reads everything again.

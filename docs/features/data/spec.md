# Data

Where data is kept, with a tab for each kind: MQTT, with the Mosquitto brokers Joktan makes and
runs, and Database, with the PostgreSQL databases it makes and runs and a browser for them.

| Item      | Value |
|-----------|-------|
| Menu path | Data, with the brokers' and databases' locations on Settings / Container Settings |
| Status    | Implemented |
| Code      | `src/Joktan.Data` (the page and the database browser, with Npgsql); the brokers and databases themselves in `src/Joktan.Containers` |

## Purpose

Keep the things that hold data in one place, apart from the developer tools that use them. The
brokers moved here from Dev Tools / Containers, which still runs them.

## Page layout

1. Page header: title "Data", subtitle "MQTT brokers and databases".
2. Tabs along the top: **MQTT** and **Database**. The page opens on MQTT.
3. **MQTT**: while a command of the Containers page runs, a progress bar, what it is doing and
   **Cancel**, and below that any error the Containers page has that belongs to no one file, such
   as finding the engine. Then the **Mosquitto brokers** card, across the page, with where the
   project is written and that the project is also on Dev Tools / Containers with its logs. The
   form for adding or changing a broker is at most 760 wide.
4. **Database**: the same progress bar and error, then the **PostgreSQL databases** card and, below
   it, the **Browse a database** card, both across the page.

While the page is shown, the containers are read every 5 seconds, as on the Containers page, so
the brokers' states stay current. Commands go through the Containers page
(`ContainersViewModel.RunCommandAsync`), one at a time across both pages.

## MQTT: Mosquitto brokers

The **Mosquitto brokers** card lists the brokers, each with a dot and status for its container (as a service row's, from
the brokers' project), its ports, who may connect, its certificate, and its MQTT client's state
(connected green, connecting amber, failed red, otherwise grey; "No MQTT client" without one),
updated as the client's state changes. Its buttons are **Start** (`compose up -d <service>`,
writing the project and putting it on the Containers page first if needed) while it has no container or is
stopped, **Stop** (`compose stop <service>`) otherwise, **Edit** and **Remove**. A command that
fails shows why on the card. **Add broker** shows the form in place of the list; **Save** checks it,
saves the broker and writes the project; **Cancel** leaves everything as it was.

| Field | Default for a new broker | Meaning |
|-------|--------------------------|---------|
| Name | `mosquitto`, then `mosquitto-2` and so on | The compose service and its folder. Lower-case letters, digits, `-` and `_`, starting with a letter or digit, at most 40 |
| Plain MQTT port | The first port from 1883 that no other broker publishes | Published on the container's 1883. Empty for none |
| TLS port | Empty | Published on the container's 8883. Choosing a server certificate fills it in with the first free port from 8883, unless one is typed; choosing **None** empties it and the mutual TLS port |
| Mutual TLS port | Empty | Published on the container's 8884, where clients must show a certificate. Choosing whose client certificates to accept fills it in with the first free port from 8884, unless one is typed; **None: no mutual TLS** empties it |
| Username | Empty | With one, `allow_anonymous false` and a password file; without one anyone can connect |
| Password | | Kept in the credential store under `Joktan Containers`, account `mosquitto:<id>`. Empty keeps the one saved |
| Server certificate | None | A server certificate from Dev Tools / Certificates, shown on the TLS ports. Needed for either |
| Client certificates signed by | None: no mutual TLS | The CAs that signed the server certificate, or any CA on the Certificates page |

A free port is one no other broker publishes and that is not typed in the form's other ports; a
broker being changed may keep its own. Saving is refused, with the reason, for a name that is
taken or not allowed, a port that is not 1 to 65535, no port at all, a port used twice or by
another broker, a login without a password or with a colon in it, a TLS port without a server
certificate, or a certificate that has gone or is of the wrong kind.

Saving a broker that was running starts it again, with `compose up -d <service>`, so the change
takes effect. A broker that was renamed has its old container removed first, with
`compose rm --stop --force <old>`; if that fails, nothing is saved. **Remove** asks first, then
removes the container in the same way, forgets the password and deletes the broker's folder.
When the last broker goes, so do the compose file and its group.

### The project

The brokers are written to `docker-compose.yml` in `containers/mosquitto` in the data folder, as
project `joktan-mosquitto`, and the file is put on the list of compose files. For each broker:

| File | Holds |
|------|-------|
| `<name>/Dockerfile` | `FROM` the image, and copies of the files below into it, with the password file and key readable only by Mosquitto |
| `<name>/mosquitto.conf` | `log_dest stdout`, the login, and a listener for each port |
| `<name>/passwd` | `user:$7$1000$salt$hash`, PBKDF2-SHA512 as `mosquitto_passwd` writes it. Written again only when the user or password changes, so the image stays the same |
| `<name>/certs/cert.pem`, `key.pem` | The server certificate with its intermediate CAs, and its key |
| `<name>/certs/ca.pem` | For mutual TLS, the CA certificates clients' certificates must chain to |

Each service is built (`pull_policy: build`) as `localhost/<project>-<name>:latest`, restarts
unless stopped, and has a health check when it has a plain port and no login. The files are
copied into the image rather than mounted, as Podman on macOS only shares some folders with its
virtual machine. Every file starts with `# Written by Joktan`; only such files are replaced or
deleted, and files that have not changed are left alone, so starting an unchanged broker does not
recreate it.

The project is written again before every command that starts or builds it (Start, Recreate,
Rebuild and start, Build images), so a certificate made again is used. If it cannot be, such as
when a certificate has been deleted, the command does not run and the group shows why.

### Its MQTT client

Each port of each broker is a way to connect from this computer, to `localhost`:

| Port | The client uses |
|------|-----------------|
| Plain MQTT | That port, with the broker's login |
| TLS | TLS on it, with the login, trusting the CAs that signed the server certificate |
| Mutual TLS | As TLS, and showing the first client certificate on the Certificates page signed by the CA asked for |

A broker that is saved and has no client linked to it gets one, on its first port in that order,
named after it (with a number if the name is taken); so does each broker when the app starts. In
a client's settings on Dev Tools / Brokers / MQTT, **From a broker on Data / MQTT**
offers every port of every broker: choosing one fills in the host, port, login, password, TLS and
certificates, and links the client to that port; **None** unlinks it and keeps what was filled in.

Linked clients are kept up to date (`ClientProfile.BrokerId` and `BrokerListener`) whenever a
broker is saved or removed, a certificate is made, renamed or deleted, and at start-up:

- How it connects is set from its port, or the broker's first port when that one has gone. The
  rest of its tab is kept, and a connected client takes the change when it next connects.
- It is renamed with the broker if it had the broker's name and the new one is free.
- Its username and password follow the broker's, unless they were changed in the client editor
  from the broker's (`ClientProfile.OwnLogin`): then they are kept, and only the host, port, TLS
  and certificates follow. Choosing **None** and then the broker again takes its login back. The
  same holds for a proxy's target on Dev Tools / Brokers / MQTT Proxy.
- Certificates are used from copies in the client's own folder, as the client editor makes them,
  so a client never reads the folders that hold the CAs' keys.
- When the broker is removed, its clients are removed, but for the last client, which stays,
  unlinked, as the user's own. **Down** and **Remove container** leave them, as **Start** makes
  the container again.

### Where it is written

On Settings / Container Settings, **Folder** (empty for the default), **Project name** and
**Apply** move the project: its files are written to the new place, the old ones deleted and the
list of compose files changed. It is refused while the project has containers, which would be left
behind under the old name. **Mosquitto image**, `docker.io/library/eclipse-mosquitto:2` by default,
is saved once valid and used the next time a broker is started.

## Database: PostgreSQL databases

The **PostgreSQL databases** card says where the project is written and the folder new databases
keep their data in. It lists the databases, each with a dot and status for its container, how to
connect (`postgres://<user>@localhost:<port>/<database>`, selectable), and its data folder. Its
buttons are **Start**, **Stop**, **Edit** and **Remove**, as a broker's. **Add database** shows the
form in place of the list.

| Field | Default for a new database | Meaning |
|-------|----------------------------|---------|
| Name | `postgres`, then `postgres-2` and so on | The compose service. As a broker's name |
| Port | The first port from 5432 that no other database publishes | Published on the container's 5432. Needed |
| Username | `postgres` | The superuser. Lower-case letters, digits and `_`, starting with a letter or `_`, at most 63 |
| Password | | Needed. Kept in the credential store under `Joktan Containers`, account `postgres:<id>`. Empty keeps the one saved. No line breaks |
| Database | The name, with `_` for `-`, while the name is typed | The database made at first start. As the username |
| Data folder | `<data root>/<name>`, while the name is typed | The folder on this computer that holds the data, with **Browse**. A full path, not another database's |

The username, password and database are only set up by PostgreSQL the first time it starts in an
empty data folder; the form says so, and once the folder holds a database (`data/PG_VERSION`) it
says that changing them there does not change them in it. Saving a running database starts it
again; renaming removes the old container first; the data folder is never moved. **Remove** asks
first, removes the container and forgets the password, and says the data stays where it is: Joktan
never deletes a data folder.

### The databases' project

Written to `docker-compose.yml` in `containers/postgres` in the data folder, as project
`joktan-postgres`, and put on the list of compose files. Each database is a service that runs the
image (`docker.io/library/postgres:18` by default), restarts unless stopped, publishes its port, sets
`POSTGRES_USER`, `POSTGRES_DB` and `PGDATA=/var/lib/postgresql/joktan/data`, and has a
`pg_isready` health check. Its data folder is bind-mounted at `/var/lib/postgresql/joktan`, so the
files are in `<data folder>/data`: a folder below the mount, as PostgreSQL only sets up an empty one
and the system may leave files in the folder chosen. The mount's path is quoted, with `$` doubled.

The password is only in `<name>/postgres.env` (`env_file`), in double quotes with `\`, `"` and `$`
escaped, readable only by the user, in a folder only the user can open. Saving or starting writes
the project again and makes the data folders; files are only replaced when changed, and only folders
whose `postgres.env` starts with `# Written by Joktan` are deleted. With Podman or Docker Desktop
the data folder must be one the engine's virtual machine shares, such as one under the home folder.

### Browse a database

Below the list. **Database** chooses one of them, by name, and connects to `localhost` on its port
with its user, password and database; **Refresh** reads everything again. While working, a
progress bar; afterwards what was done, or what PostgreSQL or the network said, as an error.

| Part | Shows | Does |
|------|-------|------|
| Schemas | The schemas, without `pg_*` and `information_schema`; `public` first chosen | **Create** with the name typed; **Drop** the one chosen, `CASCADE` |
| Tables | The base tables of the schema chosen; the first chosen | **Create** with the name typed, with an `id bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY` column while **With an id column** is ticked; **Drop** the one chosen, `CASCADE` |
| Columns | A table of each column's name, type, whether it may be NULL, its default (or "numbered for each row" for an identity), and a primary key badge | **Add column** with a name, a type (typed or chosen from common ones), a default as SQL, and not null; **Drop** on each row, `CASCADE` |
| Rows | A page of rows, each value as text, NULL for null. Each column is as wide as its longest value on the page or its heading, from 60 to 420, with the whole value on hover. Dragging the grip at a heading's right edge sets the column's width (40 to 1600), kept for that table across pages and **Refresh**; double-clicking it fits the column again. Another table starts fitted, on its first page. Below them, "Rows 101–200 of 1,234", **First**, **Previous**, "Page 2 of 13", **Next** and **Last** when there is more than one page, and how many rows a page has: 25, 50, 100 (the default), 200 or 500 | **Edit** puts the row in the form below; **Delete** deletes it. Another page or page size ends an edit. Rows are counted with `count(*)` and read in the order of the primary key, or of `ctid` without one, so pages do not overlap; a page left empty by a delete goes back to the last one |
| Insert a row / Edit the row | A box for each column, with its type, and a NULL switch | **Insert**: empty boxes are left out, so they get their defaults. **Save**: only the columns changed are set. **Cancel** goes back to inserting |

Every drop asks first in a bar above the lists, naming what goes and that it cannot be undone.
Names are always quoted, so any name PostgreSQL accepts works; values are always parameters, sent
as text of no stated type for PostgreSQL to read as the column's type. A row is found by its
`ctid`; if it changed or went in the meantime, nothing is changed and the rows are read again. A
database that is removed, or saved with another port, user or database, is let go of or connected
to again.

### Where it is written and kept

On Settings / Container Settings, the **PostgreSQL databases** card: **Data folder**, the folder
new databases' data folders are made in (empty for `databases` in the data folder, saved as typed
once a full path; existing databases keep theirs), then **Compose folder**, **Project name** and
**Apply**, and **Image**, as for the brokers. Moving the project leaves the data where it is.

### Agent tools

`containers_databases` lists them, `containers_database_save` adds or changes one as the form does,
and `containers_database_remove` removes one. They are started with `containers_run`.

## Out of scope

- Databases other than PostgreSQL, roles and users beyond the superuser, backups, and changing a
  set-up database's password from Joktan.
- Changing a column's type or name, indexes, constraints other than a new table's primary key, and
  running SQL typed by hand, beyond a column's type and default.
- Brokers other than Mosquitto, bridges, persistence and access control lists.

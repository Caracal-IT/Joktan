# Users

**Users** (top-level menu entry) keeps the people who answer workflows' questions, and the roles they have. The code
is in `src/Joktan.Users`; its help topic is `users`.

## Requirements

Asked for on 2026-11-07:

- A new module with a PostgreSQL database, and a **Create** that adds that database on Data / Database.
- Users with a first name, last name, description, notes and a role.
- Roles assigned to users, with a default administrator and recommended roles.
- A workflow's user input step is linked to a role, or to a specific user; choosing a user gives a warning that it is
  bad practice.
- A user input step can optionally be escalated, with a time after which it is.
- Then, after trying it: the database's username and password are set on the create form, not chosen by Joktan; and
  users have a username, a password and an email address, stored in the database.

## Storage

- `UserSettings` (`settings/users.json`) holds the id of the database on Data / Database the users are kept in; it is
  chosen on **Settings / User Settings**, which also has **Create database**.
- **Create database** opens a form (`CreateDatabaseViewModel`): name and port (free ones filled in), username
  (`postgres` to start with) and password, typed twice. `UserDirectory.CreateDatabaseAsync` calls
  `PostgresDatabasesViewModel.CreateAsync(name, port, username, password)`, which checks them as the Data / Database
  form does, adds the database with the default data folder, keeps the password in the credential store, writes the
  compose project and starts it. The directory then retries connecting every 2
  seconds, for up to 2 minutes, while the server sets up its data folder.
- `PostgresUserStore` keeps the schema `joktan` with the tables `roles` (`id`, `name` unique, `description`,
  `built_in`, `created`, `changed`) and `users` (`id`, `username` unique ignoring case, `email`, `password_hash`, `first_name`, `last_name`, `description`, `notes`, `role_id`
  referencing `roles` with `ON DELETE RESTRICT`, `created`, `changed`). Statements go through
  `IDatabaseConnection.RunAsync`, each giving one text value (rows as JSON); values are parameters.
- Tables made before users had a username, email and password get the columns added; a missing username is made from
  the name and id.
- Setting up (`CREATE … IF NOT EXISTS`) runs the first time a database is used in a session. A database with no roles
  is seeded with the recommended roles and the **Default Administrator** user, who has the Administrator role.

## Roles

The recommended roles are Administrator (built in), Manager, Approver, Reviewer, Operator, Support and Viewer, each
with a description. **Add recommended roles** adds those missing by name. Rules, kept by `UserDirectory`:

- Role names are required and unique, ignoring case.
- The Administrator role cannot be removed or renamed (the store keeps its name too); its description can change.
- A role that users have cannot be removed.

## Users

Each user has a username (required, unique ignoring case, letters, digits and `. _ - @`, up to 64), an email address
(optional, checked for one `@` and a dot after it), a password, a first and last name (both required), a description,
notes and exactly one role. A new user is given the first role that is not Administrator.

Passwords are required for a new user and typed twice. They are hashed in Joktan (`PasswordHasher`: PBKDF2-SHA256,
210,000 iterations, 16-byte salt, written `pbkdf2-sha256$iterations$salt$hash`) and only the hash is sent; reads give
`has_password`, never the hash. Saving without a password keeps the hash there is. `PostgresUserStore.CheckPasswordAsync`
checks a username and password, for signing in later. The default administrator, `admin`, starts without a password and
is marked **No password** in the list. The last user with the Administrator role cannot be removed or given another
role.

## Workflows

`UserDirectory` keeps the roles and users as last read; the app passes them to the workflow engine through
`AppWorkflowServices.People`, and the designer re-reads them when they change. See the user input section of
[the workflows spec](../workflows/spec.md) for assignment and escalation.

## Pages

- **Users**: with no database, a card explaining and offering **Create database** (the form) and **User Settings**. With
  one that cannot be reached, a card with **Start database**, **Refresh** and **User Settings**. A database removed on
  Data / Database is forgotten, and the page offers to create one again. With one,
  **Refresh**, the connection, and two tabs: **Users** (list, Add user, Edit, Remove with confirmation) and **Roles**
  (list with user counts, Add role, Add recommended roles, Edit, Remove with confirmation, disabled for the built-in
  role and roles in use). Edits are inline forms, as on Data / Database.
- **Settings / User Settings**: the database drop-down (None, then the databases on Data / Database), the connection,
  and **Create database** with the same form.

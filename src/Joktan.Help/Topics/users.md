# Users

**Users** holds the people who answer workflows' questions, and their roles. Each user has a first name, a last name,
a username, an email address, a password, a description, notes and one role. [Workflow](help:workflow-steps) user input steps are assigned to a role, or to one
user, and can be escalated to another when nobody answers in time.

## The users database

The users and roles are kept in a PostgreSQL database on [Data / Database](help:data), in the schema `joktan`
(tables `roles` and `users`). The first time you open Users there is none: press **Create database** and fill in its
name and port (the next free ones are filled in) and the database's own **username** and **password**, twice. Joktan
adds it on Data / Database, keeps the password in the credential store, starts its container and waits for it to be
up. It is then given the recommended roles and a default administrator. Creating needs the container engine running,
as for any database on Data / Database.

To keep the users in a database that is already on Data / Database, choose it on **Settings / User Settings**. Its
tables are made the first time it is used, and if it has no roles yet it is given the recommended roles and a default
administrator too. If the database is stopped, Users says it cannot be reached: start it on Data / Database and press
**Refresh**, or press **Start database** on the card Users shows. If the database is removed on Data / Database, Users
offers to create one again.

## Users

The **Users** tab lists everyone, with their role, username and email. **Add user** asks for:

- a **username**, which is unique whatever its case, of letters, digits and `. _ - @`;
- an **email** address, which may be left empty;
- a **password**, typed twice. It is kept only as a salted hash (PBKDF2-SHA256), never as the password itself, and is
  never shown again. When editing, leave it empty to keep the one set;
- a first and last name, a role, a description (such as "Head of finance") and notes.

**Edit** changes a user and **Remove** removes them after you confirm. A user without a password is marked
**No password**.

The **Default Administrator** (username `admin`) is made with the database, without a password: edit them to set one. Rename them to the person who looks after Joktan, or add
that person with the Administrator role. Someone always has the Administrator role: the last user who has it cannot
be removed or given another role.

## Roles

The **Roles** tab lists the roles and how many users have each. The recommended roles are:

- **Administrator**: looks after Joktan, its workflows, users and roles. It is built in, so it cannot be renamed or
  removed, but its description can change.
- **Manager**: makes the decisions a workflow cannot, and takes questions that were escalated.
- **Approver**: approves or rejects what a workflow asks about.
- **Reviewer**: checks work before it goes further.
- **Operator**: runs things day to day, and answers routine questions.
- **Support**: answers questions about customers and their requests.
- **Viewer**: sees how things are going, but is not asked to act.

**Add role** adds your own; role names are unique, whatever their case. A role that users have cannot be removed:
give them another role first. If you removed recommended roles, **Add recommended roles** puts back those missing.

## Assigning questions

In the [workflow designer](help:workflows), a user input step's **Assigned to** offers the roles and users read from
here. Assign questions to roles: a question for one user waits for nobody when they are away or leave, so choosing a
user shows a warning that it is bad practice. The same goes for **Escalate to**.

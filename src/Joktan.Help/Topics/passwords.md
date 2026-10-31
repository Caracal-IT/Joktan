# Passwords

A password manager for websites, servers, Wi-Fi and accounts. Entries are kept in folders, in an
encrypted vault file or in the operating system's keychain, as you choose.

MQTT clients' passwords are separate: see [Clients and brokers](help:mqtt-clients).

## Choosing where passwords are kept

The first time you open the page, it asks. You can change your mind later and move the entries
across.

- **An encrypted vault file** is one file protected by a master password of at least 8 characters.
  It locks itself when unused, and can go in a synced folder or be backed up. **If you forget the
  master password, the passwords cannot be recovered.** Choosing a vault that already exists opens
  it with its own master password.
- **The system keychain** keeps each password in the macOS Keychain, Windows Credential Manager or
  the Secret Service on Linux, protected by your login, with no master password. The list of entries
  stays in the data folder. It is offered only where the computer has one.

## Using entries

- **New entry** and **New folder** add to the selected folder, or to the folder of the selected entry.
- Select an entry to see it. **Show** reveals the password, and **Copy** copies the username, password
  or website. A copied password is taken off the clipboard after 30 seconds, unless you have copied
  something else since.
- **Open** opens the website in the browser, adding `https://` when no scheme is given.
- **Search** finds entries by title, username, website or notes.

## Editing

**Edit** shows the fields, and a **Folder** list to move the entry or folder. **Save** keeps the
changes, and **Cancel** drops them. Selecting something else while editing saves the changes.

**Generate** replaces the password with a random one of the **Length** you choose, from 8 to 128, with
lower- and upper-case letters, digits and, if you switch on **Symbols**, symbols. It leaves out
characters that are easy to mix up.

**Delete** deletes the entry, or a folder with everything in it, once you confirm.

## Configuring it

Press **Storage** at the top of the page.

- **Lock the vault when unused**: after 1, 5, 15 or 30 minutes, 1 hour, or never. The default is 5
  minutes. Moving the pointer or typing on the page counts as use.
- **Move the entries to**: a vault file or the keychain. A vault that does not exist yet is made with
  a new master password; one that exists asks for its own, and the entries are added to it. Every entry
  is copied first, and only then removed from where it was, including the old vault file. If the copy
  fails, nothing is removed.
- **Change the master password**: give the current one and the new one. The vault is encrypted again.

## Locking

**Lock** closes the vault at once. Locking saves an entry being edited, then forgets the entries and
the key. After a restart the vault is locked.

A locked vault asks for its master password. **Use another vault or the keychain** goes back to
choosing the storage, without moving anything.

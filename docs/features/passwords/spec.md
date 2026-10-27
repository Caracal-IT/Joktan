# Passwords

A password manager for any password worth keeping: websites, servers, Wi-Fi, accounts. Entries
are kept in folders, in an encrypted vault file or in the operating system's keychain, as the
user chooses.

| Item      | Value       |
|-----------|-------------|
| Menu path | Passwords   |
| Status    | Implemented |

It is separate from the MQTT clients' passwords, which stay in the keychain under "Joktan MQTT".

## Storage

The first time the page is opened it asks where the passwords should be kept:

| Storage            | How it is protected | Where |
|--------------------|---------------------|-------|
| Encrypted vault file | A master password of at least 8 characters. The whole vault (folders, entries and passwords) is JSON encrypted with AES-256-GCM; the key is made from the master password with PBKDF2-SHA256, 600,000 iterations and a random 16-byte salt. The format, salt and iteration count are authenticated with the data. A forgotten master password cannot be recovered. | One file, `passwords.vault` in the data folder by default, or any file chosen with **Browse**, such as one in a synced folder |
| System keychain    | The computer's login. No master password. | Each password in the macOS Keychain, Windows Credential Manager or the Secret Service (through `secret-tool`), filed under "Joktan Passwords" and the entry's ID. The folders and entries, without passwords, in `passwords.json` in the data folder |

The keychain is offered only where the computer has one. Choosing a vault file that already
exists opens it with its own master password instead of making a new one.

The choice is kept in `passwords-settings.json` in the data folder. Files are written to a
temporary file and then moved over the old one, so a failed write never leaves half a file; on
Linux and macOS only the user can read them.

## Locking

An open vault locks itself after a time without use: 1, 5 (the default), 15, 30 or 60 minutes,
or never, set under **Storage**. Moving the pointer or typing on the page counts as use. **Lock**
locks it at once. Locking saves an entry being edited, then forgets the entries and the key.
After a restart the vault is locked.

A locked vault shows the file and asks for the master password. **Use another vault or the
keychain** goes back to choosing the storage, without moving anything.

## Page layout

1. Page header: title "Passwords", and while open **Storage**, **Lock** (vault only), **New
   folder** and **New entry**.
2. While **Storage** is open, a band with where the passwords are, the lock time, moving the
   entries and changing the master password.
3. On the left, a search box and the folders and entries as a tree: folders first, then entries,
   each by name, with the username beside an entry's title.
4. On the right, the selected folder or entry, in view mode or edit mode.

## Entries and folders

An entry has a title, a username or email, a password, a website and notes, and the times it was
made and last changed. In view mode the password is hidden until **Show**. **Copy** copies the
username, password or website; a copied password is taken off the clipboard after 30 seconds,
unless something else was copied since. **Open** opens the website in the browser, adding
`https://` when no scheme is given; only http and https addresses open.

**Edit** shows the fields, a **Folder** list to move the entry or folder, and for an entry
**Generate**, which makes a random password of the length chosen (8 to 128, 20 by default), with
lower- and upper-case letters, digits and, if wanted, symbols, at least one of each, leaving out
characters that are easy to mix up. **Save** keeps the changes; **Cancel** drops them, and drops
a new entry or folder altogether. Selecting something else while editing saves the changes. A new
entry left with nothing in it is not kept.

**New entry** and **New folder** add to the selected folder, or the folder of the selected
entry. **Delete** deletes the entry, or the folder with everything in it, once confirmed.

**Search** finds entries whose title, username, website or notes contain the text, and lists them
with their folder.

## Moving the entries

Under **Storage**, **Move entries** moves every entry to a vault file or the keychain. Moving to a
vault that does not exist yet makes it with a new master password; moving to one that exists
asks for its master password and adds the entries to it. Every entry is copied first; only then
are they removed from where they were: the vault file is deleted, or the keychain items and
`passwords.json` are. If the copy fails, nothing is removed.

**Change** sets a new master password, once the current one is given. The vault is encrypted
again with a new salt.

## Not built yet

- Importing from or exporting to other password managers
- Filling passwords into other apps or browsers
- Password history and strength warnings

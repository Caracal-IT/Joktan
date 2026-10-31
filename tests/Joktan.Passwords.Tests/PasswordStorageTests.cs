using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Passwords.Tests;

public class VaultStorageTests : IDisposable
{
    private const string Master = "correct horse";

    private readonly TempFolder _folder = new();
    private readonly string _path;

    public VaultStorageTests()
    {
        _path = _folder.At("sub", "passwords.vault");
    }

    public void Dispose() => _folder.Dispose();

    private VaultStorage Created()
    {
        var vault = new VaultStorage(_path);
        vault.Create(Master);
        return vault;
    }

    private void ChangeFile(Action<JsonObject> change)
    {
        var file = JsonNode.Parse(File.ReadAllText(_path))!.AsObject();
        change(file);
        File.WriteAllText(_path, file.ToJsonString());
    }

    [Fact]
    public void Describes_itself()
    {
        var vault = new VaultStorage(_path);

        Assert.Equal(PasswordStorageKind.Vault, vault.Kind);
        Assert.Equal(_path, vault.Location);
        Assert.False(vault.Exists);
        Assert.False(vault.IsOpen);
    }

    [Fact]
    public void Creating_makes_an_empty_open_vault()
    {
        var vault = Created();

        Assert.True(vault.Exists);
        Assert.True(vault.IsOpen);
        Assert.Empty(vault.Root.AllEntries());
    }

    [Fact]
    public void What_is_saved_comes_back_with_the_master_password()
    {
        var vault = Created();
        var entry = new PasswordEntry { Title = "Mail", Username = "me", Url = "u", Notes = "n" };
        vault.WritePassword(entry, "s3cret");
        vault.Root.Folders.Add(new PasswordFolder { Name = "Work", Entries = [entry] });
        vault.Save();
        vault.Close();

        var opened = new VaultStorage(_path);

        Assert.True(opened.Open(Master));
        Assert.True(opened.IsOpen);
        var read = opened.Root.AllEntries().Single();
        Assert.Equal(("Mail", "me", "u", "n"), (read.Title, read.Username, read.Url, read.Notes));
        Assert.Equal("s3cret", opened.ReadPassword(read));
        Assert.Equal("Work", opened.Root.Folders[0].Name);
    }

    [Fact]
    public void The_file_does_not_show_what_is_in_it()
    {
        var vault = Created();
        vault.Root.Entries.Add(new PasswordEntry { Title = "MailTitle", Password = "s3cret" });

        vault.Save();

        var text = File.ReadAllText(_path);
        Assert.DoesNotContain("MailTitle", text);
        Assert.DoesNotContain("s3cret", text);
        Assert.Contains("joktan-vault", text);
    }

    [Fact]
    public void Each_save_encrypts_differently()
    {
        var vault = Created();
        var first = JsonNode.Parse(File.ReadAllText(_path))!["Nonce"]!.GetValue<string>();

        vault.Save();

        Assert.NotEqual(first, JsonNode.Parse(File.ReadAllText(_path))!["Nonce"]!.GetValue<string>());
    }

    [Fact]
    public void The_file_is_only_for_the_user_to_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are a Linux and macOS matter.");

        Created();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void A_wrong_password_does_not_open_it()
    {
        Created();
        var vault = new VaultStorage(_path);

        Assert.False(vault.Open("wrong"));
        Assert.False(vault.IsOpen);
        Assert.Throws<InvalidOperationException>(vault.Save);
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("Salt")]
    public void A_changed_file_does_not_open(string property)
    {
        Created();
        ChangeFile(file =>
        {
            var bytes = Convert.FromBase64String(file[property]!.GetValue<string>());
            bytes[0] ^= 0xFF;
            file[property] = Convert.ToBase64String(bytes);
        });

        Assert.False(new VaultStorage(_path).Open(Master));
    }

    [Fact]
    public void The_iterations_cannot_be_lowered_without_it_showing()
    {
        Created();
        ChangeFile(file => file["Iterations"] = 1);

        Assert.False(new VaultStorage(_path).Open(Master));
    }

    [Theory]
    [InlineData("Format", "other", "This is not a vault file Joktan can read.")]
    [InlineData("Kdf", "other", "This is not a vault file Joktan can read.")]
    [InlineData("Salt", "!!!", "The vault file is damaged.")]
    [InlineData("Nonce", "AAAA", "The vault file is damaged.")]
    [InlineData("Tag", "AAAA", "The vault file is damaged.")]
    public void Says_when_a_file_cannot_be_read_as_a_vault(string property, string value, string message)
    {
        Created();
        ChangeFile(file => file[property] = value);

        var error = Assert.Throws<PasswordStorageException>(() => new VaultStorage(_path).Open(Master));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Says_when_the_vault_is_from_a_newer_version()
    {
        Created();
        ChangeFile(file => file["Version"] = 2);

        var error = Assert.Throws<PasswordStorageException>(() => new VaultStorage(_path).Open(Master));

        Assert.Equal("The vault was written by a newer version of Joktan.", error.Message);
    }

    [Fact]
    public void Refuses_no_iterations()
    {
        Created();
        ChangeFile(file => file["Iterations"] = 0);

        Assert.Throws<PasswordStorageException>(() => new VaultStorage(_path).Open(Master));
    }

    [Theory]
    [InlineData("not json", "This is not a vault file, or it is damaged.")]
    [InlineData("null", "The vault file is empty.")]
    [InlineData("{}", "This is not a vault file Joktan can read.")]
    public void Says_when_the_file_is_not_a_vault(string content, string message)
    {
        var path = _folder.Write("other.vault", content);

        var error = Assert.Throws<PasswordStorageException>(() => new VaultStorage(path).Open(Master));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Says_when_the_file_is_missing()
    {
        var error = Assert.Throws<PasswordStorageException>(() => new VaultStorage(_path).Open(Master));

        Assert.StartsWith("The vault file cannot be read", error.Message);
    }

    [Fact]
    public void Closing_forgets_the_entries_and_the_key()
    {
        var vault = Created();
        vault.Root.Entries.Add(new PasswordEntry());

        vault.Close();

        Assert.False(vault.IsOpen);
        Assert.Empty(vault.Root.Entries);
        Assert.Throws<InvalidOperationException>(vault.Save);
    }

    [Fact]
    public void A_vault_opened_again_keeps_its_master_password_when_saved()
    {
        Created().Close();
        var vault = new VaultStorage(_path);
        vault.Open(Master);
        vault.Root.Entries.Add(new PasswordEntry { Title = "Added" });

        vault.Save();

        var again = new VaultStorage(_path);
        Assert.True(again.Open(Master));
        Assert.Equal("Added", again.Root.Entries.Single().Title);
    }

    [Fact]
    public void Changing_the_master_password_replaces_the_old_one()
    {
        var vault = Created();
        vault.Root.Entries.Add(new PasswordEntry { Title = "Kept" });

        vault.ChangeMasterPassword("new password");

        Assert.False(new VaultStorage(_path).Open(Master));
        var opened = new VaultStorage(_path);
        Assert.True(opened.Open("new password"));
        Assert.Equal("Kept", opened.Root.Entries.Single().Title);
    }

    [Fact]
    public void Destroying_removes_the_file()
    {
        var vault = Created();

        vault.Destroy();

        Assert.False(vault.Exists);
        Assert.False(vault.IsOpen);
    }

    [Fact]
    public void A_password_of_an_entry_without_one_is_empty()
    {
        Assert.Equal("", new VaultStorage(_path).ReadPassword(new PasswordEntry()));
    }
}

public class KeychainStorageTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeCredentialStore _credentials = new();
    private readonly string _index;

    public KeychainStorageTests()
    {
        _index = _folder.At("sub", KeychainStorage.FileName);
    }

    public void Dispose() => _folder.Dispose();

    private KeychainStorage Created()
    {
        var storage = new KeychainStorage(_credentials, _index);
        storage.Create(null);
        return storage;
    }

    [Fact]
    public void Describes_itself()
    {
        var storage = new KeychainStorage(_credentials, _index);

        Assert.Equal(PasswordStorageKind.Keychain, storage.Kind);
        Assert.Equal(_index, storage.Location);
        Assert.True(storage.IsAvailable);
        Assert.False(storage.Exists);
    }

    [Fact]
    public void Cannot_be_made_without_a_keychain()
    {
        var storage = new KeychainStorage(new FakeCredentialStore(isAvailable: false), _index);

        Assert.False(storage.IsAvailable);
        Assert.Throws<PasswordStorageException>(() => storage.Create(null));
        Assert.False(storage.Exists);
    }

    [Fact]
    public void Passwords_go_to_the_keychain_under_the_entrys_ID_and_never_in_the_file()
    {
        var storage = Created();
        var entry = new PasswordEntry { Title = "Mail", Password = "in memory" };
        storage.Root.Entries.Add(entry);

        storage.WritePassword(entry, "s3cret");
        storage.Save();

        Assert.Equal("s3cret", _credentials.Saved[entry.Id]);
        Assert.Equal("s3cret", storage.ReadPassword(entry));
        var text = File.ReadAllText(_index);
        Assert.Contains("Mail", text);
        Assert.DoesNotContain("s3cret", text);
        Assert.DoesNotContain("in memory", text);
        Assert.Equal("in memory", entry.Password);
    }

    [Fact]
    public void Entries_come_back_when_opened_again_and_any_master_password_will_do()
    {
        var storage = Created();
        storage.Root.Folders.Add(new PasswordFolder { Name = "Work", Entries = [new PasswordEntry { Title = "Mail" }] });
        storage.Save();
        storage.Close();

        var opened = new KeychainStorage(_credentials, _index);

        Assert.True(opened.Open("anything"));
        Assert.Equal("Mail", opened.Root.AllEntries().Single().Title);
    }

    [Fact]
    public void A_password_the_keychain_does_not_have_is_empty()
    {
        Assert.Equal("", Created().ReadPassword(new PasswordEntry()));
    }

    [Fact]
    public void Says_when_a_password_cannot_be_kept()
    {
        _credentials.FailWrites = true;

        Assert.Throws<PasswordStorageException>(() => Created().WritePassword(new PasswordEntry(), "x"));
    }

    [Fact]
    public void Deleting_passwords_is_done_at_once()
    {
        var storage = Created();
        var gone = new PasswordEntry();
        var kept = new PasswordEntry();
        storage.WritePassword(gone, "a");
        storage.WritePassword(kept, "b");

        storage.DeletePasswords([gone]);

        Assert.False(_credentials.Saved.ContainsKey(gone.Id));
        Assert.True(_credentials.Saved.ContainsKey(kept.Id));
    }

    [Theory]
    [InlineData("not json", "The list of entries is damaged.")]
    public void Says_when_the_list_is_damaged(string content, string message)
    {
        var path = _folder.Write("list.json", content);

        var error = Assert.Throws<PasswordStorageException>(() => new KeychainStorage(_credentials, path).Open(null));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Says_when_the_list_is_missing()
    {
        var error = Assert.Throws<PasswordStorageException>(() => new KeychainStorage(_credentials, _index).Open(null));

        Assert.StartsWith("The list of entries cannot be read", error.Message);
    }

    [Fact]
    public void Destroying_removes_the_passwords_and_the_file()
    {
        var storage = Created();
        var entry = new PasswordEntry();
        storage.Root.Folders.Add(new PasswordFolder { Entries = [entry] });
        storage.WritePassword(entry, "a");

        storage.Destroy();

        Assert.Empty(_credentials.Saved);
        Assert.False(storage.Exists);
        Assert.False(storage.IsOpen);
    }
}

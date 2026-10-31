using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Joktan.Settings.Backup;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Backup;

public class BackupArchiveTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Zip => _temp.At("out/backup.zip");

    private string Write(string relative, string text)
    {
        var path = _temp.At(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static BackupItem Item(string id, params BackupRoot[] roots) => new(id, id, roots);

    private ManifestRoot Root(string id) => BackupArchive.Read(Zip).Items.Single(i => i.Id == id).Roots[0];

    [Fact]
    public void A_backup_is_a_real_zip_of_the_files()
    {
        Write("notes/a.md", "note a");
        Write("notes/deep/b.md", "note b");

        var result = BackupArchive.Create(Zip, [Item("notes", new BackupRoot("notes", _temp.At("notes")))], "2.0.0");

        Assert.Equal(2, result.FileCount);
        Assert.Empty(result.Skipped);
        using var zip = ZipFile.OpenRead(Zip);
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("deep/b.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Says_what_it_holds()
    {
        Write("notes/a.md", "note a");
        Write("dash.json", "{}");

        BackupArchive.Create(Zip, [
            Item("notes", new BackupRoot("notes", _temp.At("notes"))),
            Item("dashboards", new BackupRoot("file", _temp.At("dash.json"), IsFile: true)),
        ], "2.0.0");

        var manifest = BackupArchive.Read(Zip);
        Assert.Equal("2.0.0", manifest.AppVersion);
        Assert.Equal(["notes", "dashboards"], manifest.Items.Select(i => i.Id));
        Assert.Equal(1, manifest.Items[1].FileCount);
        Assert.True(manifest.Items[1].Roots[0].IsFile);
    }

    [Fact]
    public void What_is_missing_is_reported_and_not_fatal()
    {
        var result = BackupArchive.Create(Zip, [Item("notes", new BackupRoot("notes", _temp.At("gone")))]);

        Assert.Equal(0, result.FileCount);
        Assert.Contains("nothing found", Assert.Single(result.Skipped));
        Assert.True(File.Exists(Zip));
    }

    [Fact]
    public void Exclusions_and_dot_files_stay_out()
    {
        Write("data/mqtt.json", "keep");
        Write("data/notes/a.md", "other");
        Write("data/notes-location.json", "here");
        Write("data/.git/config", "not ours");
        Write("data/.DS_Store", "not ours");
        var exclude = new[] { _temp.At("data/notes"), _temp.At("data/notes-location.json") };

        var result = BackupArchive.Create(Zip, [Item("settings", new BackupRoot("data", _temp.At("data"), false, exclude))]);

        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public void The_backup_folder_is_not_backed_up_into_itself()
    {
        Write("data/a.txt", "a");
        Write("data/backups/old.zip", "old");

        var result = BackupArchive.Create(_temp.At("data/backups/new.zip"), [Item("data", new BackupRoot("data", _temp.At("data")))], exclude: [_temp.At("data/backups")]);

        Assert.Equal(1, result.FileCount);
    }

    [Fact]
    public void A_failed_backup_leaves_nothing_behind()
    {
        Write("blocker", "a file where the folder should be");

        Assert.Throws<BackupException>(() => BackupArchive.Create(_temp.At("blocker/backup.zip"), []));

        Assert.False(File.Exists(_temp.At("blocker/backup.zip.partial")));
    }

    [Fact]
    public void A_restore_puts_a_folder_back_exactly()
    {
        Write("notes/a.md", "original");
        Write("notes/deep/b.md", "b");
        BackupArchive.Create(Zip, [Item("notes", new BackupRoot("notes", _temp.At("notes")))]);
        Write("notes/a.md", "changed");
        File.Delete(_temp.At("notes/deep/b.md"));
        Write("notes/new/since.md", "made since");

        BackupArchive.RestoreRoot(Zip, Root("notes").Folder, _temp.At("notes"), false);

        Assert.Equal("original", File.ReadAllText(_temp.At("notes/a.md")));
        Assert.Equal("b", File.ReadAllText(_temp.At("notes/deep/b.md")));
        Assert.False(File.Exists(_temp.At("notes/new/since.md")));
        Assert.False(Directory.Exists(_temp.At("notes/new")));
    }

    [Fact]
    public void A_restore_leaves_what_is_not_its_own()
    {
        Write("data/mqtt.json", "original");
        var exclude = new[] { _temp.At("data/notes"), _temp.At("data/notes-location.json") };
        BackupArchive.Create(Zip, [Item("settings", new BackupRoot("data", _temp.At("data"), false, exclude))]);
        Write("data/notes/a.md", "a note");
        Write("data/notes-location.json", "here");
        Write("data/.git/config", "not ours");
        Write("data/since.json", "made since");

        BackupArchive.RestoreRoot(Zip, Root("settings").Folder, _temp.At("data"), false, exclude);

        Assert.True(File.Exists(_temp.At("data/notes/a.md")));
        Assert.True(File.Exists(_temp.At("data/notes-location.json")));
        Assert.True(File.Exists(_temp.At("data/.git/config")));
        Assert.False(File.Exists(_temp.At("data/since.json")));
    }

    [Fact]
    public void A_restore_goes_to_where_it_is_kept_now()
    {
        Write("old/a.md", "a");
        BackupArchive.Create(Zip, [Item("notes", new BackupRoot("notes", _temp.At("old")))]);

        BackupArchive.RestoreRoot(Zip, Root("notes").Folder, _temp.At("new/place"), false);

        Assert.Equal("a", File.ReadAllText(_temp.At("new/place/a.md")));
    }

    [Fact]
    public void A_file_is_restored_to_its_path()
    {
        var vault = Write("passwords.vault", "original");
        BackupArchive.Create(Zip, [Item("passwords", new BackupRoot("vault", vault, IsFile: true))]);
        Write("passwords.vault", "changed");

        BackupArchive.RestoreRoot(Zip, Root("passwords").Folder, vault, true);

        Assert.Equal("original", File.ReadAllText(vault));
    }

    [Fact]
    public void A_zip_cannot_write_outside_where_it_is_restored()
    {
        Write("notes/a.md", "a");
        Directory.CreateDirectory(_temp.At("out"));
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("joktan-backup.json").Open()))
                writer.Write("""{"Version":1,"Items":[]}""");
            using (var writer = new StreamWriter(zip.CreateEntry("data/0/0/../../../../escaped.txt").Open()))
                writer.Write("bad");
        }

        var error = Assert.Throws<BackupException>(() => BackupArchive.RestoreRoot(Zip, "data/0/0", _temp.At("notes"), false));

        Assert.Contains("outside", error.Message);
        Assert.Equal("a", File.ReadAllText(_temp.At("notes/a.md")));
    }

    [Fact]
    public void Something_that_is_not_a_backup_is_refused()
    {
        var plain = _temp.At("plain.zip");
        using (ZipFile.Open(plain, ZipArchiveMode.Create))
        {
        }

        Assert.Contains("not a Joktan backup", Assert.Throws<BackupException>(() => BackupArchive.Read(plain)).Message);
        Assert.Throws<BackupException>(() => BackupArchive.Read(Write("text.zip", "not a zip")));
    }

    [Fact]
    public void A_backup_from_a_newer_app_is_refused()
    {
        Directory.CreateDirectory(_temp.At("out"));
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("joktan-backup.json").Open()))
            writer.Write("""{"Version":99}""");

        Assert.Contains("newer", Assert.Throws<BackupException>(() => BackupArchive.Read(Zip)).Message);
    }

    [Fact]
    public void Damage_is_found_before_anything_relies_on_it()
    {
        Write("notes/a.txt", string.Concat(Enumerable.Range(0, 5000).Select(i => $"line {i * 7919}\n")));
        BackupArchive.Create(Zip, [Item("notes", new BackupRoot("notes", _temp.At("notes")))]);
        var bytes = File.ReadAllBytes(Zip);
        for (var i = 200; i < 400; i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(Zip, bytes);

        Assert.Throws<BackupException>(() => BackupArchive.Verify(Zip));
    }

    [Fact]
    public void Only_the_oldest_backups_of_that_kind_go()
    {
        var folder = _temp.At("backups");
        Directory.CreateDirectory(folder);
        for (var day = 1; day <= 4; day++)
            File.WriteAllText(Path.Combine(folder, $"{BackupArchive.BackupPrefix}2026-10-0{day}-120000.zip"), "");
        File.WriteAllText(Path.Combine(folder, $"{BackupArchive.SafetyPrefix}2026-01-01-120000.zip"), "");
        File.WriteAllText(Path.Combine(folder, "my-own.zip"), "");

        var deleted = BackupArchive.Prune(folder, BackupArchive.BackupPrefix, 2);

        Assert.Equal(2, deleted.Count);
        var left = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["joktan-backup-2026-10-03-120000.zip", "joktan-backup-2026-10-04-120000.zip", "joktan-safety-2026-01-01-120000.zip", "my-own.zip"], left);
    }

    [Fact]
    public void Keeping_none_still_keeps_the_newest()
    {
        var folder = _temp.At("backups");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{BackupArchive.BackupPrefix}a.zip"), "");

        BackupArchive.Prune(folder, BackupArchive.BackupPrefix, 0);

        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void A_new_name_is_never_one_in_use()
    {
        var folder = _temp.At("backups");
        Directory.CreateDirectory(folder);
        var time = new DateTime(2026, 10, 31, 14, 5, 9);
        var first = BackupArchive.NewPath(folder, BackupArchive.BackupPrefix, time);
        File.WriteAllText(first, "");

        Assert.EndsWith("joktan-backup-2026-10-31-140509.zip", first);
        Assert.EndsWith("joktan-backup-2026-10-31-140509-2.zip", BackupArchive.NewPath(folder, BackupArchive.BackupPrefix, time));
    }
}

[Collection(ProcessStateCollection.Name)]
public class PendingRestoreTests : DataFolderTest
{
    private readonly TempFolder _temp = new();

    private (string Zip, string Folder, string Notes) MakeBackup()
    {
        var notes = _temp.At("notes");
        Directory.CreateDirectory(notes);
        File.WriteAllText(Path.Combine(notes, "a.md"), "original");
        var zip = _temp.At("backup.zip");
        BackupArchive.Create(zip, [new BackupItem("notes", "Notes", [new BackupRoot("notes", notes)])]);
        return (zip, BackupArchive.Read(zip).Items[0].Roots[0].Folder, notes);
    }

    private static PendingRestore Plan(string zip, string folder, string target)
        => new()
        {
            ZipPath = zip,
            SafetyPath = "safety.zip",
            ProcessId = Environment.ProcessId,
            Steps = [new RestoreStep { Name = "Notes", ZipFolder = folder, Target = target }],
        };

    [Fact]
    public void With_nothing_waiting_nothing_happens()
    {
        PendingRestore.ApplyIfAny();

        Assert.Null(PendingRestore.TakeOutcome());
    }

    [Fact]
    public void What_is_waiting_is_put_back_once_and_says_so_once()
    {
        var (zip, folder, notes) = MakeBackup();
        File.WriteAllText(Path.Combine(notes, "a.md"), "changed");

        Plan(zip, folder, notes).Save();
        PendingRestore.ApplyIfAny();

        Assert.Equal("original", File.ReadAllText(Path.Combine(notes, "a.md")));
        var outcome = PendingRestore.TakeOutcome();
        Assert.Null(outcome!.Error);
        Assert.Equal(["Notes"], outcome.Restored);
        Assert.Equal("safety.zip", outcome.SafetyPath);
        Assert.Null(PendingRestore.TakeOutcome());

        File.WriteAllText(Path.Combine(notes, "a.md"), "changed again");
        PendingRestore.ApplyIfAny();
        Assert.Equal("changed again", File.ReadAllText(Path.Combine(notes, "a.md")));
    }

    [Fact]
    public void A_restore_that_fails_changes_nothing_says_why_and_does_not_try_again()
    {
        var (zip, folder, notes) = MakeBackup();
        File.WriteAllText(Path.Combine(notes, "a.md"), "changed");
        File.WriteAllText(zip, "damaged");

        Plan(zip, folder, notes).Save();
        PendingRestore.ApplyIfAny();

        Assert.Equal("changed", File.ReadAllText(Path.Combine(notes, "a.md")));
        Assert.NotNull(PendingRestore.TakeOutcome()!.Error);
        PendingRestore.ApplyIfAny();
        Assert.Null(PendingRestore.TakeOutcome());
    }

    [Fact]
    public void A_withdrawn_restore_does_not_run()
    {
        var (zip, folder, notes) = MakeBackup();
        File.WriteAllText(Path.Combine(notes, "a.md"), "changed");

        Plan(zip, folder, notes).Save();
        PendingRestore.Cancel();
        PendingRestore.ApplyIfAny();

        Assert.Equal("changed", File.ReadAllText(Path.Combine(notes, "a.md")));
    }

    [Fact]
    public void A_plan_that_cannot_be_read_says_so_and_goes()
    {
        Data.Write("restore-pending.json", "not json");

        PendingRestore.ApplyIfAny();

        Assert.Contains("could not be started", PendingRestore.TakeOutcome()!.Error);
        Assert.False(File.Exists(Data.At("restore-pending.json")));
    }
}

[Collection(ProcessStateCollection.Name)]
public class BackupSettingsTests : DataFolderTest
{
    [Fact]
    public void Comes_with_ten_kept_in_the_documents_folder()
    {
        var settings = BackupSettings.Load();

        Assert.Equal(10, settings.KeepLast);
        Assert.Equal(BackupSettings.DefaultFolder, settings.EffectiveFolder);
        Assert.EndsWith("Joktan Backups", settings.EffectiveFolder);
    }

    [Fact]
    public void What_is_saved_is_loaded()
    {
        var settings = BackupSettings.Load();
        settings.Folder = "/backups";
        settings.KeepLast = 3;
        settings.Included["certificates"] = true;
        settings.Save();

        var loaded = BackupSettings.Load();

        Assert.Equal("/backups", loaded.EffectiveFolder);
        Assert.Equal(3, loaded.KeepLast);
        Assert.True(loaded.Included["certificates"]);
    }

    [Theory]
    [InlineData("""{"KeepLast":0}""", 1)]
    [InlineData("""{"KeepLast":5000}""", 100)]
    public void The_number_kept_is_brought_within_limits(string file, int expected)
    {
        Data.Write("backup.json", file);

        Assert.Equal(expected, BackupSettings.Load().KeepLast);
    }

    [Fact]
    public void An_unreadable_file_is_treated_as_none()
    {
        Data.Write("backup.json", "not json");

        Assert.Equal(10, BackupSettings.Load().KeepLast);
    }
}

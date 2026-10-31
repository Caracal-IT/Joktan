using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Passwords.Tests;

[Collection(ProcessStateCollection.Name)]
public class PasswordSettingsTests : DataFolderTest
{
    [Fact]
    public void Starts_with_nothing_chosen()
    {
        var settings = PasswordSettings.Load();

        Assert.Null(settings.Storage);
        Assert.Equal("", settings.VaultPath);
        Assert.Equal(5, settings.LockMinutes);
    }

    [Fact]
    public void What_is_saved_is_loaded()
    {
        new PasswordSettings { Storage = PasswordStorageKind.Keychain, VaultPath = "/a/b", LockMinutes = 0 }.Save();

        var loaded = PasswordSettings.Load();

        Assert.Equal(PasswordStorageKind.Keychain, loaded.Storage);
        Assert.Equal("/a/b", loaded.VaultPath);
        Assert.Equal(0, loaded.LockMinutes);
    }

    [Fact]
    public void Settings_that_cannot_be_read_ask_for_the_storage_again()
    {
        File.WriteAllText(Data.At("passwords-settings.json"), "not json");

        Assert.Null(PasswordSettings.Load().Storage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_path_the_vault_is_in_the_data_folder(string path)
    {
        Assert.Equal(Data.At(VaultStorage.FileName), new PasswordSettings { VaultPath = path }.EffectiveVaultPath);
    }

    [Fact]
    public void A_path_chosen_is_used_for_the_vault()
    {
        Assert.Equal("/a/b.vault", new PasswordSettings { VaultPath = " /a/b.vault " }.EffectiveVaultPath);
    }
}

public class PasswordGeneratorTests
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@^_~";

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(0, 8)]
    [InlineData(8, 8)]
    [InlineData(20, 20)]
    [InlineData(128, 128)]
    [InlineData(1000, 128)]
    public void The_length_stays_within_limits(int asked, int expected)
    {
        Assert.Equal(expected, PasswordGenerator.Generate(asked, symbols: true).Length);
    }

    [Fact]
    public void Always_has_each_kind_of_character()
    {
        foreach (var _ in Enumerable.Range(0, 200))
        {
            var password = PasswordGenerator.Generate(8, symbols: true);

            Assert.Contains(password, Lower.Contains);
            Assert.Contains(password, Upper.Contains);
            Assert.Contains(password, Digits.Contains);
            Assert.Contains(password, Symbols.Contains);
        }
    }

    [Fact]
    public void Leaves_out_symbols_when_asked()
    {
        foreach (var _ in Enumerable.Range(0, 100))
            Assert.DoesNotContain(PasswordGenerator.Generate(64, symbols: false), Symbols.Contains);
    }

    [Fact]
    public void Leaves_out_characters_that_are_easy_to_mix_up()
    {
        var many = string.Concat(Enumerable.Range(0, 100).Select(_ => PasswordGenerator.Generate(128, symbols: true)));

        Assert.DoesNotContain(many, "lIO01".Contains);
    }

    [Fact]
    public void The_required_characters_do_not_stay_in_front()
    {
        var firstIsAlwaysLower = Enumerable.Range(0, 100).All(_ => Lower.Contains(PasswordGenerator.Generate(32, symbols: true)[0]));

        Assert.False(firstIsAlwaysLower);
    }

    [Fact]
    public void Is_different_each_time()
    {
        Assert.NotEqual(PasswordGenerator.Generate(32, symbols: true), PasswordGenerator.Generate(32, symbols: true));
    }
}

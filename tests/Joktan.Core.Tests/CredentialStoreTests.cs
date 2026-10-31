using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Joktan.Core.Controls;
using Joktan.Core.ViewModels;
using Joktan.Core;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests;

public class CredentialStoreTests
{
    [Fact]
    public void Filing_accounts_for_a_broker()
    {
        Assert.Equal("me@broker:1883", CredentialStore.AccountFor("me", "broker", "1883"));
    }

    [Fact]
    public void Filing_accounts_for_a_key_file_by_its_full_path()
    {
        var path = Path.GetFullPath("key.pem");

        Assert.Equal($"key:{path}", CredentialStore.AccountForKey("  key.pem "));
    }

    [Fact]
    public void Reads_writes_and_deletes()
    {
        var store = new FakeCredentialStore();

        Assert.True(store.Write("a", "secret"));
        Assert.Equal("secret", store.Read("a"));

        store.Delete("a");
        Assert.Null(store.Read("a"));
    }

    public static TheoryData<Exception> StoreFailures =>
    [
        new DllNotFoundException(),
        new EntryPointNotFoundException(),
        new Win32Exception(),
        new InvalidOperationException(),
        new IOException(),
    ];

    [Theory]
    [MemberData(nameof(StoreFailures))]
    public void A_missing_or_locked_store_does_not_throw(Exception failure)
    {
        var store = new FakeCredentialStore { Throws = failure };

        Assert.Null(store.Read("a"));
        Assert.False(store.Write("a", "secret"));
        store.Delete("a");
    }

    [Fact]
    public void An_unexpected_failure_is_not_hidden()
    {
        var store = new FakeCredentialStore { Throws = new ArgumentException() };

        Assert.Throws<ArgumentException>(() => store.Read("a"));
    }
}

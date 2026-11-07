using System;
using System.IO;
using System.Threading.Tasks;
using Joktan.Containers;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;

namespace Joktan.Users.Tests;

/// <summary>A Containers page on the stand-in for docker, with a users directory over an in-memory store.</summary>
public abstract class UserTest : FakeEngineTest
{
    protected UserTest()
    {
        var settings = new ComposeSettings
        {
            ComposeFiles = [], Engine = ContainerEngine.Docker, DatabasesFolder = Folder.At("databases"), DatabasesDataRoot = Folder.At("data-root"),
        };
        settings.Save(DataFolder);
        Containers = new ContainersViewModel(DataFolder, credentials: Credentials);
        Directory = new UserDirectory(Settings, Containers.Databases, Open, (_, _) => Task.CompletedTask, () => Saves++);
    }

    protected FakeCredentialStore Credentials { get; } = new();

    protected UserSettings Settings { get; } = new();

    protected FakeUserData Data { get; } = new();

    protected ContainersViewModel Containers { get; }

    protected UserDirectory Directory { get; }

    protected int Saves { get; private set; }

    private IUserStore Open(PostgresDatabase database, string password)
    {
        Data.Opened.Add((database.Name, password));
        return new FakeUserStore(Data);
    }

    /// <summary>Makes the users database with the form's values, as the Create button does.</summary>
    protected async Task Created(string password = "s3cret")
    {
        var problem = await Directory.CreateDatabaseAsync(Directory.FreeName(), Directory.FreePort(), "joktan", password);
        Headless.Settle();
        if (problem is not null)
            throw new InvalidOperationException(problem);
    }

    protected string DatabasesFile => Path.Combine(Folder.At("databases"), "docker-compose.yml");
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Joktan.Core;
using Xunit;

namespace Joktan.Tests.TestSupport;

/// <summary>
/// Points Joktan's data folder at a scratch folder before any test runs, so that no test can reach
/// the user's real settings, whatever it calls.
/// </summary>
internal static class TestDataIsolation
{
    public static string ScratchFolder { get; private set; } = "";

    [ModuleInitializer]
    internal static void Isolate()
    {
        ScratchFolder = Path.Combine(Path.GetTempPath(), $"joktan-tests-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(DataFolder.Variable, ScratchFolder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(ScratchFolder))
                    Directory.Delete(ScratchFolder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for the system to clear from its temporary folder.
            }
        };
    }
}

/// <summary>A folder of its own for one test, deleted afterwards.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"joktan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>The path of something in the folder.</summary>
    public string At(params string[] parts)
    {
        var path = Path;
        foreach (var part in parts)
            path = System.IO.Path.Combine(path, part);
        return path;
    }

    /// <summary>Writes a file in the folder, making the folders above it.</summary>
    public string Write(string relativePath, string content)
    {
        var path = At(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clear from its temporary folder.
        }
    }
}

/// <summary>
/// For tests that change what the whole process sees, such as the data folder or PATH. They run
/// one at a time and never beside any other test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateCollection
{
    public const string Name = "Process state";
}

/// <summary>
/// Gives each test an empty data folder. Classes deriving from it belong in
/// <see cref="ProcessStateCollection"/>.
/// </summary>
public abstract class DataFolderTest : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable(DataFolder.Variable);

    protected DataFolderTest()
    {
        Data = new TempFolder();
        Environment.SetEnvironmentVariable(DataFolder.Variable, Data.Path);
    }

    protected TempFolder Data { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DataFolder.Variable, _previous);
        Data.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>A credential store in memory, so that no test reaches the real keychain.</summary>
public sealed class FakeCredentialStore(bool isAvailable = true) : CredentialStore
{
    public Dictionary<string, string> Saved { get; } = [];

    public override bool IsAvailable => isAvailable;

    public bool FailWrites { get; set; }

    /// <summary>Thrown from every operation, to act as a store that is missing or locked.</summary>
    public Exception? Throws { get; set; }

    protected override string? ReadCore(string account)
    {
        ThrowIfSet();
        return Saved.GetValueOrDefault(account);
    }

    protected override bool WriteCore(string account, string password)
    {
        ThrowIfSet();
        if (FailWrites)
            return false;
        Saved[account] = password;
        return true;
    }

    protected override void DeleteCore(string account)
    {
        ThrowIfSet();
        Saved.Remove(account);
    }

    private void ThrowIfSet()
    {
        if (Throws is not null)
            throw Throws;
    }
}

/// <summary>Files of the repository that tests check the code against.</summary>
public static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot([CallerFilePath] string thisFile = "")
    {
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(thisFile)!); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Joktan.slnx")))
                return folder.FullName;
        }

        throw new InvalidOperationException("The tests are not inside the repository.");
    }
}

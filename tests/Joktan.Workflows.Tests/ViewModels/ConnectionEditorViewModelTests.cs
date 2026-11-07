using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Resources;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class ConnectionEditorViewModelTests : DataFolderTest
{
    private readonly FakeCredentialStore _credentials = new();
    private readonly WorkflowSettings _settings = new();

    private DatabaseConnections Connections() => new(_settings, _credentials);

    [Fact]
    public void What_is_filled_in_is_checked_before_it_is_saved()
    {
        var editor = new ConnectionEditorViewModel(Connections(), null, null, _ => { }, () => { }) { Name = "A" };
        editor.Host = " ";
        editor.SaveCommand.Execute(null);
        Assert.Equal(Strings.Connection_EnterAHost, editor.Error);
        editor.Host = "h";
        editor.Port = 0;
        editor.SaveCommand.Execute(null);
        Assert.Equal(Strings.Connection_PortRange, editor.Error);
        editor.Port = 5432;
        editor.Database = "";
        editor.SaveCommand.Execute(null);
        Assert.Equal(Strings.Connection_EnterUserAndDatabase, editor.Error);
        Assert.Empty(_settings.DatabaseConnections);
    }

    [Fact]
    public async Task A_changed_connection_keeps_its_password_unless_another_is_typed()
    {
        var connections = Connections();
        var existing = new DatabaseConnectionSettings { Name = "Old", Host = "a" };
        connections.Save(existing, "first");
        string? used = null;
        var editor = new ConnectionEditorViewModel(connections, existing, (_, _, _, password, _) =>
        {
            used = password;
            return new FakeDatabase();
        }, _ => { }, () => { });
        Assert.False(editor.IsNew);
        Assert.Equal(string.Format(Strings.Connection_Change, "Old"), editor.Title);
        Assert.Equal("a", editor.Host);

        await editor.TestCommand.ExecuteAsync(null);
        Assert.Equal("first", used);
        editor.Name = "New name";
        editor.SaveCommand.Execute(null);
        Assert.Equal("New name", Assert.Single(connections.All).Name);
        Assert.Equal("first", _credentials.Saved[existing.Account]);

        editor.Password = "second";
        editor.SaveCommand.Execute(null);
        Assert.Equal("second", _credentials.Saved[existing.Account]);
    }

    [Fact]
    public async Task A_test_that_fails_says_why_and_a_password_that_cannot_be_kept_is_said()
    {
        var down = new FakeDatabase { Throws = new System.Net.Sockets.SocketException(61) };
        var editor = new ConnectionEditorViewModel(Connections(), null, (_, _, _, _, _) => down, _ => { }, () => { }) { Name = "X" };
        await editor.TestCommand.ExecuteAsync(null);
        Assert.StartsWith(Strings.Connection_DidNotWork[..10], editor.Error);
        Assert.Equal(1, down.Disposed);

        _credentials.FailWrites = true;
        var saved = false;
        editor = new ConnectionEditorViewModel(Connections(), null, null, _ => saved = true, () => { }) { Name = "X", Password = "p" };
        editor.SaveCommand.Execute(null);
        Assert.Equal(Strings.Connection_PasswordNotKept, editor.Error);
        Assert.False(saved);
        Assert.Single(_settings.DatabaseConnections);
    }

    [Fact]
    public void A_removed_connection_takes_its_password_with_it()
    {
        var connections = Connections();
        var changed = 0;
        connections.Changed += () => changed++;
        var connection = new DatabaseConnectionSettings { Name = "Gone" };
        connections.Save(connection, "pw");
        connections.Remove(connection.Id);
        connections.Remove("nothing");
        Assert.Empty(connections.All);
        Assert.Empty(_credentials.Saved);
        Assert.Equal(2, changed);
        Assert.Empty(WorkflowSettings.Load().DatabaseConnections);
    }
}

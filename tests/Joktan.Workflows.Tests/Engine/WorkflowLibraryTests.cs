using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class WorkflowLibraryTests
{
    [Fact]
    public void A_workflow_is_kept_in_a_file_named_after_it_and_read_back()
    {
        using var folder = new TempFolder();
        var library = new WorkflowLibrary(folder.Path);
        var flow = new Flow("Order check").Then(Flow.End());

        library.Save(flow.Workflow);

        Assert.True(File.Exists(folder.At("Order check.workflow.json")));
        var again = new WorkflowLibrary(folder.Path).Find("order CHECK")!;
        Assert.Equal(flow.Workflow.Id, again.Id);
        Assert.Equal(2, again.Steps.Count);
        Assert.Single(again.Links);
    }

    [Fact]
    public void Renaming_a_workflow_moves_its_file()
    {
        using var folder = new TempFolder();
        var library = new WorkflowLibrary(folder.Path);
        var workflow = new Flow("Before").Workflow;
        library.Save(workflow);

        workflow.Name = "After / now";
        library.Save(workflow);

        Assert.Equal(["After - now.workflow.json"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
        Assert.Same(workflow, library.Find(workflow.Id));
    }

    [Fact]
    public void Deleting_a_workflow_removes_its_file()
    {
        using var folder = new TempFolder();
        var library = new WorkflowLibrary(folder.Path);
        var workflow = new Flow("Gone").Workflow;
        library.Save(workflow);
        var changes = 0;
        library.Changed += () => changes++;

        library.Delete(workflow.Id);

        Assert.Empty(Directory.GetFiles(folder.Path));
        Assert.Null(library.Find(workflow.Id));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Files_that_are_not_workflows_are_left_out_and_missing_parts_filled_in()
    {
        using var folder = new TempFolder();
        folder.Write("broken.workflow.json", "{ not json");
        folder.Write("bare.workflow.json", "{}");
        folder.Write("other.json", "{}");

        var library = new WorkflowLibrary(folder.Path);

        var bare = Assert.Single(library.All);
        Assert.Equal("bare", bare.Name);
        Assert.NotEmpty(bare.Id);
        Assert.NotNull(bare.Triggers);
    }

    [Fact]
    public void Names_are_made_free_and_a_new_folder_can_be_used()
    {
        using var folder = new TempFolder();
        using var other = new TempFolder();
        var library = new WorkflowLibrary(folder.Path);
        library.Save(new Workflow { Id = "a", Name = "New workflow" });

        Assert.Equal("New workflow 2", library.FreeName("New workflow"));

        library.UseFolder(other.Path);
        Assert.Empty(library.All);
        Assert.Equal("New workflow", library.FreeName("New workflow"));
    }

    [Fact]
    public void A_workflow_saved_with_one_sample_input_gets_it_as_its_first_sample()
    {
        using var folder = new TempFolder();
        folder.Write("Old.workflow.json", """{"Id": "o", "Name": "Old", "SampleInput": "{\"a\": 1}"}""");
        var library = new WorkflowLibrary(folder.Path);

        var old = library.Find("Old")!;
        Assert.Equal("""{"a": 1}""", Assert.Single(old.Samples).Input);
        Assert.Null(old.SampleInput);

        library.Save(old);
        Assert.DoesNotContain("SampleInput", File.ReadAllText(library.PathOf("o")!));
    }

    [Fact]
    public void Workflows_are_kept_in_folders_and_moved_between_them()
    {
        using var folder = new TempFolder();
        var library = new WorkflowLibrary(folder.Path);
        Assert.Equal("Orders/Returns", library.CreateFolder(" Orders / Returns "));
        library.CreateFolder("Empty");
        folder.Write(".git/Hidden.workflow.json", """{"id": "h", "name": "Hidden"}""");
        var workflow = new Flow("Refund").Workflow;

        library.Save(workflow, "Orders/Returns");
        Assert.Equal("Orders/Returns", library.FolderOf(workflow.Id));
        Assert.True(File.Exists(folder.At("Orders", "Returns", "Refund.workflow.json")));

        library.Save(workflow, "");
        Assert.Equal("", library.FolderOf(workflow.Id));
        Assert.False(File.Exists(folder.At("Orders", "Returns", "Refund.workflow.json")));

        Assert.Equal(["Empty", "Orders", "Orders/Returns"], library.Folders);
        Assert.False(library.DeleteFolder("Orders"));
        Assert.True(library.DeleteFolder("Empty"));

        var again = new WorkflowLibrary(folder.Path);
        Assert.Null(again.Find("Hidden"));
        library.Save(workflow, "Orders");
        Assert.Equal("Orders", new WorkflowLibrary(folder.Path).FolderOf(workflow.Id));

        // A folder cannot lead out of the workflows folder.
        library.Save(workflow, "../../elsewhere");
        Assert.StartsWith(folder.Path, library.PathOf(workflow.Id));
    }
}

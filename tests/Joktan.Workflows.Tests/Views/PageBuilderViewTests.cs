using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Joktan.Workflows.ViewModels;
using Joktan.Workflows.Views;
using Xunit;

namespace Joktan.Workflows.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class PageBuilderViewTests : EngineTest
{
    [Fact]
    public Task The_builder_shows_the_page_as_the_website_will_and_the_settings_of_the_item_selected() => Headless.Run(() =>
    {
        var vm = new PageBuilderViewModel(Engine.Pages);
        var window = Headless.Show(new PageBuilderView(), vm, 1400);
        Assert.False(Headless.Find<ListBox>(window, l => l.Name == "ItemsList").IsEffectivelyVisible);

        vm.NewPageCommand.Execute(null);
        vm.PageTitle = "Approve order {{input.order}}";
        vm.AddItemCommand.Execute(vm.InputTypes.Single(t => t.Type == PageItemType.DropDown));
        vm.SelectedItem!.Label = "Colour";
        vm.SelectedItem.Required = true;
        Headless.Settle();

        Assert.True(Headless.Find<ListBox>(window, l => l.Name == "ItemsList").IsEffectivelyVisible);
        Assert.Equal(4, Headless.Find<ListBox>(window, l => l.Name == "ItemsList").ItemCount);
        Assert.Equal("field", Headless.Find<TextBox>(window, t => t.Name == "FieldNameBox").Text);
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "OptionsBox").IsEffectivelyVisible);
        Assert.True(Headless.Find<ToggleSwitch>(window, t => t.Name == "RequiredSwitch").IsChecked);
        Assert.Contains(Headless.All<TextBlock>(window), t => t.Text == "Colour *");
        WorkflowViewsTests.Picture(window, "page-builder");

        vm.AddItemCommand.Execute(vm.ShownTypes.Single(t => t.Type == PageItemType.Heading));
        Headless.Settle();
        Assert.False(Headless.Find<TextBox>(window, t => t.Name == "FieldNameBox").IsEffectivelyVisible);
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "ItemTextBox").IsEffectivelyVisible);
        window.Close();
    });

    [Fact]
    public Task A_user_input_step_offers_the_pages_to_show() => Headless.Run(() =>
    {
        Engine.Pages.Save(new WorkflowPage { Id = "p1", Name = "Approve order" });
        var designer = new WorkflowDesignerViewModel(Engine);
        designer.NewWorkflowCommand.Execute(null);
        var window = Headless.Show(new WorkflowDesignerView(), designer, 1500, 950);
        designer.AddFromPaletteCommand.Execute(designer.Palette.SelectMany(g => g.Items).First(i => i.Type == StepType.UserInput));
        Headless.Settle();

        var box = Headless.Find<ComboBox>(window, c => c.Name == "PageChoiceBox");
        Assert.True(box.IsEffectivelyVisible);
        var step = designer.SelectedStep!;
        Assert.Equal(["", "p1"], step.PageChoices.Select(c => c.Id));
        Assert.Equal("", step.PageChoice!.Id);

        step.PageChoice = step.PageChoices[1];
        Assert.Equal("p1", step.Step.PageId);
        Engine.Pages.Save(new WorkflowPage { Id = "p2", Name = "Another" });
        Engine.Pages.Delete("p1");
        Assert.Equal(["", "p2", "p1"], step.PageChoices.Select(c => c.Id));
        Assert.Contains("deleted", step.PageChoice!.Name);
        window.Close();
    });
}

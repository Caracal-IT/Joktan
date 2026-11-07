using System;
using System.Threading.Tasks;
using Joktan.Agent.Rest;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Website.Tests;

/// <summary>
/// Not a test: serves the website on SITE_PORT with sample work items, to look at it in a browser, until the
/// first item is answered or about seven minutes pass. Run it with
/// <c>SITE_PORT=47999 dotnet test tests/Joktan.Website.Tests -- --filter-class Joktan.Website.Tests.WebsitePreview</c>,
/// then open http://localhost:47999 and sign in as ann / pw (Approver) or admin / pw.
/// </summary>
[Collection(ProcessStateCollection.Name)]
public class WebsitePreview : WebsiteTest
{
    [Fact]
    public async Task Serve()
    {
        var port = Environment.GetEnvironmentVariable("SITE_PORT");
        Assert.SkipWhen(port is null, "Set SITE_PORT to serve the website with sample items and look at it.");
        Engine.Pages.Save(new WorkflowPage
        {
            Id = "approve", Name = "Approve order", Title = "Approve order {{input.order}}", Description = "Check the order and the customer before approving.",
            SubmitText = "Send decision",
            Items =
            [
                new PageItem { Type = PageItemType.Heading, Text = "Order" },
                new PageItem { Type = PageItemType.Value, Label = "Customer", Text = "{{input.customer}}" },
                new PageItem { Type = PageItemType.Value, Label = "Total", Text = "{{input.total}}" },
                new PageItem { Type = PageItemType.Divider },
                new PageItem { Type = PageItemType.CheckBox, Name = "approved", Label = "I approve this order", Required = true },
                new PageItem { Type = PageItemType.DropDown, Name = "speed", Label = "Delivery", Options = ["Normal", "Express"], Required = true },
                new PageItem { Type = PageItemType.Number, Name = "qty", Label = "Quantity", Default = "1" },
                new PageItem { Type = PageItemType.TextArea, Name = "comment", Label = "Comment", Help = "Optional; the customer does not see it.", Placeholder = "Anything the next step should know" },
            ],
        });
        Engine.Pages.Save(new WorkflowPage { Id = "refund", Name = "Refund request", Title = "Refund" });
        var run = await AskAsync(Role("Approver"), pageId: "approve", input: """{"order":"A-1042","total":"1 250.00 EUR","customer":"Acme Ltd"}""");
        await AskAsync(Role("Approver"), "Returns", fields: [new InputField { Name = "reason", Label = "Reason" }]);
        await AskAsync(Role("Manager"), "Purchasing");
        await AskAsync(User(Ann), "Onboarding");
        using var server = new RestServer(false, int.Parse(port), Site.AnswerAsync);
        await Engine.WhenFinished(run.Id).WaitAsync(TimeSpan.FromSeconds(400), TestContext.Current.CancellationToken);
    }
}

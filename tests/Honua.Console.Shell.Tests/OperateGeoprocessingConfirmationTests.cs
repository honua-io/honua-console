using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Web;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateGeoprocessingPageTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "current")]
    [InlineData(ConsolePresentationMode.V4, "current")]
    [InlineData(ConsolePresentationMode.Legacy, null)]
    [InlineData(ConsolePresentationMode.V4, null)]
    [InlineData(ConsolePresentationMode.Legacy, "earlier")]
    [InlineData(ConsolePresentationMode.V4, "earlier")]
    public async Task CancelConfirmationCannotDispatchAfterLeavingItsView(ConsolePresentationMode mode, string? destination)
    {
        var client = new StubGeoprocessingClient
        {
            ReadDetail = id => Task.FromResult(OperateSectionResult<OperateJobRun>.Allowed(
                Job(id, "Running", "Executing", 40, "operator"))),
            ReadDiagnostics = (id, _) => Task.FromResult(
                OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection(id, cancel: true)))
        };
        using var context = NavigationContext(mode, client);
        var page = context.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "earlier"));
        await page.FindAll("button").Single(button => button.TextContent == "Cancel job").ClickAsync(new MouseEventArgs());
        page.Render(p => p.Add(x => x.SelectedJobRunId, (string?)null));
        if (destination is not null)
        {
            page.Render(p => p.Add(x => x.SelectedJobRunId, destination));
        }

        await page.Find("[data-console-confirm-accept]").ClickAsync(new MouseEventArgs());

        Assert.Empty(client.CancelCalls);
        Assert.DoesNotContain("n/a", page.Markup);
        if (destination is not null)
        {
            Assert.Equal(destination, page.Find("#job-detail-heading").TextContent);
            await page.FindAll("button").Single(button => button.TextContent == "Cancel job").ClickAsync(new MouseEventArgs());
            await page.Find("[data-console-confirm-accept]").ClickAsync(new MouseEventArgs());
            Assert.Equal(destination, Assert.Single(client.CancelCalls));
        }
    }
}

using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Coverage for the shared Diagnostics disclosure (honua-console#311): the plain-language first line reads
/// on its own with the disclosure collapsed, the full technical detail (state, contract, HTTP status, issue
/// link) lives inside the expandable block, and the tracking issue renders as a real link.
/// </summary>
public sealed class ConsoleDiagnosticsRenderTests
{
    [Fact]
    public void Diagnostics_Collapsed_ShowsHumanSummaryAndKeepsDetailBehindClosedDisclosure()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ConsoleDiagnostics>(parameters => parameters
            .Add(p => p.Summary, "Maps can't be listed on this server version yet — create one from a prompt instead.")
            .Add(p => p.Detail, "honua-server exposes no saved-query list endpoint.")
            .Add(p => p.State, "Unsupported")
            .Add(p => p.Contract, "GET /api/v1/studio/package-drafts (list)")
            .Add(p => p.StatusCode, 405)
            .Add(p => p.IssueRef, "honua-server#1182"));

        // The human first line reads on its own.
        var summary = cut.Find("[data-diagnostics-summary]").TextContent;
        Assert.Contains("Maps can't be listed", summary, StringComparison.Ordinal);
        // No engineering artifacts leak into the first line.
        Assert.DoesNotContain("405", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("#1182", summary, StringComparison.Ordinal);

        // The disclosure is present but collapsed by default (no open attribute).
        var details = cut.Find("[data-diagnostics-detail]");
        Assert.False(details.HasAttribute("open"));
    }

    [Fact]
    public void Diagnostics_Disclosure_PreservesTechnicalDetailAndRendersIssueLink()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ConsoleDiagnostics>(parameters => parameters
            .Add(p => p.Summary, "This server version can't list saved queries yet.")
            .Add(p => p.Detail, "honua-server exposes no saved-query list endpoint.")
            .Add(p => p.State, "Unsupported")
            .Add(p => p.Contract, "GET /api/v1/studio/package-drafts (list)")
            .Add(p => p.StatusCode, 405)
            .Add(p => p.IssueRef, "honua-server#1182"));

        // Verbatim technical detail is preserved inside the disclosure.
        Assert.Contains("no saved-query list endpoint", cut.Find("[data-diagnostics-detail-text]").TextContent, StringComparison.Ordinal);
        Assert.Equal("Unsupported", cut.Find("[data-diagnostics-state]").TextContent);
        Assert.Contains("GET /api/v1/studio/package-drafts (list)", cut.Find("[data-diagnostics-contract]").TextContent, StringComparison.Ordinal);
        Assert.Contains("HTTP 405", cut.Find("[data-diagnostics-status]").TextContent, StringComparison.Ordinal);

        // The tracking issue renders as a real, resolvable link.
        var issue = cut.Find("[data-diagnostics-issue]");
        Assert.Equal("honua-server#1182", issue.TextContent);
        Assert.Equal("https://github.com/honua-io/honua-server/issues/1182", issue.GetAttribute("href"));
    }

    [Fact]
    public void Diagnostics_WithoutSummary_RendersDisclosureOnly()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<ConsoleDiagnostics>(parameters => parameters
            .Add(p => p.Detail, "No execution-job store is registered on the connected server."));

        Assert.Empty(cut.FindAll("[data-diagnostics-summary]"));
        Assert.Contains("No execution-job store is registered", cut.Find("[data-diagnostics-detail-text]").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("honua-server#1182", "https://github.com/honua-io/honua-server/issues/1182")]
    [InlineData("console#290", "https://github.com/honua-io/honua-console/issues/290")]
    [InlineData("#193", "https://github.com/honua-io/honua-console/issues/193")]
    public void IssueUrl_ResolvesReferenceToRepoIssue(string issueRef, string expected)
    {
        Assert.Equal(expected, ConsoleDiagnostics.IssueUrl(issueRef));
    }
}

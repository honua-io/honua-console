using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class OperateObjectStoragePageTests
{
    private static readonly Guid ConnectionId = Guid.Parse("6f1b2c9e-0000-4000-8000-000000000001");

    [Fact]
    public void AddNeedsTheSetupLinkBeforeTestOrCreate()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Setup = OperateSectionResult<ObjectStorageSetupResponse>.Allowed(new ObjectStorageSetupResponse
        {
            ExternalId = "ext-123", QuickCreateUrl = "https://console.example/quick", RoleNamePrefix = "honua-connection-"
        });
        var page = context.Render<OperateConnectionObjectStorageNewPage>();

        Fill(page);
        Assert.True(page.Find("[data-object-storage-create]").HasAttribute("disabled"));

        page.Find("[data-object-storage-setup]").Click();

        Assert.Equal(("imagery-2026", "ortho/"), Assert.Single(client.SetupCalls));
        Assert.Contains("ext-123", page.Find("[data-object-storage-setup-result]").TextContent, StringComparison.Ordinal);
        Assert.Equal("https://console.example/quick", page.Find("[data-object-storage-quick-create]").GetAttribute("href"));
        Assert.False(page.Find("[data-object-storage-create]").HasAttribute("disabled"));
        V4PagePreview.Capture("operate-object-storage-new", page.Markup);
    }

    [Fact]
    public void ChangingTheBucketAsksForANewSetupLink()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Setup = OperateSectionResult<ObjectStorageSetupResponse>.Allowed(new ObjectStorageSetupResponse { ExternalId = "ext-123" });
        var page = context.Render<OperateConnectionObjectStorageNewPage>();
        Fill(page);
        page.Find("[data-object-storage-setup]").Click();

        page.Find("[data-object-storage-field='bucket']").Input("other-bucket");

        Assert.Empty(page.FindAll("[data-object-storage-setup-result]"));
        Assert.True(page.Find("[data-object-storage-create]").HasAttribute("disabled"));
    }

    [Fact]
    public void TestThenCreateSendTheDraftWithTheExternalId()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Setup = OperateSectionResult<ObjectStorageSetupResponse>.Allowed(new ObjectStorageSetupResponse { ExternalId = "ext-123" });
        client.Test = OperateSectionResult<ObjectStorageTestResponse>.Allowed(new ObjectStorageTestResponse { IsHealthy = true, Message = "Listed ortho/a.tif." });
        client.Connection = OperateSectionResult<ObjectStorageConnectionResponse>.Allowed(new ObjectStorageConnectionResponse { Id = ConnectionId, Name = "imagery" });
        var page = context.Render<OperateConnectionObjectStorageNewPage>();
        Fill(page);
        page.Find("[data-object-storage-setup]").Click();

        page.Find("[data-object-storage-test]").Click();
        Assert.Contains("Bucket reachable", page.Find("[data-object-storage-test-result]").TextContent, StringComparison.Ordinal);
        page.Find("[data-object-storage-create]").Click();

        var draft = Assert.Single(client.ConnectionCalls);
        Assert.Equal(Assert.Single(client.TestCalls), draft);
        Assert.Equal("object-storage", draft.Kind);
        Assert.Equal("ext-123", draft.ExternalId);
        Assert.Equal("us-west-2", draft.Region);
        Assert.EndsWith($"/operate/connections/{ConnectionId:D}/objects",
            page.Find("[data-object-storage-created] a").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusedCreateShowsTheReason()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Setup = OperateSectionResult<ObjectStorageSetupResponse>.Allowed(new ObjectStorageSetupResponse { ExternalId = "ext-123" });
        client.Connection = OperateSectionResult<ObjectStorageConnectionResponse>.Denied(OperateSectionStatus.Rejected, "Requester-pays buckets are not supported.");
        var page = context.Render<OperateConnectionObjectStorageNewPage>();
        Fill(page);
        page.Find("[data-object-storage-setup]").Click();

        page.Find("[data-object-storage-create]").Click();

        Assert.Contains("Requester-pays buckets are not supported.", page.Find("[data-object-storage-create-failed]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserListsFoldersAndDetectedKindsUnderThePrefix()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Listing = OperateSectionResult<ObjectStorageBrowseResponse>.Allowed(new ObjectStorageBrowseResponse
        {
            Bucket = "imagery-2026",
            Prefix = "ortho/2026/",
            Folders = ["ortho/2026/north/"],
            Objects = [new ObjectStorageObjectResponse { Key = "ortho/2026/a.tif", Kind = "cog", Bytes = 2048 },
                       new ObjectStorageObjectResponse { Key = "ortho/2026/notes.txt", Kind = "other", Bytes = 10 }],
            Truncated = true
        });
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo($"operate/connections/{ConnectionId:D}/objects?prefix=ortho%2F2026%2F");

        var page = context.Render<OperateConnectionObjectsPage>(parameters => parameters.Add(p => p.ConnectionId, ConnectionId));

        Assert.Equal((ConnectionId, "ortho/2026/"), Assert.Single(client.BrowseCalls));
        Assert.Contains("imagery-2026", page.Find("h1").TextContent, StringComparison.Ordinal);
        Assert.EndsWith("?prefix=ortho%2F2026%2Fnorth%2F", page.Find("[data-objects-folder] a").GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains("Cloud-optimized GeoTIFF", page.Find("[data-objects-file='ortho/2026/a.tif']").TextContent, StringComparison.Ordinal);
        Assert.Contains("2 KB", page.Find("[data-objects-file='ortho/2026/a.tif']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Other", page.Find("[data-objects-file='ortho/2026/notes.txt']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-objects-truncated]"));
        var trail = page.FindAll("[data-objects-breadcrumb] a").Select(link => link.TextContent).ToArray();
        Assert.Equal(["Bucket root", "ortho", "2026"], trail);
        V4PagePreview.Capture("operate-connection-objects", page.Markup);
    }

    [Fact]
    public void BrowserShowsAnUnreadableBucket()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Listing = OperateSectionResult<ObjectStorageBrowseResponse>.Denied(OperateSectionStatus.Forbidden, "The role cannot list this bucket.");

        var page = context.Render<OperateConnectionObjectsPage>(parameters => parameters.Add(p => p.ConnectionId, ConnectionId));

        Assert.Contains("The role cannot list this bucket.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-objects-file]"));
    }

    private static void Fill(IRenderedComponent<OperateConnectionObjectStorageNewPage> page)
    {
        page.Find("[data-object-storage-field='name']").Input("imagery");
        page.Find("[data-object-storage-field='bucket']").Input("imagery-2026");
        page.Find("[data-object-storage-field='prefix']").Input("ortho/");
        page.Find("[data-object-storage-field='region']").Input("us-west-2");
        page.Find("[data-object-storage-field='role']").Input("arn:aws:iam::123456789012:role/honua-connection-imagery");
    }
}

using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

// The connection form and the Export pages keep a reply that has a server-side effect and drop only stale reads.
public sealed class ExportsDraftGuardTests
{
    private static readonly Guid ConnectionId = Guid.Parse("6f1b2c9e-0000-4000-8000-000000000002");

    [Fact]
    public async Task FieldsAreReadOnlyWhileCreatingAndTheCreatedConnectionIsKept()
    {
        using var fixture = new ExportsPageFixture();
        var client = fixture.Client;
        client.Setup = OperateSectionResult<ObjectStorageSetupResponse>.Allowed(new ObjectStorageSetupResponse { ExternalId = "ext-123" });
        var pending = new TaskCompletionSource<OperateSectionResult<ObjectStorageConnectionResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.PendingConnection = pending.Task;
        var page = fixture.Context.Render<OperateConnectionObjectStorageNewPage>();
        Fill(page);
        page.Find("[data-object-storage-setup]").Click();

        page.Find("[data-object-storage-create]").Click();

        Assert.All(page.FindAll("[data-object-storage-field]"), field => Assert.True(field.HasAttribute("disabled")));
        pending.SetResult(OperateSectionResult<ObjectStorageConnectionResponse>.Allowed(new ObjectStorageConnectionResponse { Id = ConnectionId, Name = "imagery" }));
        await Task.Yield();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-object-storage-created]")));
        Assert.True(page.Find("[data-object-storage-create]").HasAttribute("disabled"));
        Assert.All(page.FindAll("[data-object-storage-field]"), field => Assert.True(field.HasAttribute("disabled")));
        Assert.Single(client.ConnectionCalls);
    }

    [Fact]
    public async Task ASetupReplySurvivesAnEditOutsideBucketAndPrefix()
    {
        using var fixture = new ExportsPageFixture();
        var pending = new TaskCompletionSource<OperateSectionResult<ObjectStorageSetupResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.PendingSetup = pending.Task;
        var page = fixture.Context.Render<OperateConnectionObjectStorageNewPage>();
        Fill(page);
        page.Find("[data-object-storage-setup]").Click();

        page.Find("[data-object-storage-field='name']").Input("imagery-renamed");
        pending.SetResult(OperateSectionResult<ObjectStorageSetupResponse>.Allowed(new ObjectStorageSetupResponse { ExternalId = "ext-kept" }));
        await Task.Yield();

        page.WaitForAssertion(() => Assert.Contains("ext-kept", page.Find("[data-object-storage-setup-result]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void UnpublishIsOfferedOnlyForAnExportWithAValidId()
    {
        using var fixture = new ExportsPageFixture();
        fixture.Client.List = OperateSectionResult<ExportListResponse>.Allowed(new ExportListResponse
        {
            Items = [new ExportResponse { Id = null!, Status = "ready" }, new ExportResponse { Id = "parcels", Status = "ready" }]
        });

        var page = fixture.Context.Render<OperateExportsPage>();

        var buttons = page.FindAll("tbody tr button");
        Assert.Equal(2, buttons.Count);
        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.False(buttons[1].HasAttribute("disabled"));
    }

    [Fact]
    public void OpeningAnotherLayerClearsTheStartedExport()
    {
        using var fixture = new ExportsPageFixture();
        fixture.Client.Create = OperateSectionResult<ExportResponse>.Allowed(new ExportResponse { Id = "parcels", Status = "running" });
        var navigation = fixture.Context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo(ExportPresentation.NewExportRoute("cadastre", 1, "Parcels").TrimStart('/'));
        var page = fixture.Context.Render<OperateExportNewPage>();
        page.Find("[data-export-create]").Click();
        Assert.NotNull(page.Find("[data-export-started]"));

        navigation.NavigateTo(ExportPresentation.NewExportRoute("cadastre", 2, "Roads").TrimStart('/'));

        page.WaitForAssertion(() => Assert.Contains("Roads", page.Find("[data-export-source]").TextContent, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-export-started]"));
    }

    private static void Fill(IRenderedComponent<OperateConnectionObjectStorageNewPage> page)
    {
        page.Find("[data-object-storage-field='name']").Input("imagery");
        page.Find("[data-object-storage-field='bucket']").Input("imagery-2026");
        page.Find("[data-object-storage-field='region']").Input("us-west-2");
        page.Find("[data-object-storage-field='role']").Input("role/honua-connection-imagery");
    }
}

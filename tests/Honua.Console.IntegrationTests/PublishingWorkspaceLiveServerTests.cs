using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-bound publishing workspace renders and drives the lifecycle from live
/// honua-server data (Console Patterns Charter section 11 real-server policy). Boots
/// honua-server/PostgreSQL via Testcontainers, seeds a known publication through the real publish POST
/// (<c>/api/v1/console/publications</c>, honua-server#1183), then drives
/// <see cref="HonuaServerPublishingWorkspaceDataSource"/> over the live get/republish/rollback verbs —
/// never an in-memory client. Docker-unavailable environments skip cleanly.
/// </summary>
[Collection(PublishingWorkspaceIntegrationCollection.Name)]
public sealed class PublishingWorkspaceLiveServerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PublishingWorkspaceFixture _fixture;

    public PublishingWorkspaceLiveServerTests(PublishingWorkspaceFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task PublishingWorkspace_SeedsPublicationAndRendersFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        // 1. Seed a known publication on the live registry through the real publish POST.
        var slug = $"console-publishing-fixture-{Guid.NewGuid():N}";
        var publicationId = await SeedPublicationAsync(slug);

        var client = _fixture.CreatePublicationClient();
        var options = PublishingWorkspaceOptions.FromConfiguredList(publicationId);
        var dataSource = new HonuaServerPublishingWorkspaceDataSource(client, options);

        // 2. The workspace matrix + review project from the live route state.
        var workspace = await dataSource.GetWorkspaceAsync();
        Assert.Empty(workspace.CapabilityStates);
        var review = Assert.Single(workspace.Reviews);
        Assert.Equal(publicationId, review.ResourceId);
        Assert.Contains(review.GeneratedEndpoints, endpoint => endpoint.Url.Contains(slug, StringComparison.Ordinal));

        // 3. Republish advances the active revision against the live verb.
        var republished = await dataSource.RepublishAsync(
            publicationId,
            new Shell.Models.PublishingRepublishCommand(Title: "Refreshed by Console smoke"));
        Assert.True(republished.HasReview, "The live server rejected the republish.");
        Assert.True(republished.Versions.Count >= 2);
        var activeRevision = republished.Versions.First(v => v.IsActive).Revision;
        Assert.Equal(2, activeRevision);

        // 4. Rollback moves the active pointer back to the first revision against the live verb.
        var firstVersionId = republished.Versions.First(v => v.Revision == 1).VersionId;
        var rolledBack = await dataSource.RollbackAsync(
            publicationId,
            new Shell.Models.PublishingRollbackCommand(TargetVersionId: firstVersionId));
        Assert.True(rolledBack.HasReview, "The live server rejected the rollback.");
        Assert.Equal(1, rolledBack.Versions.First(v => v.IsActive).Revision);
        Assert.Equal("rolled-back", rolledBack.Review!.RollbackClass);

        // 5. The page renders the live workspace.
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(dataSource);
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        var page = ctx.RenderComponent<OperatePublishingPage>();
        page.WaitForAssertion(
            () => Assert.Contains("Publication Matrix", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.Contains(slug, page.Markup, StringComparison.Ordinal);
    }

    private async Task<string> SeedPublicationAsync(string slug)
    {
        using var http = _fixture.CreateHttpClient();
        if (!string.IsNullOrWhiteSpace(_fixture.Options.StudioAdminApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _fixture.Options.StudioAdminApiKey);
        }

        // Mirrors honua-server PublishContentRequest. The server allocates the publication id, version
        // id, and revision; we only declare kind + route slug + a content hash for the immutable version.
        var request = new
        {
            kind = HonuaContentPublicationKinds.Map,
            routeSlug = slug,
            title = "Console publishing smoke fixture",
            contentHash = $"sha256:{Guid.NewGuid():N}",
            policy = new { visibility = HonuaContentPublicationVisibilities.Public }
        };

        using var response = await http.PostAsync(
            "/api/v1/console/publications",
            JsonContent.Create(request, options: JsonOptions));
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<HonuaContentPublicationDetail>(JsonOptions);
        Assert.NotNull(detail);
        var publicationId = detail!.Route.PublicationId;
        Assert.False(string.IsNullOrWhiteSpace(publicationId), "The live server returned no publication id.");
        return publicationId;
    }
}

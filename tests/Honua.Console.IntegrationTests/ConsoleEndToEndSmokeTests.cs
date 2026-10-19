using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Components.Studio;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Console end-to-end smoke against a REAL honua-server (issue #59). Boots ONE live honua-server +
/// PostgreSQL via Testcontainers and checks the catalog, retained package inspector, and publishing
/// backend contracts against that single server. The Studio route is independently checked as the
/// embedded realtime host; bUnit cannot execute its JavaScript editor. The installed browser journey
/// in workspace-live/specs/studio-map.live.spec.ts owns realtime map authoring qualification.
/// Backend responses use live server-bound clients (Console Patterns Charter section 11). This supersedes the in-memory-only <c>smoke/parity</c>
/// behaviour for the <c>honua-console#9</c> gate: it emits evidence recording the server image, the seed
/// profile, and <c>sourceHydrated: true</c>.
///
/// The whole lane is opt-in and SKIPS cleanly (green) on the standard CI runner without Docker, exactly
/// like the existing live-server lanes — gated behind <c>HONUA_CONSOLE_INTEGRATION</c> /
/// <c>HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS</c> + <c>HONUA_CONSOLE_SERVER_IMAGE</c> +
/// <c>HONUA_CONSOLE_ADMIN_API_KEY</c>.
/// </summary>
[Collection(ConsoleEndToEndSmokeCollection.Name)]
public sealed class ConsoleEndToEndSmokeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConsoleEndToEndSmokeFixture _fixture;

    public ConsoleEndToEndSmokeTests(ConsoleEndToEndSmokeFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task ConsoleE2eChain_DrivesCatalogStudioAndOperate_AgainstLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var runId = Guid.NewGuid().ToString("N");
        var steps = new List<ConsoleEndToEndSmokeStep>();
        var seedProfile = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---------------------------------------------------------------------------------------------
        // Flow 1 — Catalog list against the live server. Seed a known content item through the real
        // create path, then assert the server-bound catalog client surfaces it and the Catalog page
        // renders it from live data.
        // ---------------------------------------------------------------------------------------------
        var contentClient = _fixture.CreateContentClient();
        var catalog = new HonuaServerConsoleCatalogClient(contentClient);

        var catalogName = $"console-e2e-catalog-{runId}";
        var catalogTitle = $"Console E2E catalog fixture {runId}";
        seedProfile["catalogItemName"] = catalogName;

        var created = await contentClient.CreateAsync(new HonuaCreateConsoleContentItemRequest
        {
            Name = catalogName,
            ItemType = HonuaConsoleContentItemTypes.Service,
            Title = catalogTitle,
            Description = "Seeded by the Console end-to-end real-server smoke (issue #59).",
            Visibility = HonuaConsoleVisibilities.Public,
            Tags = ["console-e2e", "open-data"]
        });

        // Past the env/auth skip gate a known-valid create must succeed; a rejection is a real regression,
        // never a false-green skip.
        Assert.Null(created.Issue);
        Assert.NotNull(created.Data);
        var catalogItemId = created.Data!.Id;
        Assert.False(string.IsNullOrWhiteSpace(catalogItemId));
        seedProfile["catalogItemId"] = catalogItemId;

        var search = await catalog.SearchAsync(
            new CatalogListRequest { Query = catalogTitle },
            CatalogReadContext.Authenticated);
        Assert.Contains(search.Items, summary => summary.Id == catalogItemId && summary.Title == catalogTitle);

        using (var catalogContext = new Bunit.BunitContext())
        {
            catalogContext.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
            catalogContext.AddConsoleNotifications();
            catalogContext.Services.AddSingleton<IConsoleCatalogClient>(catalog);
            catalogContext.Services.AddSingleton<IConsoleCatalogReadContextResolver>(
                new AuthenticatedReadContextResolver());
            catalogContext.Services.AddSingleton<IOperateTransitionDataSource>(
                new UnsupportedOperateTransitionDataSource());
            var catalogPage = catalogContext.Render<CatalogPage>();
            catalogPage.WaitForAssertion(
                () => Assert.Contains(catalogTitle, catalogPage.Markup, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
        }

        steps.Add(new ConsoleEndToEndSmokeStep(
            "catalog-list",
            "server",
            "Seeded a public content item via the live create path and rendered it through the Catalog page.",
            $"itemId={catalogItemId}"));

        // ---------------------------------------------------------------------------------------------
        // Flow 2 — Preserve the retained package inspector/validation contract against live data,
        // then verify /studio hosts the current realtime editor rather than the retired builder.
        // ---------------------------------------------------------------------------------------------
        var studioClient = _fixture.CreateStudioClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(studioClient);

        var session = await shell.CreateInitialSessionAsync();
        Assert.True(session.BindingState is null,
            $"The configured live server did not return Studio package families: {session.BindingState?.Detail}");
        Assert.NotEmpty(session.Workflows);

        session = await shell.GeneratePackageAsync(
            session,
            session.SelectedWorkflowId,
            "Create an org map from the parcels layer for the Console end-to-end smoke");
        Assert.Null(session.BindingState);
        Assert.NotNull(session.Draft);
        Assert.StartsWith("studio-", session.ActivePackage.PackageRef, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(session.ActivePackage.SchemaVersion));
        seedProfile["studioPackageRef"] = session.ActivePackage.PackageRef;

        session = await shell.ValidateAsync(session);
        Assert.Null(session.BindingState);
        Assert.NotEmpty(session.ActivePackage.ValidationItems);

        // The retained inspector still has a live backend contract, but is no longer
        // mounted at /studio. Preserve its actual draft and validation assertions in
        // isolation rather than restoring the legacy authoring page to satisfy a test.
        using (var inspectorContext = new Bunit.BunitContext())
        {
            var inspector = inspectorContext.Render<StudioPackageInspector>(parameters =>
                parameters.Add(component => component.Package, session.ActivePackage));
            Assert.Equal(session.ActivePackage.PackageRef,
                inspector.Find("[data-package-ref]").GetAttribute("data-package-ref"));
            Assert.Contains(session.ActivePackage.ValidationItems[0].Detail,
                inspector.Find(".studio-validation-list").TextContent, StringComparison.Ordinal);
        }

        using (var studioContext = new Bunit.BunitContext())
        {
            studioContext.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" })
                .Build());
            studioContext.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
                .NavigateTo("/studio");
            var studioPage = studioContext.Render<StudioPage>();
            Assert.Single(studioPage.FindAll("[data-studio-embed] [data-studio-mount]"));
            Assert.Contains("Connecting", studioPage.Find("[data-studio-status]").TextContent, StringComparison.Ordinal);
            Assert.Single(studioPage.FindAll("a[href='/studio/processes']"));
            Assert.Empty(studioPage.FindAll("[data-authoring-contract], [data-legacy-studio-retired], [data-studio-unavailable]"));
            Assert.Empty(studioPage.FindAll("[data-package-ref]"));
        }

        steps.Add(new ConsoleEndToEndSmokeStep(
            "studio-package-render",
            "server",
            "Rendered the live validated draft in the retained inspector; separately verified the realtime Studio host. JavaScript editor execution requires the installed browser journey.",
            $"packageRef={session.ActivePackage.PackageRef};schema={session.ActivePackage.SchemaVersion};host=embedded;browserExecuted=false"));

        // ---------------------------------------------------------------------------------------------
        // Flow 3 — Operate surface against the live server. Seed a publication via the real publish POST,
        // then drive the server-bound publishing workspace and render the operate publishing page from
        // live data (honua-server#1183).
        // ---------------------------------------------------------------------------------------------
        var publicationSlug = $"console-e2e-publication-{runId}";
        var publicationId = await SeedPublicationAsync(publicationSlug);
        seedProfile["publicationSlug"] = publicationSlug;
        seedProfile["publicationId"] = publicationId;

        var publicationClient = _fixture.CreatePublicationClient();
        var publishingOptions = PublishingWorkspaceOptions.FromConfiguredList(publicationId);
        var publishingDataSource = new HonuaServerPublishingWorkspaceDataSource(publicationClient, publishingOptions);

        var workspace = await publishingDataSource.GetWorkspaceAsync();
        var review = Assert.Single(workspace.Reviews);
        Assert.Equal(publicationId, review.ResourceId);
        Assert.Contains(
            review.GeneratedEndpoints,
            endpoint => endpoint.Url.Contains(publicationSlug, StringComparison.Ordinal));

        using (var operateContext = new Bunit.BunitContext())
        {
            operateContext.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
            operateContext.AddConsoleNotifications();
            operateContext.Services.AddSingleton<IPublishingWorkspaceDataSource>(publishingDataSource);
            operateContext.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
            operateContext.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
            var operatePage = operateContext.Render<OperatePublishingPage>();
            operatePage.WaitForAssertion(
                () => Assert.Contains("Publication Matrix", operatePage.Markup, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.Contains(publicationSlug, operatePage.Markup, StringComparison.Ordinal);
        }

        steps.Add(new ConsoleEndToEndSmokeStep(
            "operate-publishing",
            "server",
            "Seeded a publication via the live publish POST and rendered it through the operate publishing page.",
            $"publicationId={publicationId};slug={publicationSlug}"));

        // ---------------------------------------------------------------------------------------------
        // Emit evidence recording the server image, the seed profile, and sourceHydrated: true so the
        // honua-console#9 gate can prove this ran against a real server (not the in-memory parity harness).
        // ---------------------------------------------------------------------------------------------
        var evidence = new ConsoleEndToEndSmokeEvidence
        {
            SourceHydrated = true,
            ServerImage = _fixture.ServerImageOrOrigin,
            ServerBaseAddress = _fixture.BaseAddress.ToString(),
            SeedProfile = seedProfile,
            Steps = steps,
            Result = "ok"
        };
        await evidence.WriteAsync();

        // The smoke is only honest if all three cross-surface flows ran against the live server.
        Assert.Equal(3, evidence.Steps.Count);
        Assert.True(evidence.SourceHydrated);
    }

    private async Task<string> SeedPublicationAsync(string slug)
    {
        using var http = _fixture.CreateHttpClient();

        // Mirrors honua-server PublishContentRequest. The server allocates the publication/version/revision
        // ids; we only declare kind + route slug + a content hash for the immutable version.
        var request = new
        {
            kind = HonuaContentPublicationKinds.Map,
            routeSlug = slug,
            title = "Console end-to-end smoke publication fixture",
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

    private sealed class AuthenticatedReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(
            string? publicLinkToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.Authenticated);
    }

}

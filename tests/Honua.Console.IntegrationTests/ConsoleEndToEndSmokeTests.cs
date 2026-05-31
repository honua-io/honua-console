using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Console end-to-end smoke against a REAL honua-server (issue #59). Boots ONE live honua-server +
/// PostgreSQL via Testcontainers and drives the core cross-surface chain — catalog list, a Studio package
/// render, and an operate/publishing surface — against that single server, asserting live responses at
/// every step and rendering each Console page from the live server-bound client (never an in-memory
/// adapter; Console Patterns Charter section 11). This supersedes the in-memory-only <c>smoke/parity</c>
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

        using (var catalogContext = new Bunit.TestContext())
        {
            catalogContext.Services.AddSingleton<IConsoleCatalogClient>(catalog);
            catalogContext.Services.AddSingleton<IConsoleCatalogReadContextResolver>(
                new AuthenticatedReadContextResolver());
            var catalogPage = catalogContext.RenderComponent<CatalogPage>();
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
        // Flow 2 — Studio package render against the live server. Discover families, generate a real
        // draft, run server validation, and render the live draft through the Studio page (#1180/#1181).
        // ---------------------------------------------------------------------------------------------
        var studioClient = _fixture.CreateStudioClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(studioClient);

        var session = await shell.CreateInitialSessionAsync();
        Skip.If(
            session.BindingState is not null,
            $"The live server did not return Studio package families: {session.BindingState?.Detail}");
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

        // Render the Studio page against the draft we just generated and validated against the live
        // server — not a fresh, empty session. The page calls IStudioAuthoringShell.CreateInitialSessionAsync
        // on init, so we hand it a shell that seeds the prepared live session; the assertions then prove the
        // generated package ref and its server validation state actually render through the Studio UI
        // (inspector + validation panel), so a regression in the draft/inspector/validate render path fails
        // this step instead of staying green on workflow-family chrome alone.
        var seededShell = new PreparedSessionAuthoringShell(shell, session);
        var renderedPackageRef = session.ActivePackage.PackageRef;
        var renderedValidationDetail = session.ActivePackage.ValidationItems[0].Detail;

        using (var studioContext = new Bunit.TestContext())
        {
            studioContext.Services.AddSingleton<IStudioAuthoringShell>(seededShell);
            var studioPage = studioContext.RenderComponent<StudioPage>();
            studioPage.WaitForAssertion(
                () =>
                {
                    Assert.Contains("data-authoring-contract", studioPage.Markup, StringComparison.Ordinal);
                    // The generated draft must render through the package inspector (data-package-ref) and
                    // its live validation state through the validation panel.
                    Assert.Contains(
                        $"data-package-ref=\"{renderedPackageRef}\"",
                        studioPage.Markup,
                        StringComparison.Ordinal);
                    Assert.Contains(renderedValidationDetail, studioPage.Markup, StringComparison.Ordinal);
                },
                TimeSpan.FromSeconds(10));
        }

        steps.Add(new ConsoleEndToEndSmokeStep(
            "studio-package-render",
            "server",
            "Generated and validated a real Studio package draft and rendered it through the Studio page.",
            $"packageRef={session.ActivePackage.PackageRef};schema={session.ActivePackage.SchemaVersion}"));

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

        using (var operateContext = new Bunit.TestContext())
        {
            operateContext.Services.AddSingleton<IPublishingWorkspaceDataSource>(publishingDataSource);
            var operatePage = operateContext.RenderComponent<OperatePublishingPage>();
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

    /// <summary>
    /// Wraps the live <see cref="IStudioAuthoringShell"/> but seeds the Studio page with a session that has
    /// already been generated and validated against the real server, so <see cref="StudioPage"/> renders that
    /// concrete draft (package inspector + validation panel) on init instead of recreating a fresh, empty
    /// session. Every other operation delegates to the real shell, keeping the rendered surface server-bound.
    /// </summary>
    private sealed class PreparedSessionAuthoringShell : IStudioAuthoringShell
    {
        private readonly IStudioAuthoringShell _inner;
        private readonly StudioAuthoringSession _prepared;

        public PreparedSessionAuthoringShell(IStudioAuthoringShell inner, StudioAuthoringSession prepared)
        {
            _inner = inner;
            _prepared = prepared;
        }

        public Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_prepared);

        public Task<StudioAuthoringSession> SelectWorkflowAsync(
            StudioAuthoringSession session,
            string workflowId,
            CancellationToken cancellationToken = default) =>
            _inner.SelectWorkflowAsync(session, workflowId, cancellationToken);

        public Task<StudioAuthoringSession> GeneratePackageAsync(
            StudioAuthoringSession session,
            string workflowId,
            string prompt,
            CancellationToken cancellationToken = default) =>
            _inner.GeneratePackageAsync(session, workflowId, prompt, cancellationToken);

        public Task<StudioAuthoringSession> ApplyClarificationAsync(
            StudioAuthoringSession session,
            string questionId,
            string choiceId,
            CancellationToken cancellationToken = default) =>
            _inner.ApplyClarificationAsync(session, questionId, choiceId, cancellationToken);

        public Task<StudioAuthoringSession> ValidateAsync(
            StudioAuthoringSession session,
            CancellationToken cancellationToken = default) =>
            _inner.ValidateAsync(session, cancellationToken);

        public Task<StudioAuthoringSession> PreviewPlanAsync(
            StudioAuthoringSession session,
            CancellationToken cancellationToken = default) =>
            _inner.PreviewPlanAsync(session, cancellationToken);

        public Task<StudioAuthoringSession> SaveVersionAsync(
            StudioAuthoringSession session,
            CancellationToken cancellationToken = default) =>
            _inner.SaveVersionAsync(session, cancellationToken);

        public Task<StudioAuthoringSession> PublishAsync(
            StudioAuthoringSession session,
            CancellationToken cancellationToken = default) =>
            _inner.PublishAsync(session, cancellationToken);
    }
}

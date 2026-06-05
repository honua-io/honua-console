using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
// The server wire enum, not the editor-catalog enum of the same simple name (mirrors the data sources).
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.IntegrationTests;

// Studio authoring→publish→content round-trips (console-integration-test-plan.md Wave 4, Family C, P0/P1).
//
// For each of the four Studio builders with a DISTINCT content/package shape — map, dashboard, report, form —
// these SkippableFacts drive the console's REAL draft→validate→publish operation for that content type and then
// assert the published content landed correctly INDEPENDENTLY through the ServerStateVerifier oracle, reading
// back through the server's own content/publication + offline-policy surfaces (NOT the console's own projection;
// plan §4 rule #2). Each positive round-trip verifies the package kind / content type, title, visibility,
// route/slug, and active revision/version where the content type exposes it, and that the builder/catalog
// surface reflects it. Each type has a negative companion proving a validation-violating publish is rejected
// with field-level errors (the validation initiative, task #70) and NOTHING lands.
//
// The four builders publish through three distinct server contracts:
//   • map + dashboard → the Studio package lifecycle (draft → content-version → publish-request,
//     /api/v1/studio/...). Verified via the canonical content-item version read.
//   • report          → the content publication registry (POST /api/v1/console/publications). Verified via the
//     admin publication-route detail (slug/kind/active-revision/visibility).
//   • form            → the admin form-package lifecycle (/api/v1/admin/forms/packages). Verified via the admin
//     form read AND the RUNTIME offline-policy contract (a different surface than the publish route).
//
// query / analysis / app / workflow are intentionally NOT covered here (tracked for follow-up): query +
// analysis publish through their own analysis/run + content contracts, app reuses the Studio lifecycle (same
// shape as map/dashboard already exercised), and workflow publishes through the workflow-publication contract;
// each has its own builder integration suite and warrants a dedicated round-trip in a follow-up wave.
//
// Off by default; every fact skips cleanly without Docker / the opt-in env (Console Patterns Charter section 11)
// and RUNs in the nightly lane (.github/workflows/console-nightly.yml).

/// <summary>
/// Map authoring→publish→content round-trip (Studio package lifecycle). Drives
/// <see cref="HonuaServerStudioMapPackageDataSource"/> save→publish and verifies the immutable content version
/// the publish froze through the canonical server content API, independently of the console projection.
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioMapPublishRoundTripTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioMapPublishRoundTripTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task MapPublish_LandsContentVersion_VerifiedIndependently_AndBuilderReflectsIt()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var title = $"Console IT map {suffix}";
        var source = new HonuaServerStudioMapPackageDataSource(_fixture.CreateClient(), new NoopStudioMapGenerationClient());

        // --- OPERATION: author + save a real map draft, then publish it (freezes an immutable version). ---
        var load = await source.LoadAsync(null);
        Assert.True(load.HasEditor);
        var state = load.State!;
        state.Title = title;
        state.Basemap = "basemap:streets";
        state.InitialExtent = "-158.3,21.2,-157.6,21.7";
        state.ShareTier = "organization";
        state.EmbedAllowed = true;
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:parcels@v1", Title = "Parcels" });

        var saved = await source.SaveDraftAsync(state);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(saved.Succeeded, saved.Issue?.State, saved.Message, "map draft save");
        Assert.NotNull(saved.State!.DraftId);

        var published = await source.PublishAsync(saved.State);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(published.Succeeded, published.Issue?.State, published.Message, "map publish");
        Assert.Equal(StudioMapStatuses.Published, published.State!.Status);
        Assert.NotNull(published.State.ItemId);
        Assert.NotNull(published.State.VersionId);

        // --- Independent server read: the immutable content version landed with the right map shape. ---
        using var verifier = _fixture.CreateVerifier();
        var version = await verifier.GetStudioContentVersionAsync(
            published.State.ItemId!.Value,
            published.State.VersionId!.Value);
        Assert.NotNull(version);
        Assert.Equal("map", version!.Family);
        Assert.Equal(StudioMapPackageMapper.SchemaVersion, version.SchemaVersion);
        Assert.Equal(published.State.VersionId!.Value.ToString(), version.VersionId);
        Assert.NotNull(version.VersionNumber);
        // The publication intent (visibility/route the operator chose) is frozen on the content envelope.
        Assert.Equal("organization", version.Visibility);

        // The version list (a different read than the single-version GET) shows exactly the cut version.
        var versionNumbers = await verifier.ListStudioContentVersionNumbersAsync(published.State.ItemId!.Value);
        Assert.Contains(version.VersionNumber!.Value, versionNumbers);

        // --- Console reflection: the builder page renders the live data source, not the missing-binding state. ---
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioMapPackageDataSource>(source);
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource, UnsupportedStudioMapStyleCatalogDataSource>();
        var page = ctx.RenderComponent<StudioMapBuilderPage>();
        page.WaitForAssertion(
            () => Assert.DoesNotContain("Map package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }

    [SkippableFact]
    public async Task MapPublish_WithInvalidDraft_IsRejectedWithFieldErrors_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateClient();

        // Create a real draft whose envelope deliberately violates the map package schema (no basemap, no
        // layers, no frame). Server-side validation must flag it; the gated publish path never cuts a version.
        var createResult = await client.CreatePackageDraftAsync(new CreateStudioPackageDraftRequest
        {
            PackageKey = $"studio-map-invalid-{Guid.NewGuid():N}"[..40],
            Envelope = new StudioPackageEnvelope
            {
                Family = StudioPackageFamily.Map,
                SchemaVersion = StudioMapPackageMapper.SchemaVersion,
                Format = "map.package",
                Body = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
            }
        });
        StudioLifecycleSkips.SkipIfDraftNotReady(createResult.Issue);
        Assert.Null(createResult.Issue);
        var draft = createResult.Data!;

        var validation = await client.ValidatePackageDraftAsync(draft.DraftId);
        StudioLifecycleSkips.SkipIfValidateNotReady(validation.Issue);
        Assert.Null(validation.Issue);

        // The validation initiative requires a rejecting validation to carry field-addressable diagnostics.
        // A pinned image whose map validation depth does not yet reject an empty body would return Valid;
        // skip cleanly in that case rather than asserting a contract the image does not implement yet.
        var summary = validation.Data!;
        Skip.If(
            summary.Status is StudioPackageValidationStatus.Valid,
            "The pinned server image's map package validation did not reject an empty envelope body; "
            + "the field-level rejection round-trip needs a server build whose map validation depth flags it.");
        Assert.Contains(
            summary.Diagnostics,
            diagnostic => diagnostic.Severity is StudioPackageDiagnosticSeverity.Error or StudioPackageDiagnosticSeverity.Blocker);
        Assert.Contains(summary.Diagnostics, diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Path));

        // Exercise the GATED publish path the operation uses (Codex #155): drive cut-version → publish-request
        // and assert no PUBLISHED content lands for a blocker-validated draft. Some server images defer
        // validation enforcement to publish-request (the cut is allowed but the publish is refused), so accept
        // a rejection at EITHER gate; what must never happen is an accepted publish request.
        await StudioLifecycleSkips.AssertInvalidDraftNeverPublishesAsync(client, draft);

        // Independently confirm NOTHING landed as published: the publication route for the item is absent.
        using var verifier = _fixture.CreateVerifier();
        Assert.Null(await verifier.GetPublicationRouteAsync(draft.ItemId.ToString()));
    }
}

/// <summary>
/// Dashboard authoring→publish→content round-trip (Studio package lifecycle). Drives
/// <see cref="HonuaServerStudioDashboardPackageDataSource"/> save→validate→publish and verifies the immutable
/// content version through the canonical server content API, independently of the console projection.
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioDashboardPublishRoundTripTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioDashboardPublishRoundTripTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task DashboardPublish_LandsContentVersion_VerifiedIndependently_AndBuilderReflectsIt()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var source = new HonuaServerStudioDashboardPackageDataSource(_fixture.CreateClient());
        var editor = ReadyDashboard($"Console IT dashboard {Guid.NewGuid():N}"[..40]);

        // --- OPERATION: save → server-validate → publish (freezes an immutable content version). ---
        var saved = await source.SaveDraftAsync(editor);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(saved.Succeeded, saved.Issue?.State, saved.Message, "dashboard draft save");
        Assert.NotNull(saved.State!.DraftId);

        var validated = await source.ValidateAsync(saved.State);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(validated.Succeeded, validated.Issue?.State, validated.Message, "dashboard validate");

        var published = await source.PublishAsync(validated.State!);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(published.Succeeded, published.Issue?.State, published.Message, "dashboard publish");
        Assert.Equal(StudioDashboardStatuses.Published, published.State!.Status);
        Assert.NotNull(published.State.ItemId);
        Assert.NotNull(published.State.CurrentVersionId);
        Assert.NotNull(published.State.PublishedVersion);

        // --- Independent server read: the immutable content version landed with the dashboard shape. ---
        using var verifier = _fixture.CreateVerifier();
        var version = await verifier.GetStudioContentVersionAsync(
            published.State.ItemId!.Value,
            published.State.CurrentVersionId!.Value);
        Assert.NotNull(version);
        Assert.Equal("dashboard", version!.Family);
        Assert.Equal(StudioDashboardPackageMapper.SchemaVersion, version.SchemaVersion);
        Assert.Equal(published.State.PublishedVersion!.Value, version.VersionNumber);
        // The dashboard data source routes a workspace-scoped publish to the "team" visibility at /share/dashboard.
        Assert.Equal("team", version.Visibility);

        var versionNumbers = await verifier.ListStudioContentVersionNumbersAsync(published.State.ItemId!.Value);
        Assert.Contains(published.State.PublishedVersion!.Value, versionNumbers);

        // --- Console reflection: the builder page renders the live data source, not the missing-binding state. ---
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioDashboardPackageDataSource>(source);
        var page = ctx.RenderComponent<StudioDashboardBuilderPage>();
        page.WaitForAssertion(
            () => Assert.DoesNotContain("Dashboard package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }

    [SkippableFact]
    public async Task DashboardPublish_WithInvalidDraft_IsRejectedWithFieldErrors_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateClient();

        // A dashboard draft with an empty body (no panels/bindings) violates the dashboard package schema.
        var createResult = await client.CreatePackageDraftAsync(new CreateStudioPackageDraftRequest
        {
            PackageKey = $"studio-dashboard-invalid-{Guid.NewGuid():N}"[..40],
            Envelope = new StudioPackageEnvelope
            {
                Family = StudioPackageFamily.Dashboard,
                SchemaVersion = StudioDashboardPackageMapper.SchemaVersion,
                Format = StudioDashboardPackageMapper.EnvelopeFormat,
                Body = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
            }
        });
        StudioLifecycleSkips.SkipIfDraftNotReady(createResult.Issue);
        Assert.Null(createResult.Issue);
        var draft = createResult.Data!;

        var validation = await client.ValidatePackageDraftAsync(draft.DraftId);
        StudioLifecycleSkips.SkipIfValidateNotReady(validation.Issue);
        Assert.Null(validation.Issue);

        var summary = validation.Data!;
        Skip.If(
            summary.Status is StudioPackageValidationStatus.Valid,
            "The pinned server image's dashboard package validation did not reject an empty envelope body; "
            + "the field-level rejection round-trip needs a server build whose dashboard validation depth flags it.");
        Assert.Contains(
            summary.Diagnostics,
            diagnostic => diagnostic.Severity is StudioPackageDiagnosticSeverity.Error or StudioPackageDiagnosticSeverity.Blocker);
        Assert.Contains(summary.Diagnostics, diagnostic => !string.IsNullOrWhiteSpace(diagnostic.Path));

        // Exercise the GATED publish path the operation uses (Codex #155): drive cut-version → publish-request
        // and assert no PUBLISHED content lands for a blocker-validated draft (rejection accepted at either
        // gate; an accepted publish request must never happen).
        await StudioLifecycleSkips.AssertInvalidDraftNeverPublishesAsync(client, draft);

        // Independently confirm NOTHING landed as published: the publication route for the item is absent.
        using var verifier = _fixture.CreateVerifier();
        Assert.Null(await verifier.GetPublicationRouteAsync(draft.ItemId.ToString()));
    }

    private static StudioDashboardEditorState ReadyDashboard(string title)
    {
        var state = new StudioDashboardEditorState { Title = title };
        state.Bindings.Add(new StudioDashboardBindingEditor
        {
            Alias = "requests",
            ContentRef = "content:service-requests",
            VersionPin = "v5"
        });
        state.Panels.Add(new StudioDashboardPanelEditor
        {
            Title = "Requests by district",
            Kind = StudioDashboardPanelKinds.Chart,
            BindingAlias = "requests",
            VegaLiteSpec = StudioDashboardChartSpec.DefaultBarChart("district", "request_count")
        });
        return state;
    }
}

/// <summary>
/// Report authoring→publish→content round-trip (content publication registry). Drives
/// <see cref="HonuaServerStudioReportPublicationDataSource"/> publish and verifies the publication route
/// (slug/kind/active-revision/visibility) through the admin publication detail — a DIFFERENT read API than the
/// publish path — plus the report builder page reflection.
/// </summary>
[Collection(StudioReportPublicationIntegrationCollection.Name)]
public sealed class StudioReportPublishRoundTripTests
{
    private readonly StudioReportPublicationFixture _fixture;

    public StudioReportPublishRoundTripTests(StudioReportPublicationFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ReportPublish_LandsPublicationRoute_VerifiedIndependently_AndBuilderReflectsIt()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"console-it-report-{suffix}";
        var title = $"Console IT report {suffix}";
        var dataSource = new HonuaServerStudioReportPublicationDataSource(_fixture.CreatePublicationClient());

        // --- OPERATION: author + publish a report through the real publication registry (claims a route). ---
        var seed = new StudioReportEditorState
        {
            Title = title,
            RouteSlug = slug,
            Narrative = "Wave 4 authoring→publish round-trip.",
            Visibility = StudioReportVisibilities.Organization,
            Embeddable = true
        };
        seed.Bindings.Add(new StudioReportBindingEditor { Alias = "incidents", ContentRef = "content:incidents", VersionPin = "v1" });
        seed.Panels.Add(new StudioReportPanelEditor
        {
            Title = "Incidents by district",
            Kind = StudioReportPanelKinds.Chart,
            BindingAlias = "incidents",
            VegaLiteSpec = StudioReportChartSpec.DefaultBarChart("district", "incident_count")
        });

        var published = await dataSource.PublishAsync(seed);
        Assert.True(published.Succeeded, $"The live server rejected the report publish: {published.Message}");
        Assert.NotNull(published.Publication);
        var publicationId = published.Publication!.PublicationId;
        Assert.False(string.IsNullOrWhiteSpace(publicationId));
        Assert.Equal(1, published.Publication.ActiveRevision);

        // --- Independent server read: the publication route landed with the right kind/slug/revision/visibility. ---
        using var verifier = _fixture.CreateVerifier();
        var route = await verifier.GetPublicationRouteAsync(publicationId);
        Assert.NotNull(route);
        Assert.Equal(slug, route!.RouteSlug);
        Assert.Equal(HonuaContentPublicationKinds.Report, route.Kind);
        Assert.Equal(1, route.ActiveRevision);
        Assert.Equal("organization", route.Visibility);

        // --- Console reflection: the report builder page renders the live publication + version history. ---
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(dataSource);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("publicationId", publicationId));
        var page = ctx.RenderComponent<StudioReportBuilderPage>();
        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("data-report-publication", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Immutable versions", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(10));
    }

    [SkippableFact]
    public async Task ReportPublish_WithInvalidConfig_IsRejected_AndNothingLands()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"console-it-report-bad-{suffix}";

        // The report builder publishes through the content publication client; drive it directly with an
        // inverted default-view bbox (minX > maxX), which the server's publish validation rejects
        // deterministically (ties to the validation initiative). No route is claimed for the slug.
        var client = _fixture.CreatePublicationClient();
        var result = await client.PublishAsync(new HonuaPublishContentRequest
        {
            Kind = HonuaContentPublicationKinds.Report,
            RouteSlug = slug,
            Title = $"Console IT report bad {suffix}",
            ContentPayload = """{"report":"console-integration-test-invalid"}""",
            DefaultViewBbox = new HonuaContentPublicationBbox
            {
                Crs = "EPSG:4326",
                MinX = 10,
                MinY = 10,
                MaxX = -10,
                MaxY = -10
            }
        });

        Skip.If(
            result.Issue is { } issue5 && issue5.StatusCode >= 500,
            $"The pinned honua-server image could not service the publish path ({result.Issue!.State} — {result.Issue.Detail}).");
        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal("Rejected", result.Issue!.State);

        // Independently confirm NOTHING landed: no publication is reachable at the slug, anonymously or not.
        using var verifier = _fixture.CreateVerifier();
        var anon = await verifier.FetchPublishedRouteAnonymouslyAsync(slug);
        Assert.False(anon.Granted, $"A rejected report publish left an anonymously-reachable route (HTTP {anon.StatusCode}).");
    }
}

/// <summary>
/// Form authoring→publish→content round-trip (admin form-package lifecycle). Drives
/// <see cref="HonuaServerStudioFormPackageDataSource"/> save→validate→publish and verifies the published form
/// through the admin form read AND the RUNTIME offline-policy contract — a different surface than the publish
/// route — independently of the console projection.
/// </summary>
[Collection(StudioFormPackageIntegrationCollection.Name)]
public sealed class StudioFormPublishRoundTripTests
{
    private readonly StudioFormPackageFixture _fixture;

    public StudioFormPublishRoundTripTests(StudioFormPackageFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task FormPublish_FlipsPublishedVersion_VerifiedIndependently_AndBuilderReflectsIt()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var title = $"Console IT form {suffix}";
        var dataSource = new HonuaServerStudioFormPackageDataSource(_fixture.CreateFormClient());

        // --- OPERATION: author a publishable form, save → validate → publish through the real lifecycle. ---
        var seed = StudioFormPackageMapper.CreateTemplate();
        seed.Title = title;
        seed.ServiceId = "console-form-fixture";
        seed.LayerId = 0;
        seed.OfflinePolicyReviewed = true;

        var saved = await dataSource.SaveDraftAsync(seed);
        Assert.True(saved.Succeeded, $"The live server rejected the seeded form draft: {saved.Message}");
        var formId = saved.State!.FormId;
        Assert.False(string.IsNullOrWhiteSpace(formId));

        var validated = await dataSource.ValidateAsync(saved.State);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(validated.Succeeded, validated.Issue?.State, validated.Message, "form validate");

        var published = await dataSource.PublishAsync(validated.State!);
        StudioLifecycleSkips.SkipOrFailOnConsoleOperation(published.Succeeded, published.Issue?.State, published.Message, "form publish");
        Assert.Equal(HonuaFormStatuses.Published, published.State!.Status);
        var publishedVersion = published.State.Version;

        // --- Independent server read #1: the admin form-package row shows the published version + shape. ---
        using var verifier = _fixture.CreateVerifier();
        var package = await verifier.GetFormPackageAsync(formId!);
        Assert.NotNull(package);
        Assert.Equal(formId, package!.FormId);
        Assert.Equal(title, package.Title);
        Assert.Equal("console-form-fixture", package.ServiceId);
        Assert.Equal(HonuaFormPackageStatus.Published, package.Status);
        Assert.Equal(publishedVersion, package.Version);

        // --- Independent server read #2: the RUNTIME offline-policy contract resolves for the published form
        //     (a genuinely different surface than the admin publish route). ---
        var offline = await verifier.GetFormOfflinePolicyAsync(formId!);
        Assert.NotNull(offline);

        // --- Console reflection: the form builder page renders the published form from the live data source. ---
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(dataSource);
        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(
            () => Assert.Contains(title, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }

    [SkippableFact]
    public async Task FormPublish_WithInvalidTarget_IsRejectedWithFieldErrors_AndNothingPublishes()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var title = $"Console IT form bad {suffix}";
        var client = _fixture.CreateFormClient();
        var dataSource = new HonuaServerStudioFormPackageDataSource(client);

        // A form whose field declares a submit target field that does not map to the layer is rejected by the
        // server's publish validation with a field-level issue (validation initiative). Save it first so the
        // draft exists, then publish to trigger the server-side rejection.
        var seed = StudioFormPackageMapper.CreateTemplate();
        seed.Title = title;
        seed.ServiceId = "console-form-fixture-bad";
        seed.LayerId = 0;
        seed.OfflinePolicyReviewed = true;
        seed.Fields.Clear();
        seed.Fields.Add(new StudioFormFieldEditor
        {
            FieldId = "asset_id",
            Label = "Asset",
            TargetField = "this_target_field_does_not_exist_on_layer"
        });

        var saved = await dataSource.SaveDraftAsync(seed);
        Assert.True(saved.Succeeded, $"The live server rejected the seeded form draft: {saved.Message}");
        var formId = saved.State!.FormId;
        Assert.False(string.IsNullOrWhiteSpace(formId));

        // Run server validation against the invalid draft. The validate endpoint is the field-level rejection
        // surface (validation initiative): a ready server returns field-addressable validation issues for the
        // bad submit target. A 5xx / Unavailable here means the pinned image cannot service the form
        // validate/publish path (contract drift) — skip cleanly rather than false-fail (mirrors W1/W3).
        var validated = await dataSource.ValidateAsync(saved.State);
        StudioLifecycleSkips.SkipIfFormValidateNotReady(validated);

        // ValidateAsync returns Succeeded=true even when the server reports issues (the command surfaces the
        // findings on Validation); a genuinely invalid form must therefore carry validation issues, and at
        // least one must be field-addressable. If the server validated it clean, the pinned image's form
        // validation depth does not yet flag a bad submit target — skip cleanly.
        var validationIssues = validated.Validation?.Issues ?? saved.State.LastValidation?.Issues ?? [];
        Skip.If(
            validated.Validation is { IsValid: true } || validationIssues.Count == 0,
            "The pinned server image's form validation did not flag an invalid submit target; the field-level "
            + "rejection round-trip needs a server build whose form validation depth flags it.");
        Assert.Contains(validationIssues, issue => !string.IsNullOrWhiteSpace(issue.FieldId));

        // The publish gate must keep this draft unpublished (validation is not valid), and the server must
        // never flip it to published.
        var published = await dataSource.PublishAsync(validated.State ?? saved.State);
        Assert.False(published.Succeeded, "A form with an invalid submit target unexpectedly published.");

        // Independently confirm NOTHING published: the admin form row is not in the published state.
        using var verifier = _fixture.CreateVerifier();
        var package = await verifier.GetFormPackageAsync(formId!);
        if (package is not null)
        {
            Assert.NotEqual(HonuaFormPackageStatus.Published, package.Status);
        }
    }
}

/// <summary>
/// Shared skip helpers for the Studio authoring→publish round-trips: a not-ready signal from the pinned image
/// (missing lifecycle path / unsupported verb / 5xx) is a server-readiness condition, not a console
/// regression, so the lane reports "not exercised" rather than a false failure (mirrors the W1/W3 pattern).
/// </summary>
internal static class StudioLifecycleSkips
{
    // Server-readiness states the builder data sources surface on their capability state when the pinned image
    // cannot service a lifecycle path (transport/5xx → "Unavailable"; missing route/verb → "Unsupported";
    // permission gaps → "Missing permission"). These are NOT console regressions, so they justify a clean skip.
    private static readonly string[] ServerNotReadyStates =
        ["Unavailable", "Unsupported", "Missing permission"];

    /// <summary>
    /// For a console builder OPERATION driven with a known-good input: succeed silently, SKIP cleanly when the
    /// failure carries a server-not-ready capability state (the pinned image lacks the path — mirrors W1/W3), or
    /// FAIL when the operation failed for any other reason (a real console-side regression — e.g. the operation
    /// built an invalid envelope/publish request). This is the Codex #155 fix: a rejected publish of a valid
    /// input must fail, not silently skip and let the nightly lane look green while nothing was published.
    /// </summary>
    public static void SkipOrFailOnConsoleOperation(bool succeeded, string? issueState, string message, string operation)
    {
        if (succeeded)
        {
            return;
        }

        var serverNotReady = issueState is not null
            && ServerNotReadyStates.Any(state => string.Equals(state, issueState, StringComparison.OrdinalIgnoreCase));

        Skip.If(
            serverNotReady,
            $"The pinned honua-server image could not service the {operation} path ({issueState} — {message}); "
            + "the authoring→publish round-trip needs a server build whose Studio lifecycle is ready.");

        Assert.True(
            succeeded,
            $"The console {operation} operation failed against a ready server (no server-not-ready signal): {message}. "
            + "A regression in the operation under test must fail here, not skip.");
    }

    /// <summary>
    /// Drives the gated cut-version → publish-request path for a blocker-validated draft and asserts no
    /// PUBLISHED content lands (Codex #155). Server images differ on WHERE the validation gate enforces: some
    /// refuse the version cut, others allow the cut but refuse the publish request. Accept a rejection at
    /// either gate; the only forbidden outcome is an accepted publish request. When the cut is allowed and the
    /// publish request is rejected, the cut version is an unpublished draft revision — that is fine; the
    /// caller's independent publication-route check proves nothing reachable landed.
    /// </summary>
    public static async Task AssertInvalidDraftNeverPublishesAsync(
        IStudioPackageLifecycleClient client,
        StudioPackageDraft draft)
    {
        var versionAttempt = await client.SaveContentVersionAsync(
            draft.DraftId,
            new SaveStudioContentVersionRequest { ChangeNote = "invalid-draft negative companion" });

        if (versionAttempt.Issue is not null)
        {
            // The server gated at the version-cut step — no version, nothing to publish.
            Assert.Null(versionAttempt.Data);
            return;
        }

        // The cut was allowed; the publish request MUST be the gate that refuses a blocker-validated version.
        var version = versionAttempt.Data!;
        var publishAttempt = await client.CreatePublishRequestAsync(
            version.ItemId,
            version.VersionId,
            new CreateStudioPublicationRequest());

        Assert.True(
            publishAttempt.Issue is not null
                || publishAttempt.Data?.Status is StudioPublicationRequestStatus.Rejected,
            "A blocker-validated draft must not yield an accepted publish request; "
            + $"the server accepted it (status {publishAttempt.Data?.Status}).");
    }

    public static void SkipIfDraftNotReady(StudioEndpointIssue? issue)
    {
        if (issue is null)
        {
            return;
        }

        Skip.If(
            issue.State is "Unsupported" or "Unavailable" || issue.StatusCode >= 500,
            $"The pinned honua-server image could not service the Studio package-draft path ({issue.State} — {issue.Detail}); "
            + "the authoring→publish round-trip needs a server build whose Studio package lifecycle (#1180) is ready.");
    }

    public static void SkipIfValidateNotReady(StudioEndpointIssue? issue)
    {
        if (issue is null)
        {
            return;
        }

        Skip.If(
            issue.State is "Unsupported" or "Unavailable" || issue.StatusCode >= 500,
            $"The pinned honua-server image could not service the Studio validate path ({issue.State} — {issue.Detail}); "
            + "the field-level rejection round-trip needs a server build whose Studio validation (#1181) is ready.");
    }

    public static void SkipIfFormValidateNotReady(StudioFormCommandResult result)
    {
        if (result.Issue is not { } issue)
        {
            return;
        }

        // The form client maps a 5xx / transport failure to the neutral "Unavailable"/"Unsupported" state. That
        // signals the pinned image cannot service the form validate/publish path (the nightly image currently
        // 500s on form validate), so skip cleanly rather than false-fail. A genuine "Rejected" with findings is
        // the real reject path and must not be skipped.
        Skip.If(
            issue.State is "Unavailable" or "Unsupported",
            $"The pinned honua-server image could not service the form validate path ({issue.State} — {issue.Detail}).");
    }
}

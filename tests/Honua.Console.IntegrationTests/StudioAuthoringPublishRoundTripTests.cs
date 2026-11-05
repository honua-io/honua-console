using Honua.Sdk.Studio.Packages;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
// The server wire enum, not the editor-catalog enum of the same simple name (mirrors the data sources).
using StudioPackageFamily = Honua.Sdk.Studio.Packages.StudioPackageFamily;

namespace Honua.Console.IntegrationTests;

// Studio authoring→publish→content round-trips for the report and form builders. Each drives the console's
// real publish operation, then verifies the published content independently through the server's own
// publication and offline-policy surfaces, with a negative companion proving an invalid publish lands nothing.
//   • report → the content publication registry (POST /api/v1/console/publications), verified via the admin
//     publication-route detail.
//   • form   → the admin form-package lifecycle (/api/v1/admin/forms/packages), verified via the admin form
//     read and the runtime offline-policy contract.
// Off by default; every fact skips cleanly without Docker and the opt-in env, and runs in the nightly lane
// (.github/workflows/console-nightly.yml).

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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(dataSource);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("publicationId", publicationId));
        var page = ctx.Render<StudioReportBuilderPage>();
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

        Assert.False(
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
        seed.ServiceId = _fixture.TargetServiceId;
        seed.LayerId = _fixture.TargetLayerId;
        seed.OfflinePolicyReviewed = true;

        var saved = await dataSource.SaveDraftAsync(seed);
        Assert.True(saved.Succeeded, $"The live server rejected the seeded form draft: {saved.Message}");
        var formId = saved.State!.FormId;
        Assert.False(string.IsNullOrWhiteSpace(formId));

        var validated = await dataSource.ValidateAsync(saved.State);
        StudioLifecycleAssertions.RequireConsoleOperation(validated.Succeeded, validated.Issue?.State, validated.Message, "form validate");

        var published = await dataSource.PublishAsync(validated.State!);
        StudioLifecycleAssertions.RequireConsoleOperation(published.Succeeded, published.Issue?.State, published.Message, "form publish");
        Assert.Equal(HonuaFormStatuses.Published, published.State!.Status);
        var publishedVersion = published.State.Version;

        // --- Independent server read #1: the admin form-package row shows the published version + shape. ---
        using var verifier = _fixture.CreateVerifier();
        var package = await verifier.GetFormPackageAsync(formId!);
        Assert.NotNull(package);
        Assert.Equal(formId, package!.FormId);
        Assert.Equal(title, package.Title);
        Assert.Equal(_fixture.TargetServiceId, package.ServiceId);
        Assert.Equal(HonuaFormPackageStatus.Published, package.Status);
        Assert.Equal(publishedVersion, package.Version);

        // --- Independent server read #2: the RUNTIME offline-policy contract resolves for the published form
        //     (a genuinely different surface than the admin publish route). ---
        var offline = await verifier.GetFormOfflinePolicyAsync(formId!);
        Assert.NotNull(offline);

        // --- Console reflection: the form builder page renders the published form from the live data source. ---
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(dataSource);
        var page = ctx.Render<StudioFormBuilderPage>();
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
        seed.ServiceId = _fixture.TargetServiceId;
        seed.LayerId = _fixture.TargetLayerId;
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
        StudioLifecycleAssertions.AssertFormValidateReady(validated);

        // ValidateAsync returns Succeeded=true even when the server reports issues (the command surfaces the
        // findings on Validation); a genuinely invalid form must therefore carry validation issues, and at
        // least one must be field-addressable. If the server validated it clean, the pinned image's form
        // validation depth does not yet flag a bad submit target — skip cleanly.
        var validationIssues = validated.Validation?.Issues ?? saved.State.LastValidation?.Issues ?? [];
        Assert.False(
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

internal static class StudioLifecycleAssertions
{
    public static void RequireConsoleOperation(bool succeeded, string? issueState, string message, string operation)
    {
        Assert.True(succeeded, $"The console {operation} failed ({issueState}): {message}.");
    }

    public static void AssertFormValidateReady(StudioFormCommandResult result)
    {
        if (result.Issue is not { } issue)
        {
            return;
        }

        // The form client maps a 5xx / transport failure to the neutral "Unavailable"/"Unsupported" state. That
        // fails the live contract receipt. A "Rejected" response with findings is the expected negative path.
        Assert.False(
            issue.State is "Unavailable" or "Unsupported",
            $"The pinned honua-server image could not service the form validate path ({issue.State} — {issue.Detail}).");
    }
}

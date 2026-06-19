using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the two metadata-authoring gaps closed in this branch (gap report
/// Bucket 3-A #3 relationships, #4 time-info): the relationships editor page
/// (<c>/operate/layers/{id}/relationships</c>) and the self-contained time-info setter component on the
/// temporal surface. Drives both through fakes (never a mock server). The merged-build Unsupported* sources
/// are exercised through real DI to prove the honest missing-binding state, and the bound paths prove the
/// GET load + the add/remove/save round-trip surface the result.
/// </summary>
public sealed class LayerRelationshipsAndTimeInfoRenderTests
{
    private const string ResourceId = "conn-1-layer-1";

    // ---- Relationships editor ----

    [Fact]
    public void Relationships_WhenBound_RendersExistingRowsFromGet()
    {
        var page = RenderRelationships(new FakeRelationships
        {
            Read = new ConsoleLayerRelationships
            {
                Bound = true,
                LayerId = 1,
                Relationships =
                [
                    new ConsoleLayerRelationship
                    {
                        Id = "rel0", Name = "self", RelatedLayerId = 1, Role = "origin",
                        Cardinality = "one-to-many", OriginField = "id", DestinationField = "id", EsriRelationshipId = 7,
                    }
                ],
            },
        });

        page.WaitForAssertion(
            () => Assert.Contains("data-relationship-row", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"self\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rel-save", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Relationships_AddRowThenSave_IssuesSaveWithTheRows()
    {
        var fake = new FakeRelationships
        {
            Read = new ConsoleLayerRelationships { Bound = true, LayerId = 1, Relationships = [] },
            SaveResult = new ConsoleSetRelationshipsResult { Succeeded = true, State = "Updated", Detail = "Saved 1 relationship(s) on this layer." },
        };
        var page = RenderRelationships(fake);

        page.WaitForAssertion(
            () => Assert.Contains("data-relationships-empty", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-rel-add]").Click();
        page.Find("[data-rel-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-relationships-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The page issued a real save call carrying the (one) added row.
        Assert.NotNull(fake.LastSaved);
        Assert.Single(fake.LastSaved!);
        Assert.Contains("Saved 1 relationship", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Relationships_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>();

        var page = ctx.Render<OperateLayerRelationshipsPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.Contains("data-relationships-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    // ---- Time-info setter ----

    [Fact]
    public void TimeInfo_WhenOperationMissing_RendersMissingBindingOnSave()
    {
        // No IConsoleTimeInfoOperation registered: the component must still render and degrade honestly.
        using var ctx = new Bunit.BunitContext();
        var component = ctx.Render<ServiceTimeInfoSetter>();

        component.Find("[data-timeinfo-service]").Change("svc");
        component.Find("[data-timeinfo-save]").Click();

        component.WaitForAssertion(
            () => Assert.Contains("data-timeinfo-result", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Missing binding", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeInfo_WhenBound_SaveIssuesTheTimeInfoUpdateWithFields()
    {
        var fake = new FakeTimeInfo
        {
            SaveResult = new ConsoleSetTimeInfoResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "The service's time-info was updated on honua-server.",
                StartTimeField = "observed_at",
                EndTimeField = "observed_until",
                TrackIdField = "vehicle_id",
            },
        };
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleTimeInfoOperation>(fake);
        var component = ctx.Render<ServiceTimeInfoSetter>();

        component.Find("[data-timeinfo-service]").Change("svc");
        component.Find("[data-timeinfo-start]").Change("observed_at");
        component.Find("[data-timeinfo-end]").Change("observed_until");
        component.Find("[data-timeinfo-track]").Change("vehicle_id");
        component.Find("[data-timeinfo-save]").Click();

        component.WaitForAssertion(
            () => Assert.Contains("data-timeinfo-result", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Equal("svc", fake.SavedService);
        Assert.Equal("observed_at", fake.SavedStart);
        Assert.Equal("observed_until", fake.SavedEnd);
        Assert.Equal("vehicle_id", fake.SavedTrack);
        Assert.Contains("time-info was updated", component.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateLayerRelationshipsPage> RenderRelationships(FakeRelationships relationships)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeTransition());
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation>(relationships);
        return ctx.Render<OperateLayerRelationshipsPage>(p => p.Add(x => x.ResourceId, ResourceId));
    }

    private sealed class FakeTransition : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace(
                [],
                [],
                [
                    new OperateServiceDetail(
                        "svc", "Service", "FeatureServer", "running", "server",
                        [new OperateServiceLayerProjection(1, "Parcels", "polygon", ResourceId, "parcels")],
                        [],
                        [])
                ],
                [],
                []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class FakeRelationships : IConsoleLayerRelationshipsOperation
    {
        public ConsoleLayerRelationships Read { get; set; } = ConsoleLayerRelationships.Unbound("test");
        public ConsoleSetRelationshipsResult? SaveResult { get; set; }
        public IReadOnlyList<ConsoleLayerRelationship>? LastSaved { get; private set; }

        public Task<ConsoleLayerRelationships> GetRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Read);

        public Task<ConsoleSetRelationshipsResult> SetRelationshipsAsync(
            int layerId, IReadOnlyList<ConsoleLayerRelationship> relationships, CancellationToken cancellationToken = default)
        {
            LastSaved = relationships;
            return Task.FromResult(SaveResult ?? new ConsoleSetRelationshipsResult { Succeeded = true, State = "Updated" });
        }
    }

    private sealed class FakeTimeInfo : IConsoleTimeInfoOperation
    {
        public ConsoleSetTimeInfoResult? SaveResult { get; set; }
        public string? SavedService { get; private set; }
        public string? SavedStart { get; private set; }
        public string? SavedEnd { get; private set; }
        public string? SavedTrack { get; private set; }

        public Task<ConsoleServiceTimeInfo> GetTimeInfoAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleServiceTimeInfo { Bound = true, ServiceName = serviceName });

        public Task<ConsoleSetTimeInfoResult> SetTimeInfoAsync(
            string serviceName, string? startTimeField, string? endTimeField, string? trackIdField,
            CancellationToken cancellationToken = default)
        {
            SavedService = serviceName;
            SavedStart = startTimeField;
            SavedEnd = endTimeField;
            SavedTrack = trackIdField;
            return Task.FromResult(SaveResult ?? new ConsoleSetTimeInfoResult { Succeeded = true, State = "Updated" });
        }
    }
}

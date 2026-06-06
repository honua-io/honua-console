using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Null-safety coverage for the non-Studio wire-DTO mappers that re-project freshly-deserialized server
/// records. System.Text.Json overrides a collection's <c>[]</c>/<c>Array.Empty()</c> initializer (and a
/// nested object's <c>new()</c> initializer) with null when the server emits an explicit JSON <c>null</c>
/// for the key, so every mapper must coalesce before LINQ rather than throwing and tearing down the Blazor
/// circuit (Console Patterns Charter section 11 — bind the real server, never throw on an honest-but-empty
/// payload). Each test feeds an explicit <c>null</c> into a collection/nested-object the wire contract
/// declares non-null and asserts the projection succeeds with an empty result.
/// </summary>
public sealed class DeserializedDtoNullCollectionMapperTests
{
    private static ConsoleEnvironmentProfile Profile() =>
        new() { Id = "env-1", DisplayName = "Env One" };

    [Fact]
    public void CatalogDiscoveryMapper_Registry_NullEndpointsAndFeeders_ProjectsEmptyWithoutThrowing()
    {
        var registry = new HonuaCatalogDiscoveryRegistry
        {
            WorkspaceId = "ws-1",
            Endpoints = null!
        };

        var view = CatalogDiscoveryMapper.ToView(registry);

        Assert.Equal("ws-1", view.WorkspaceId);
        Assert.Empty(view.Endpoints);
    }

    [Fact]
    public void CatalogDiscoveryMapper_EndpointDetail_NullItemsAndNestedCollections_ProjectsEmpty()
    {
        var detail = new HonuaCatalogEndpointDetail
        {
            Endpoint = new HonuaCatalogEndpoint
            {
                Key = "esri",
                Title = "Esri",
                Dialect = "esri",
                Feeders = null!
            },
            Items = null!
        };

        var view = CatalogDiscoveryMapper.ToView(detail);

        Assert.Empty(view.Endpoint.Feeders);
        Assert.Empty(view.Items);
    }

    [Fact]
    public void CatalogDiscoveryMapper_Item_NullGroupsAndFields_ProjectsEmpty()
    {
        var item = new HonuaCatalogItem
        {
            Id = "item-1",
            Title = "Parcels",
            Groups = null!
        };

        var view = CatalogDiscoveryMapper.ToView(item);

        Assert.Empty(view.Groups);
    }

    [Fact]
    public void RbacAccessMapper_Overview_NullCollections_ProjectsEmptyWithoutThrowing()
    {
        var overview = new HonuaConsoleRbacOverview
        {
            WorkspaceId = "ws-1",
            Scopes = null!,
            Permissions = null!,
            Roles = null!
        };

        var view = RbacAccessMapper.ToView(overview);

        Assert.Empty(view.Scopes);
        Assert.Empty(view.Permissions);
        Assert.Empty(view.Roles);
    }

    [Fact]
    public void RbacAccessMapper_Membership_NullCollections_ProjectsEmptyWithoutThrowing()
    {
        var membership = new HonuaConsoleTeamMembership
        {
            WorkspaceId = "ws-1",
            Members = null!,
            Invitations = null!
        };

        var view = RbacAccessMapper.ToView(membership);

        Assert.Empty(view.Members);
        Assert.Empty(view.Invitations);
    }

    [Fact]
    public void OperateObservabilityMapper_MapEvents_NullItems_ProjectsEmptyWithoutThrowing()
    {
        var page = new OperateEventPageResponse { Items = null! };

        var rows = OperateObservabilityMapper.MapEvents(page, Profile());

        Assert.Empty(rows);
    }

    [Fact]
    public void OperateObservabilityMapper_MapJobSummary_NullResourceRefs_ProjectsEmptyWithoutThrowing()
    {
        var summary = new ConsoleJobSummary
        {
            JobId = "job-1",
            Kind = "import",
            Status = "Running",
            ResourceRefs = null!
        };

        var row = OperateObservabilityMapper.MapJobSummary(summary, Profile());

        Assert.Equal("job-1", row.JobRunId);
        Assert.Empty(row.ResourceRefs);
    }

    [Fact]
    public void OperateObservabilityMapper_MapRule_NullChannelsAndHealthChannels_ProjectsWithoutThrowing()
    {
        var rule = new AlertRuleResponse
        {
            RuleId = 7,
            RuleName = "High latency",
            TriggerType = "metric",
            Channels = null!
        };
        var health = new AlertRuleHealthResponse { RuleId = 7, DeliveryChannels = null! };

        var mapped = OperateObservabilityMapper.MapRule(rule, health);

        Assert.Equal("7", mapped.RuleId);
        Assert.Contains("no channel configured", mapped.DeliverySummary, StringComparison.Ordinal);
    }

    [Fact]
    public void OperateObservabilityMapper_MapInvestigation_NullLinksAndPins_ProjectsWithoutThrowing()
    {
        var investigation = new InvestigationResponse
        {
            InvestigationId = "inv-1",
            Title = "Outage",
            Status = "open",
            CreatedBy = "ops",
            Links = null!,
            Pins = null!
        };

        var view = OperateObservabilityMapper.MapInvestigation(investigation);

        Assert.Equal("inv-1", view.InvestigationId);
        Assert.Empty(view.PinnedEventIds);
        Assert.Empty(view.LinkedAlertIds);
        Assert.Empty(view.LinkedJobRunIds);
    }
}

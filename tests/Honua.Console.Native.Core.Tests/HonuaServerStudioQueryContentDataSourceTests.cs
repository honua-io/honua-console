using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the server-bound query data source (honua-console#52). Drives
/// <see cref="HonuaServerStudioQueryContentDataSource"/> over a recording fake of the real
/// <see cref="IHonuaAnalysisContentClient"/> shim to assert the live binding maps the authored query to the
/// honua-server saved-query content contract (honua-server#1182, AnalysisContentKind.SavedQuery), surfaces
/// server failures and the documented list gap as explicit capability states rather than fabricating data,
/// and resolves the live map/table preview + downstream binding (AC#1/AC#2/AC#3).
/// </summary>
public sealed class HonuaServerStudioQueryContentDataSourceTests
{
    [Fact]
    public async Task GetWorkspace_HasNoListRoute_SurfacesUnsupportedListStateNotMockData()
    {
        var source = new HonuaServerStudioQueryContentDataSource(new FakeQueryContentClient());

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
        Assert.Contains("list", state.Contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_NewDraft_ReturnsBlankTemplateWithoutServerCall()
    {
        var client = new FakeQueryContentClient();
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var load = await source.LoadAsync(null);

        Assert.True(load.HasEditor);
        Assert.False(load.Query!.IsExistingQuery);
        Assert.Equal(0, client.GetVersionCalls);
    }

    [Fact]
    public async Task Load_ExistingQuery_LiftsServerSavedQueryAndPredicates()
    {
        var client = new FakeQueryContentClient
        {
            GetVersionResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromData(
                BuildVersionResponse("query-7", 3))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var load = await source.LoadAsync("query-7");

        Assert.True(load.HasEditor);
        Assert.Equal("query-7", load.Query!.QueryId);
        Assert.Equal(3, load.Query.Version);
        Assert.Equal("permits", load.Query.ServiceName);
        Assert.Equal(5, load.Query.LayerId);
        // The server filter plan is lifted back into editor predicates.
        Assert.Contains(load.Query.Predicates, p => p.Kind == StudioQueryPredicateKinds.Comparison && p.Field == "status");
        Assert.Contains(load.Query.OutFields, f => f == "permit_id");
    }

    [Fact]
    public async Task Load_ServerReturnsNotFound_SurfacesIssueWithoutEditor()
    {
        var client = new FakeQueryContentClient
        {
            GetVersionResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET version", "Not found.", 404))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var load = await source.LoadAsync("missing");

        Assert.False(load.HasEditor);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
        Assert.Contains("404", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_NewQuery_CreatesSavedQueryItemAndReturnsServerAssignedId()
    {
        var client = new FakeQueryContentClient
        {
            CreateItemResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromData(
                BuildVersionResponse("server-assigned-1", 1))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var result = await source.SaveAsync(ReadyQuery(queryId: null));

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.CreateItemCalls);
        Assert.Equal(0, client.CreateVersionCalls);
        Assert.Equal("server-assigned-1", result.Query!.QueryId);
        // The item was created as a savedQuery kind carrying the real saved-query content with a filter plan.
        Assert.Equal(HonuaAnalysisContentKinds.SavedQuery, client.LastCreatedItem!.Kind);
        Assert.NotNull(client.LastCreatedItem.SavedQuery);
        Assert.NotNull(client.LastCreatedItem.SavedQuery!.FilterPlan);
        Assert.Contains(
            client.LastCreatedItem.SavedQuery.FilterPlan!.Clauses,
            c => c.Type == HonuaFilterClauseTypes.Comparison && c.Comparison!.Property == "status");
    }

    [Fact]
    public async Task Save_ExistingQuery_CreatesNewVersionCarryingETag()
    {
        var client = new FakeQueryContentClient
        {
            CreateVersionResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromData(
                BuildVersionResponse("query-7", 4))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var result = await source.SaveAsync(ReadyQuery(queryId: "query-7"));

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.CreateVersionCalls);
        Assert.Equal(0, client.CreateItemCalls);
        Assert.Equal(4, result.Query!.Version);
        Assert.Equal("hash-3", client.LastCreatedVersionBasedOn);
    }

    [Fact]
    public async Task Save_ServerRejectsQuery_SurfacesRejectedIssue()
    {
        var client = new FakeQueryContentClient
        {
            CreateItemResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Rejected", "POST items", "Filter plan invalid.", 400))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var result = await source.SaveAsync(ReadyQuery(queryId: null));

        Assert.False(result.Succeeded);
        Assert.Equal("Rejected", result.Issue!.State);
    }

    [Fact]
    public async Task Preview_BeforeSave_IsRefusedWithoutServerCall()
    {
        var client = new FakeQueryContentClient();
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var result = await source.PreviewAsync(ReadyQuery(queryId: null));

        Assert.False(result.Succeeded);
        Assert.Contains("Save", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.PreviewCalls);
    }

    [Fact]
    public async Task Preview_SavedQuery_ResolvesLiveFeaturesAndDownstreamTargets()
    {
        var client = new FakeQueryContentClient
        {
            PreviewResult = HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>.FromData(
                new HonuaSavedQueryPreviewResult
                {
                    PreviewArtifactId = "preview-1",
                    ItemId = "query-7",
                    Version = 3,
                    LayerId = 5,
                    TotalCount = 42,
                    ExceededPreviewLimit = true,
                    Features =
                    [
                        new HonuaSavedQueryPreviewFeature
                        {
                            Id = 1,
                            HasGeometry = true,
                            Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                            {
                                ["status"] = JsonSerializer.SerializeToElement("approved"),
                                ["year"] = JsonSerializer.SerializeToElement(2024)
                            }
                        }
                    ]
                })
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);
        var query = ReadyQuery(queryId: "query-7");

        var result = await source.PreviewAsync(query);

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.PreviewCalls);
        Assert.Equal(query.PreviewLimit, client.LastPreviewLimit);
        Assert.NotNull(query.Preview);
        Assert.Equal(1, query.Preview!.FeatureCount);
        Assert.Equal(42, query.Preview.TotalCount);
        Assert.True(query.Preview.ExceededPreviewLimit);
        // The preview feature attribute values are projected to display strings.
        var feature = Assert.Single(query.Preview.Features);
        Assert.Equal("approved", feature.Attributes["status"]);
        // A geometry-bearing query can feed a map plus dashboard/report/app/workflow (AC#3).
        Assert.Contains("map", query.Preview.DownstreamTargets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("workflow", query.Preview.DownstreamTargets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_TransportFailure_SurfacesUnavailableIssue()
    {
        var client = new FakeQueryContentClient
        {
            PreviewResult = HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST preview", "Server unreachable."))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var result = await source.PreviewAsync(ReadyQuery(queryId: "query-7"));

        Assert.False(result.Succeeded);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    private static StudioQueryEditor ReadyQuery(string? queryId)
    {
        var query = new StudioQueryEditor
        {
            QueryId = queryId,
            Version = queryId is null ? 0 : 3,
            ETag = queryId is null ? null : "hash-3",
            Title = "Flood-zone permits",
            NaturalLanguageQuery = "Approved permits in flood zones",
            ServiceName = "permits",
            LayerId = 5,
            Combinator = StudioQueryCombinators.And,
            PreviewLimit = 25
        };
        query.Predicates.Add(new StudioQueryPredicateEditor
        {
            Kind = StudioQueryPredicateKinds.Comparison,
            Field = "status",
            Operator = "=",
            Value = "approved"
        });
        query.OutFields.Add("permit_id");
        query.Parameters.Add(new StudioQueryParameterEditor { Name = "minYear", Value = "2024" });
        return query;
    }

    private static HonuaAnalysisContentVersionResponse BuildVersionResponse(string itemId, int version) =>
        new()
        {
            Item = new HonuaAnalysisContentItem
            {
                ItemId = itemId,
                Kind = HonuaAnalysisContentKinds.SavedQuery,
                Name = "flood-permits",
                Title = "Flood-zone permits",
                CurrentVersion = version,
                CurrentVersionId = $"{itemId}-v{version}"
            },
            Version = new HonuaAnalysisContentVersion
            {
                VersionId = $"{itemId}-v{version}",
                ItemId = itemId,
                Version = version,
                Kind = HonuaAnalysisContentKinds.SavedQuery,
                ContentHash = $"hash-{version}",
                SavedQuery = new HonuaSavedQueryContent
                {
                    NaturalLanguageQuery = "Approved permits in flood zones",
                    LayerId = 5,
                    ServiceName = "permits",
                    OutFields = ["permit_id", "status"],
                    PreviewLimit = 50,
                    OutputFormat = "geojson",
                    FilterPlan = new HonuaFilterPlan
                    {
                        Combinator = HonuaFilterPlanCombinators.And,
                        Clauses =
                        [
                            new HonuaFilterPlanClause
                            {
                                Type = HonuaFilterClauseTypes.Comparison,
                                Comparison = new HonuaComparisonClause
                                {
                                    Property = "status",
                                    Operator = "=",
                                    Value = JsonSerializer.SerializeToElement("approved")
                                }
                            }
                        ]
                    },
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["console.title"] = "Flood-zone permits",
                        ["console.parameters"] = "minYear=2024"
                    }
                }
            }
        };

    private sealed class FakeQueryContentClient : IHonuaAnalysisContentClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public int GetVersionCalls { get; private set; }

        public int CreateItemCalls { get; private set; }

        public int CreateVersionCalls { get; private set; }

        public int PreviewCalls { get; private set; }

        public int? LastPreviewLimit { get; private set; }

        public HonuaCreateAnalysisContentItemRequest? LastCreatedItem { get; private set; }

        public string? LastCreatedVersionBasedOn { get; private set; }

        public HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> GetVersionResult { get; init; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "GET version", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> CreateItemResult { get; init; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST items", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> CreateVersionResult { get; init; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST versions", "not configured"));

        public HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult> PreviewResult { get; init; } =
            HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST preview", "not configured"));

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateItemAsync(
            HonuaCreateAnalysisContentItemRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateItemCalls++;
            LastCreatedItem = request;
            return Task.FromResult(CreateItemResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentItemListResponse>> ListItemsAsync(
            HonuaAnalysisContentListQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaAnalysisContentItemListResponse>.FromData(
                new HonuaAnalysisContentItemListResponse()));

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentEstimateResponse>> EstimateAsync(
            string itemId,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaAnalysisContentEstimateResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST estimate", "not configured")));

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetItemAsync(
            string itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetVersionResult);

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> GetVersionAsync(
            string itemId,
            int? version,
            CancellationToken cancellationToken = default)
        {
            GetVersionCalls++;
            return Task.FromResult(GetVersionResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateVersionAsync(
            string itemId,
            HonuaCreateAnalysisContentVersionRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateVersionCalls++;
            LastCreatedVersionBasedOn = request.BasedOnVersionId;
            return Task.FromResult(CreateVersionResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>> PreviewSavedQueryAsync(
            string itemId,
            int version,
            int? limit,
            CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            LastPreviewLimit = limit;
            return Task.FromResult(PreviewResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>> RunAsync(
            string itemId,
            int version,
            HonuaRunAnalysisContentVersionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The query builder does not run analysis jobs.");

        public Task<HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>> GetArtifactAsync(
            string artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The query builder does not resolve analysis artifacts.");

        public Task<HonuaAdminEndpointResult<HonuaAnalysisJobFailure>> GetJobFailureAsync(
            string jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The query builder does not resolve job failures.");

        public Task<HonuaAdminEndpointResult<HonuaAnalysisGenerationResult>> GenerateAsync(
            HonuaGenerateAnalysisRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The query builder does not generate analysis packages.");
    }
}

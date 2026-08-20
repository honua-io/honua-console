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
        // honua-console#311: the transport code is relocated out of the human detail into StatusCode for the
        // diagnostics disclosure; the detail stays verbatim from the server issue.
        Assert.Equal(404, state.StatusCode);
        Assert.Equal("Not found.", state.Detail);
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

    [Fact]
    public async Task Generate_ServerProposesQuery_AppliesProposalOntoEditorAndClearsPreview()
    {
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromData(
                new HonuaSavedQueryGenerationResult
                {
                    Status = "generated",
                    Rationale = "Proposed a flood-zone permit query.",
                    Query = new HonuaSavedQueryContent
                    {
                        NaturalLanguageQuery = "Approved permits in flood zones",
                        ServiceName = "permits",
                        LayerId = 9,
                        OutFields = ["permit_id", "status"],
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
                        }
                    }
                })
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);
        var current = ReadyQuery(queryId: "query-7");
        current.Preview = null;

        var outcome = await source.GenerateAsync(current, new StudioQueryGenerationRequest { Prompt = "permits in flood zones" });

        Assert.Equal(1, client.GenerateQueryCalls);
        Assert.True(outcome.IsGenerated);
        Assert.NotNull(outcome.Query);
        // Server-owned identity is preserved; the authored content is replaced from the proposal.
        Assert.Equal("query-7", outcome.Query!.QueryId);
        Assert.Equal("permits", outcome.Query.ServiceName);
        Assert.Equal(9, outcome.Query.LayerId);
        Assert.Contains(outcome.Query.Predicates, p => p.Field == "status" && p.Value == "approved");
        Assert.Contains(outcome.Query.OutFields, f => f == "permit_id");
        Assert.Equal("Proposed a flood-zone permit query.", outcome.Rationale);
        // A refine on a saved draft ships the current query so the server edits it.
        Assert.NotNull(client.LastGenerateQueryRequest!.Query);
    }

    [Fact]
    public async Task Generate_ServerOmitsEmptyCollections_MapsWithoutThrowing()
    {
        // The server serializes empty collections/maps as explicit JSON null (System.Text.Json overrides the
        // DTO's non-null initializer on deserialize), so a generated query commonly arrives with
        // Metadata/OutFields/Clauses == null. The mapper must coalesce before any lookup/enumeration rather
        // than NRE and freeze the generate turn (regression: StudioQueryPackageMapper.ResolveTitle).
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromData(
                new HonuaSavedQueryGenerationResult
                {
                    Status = "generated",
                    Rationale = "Proposed a query.",
                    Query = new HonuaSavedQueryContent
                    {
                        NaturalLanguageQuery = "every parcel",
                        ServiceName = "parcels",
                        LayerId = 3,
                        OutFields = null!,
                        Metadata = null!,
                        FilterPlan = new HonuaFilterPlan { Combinator = HonuaFilterPlanCombinators.And, Clauses = null! }
                    }
                })
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var outcome = await source.GenerateAsync(new StudioQueryEditor(), new StudioQueryGenerationRequest { Prompt = "all parcels" });

        Assert.True(outcome.IsGenerated);
        Assert.NotNull(outcome.Query);
        Assert.Equal("parcels", outcome.Query!.ServiceName);
        Assert.Equal(3, outcome.Query.LayerId);
        Assert.Empty(outcome.Query.Predicates);
        Assert.Empty(outcome.Query.OutFields);
    }

    [Fact]
    public async Task Generate_ServerLacksContract_SurfacesUnsupportedNotMissingBinding()
    {
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST queries/generate", "Not found.", 404))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var outcome = await source.GenerateAsync(new StudioQueryEditor(), new StudioQueryGenerationRequest { Prompt = "hi" });

        Assert.Equal(StudioQueryGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.BindingState);
        Assert.False(outcome.IsGenerated);
    }

    [Fact]
    public async Task Generate_ServerLacksContractButCatalogHasALayer_SeedsBaselineBoundToTheRealLayer()
    {
        // A server that never shipped the generation route answers 404; one that shipped it and turned it off
        // answers 200 with status="unsupported". Both mean "no AI generation here", so both must land on the
        // same honest, catalog-bound baseline. Seeding on only the second left the 404 case showing the blank
        // scaffold's unbound layer 0 — a draft that resolves against nothing.
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST queries/generate", "Not found.", 404))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client, CatalogWith(("parcels", 7, "Parcels")));

        var outcome = await source.GenerateAsync(
            new StudioQueryEditor(),
            new StudioQueryGenerationRequest { Prompt = "everything in parcels" });

        Assert.Equal(StudioQueryGenerationStatuses.Generated, outcome.Status);
        Assert.NotNull(outcome.Query);
        Assert.Equal("parcels", outcome.Query!.ServiceName);
        Assert.Equal(7, outcome.Query.LayerId);
        // Honest: the turn says plainly that this is a baseline, not an AI-authored query.
        Assert.Contains(outcome.Warnings, w => w.Contains("Baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generate_ServerLacksContractDuringRefinement_PreservesExistingQuery()
    {
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST queries/generate", "Not found.", 404))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client, CatalogWith(("parcels", 7, "Parcels")));
        var current = new StudioQueryEditor
        {
            Title = "Authored query",
            NaturalLanguageQuery = "existing intent",
            ServiceName = "authored",
            LayerId = 9,
        };

        var outcome = await source.GenerateAsync(
            current,
            new StudioQueryGenerationRequest { Prompt = "refine the query" });

        Assert.Equal(StudioQueryGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.Query);
        Assert.Equal("Authored query", current.Title);
        Assert.Equal("existing intent", current.NaturalLanguageQuery);
        Assert.Equal("authored", current.ServiceName);
        Assert.Equal(9, current.LayerId);
    }

    [Fact]
    public async Task Generate_ServerLacksContractAndCatalogIsEmpty_StaysUnsupportedRatherThanInventingALayer()
    {
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST queries/generate", "Not found.", 404))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client, CatalogWith());

        var outcome = await source.GenerateAsync(new StudioQueryEditor(), new StudioQueryGenerationRequest { Prompt = "hi" });

        Assert.Equal(StudioQueryGenerationStatuses.Unsupported, outcome.Status);
        Assert.Null(outcome.Query);
    }

    [Fact]
    public async Task Generate_ServerError_BlocksSurfaceWithBindingState()
    {
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST queries/generate", "Server unreachable."))
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var outcome = await source.GenerateAsync(new StudioQueryEditor(), new StudioQueryGenerationRequest { Prompt = "hi" });

        Assert.NotNull(outcome.BindingState);
        Assert.Equal("Unavailable", outcome.BindingState!.State);
    }

    [Fact]
    public async Task Generate_FirstTurnFromBlankScaffold_RequestsFreshGenerationWithNullQuery()
    {
        var client = new FakeQueryContentClient
        {
            GenerateQueryResult = HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromData(
                new HonuaSavedQueryGenerationResult { Status = "needs-clarification" })
        };
        var source = new HonuaServerStudioQueryContentDataSource(client);

        var outcome = await source.GenerateAsync(new StudioQueryEditor(), new StudioQueryGenerationRequest { Prompt = "permits" });

        Assert.True(outcome.NeedsClarification);
        // A blank scaffold has no authored intent, so the first turn requests fresh generation (null query).
        Assert.Null(client.LastGenerateQueryRequest!.Query);
    }

    /// <summary>A live catalog exposing exactly the given (service, layerId, layerName) triples.</summary>
    private static IOperateTransitionDataSource CatalogWith(params (string Service, int LayerId, string LayerName)[] layers) =>
        new StubCatalogDataSource(layers
            .GroupBy(l => l.Service, StringComparer.Ordinal)
            .Select(group => new OperateServiceDetail(
                group.Key,
                group.Key,
                "FeatureServer",
                "Running",
                "Server",
                group
                    .Select(l => new OperateServiceLayerProjection(l.LayerId, l.LayerName, "Point", $"res-{l.LayerId}", l.LayerName))
                    .ToArray(),
                [],
                []))
            .ToArray());

    private sealed class StubCatalogDataSource(IReadOnlyList<OperateServiceDetail> services) : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The query data source reads the layers view, not the whole workspace.");

        public Task<OperateServicesView> GetLayersViewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateServicesView(services, []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(services.FirstOrDefault(s => s.Name == serviceName));
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

        public int GenerateQueryCalls { get; private set; }

        public HonuaGenerateSavedQueryRequest? LastGenerateQueryRequest { get; private set; }

        public HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult> GenerateQueryResult { get; set; } =
            HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST queries/generate", "not configured", 404));

        public Task<HonuaAdminEndpointResult<HonuaSavedQueryGenerationResult>> GenerateQueryAsync(
            HonuaGenerateSavedQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            GenerateQueryCalls++;
            LastGenerateQueryRequest = request;
            return Task.FromResult(GenerateQueryResult);
        }
    }
}

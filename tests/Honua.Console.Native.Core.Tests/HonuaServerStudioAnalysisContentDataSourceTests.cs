using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the server-bound analysis data source (honua-console#53). Drives
/// <see cref="HonuaServerStudioAnalysisContentDataSource"/> over a recording fake of the real
/// <see cref="IHonuaAnalysisContentClient"/> shim to assert the live binding maps the plan card to the
/// honua-server analysis content contract (honua-server#1182), surfaces server failures and the documented
/// server gaps (list, estimate, analysis-package preview) as explicit capability states rather than
/// fabricating data, and resolves the submitted job + result artifact (AC#1/AC#3).
/// </summary>
public sealed class HonuaServerStudioAnalysisContentDataSourceTests
{
    [Fact]
    public async Task GetWorkspace_HasNoListRoute_SurfacesUnsupportedListStateNotMockData()
    {
        var source = new HonuaServerStudioAnalysisContentDataSource(new FakeAnalysisContentClient());

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Plans);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
        Assert.Contains("list", state.Contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Load_NewDraft_ReturnsBlankTemplateWithoutServerCall()
    {
        var client = new FakeAnalysisContentClient();
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var load = await source.LoadAsync(null);

        Assert.True(load.HasEditor);
        Assert.False(load.Plan!.IsExistingPackage);
        Assert.Equal(0, client.GetVersionCalls);
    }

    [Fact]
    public async Task Load_ExistingPackage_LiftsServerVersionAndServerPipeline()
    {
        var client = new FakeAnalysisContentClient
        {
            GetVersionResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromData(
                BuildVersionResponse("analysis-7", 3))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var load = await source.LoadAsync("analysis-7");

        Assert.True(load.HasEditor);
        Assert.Equal("analysis-7", load.Plan!.AnalysisId);
        Assert.Equal(3, load.Plan.Version);
        Assert.Equal("buffer", load.Plan.Method);
        Assert.Contains(load.Plan.Inputs, i => i.ServiceId == "hydrants" && i.LayerId == 2);
        Assert.Contains(load.Plan.OutputSchema, f => f.Name == "buffer_distance");
        // DAG view binds to the real server-compiled plan steps, not a Console reconstruction.
        Assert.NotEmpty(load.Plan.ServerPipeline);
        Assert.Contains(load.Plan.ServerPipeline, n => n.Method == "buffer");
    }

    [Fact]
    public async Task Load_ServerReturnsNotFound_SurfacesIssueWithoutEditor()
    {
        var client = new FakeAnalysisContentClient
        {
            GetVersionResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET version", "Not found.", 404))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var load = await source.LoadAsync("missing");

        Assert.False(load.HasEditor);
        var state = Assert.Single(load.CapabilityStates);
        Assert.Equal("Unsupported", state.State);
        Assert.Contains("404", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveDraft_NewPackage_CreatesItemAndReturnsServerAssignedId()
    {
        var client = new FakeAnalysisContentClient
        {
            CreateItemResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromData(
                BuildVersionResponse("server-assigned-1", 1))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var result = await source.SaveDraftAsync(ReadyPlan(analysisId: null));

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.CreateItemCalls);
        Assert.Equal(0, client.CreateVersionCalls);
        Assert.NotNull(result.Plan);
        Assert.Equal("server-assigned-1", result.Plan!.AnalysisId);
        // Saving a new immutable version clears any prior estimate so submit re-reviews runtime/cost.
        Assert.Null(result.Plan.Estimate);
        // The authored plan lowered into a real server package document with a method step + load step.
        Assert.NotNull(client.LastCreatedItem?.AnalysisPackage);
        Assert.Contains(
            client.LastCreatedItem!.AnalysisPackage!.Plan.Steps,
            s => s.Kind == HonuaAnalysisPlanStepKinds.Geoprocess && s.ProcessId == "buffer");
    }

    [Fact]
    public async Task SaveDraft_ExistingPackage_CreatesNewVersion()
    {
        var client = new FakeAnalysisContentClient
        {
            CreateVersionResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromData(
                BuildVersionResponse("analysis-7", 4))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var result = await source.SaveDraftAsync(ReadyPlan(analysisId: "analysis-7"));

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.CreateVersionCalls);
        Assert.Equal(0, client.CreateItemCalls);
        Assert.Equal(4, result.Plan!.Version);
    }

    [Fact]
    public async Task SaveDraft_ServerRejectsPackage_SurfacesRejectedIssue()
    {
        var client = new FakeAnalysisContentClient
        {
            CreateItemResult = HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Rejected", "POST items", "Plan invalid.", 400))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var result = await source.SaveDraftAsync(ReadyPlan(analysisId: null));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Issue);
        Assert.Equal("Rejected", result.Issue!.State);
    }

    [Fact]
    public async Task Estimate_NoServerRoute_ProducesLocalEstimateAndDocumentsGap()
    {
        var client = new FakeAnalysisContentClient();
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");
        plan.Estimate = null;

        var result = await source.EstimateAsync(plan);

        Assert.True(result.Succeeded);
        Assert.NotNull(plan.Estimate);
        Assert.True(plan.Estimate!.EstimatedRuntimeSeconds > 0);
        // The local estimate is accompanied by an explicit capability state naming the missing server route.
        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Contains("estimate", result.Issue.Contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Estimate_BeforeSave_IsRefused()
    {
        var source = new HonuaServerStudioAnalysisContentDataSource(new FakeAnalysisContentClient());

        var result = await source.EstimateAsync(ReadyPlan(analysisId: null));

        Assert.False(result.Succeeded);
        Assert.Contains("Save", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_AnalysisPackage_IsUnsupportedNotARealJob()
    {
        var client = new FakeAnalysisContentClient();
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var result = await source.PreviewAsync(ReadyPlan(analysisId: "analysis-7"));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported", result.Issue!.State);
        // Preview must NOT silently run a job under the hood.
        Assert.Equal(0, client.RunCalls);
    }

    [Fact]
    public async Task Submit_GateUnmet_DoesNotCallServer()
    {
        var client = new FakeAnalysisContentClient();
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");
        plan.Estimate = null; // Missing estimate fails the pre-submit gate.

        var result = await source.SubmitAsync(plan);

        Assert.False(result.Succeeded);
        Assert.Equal(0, client.RunCalls);
        Assert.Contains("estimate", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submit_Ready_RunsJobAndRecordsSubmittedJob()
    {
        var client = new FakeAnalysisContentClient
        {
            RunResult = HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>.FromData(
                new HonuaAnalysisContentJobResponse
                {
                    JobId = "job-99",
                    Status = "Queued",
                    Version = new HonuaAnalysisContentVersion { ItemId = "analysis-7", Version = 3 }
                })
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");

        var result = await source.SubmitAsync(plan);

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.RunCalls);
        Assert.NotNull(plan.SubmittedJob);
        Assert.Equal("job-99", plan.SubmittedJob!.JobId);
        Assert.False(plan.SubmittedJob.HasFailure);
    }

    [Fact]
    public async Task Submit_ServerJobFails_ResolvesSafeFailureClassification()
    {
        var client = new FakeAnalysisContentClient
        {
            RunResult = HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>.FromData(
                new HonuaAnalysisContentJobResponse
                {
                    JobId = "job-failed",
                    Status = "Failed",
                    Version = new HonuaAnalysisContentVersion { ItemId = "analysis-7", Version = 3 }
                }),
            JobFailureResult = HonuaAdminEndpointResult<HonuaAnalysisJobFailure>.FromData(
                new HonuaAnalysisJobFailure
                {
                    JobId = "job-failed",
                    Classification = "validationFailed",
                    Message = "The plan failed validation.",
                    IsTerminal = true
                })
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");

        var result = await source.SubmitAsync(plan);

        Assert.True(result.Succeeded); // The run command itself succeeded; the job is terminal-failed.
        Assert.NotNull(plan.SubmittedJob);
        Assert.True(plan.SubmittedJob!.HasFailure);
        Assert.Equal("validationFailed", plan.SubmittedJob.Failure!.Classification);
    }

    [Fact]
    public async Task Submit_TransportFailure_SurfacesUnavailableIssue()
    {
        var client = new FakeAnalysisContentClient
        {
            RunResult = HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST runs", "Server unreachable."))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);

        var result = await source.SubmitAsync(ReadyPlan(analysisId: "analysis-7"));

        Assert.False(result.Succeeded);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    [Fact]
    public async Task ResolveArtifact_Success_ProjectsDownstreamContentFamilies()
    {
        var client = new FakeAnalysisContentClient
        {
            ArtifactResult = HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>.FromData(
                new HonuaAnalysisArtifactResponse
                {
                    Artifact = new HonuaResultArtifactRecord
                    {
                        ArtifactId = "artifact-1",
                        JobId = "job-99",
                        Kind = HonuaArtifactKinds.FeatureLayer,
                        Label = "Buffered hydrants",
                        RetentionState = "retained"
                    },
                    Binding = new HonuaArtifactBindingRef { TargetKind = "layer" }
                })
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");

        var result = await source.ResolveArtifactAsync("artifact-1", plan);

        Assert.True(result.Succeeded);
        Assert.NotNull(plan.SubmittedJob?.Artifact);
        var targets = plan.SubmittedJob!.Artifact!.DownstreamTargets;
        // A feature-layer artifact can become a layer, content item, dashboard input, and workflow input (AC#3).
        Assert.Contains("layer", targets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("content", targets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("workflow", targets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveArtifact_ServerNotFound_SurfacesIssueWithoutMutatingJob()
    {
        var client = new FakeAnalysisContentClient
        {
            ArtifactResult = HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET artifact", "Artifact not found.", 404))
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");
        plan.SubmittedJob = new StudioAnalysisJobView("job-99", "Running", 3);

        var result = await source.ResolveArtifactAsync("missing-artifact", plan);

        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.Issue!.State);
        // A failed resolve must not fabricate an artifact onto the submitted job.
        Assert.Null(plan.SubmittedJob!.Artifact);
    }

    [Fact]
    public async Task ResolveArtifact_Scalar_OffersScalarDownstreamFamiliesNotLayer()
    {
        // A scalar result cannot become a map layer; the panel must never claim an impossible binding (AC#3).
        var client = new FakeAnalysisContentClient
        {
            ArtifactResult = HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>.FromData(
                new HonuaAnalysisArtifactResponse
                {
                    Artifact = new HonuaResultArtifactRecord
                    {
                        ArtifactId = "artifact-scalar",
                        JobId = "job-99",
                        Kind = HonuaArtifactKinds.Scalar,
                        Label = "Total area",
                        RetentionState = "retained"
                    },
                    Binding = new HonuaArtifactBindingRef { TargetKind = "report" }
                })
        };
        var source = new HonuaServerStudioAnalysisContentDataSource(client);
        var plan = ReadyPlan(analysisId: "analysis-7");

        var result = await source.ResolveArtifactAsync("artifact-scalar", plan);

        Assert.True(result.Succeeded);
        var targets = plan.SubmittedJob!.Artifact!.DownstreamTargets;
        Assert.Contains("report", targets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("dashboard", targets, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("layer", targets, StringComparer.OrdinalIgnoreCase);
    }

    private static StudioAnalysisPlanEditor ReadyPlan(string? analysisId)
    {
        var plan = new StudioAnalysisPlanEditor
        {
            AnalysisId = analysisId,
            Version = analysisId is null ? 0 : 3,
            Title = "Hydrant buffer",
            Goal = "Buffer hydrants by 250m",
            Method = "buffer",
            ComputeProfile = "standard",
            OutputContentType = "layer",
            ETag = analysisId is null ? null : "hash-3",
            Estimate = new StudioAnalysisComputeEstimate(12.5, 4_200, "standard", "~0.01 credit")
        };
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "primary", ServiceId = "hydrants", LayerId = 2 });
        plan.Parameters.Add(new StudioAnalysisParameterEditor { Name = "distanceMeters", Value = "250" });
        plan.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });
        return plan;
    }

    private static HonuaAnalysisContentVersionResponse BuildVersionResponse(string itemId, int version) =>
        new()
        {
            Item = new HonuaAnalysisContentItem
            {
                ItemId = itemId,
                Kind = HonuaAnalysisContentKinds.AnalysisPackage,
                Name = "hydrant-buffer",
                Title = "Hydrant buffer",
                CurrentVersion = version,
                CurrentVersionId = $"{itemId}-v{version}"
            },
            Version = new HonuaAnalysisContentVersion
            {
                VersionId = $"{itemId}-v{version}",
                ItemId = itemId,
                Version = version,
                Kind = HonuaAnalysisContentKinds.AnalysisPackage,
                ContentHash = $"hash-{version}",
                AnalysisPackage = new HonuaAnalysisPackageContent
                {
                    Plan = new HonuaAnalysisPlan
                    {
                        PlanId = "plan-1",
                        IntentId = "intent-1",
                        Steps =
                        [
                            new HonuaAnalysisPlanStep
                            {
                                StepId = "load-0",
                                Kind = HonuaAnalysisPlanStepKinds.QueryFeatures,
                                Inputs = new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["serviceId"] = "hydrants",
                                    ["layerId"] = "2",
                                    ["role"] = "primary"
                                }
                            },
                            new HonuaAnalysisPlanStep
                            {
                                StepId = "method",
                                Kind = HonuaAnalysisPlanStepKinds.Geoprocess,
                                ProcessId = "buffer",
                                DependsOn = ["load-0"]
                            }
                        ]
                    },
                    BindingHints = [new HonuaArtifactBindingRef { TargetKind = "layer" }],
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["console.method"] = "buffer",
                        ["console.computeProfile"] = "standard",
                        ["console.outputContentType"] = "layer",
                        ["console.title"] = "Hydrant buffer",
                        ["console.outputSchema"] = "buffer_distance:double"
                    }
                }
            }
        };

    private sealed class FakeAnalysisContentClient : IHonuaAnalysisContentClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public int GetVersionCalls { get; private set; }

        public int CreateItemCalls { get; private set; }

        public int CreateVersionCalls { get; private set; }

        public int RunCalls { get; private set; }

        public HonuaCreateAnalysisContentItemRequest? LastCreatedItem { get; private set; }

        public HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> GetVersionResult { get; set; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "GET version", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> CreateItemResult { get; set; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST items", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> CreateVersionResult { get; set; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST versions", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse> RunResult { get; set; } =
            HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "POST runs", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisJobFailure> JobFailureResult { get; set; } =
            HonuaAdminEndpointResult<HonuaAnalysisJobFailure>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "GET failure", "not configured"));

        public HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse> ArtifactResult { get; set; } =
            HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>.FromIssue(
                new HonuaAdminEndpointIssue("Unavailable", "GET artifact", "not configured"));

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse>> CreateItemAsync(
            HonuaCreateAnalysisContentItemRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateItemCalls++;
            LastCreatedItem = request;
            return Task.FromResult(CreateItemResult);
        }

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
            return Task.FromResult(CreateVersionResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>> PreviewSavedQueryAsync(
            string itemId,
            int version,
            int? limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaSavedQueryPreviewResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST preview", "saved-query only")));

        public Task<HonuaAdminEndpointResult<HonuaAnalysisContentJobResponse>> RunAsync(
            string itemId,
            int version,
            HonuaRunAnalysisContentVersionRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCalls++;
            return Task.FromResult(RunResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaAnalysisArtifactResponse>> GetArtifactAsync(
            string artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ArtifactResult);

        public Task<HonuaAdminEndpointResult<HonuaAnalysisJobFailure>> GetJobFailureAsync(
            string jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(JobFailureResult);
    }
}

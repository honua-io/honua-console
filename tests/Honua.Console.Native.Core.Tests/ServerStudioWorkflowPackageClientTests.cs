using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Unit coverage for <see cref="ServerStudioWorkflowPackageClient"/> over an in-memory fake of the
/// honua-server #1185 workflow API. Proves the editor binds to and maps live server data (node registry,
/// package draft, save/version, dry-run, publish + run), round-trips editor-only authoring fields, and
/// surfaces missing-binding states instead of fabricating success - without Docker.
/// </summary>
public sealed class ServerStudioWorkflowPackageClientTests
{
    [Fact]
    public async Task OpenEditor_MapsNodeRegistryCategories_FromPorts()
    {
        var client = new ServerStudioWorkflowPackageClient(new FakeWorkflowApi());

        var context = await client.OpenEditorAsync("new");

        Assert.Null(context.BindingState);
        Assert.NotNull(context.Draft);
        Assert.Empty(context.Draft!.ContentItemId); // new package is local until first save
        Assert.Contains(context.NodeDefinitions, d => d.Category == StudioWorkflowContractValues.NodeCategorySource);
        Assert.Contains(context.NodeDefinitions, d => d.Category == StudioWorkflowContractValues.NodeCategoryTransform);
        Assert.Contains(context.NodeDefinitions, d => d.Category == StudioWorkflowContractValues.NodeCategorySink);
    }

    [Fact]
    public async Task OpenEditor_ReturnsMissingBinding_WhenRegistryUnreachable()
    {
        var client = new ServerStudioWorkflowPackageClient(new FakeWorkflowApi { RegistryReachable = false });

        var context = await client.OpenEditorAsync("new");

        Assert.NotNull(context.BindingState);
        Assert.Equal("Studio workflow.package", context.BindingState!.Surface);
        Assert.Equal("Unavailable", context.BindingState.State);
        Assert.Null(context.Draft);
        Assert.Empty(context.NodeDefinitions);
    }

    [Fact]
    public async Task SaveVersion_PersistsPackageAndCreatesServerVersion()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);

        var save = await client.SaveVersionAsync(draft, "first save");

        Assert.Null(save.BindingState);
        Assert.False(string.IsNullOrEmpty(save.ContentItemId));
        Assert.Equal(1, save.VersionNumber);
        Assert.Equal(save.ContentItemId, draft.ContentItemId);
        Assert.EndsWith(":v1", draft.CurrentVersionId, StringComparison.Ordinal);
        Assert.Single(api.Packages);
        Assert.Single(api.Versions);
    }

    [Fact]
    public async Task SaveVersion_RoundTripsEditorOnlyAuthoringFields_ThroughServerGraph()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.Parameters.Add(new StudioWorkflowParameter { Name = "county", Type = "string", Required = true });
        draft.RetryPolicy.MaxAttempts = 7;
        draft.PublicationIntent.Mode = StudioWorkflowContractValues.PublicationModeScheduledJob;
        draft.PublicationIntent.RouteSlug = "nightly-normalizer";

        await client.SaveVersionAsync(draft, "save with authoring fields");

        // Reload from the server: the editor-only fields the #1185 graph does not model first-class must
        // survive via the opaque editor metadata, not be silently dropped.
        var reloaded = await client.GetDraftAsync(draft.ContentItemId);

        Assert.NotNull(reloaded);
        Assert.Contains(reloaded!.Parameters, p => p.Name == "county" && p.Required);
        Assert.Equal(7, reloaded.RetryPolicy.MaxAttempts);
        Assert.Equal(StudioWorkflowContractValues.PublicationModeScheduledJob, reloaded.PublicationIntent.Mode);
        Assert.Equal("nightly-normalizer", reloaded.PublicationIntent.RouteSlug);
    }

    [Fact]
    public async Task GetDraft_MapsServerNodesEdgesAndConfiguration()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.Nodes[0].Configuration["binding"] = "content-item:parcels";
        await client.SaveVersionAsync(draft, "save graph");

        var reloaded = await client.GetDraftAsync(draft.ContentItemId);

        Assert.NotNull(reloaded);
        var source = Assert.Single(reloaded!.Nodes, n => n.Category == StudioWorkflowContractValues.NodeCategorySource);
        Assert.Equal("content-item:parcels", source.Configuration["binding"]);
        Assert.Equal(StudioWorkflowContractValues.PackageType, reloaded.PackageType);
    }

    [Fact]
    public async Task OpenEditor_DoesNotPinReopenedDraftToLatestVersion()
    {
        // Regression for honua-console#62: the server persists the mutable package graph independently of
        // immutable versions, so a reopened package's graph can be AHEAD of LatestVersion (e.g. a prior save
        // whose POST .../versions failed graph validation still PUT the graph but created no version). The
        // reopened draft must surface the latest committed version for display, but must NOT carry it as a
        // reusable CurrentVersionId - otherwise a dry-run/publish reuses a stale version that differs from
        // the loaded graph.
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        await client.SaveVersionAsync(draft, "v1"); // LatestVersion => 1

        var reopened = await client.OpenEditorAsync(draft.ContentItemId);

        Assert.NotNull(reopened.Draft);
        Assert.Equal(1, reopened.Draft!.VersionNumber); // latest committed version still shown
        Assert.Empty(reopened.Draft.CurrentVersionId);  // ...but not pinned for blind reuse
    }

    [Fact]
    public async Task DryRun_OnReopenedDraft_CreatesFreshVersion_InsteadOfReusingStaleLatest()
    {
        // The loaded graph is not proven to equal the latest committed version, so a run re-saves and
        // re-versions the loaded graph rather than reusing a potentially stale version. Regression for
        // honua-console#62 (reopened validation-failed drafts reusing stale versions).
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        await client.SaveVersionAsync(draft, "v1");

        var reopened = (await client.OpenEditorAsync(draft.ContentItemId)).Draft!;
        var dryRun = await client.DryRunAsync(reopened);

        Assert.Null(dryRun.BindingState);
        Assert.Equal(2, api.Versions.Count); // v1 + the freshly created v2, not a reuse of v1
        Assert.EndsWith(":v2", reopened.CurrentVersionId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveVersion_OnServerValidationFailure_ClearsStaleVersionPin()
    {
        // Regression for honua-console#62 "Validation-failed saves leave stale version pins reusable": a save
        // PUTs the mutable graph (PersistPackageAsync) before POST .../versions, so a version-create failure
        // leaves the server graph holding the rejected edits while the prior version no longer matches. The
        // save must invalidate the version pin so EnsureVersionAsync cannot reuse the stale version.
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        await client.SaveVersionAsync(draft, "v1");
        Assert.EndsWith(":v1", draft.CurrentVersionId, StringComparison.Ordinal); // pinned to the good version

        api.VersionStatusCode = 400; // the next version-create fails server graph validation
        var failed = await client.SaveVersionAsync(draft, "invalid edit");

        Assert.Null(failed.BindingState); // a 400 is a bound-surface validation outcome, not a missing binding
        Assert.True(string.IsNullOrEmpty(failed.VersionId)); // no immutable version was created
        Assert.NotEmpty(failed.ValidationIssues);
        Assert.Empty(draft.CurrentVersionId); // ...and the stale pin is cleared
        Assert.Equal(1, draft.VersionNumber); // latest committed version still carried for display
    }

    [Fact]
    public async Task Publish_AfterValidationFailedSave_CreatesFreshVersion_InsteadOfReusingStalePin()
    {
        // With the pin cleared by the failed save, EnsureVersionAsync re-persists and re-versions the current
        // graph rather than reusing the stale prior version - so a later publish ships a fresh version, never
        // the superseded prior one. Without the pin-clearing fix this would publish the stale v1.
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        await client.SaveVersionAsync(draft, "v1");

        api.VersionStatusCode = 400;
        await client.SaveVersionAsync(draft, "invalid edit"); // clears the stale pin

        api.VersionStatusCode = null; // server graph validation passes again
        var publish = await client.PublishAsync(draft);

        Assert.Null(publish.BindingState);
        Assert.Equal("queued", publish.Status);
        Assert.Equal(2, api.Versions.Count); // v1 + a freshly created v2, not a reuse of v1
        Assert.EndsWith(":v2", draft.CurrentVersionId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRun_RendersServerEstimateLogsAndOutputSchemas_WithoutOperateJobLink()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);

        var dryRun = await client.DryRunAsync(draft);

        Assert.Null(dryRun.BindingState);
        Assert.Equal("succeeded", dryRun.Status);
        Assert.Equal(StudioWorkflowContractValues.DryRunJobKind, dryRun.JobKind);
        Assert.Contains(dryRun.Logs, log => log.Contains("Estimated duration", StringComparison.Ordinal));
        Assert.NotEmpty(dryRun.OutputSchemas);
        Assert.NotEmpty(dryRun.Artifacts);
        // #1185 dry-run is synchronous estimation - no Operate job is created.
        Assert.Empty(dryRun.OperateJobUrl);
        Assert.Empty(dryRun.OperateEventsUrl);
    }

    [Fact]
    public async Task Publish_PublishesVersionAndStartsRun_LinkingOperateEvidence()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        await client.SaveVersionAsync(draft, "save before publish");

        var publish = await client.PublishAsync(draft);

        Assert.Null(publish.BindingState);
        Assert.Equal("queued", publish.Status);
        Assert.False(string.IsNullOrEmpty(publish.PublicationId));
        Assert.False(string.IsNullOrEmpty(publish.JobId));
        Assert.StartsWith("/operate/jobs/", publish.OperateJobUrl, StringComparison.Ordinal);
        Assert.StartsWith("/operate/events?jobId=", publish.OperateEventsUrl, StringComparison.Ordinal);
        Assert.Single(api.Publications);
        Assert.Equal(1, api.RunCount);
    }

    [Fact]
    public async Task Publish_ProcessEndpoint_SurfacesServerInvocationEndpoint()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.PublicationIntent.Mode = StudioWorkflowContractValues.PublicationModeProcessEndpoint;
        await client.SaveVersionAsync(draft, "save before publish");

        var publish = await client.PublishAsync(draft);

        Assert.Equal(WorkflowPublicationTarget.ProcessEndpoint, api.Publications[0].Target);
        Assert.False(string.IsNullOrEmpty(publish.InvocationEndpoint));
    }

    [Fact]
    public async Task Publish_SurfacesBlockedState_OnServerEligibilityFailure()
    {
        var api = new FakeWorkflowApi { PublishStatusCode = 400 };
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        await client.SaveVersionAsync(draft, "save before publish");

        var publish = await client.PublishAsync(draft);

        Assert.Equal("blocked", publish.Status);
        Assert.Null(publish.BindingState); // a 400 is a bound-surface validation outcome, not a missing binding
        Assert.Contains(publish.ValidationIssues, issue => issue.Scope == "publish" && issue.Severity == "error");
        Assert.Equal(0, api.RunCount);
    }

    private static async Task<StudioWorkflowPackageDraft> NewDraftWithSinkAsync(ServerStudioWorkflowPackageClient client)
    {
        var context = await client.OpenEditorAsync("new");
        var draft = context.Draft!;
        draft.Title = "Parcel normalizer";
        draft.Nodes.Add(new StudioWorkflowNode
        {
            Id = "source-1",
            Type = "honua.source.feature-layer",
            Category = StudioWorkflowContractValues.NodeCategorySource,
            Label = "Source",
            Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
        });
        draft.Nodes.Add(new StudioWorkflowNode
        {
            Id = "sink-1",
            Type = "honua.sink.feature-service",
            Category = StudioWorkflowContractValues.NodeCategorySink,
            Label = "Sink"
        });
        return draft;
    }

    /// <summary>In-memory simulation of the honua-server #1185 workflow API for fast, deterministic tests.</summary>
    private sealed class FakeWorkflowApi : IWorkflowPackageApiClient
    {
        private int _packageSequence;

        public bool RegistryReachable { get; init; } = true;
        public int? PublishStatusCode { get; init; }

        // Settable so a single test can fail one version-create (e.g. a 400) and then succeed on a later save.
        public int? VersionStatusCode { get; set; }
        public List<WorkflowPackage> Packages { get; } = [];
        public List<WorkflowPackageVersion> Versions { get; } = [];
        public List<WorkflowPublication> Publications { get; } = [];
        public int RunCount { get; private set; }

        public Uri BaseUri { get; } = new("https://fake.honua.test");

        public Task<WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>> GetNodeRegistryAsync(
            CancellationToken cancellationToken = default)
        {
            if (!RegistryReachable)
            {
                return Task.FromResult(WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>.FromIssue(
                    new WorkflowEndpointIssue("Unavailable", "GET node-registry", "unreachable")));
            }

            var snapshot = new WorkflowNodeRegistrySnapshot
            {
                RegistryVersion = "test-1",
                GeneratedAt = DateTimeOffset.UtcNow,
                Nodes =
                [
                    new WorkflowNodeDefinition
                    {
                        NodeTypeId = "honua.source.feature-layer",
                        ProviderId = "test",
                        RuntimeKind = WorkflowNodeRuntimeKind.Geoprocessing,
                        Title = "Feature source",
                        Description = "source",
                        Category = "Source",
                        OutputSchemas = [Port("features")]
                    },
                    new WorkflowNodeDefinition
                    {
                        NodeTypeId = "honua.transform.filter",
                        ProviderId = "test",
                        RuntimeKind = WorkflowNodeRuntimeKind.Geoprocessing,
                        Title = "Filter",
                        Description = "transform",
                        Category = "Transform",
                        InputSchemas = [Port("features")],
                        OutputSchemas = [Port("matched")]
                    },
                    new WorkflowNodeDefinition
                    {
                        NodeTypeId = "honua.sink.feature-service",
                        ProviderId = "test",
                        RuntimeKind = WorkflowNodeRuntimeKind.Geoprocessing,
                        Title = "Feature sink",
                        Description = "sink",
                        Category = "Sink",
                        InputSchemas = [Port("features")]
                    }
                ]
            };
            return Task.FromResult(WorkflowEndpointResult<WorkflowNodeRegistrySnapshot>.FromData(snapshot));
        }

        public Task<WorkflowEndpointResult<WorkflowPackageListResponse>> ListPackagesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowEndpointResult<WorkflowPackageListResponse>.FromData(
                new WorkflowPackageListResponse { Items = Packages.ToArray() }));

        public Task<WorkflowEndpointResult<WorkflowPackage>> CreatePackageAsync(
            SaveWorkflowPackageRequest request,
            CancellationToken cancellationToken = default)
        {
            var package = new WorkflowPackage
            {
                PackageId = $"pkg-{++_packageSequence}",
                Name = request.Name,
                Description = request.Description,
                Namespace = request.Namespace,
                Graph = request.Graph,
                Metadata = request.Metadata,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Packages.Add(package);
            return Task.FromResult(WorkflowEndpointResult<WorkflowPackage>.FromData(package));
        }

        public Task<WorkflowEndpointResult<WorkflowPackage>> GetPackageAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var package = Packages.FirstOrDefault(p => p.PackageId == packageId);
            return Task.FromResult(package is null
                ? WorkflowEndpointResult<WorkflowPackage>.FromIssue(new WorkflowEndpointIssue("Unsupported", "GET", "missing", 404))
                : WorkflowEndpointResult<WorkflowPackage>.FromData(package));
        }

        public Task<WorkflowEndpointResult<WorkflowPackage>> UpdatePackageAsync(
            string packageId,
            SaveWorkflowPackageRequest request,
            CancellationToken cancellationToken = default)
        {
            var index = Packages.FindIndex(p => p.PackageId == packageId);
            if (index < 0)
            {
                return Task.FromResult(WorkflowEndpointResult<WorkflowPackage>.FromIssue(
                    new WorkflowEndpointIssue("Unsupported", "PUT", "missing", 404)));
            }

            var updated = Packages[index] with { Graph = request.Graph, Name = request.Name, Description = request.Description, UpdatedAt = DateTimeOffset.UtcNow };
            Packages[index] = updated;
            return Task.FromResult(WorkflowEndpointResult<WorkflowPackage>.FromData(updated));
        }

        public Task<WorkflowEndpointResult<WorkflowPackageVersionListResponse>> ListVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowEndpointResult<WorkflowPackageVersionListResponse>.FromData(
                new WorkflowPackageVersionListResponse { Items = Versions.Where(v => v.PackageId == packageId).ToArray() }));

        public Task<WorkflowEndpointResult<WorkflowPackageVersion>> CreateVersionAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            var index = Packages.FindIndex(p => p.PackageId == packageId);
            if (index < 0)
            {
                return Task.FromResult(WorkflowEndpointResult<WorkflowPackageVersion>.FromIssue(
                    new WorkflowEndpointIssue("Unsupported", "POST versions", "missing", 404)));
            }

            // The graph is PUT by UpdatePackageAsync before this call; a non-success here mirrors honua-server
            // rejecting the version (e.g. a 400 graph-validation failure) after the mutable graph was persisted.
            if (VersionStatusCode is int code)
            {
                return Task.FromResult(WorkflowEndpointResult<WorkflowPackageVersion>.FromIssue(
                    new WorkflowEndpointIssue("Validation failed", "POST versions", "Workflow graph invalid: missing sink node.", code)));
            }

            var next = (Packages[index].LatestVersion ?? 0) + 1;
            var version = new WorkflowPackageVersion
            {
                PackageId = packageId,
                Version = next,
                SchemaVersion = "workflow-package.v1",
                PackageHash = $"hash-{next}",
                Graph = Packages[index].Graph,
                Validation = new WorkflowPackageValidationResult { IsValid = true },
                CreatedAt = DateTimeOffset.UtcNow
            };
            Versions.Add(version);
            Packages[index] = Packages[index] with { LatestVersion = next };
            return Task.FromResult(WorkflowEndpointResult<WorkflowPackageVersion>.FromData(version));
        }

        public Task<WorkflowEndpointResult<WorkflowPackageVersion>> GetVersionAsync(
            string packageId,
            int version,
            CancellationToken cancellationToken = default)
        {
            var found = Versions.FirstOrDefault(v => v.PackageId == packageId && v.Version == version);
            return Task.FromResult(found is null
                ? WorkflowEndpointResult<WorkflowPackageVersion>.FromIssue(new WorkflowEndpointIssue("Unsupported", "GET", "missing", 404))
                : WorkflowEndpointResult<WorkflowPackageVersion>.FromData(found));
        }

        public Task<WorkflowEndpointResult<WorkflowPackageValidationResult>> ValidateVersionAsync(
            string packageId,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowEndpointResult<WorkflowPackageValidationResult>.FromData(
                new WorkflowPackageValidationResult { IsValid = true }));

        public Task<WorkflowEndpointResult<WorkflowDryRunResult>> DryRunVersionAsync(
            string packageId,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowEndpointResult<WorkflowDryRunResult>.FromData(new WorkflowDryRunResult
            {
                Validation = new WorkflowPackageValidationResult { IsValid = true },
                EstimatedDurationSeconds = 12,
                EstimatedCostWeight = 3.5,
                PackageHash = "hash-1",
                Logs = [new WorkflowDryRunLogEntry { Timestamp = DateTimeOffset.UtcNow, Level = "info", Message = "compiled plan" }],
                Artifacts = [new WorkflowDryRunArtifact { ArtifactId = "a1", Kind = "feature-layer", Label = "output preview" }],
                OutputSchemas =
                [
                    new WorkflowNodePortSchema
                    {
                        Name = "features",
                        Title = "Validated features",
                        Schema = new WorkflowSchemaDefinition { Type = WorkflowSchemaValueType.Structured, Format = "feature-layer" }
                    }
                ]
            }));

        public Task<WorkflowEndpointResult<WorkflowPublication>> PublishVersionAsync(
            string packageId,
            int version,
            PublishWorkflowPackageRequest request,
            CancellationToken cancellationToken = default)
        {
            if (PublishStatusCode is int code)
            {
                return Task.FromResult(WorkflowEndpointResult<WorkflowPublication>.FromIssue(
                    new WorkflowEndpointIssue("Validation failed", "POST publish", "Publication ineligible: missing failure edge.", code)));
            }

            var publication = new WorkflowPublication
            {
                PublicationId = $"pub-{Publications.Count + 1}",
                PackageId = packageId,
                PackageVersion = version,
                PackageHash = $"hash-{version}",
                Target = request.Target,
                Status = WorkflowPublicationStatus.Active,
                EndpointPath = request.Target == WorkflowPublicationTarget.ProcessEndpoint
                    ? $"/api/v1/console/workflow-publications/pub-{Publications.Count + 1}/runs"
                    : null,
                Eligibility = new WorkflowPackageValidationResult { IsValid = true },
                CreatedAt = DateTimeOffset.UtcNow
            };
            Publications.Add(publication);
            return Task.FromResult(WorkflowEndpointResult<WorkflowPublication>.FromData(publication));
        }

        public Task<WorkflowEndpointResult<WorkflowPublicationListResponse>> ListPublicationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowEndpointResult<WorkflowPublicationListResponse>.FromData(
                new WorkflowPublicationListResponse { Items = Publications.ToArray() }));

        public Task<WorkflowEndpointResult<WorkflowPublicationRunResult>> RunPublicationAsync(
            string publicationId,
            RunWorkflowPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            var publication = Publications.First(p => p.PublicationId == publicationId);
            return Task.FromResult(WorkflowEndpointResult<WorkflowPublicationRunResult>.FromData(new WorkflowPublicationRunResult
            {
                PublicationId = publicationId,
                PackageId = publication.PackageId,
                PackageVersion = publication.PackageVersion,
                Target = publication.Target,
                JobId = $"job-{RunCount}",
                Provenance = new Dictionary<string, string>()
            }));
        }

        private static WorkflowNodePortSchema Port(string name) => new()
        {
            Name = name,
            Title = name,
            Schema = new WorkflowSchemaDefinition { Type = WorkflowSchemaValueType.Structured }
        };
    }
}

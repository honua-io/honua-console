using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Recording-HttpClient unit tests proving the GitOps metadata release client binds
/// to the SHIPPED honua-server contracts: the release package + GitOps manifest
/// (honua-server#1163) and the release-operation lifecycle / rollback (honua-server#1165).
/// </summary>
public sealed class ConsoleGitOpsReleaseClientTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task ProposalListBindsLiveReleasePackageListEndpoint()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/release-packages", StringComparison.Ordinal)
            ? JsonResponse(BuildList(), MetadataReleaseJsonContext.Default.MetadataReleasePackageListResponse)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.GetReleaseProposalsAsync();

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var proposals = result.Value!;
        Assert.Equal(2, proposals.Count);

        // Lightweight list-view proposal mapped from the summary (title, environments,
        // desired revision). The full diff/matrix is only loaded on drill-down.
        var ready = Assert.Single(proposals, p => p.ReleasePackageId == PackageId.ToString("D"));
        Assert.Equal("Promote parcels", ready.Title);
        Assert.Equal("staging", ready.SourceEnvironmentId);
        Assert.Contains("prod", ready.TargetEnvironmentIds);
        Assert.Equal("rev-77", ready.DesiredRevision);
        Assert.Empty(ready.ChangedResources);

        // The list call carries admin auth and hits the collection route (not by-id).
        var listRequest = Assert.Single(handler.Requests);
        Assert.EndsWith("/release-packages", listRequest.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.True(listRequest.Headers.TryGetValues("X-API-Key", out var keys) && keys.Single() == "admin-key");
    }

    [Fact]
    public async Task ProposalListSurfacesServerFailureHonestly()
    {
        // A server that does not expose the list route (or denies it) is reported as
        // the mapped failure status rather than a fabricated list (charter §11).
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented));
        var client = CreateClient(handler);

        var result = await client.GetReleaseProposalsAsync();

        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task DetailBindsPackageManifestAndOperationFromLiveServer()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith($"/release-packages/{PackageId:D}", StringComparison.Ordinal) =>
                JsonResponse(BuildPackage(), MetadataReleaseJsonContext.Default.MetadataReleasePackageResponse),
            var path when path.EndsWith("/gitops-manifest", StringComparison.Ordinal) =>
                JsonResponse(BuildManifest(), MetadataReleaseJsonContext.Default.GitOpsMetadataReleaseManifestResponse),
            var path when path.EndsWith("/operation", StringComparison.Ordinal) =>
                JsonResponse(BuildOperation(blocked: false), MetadataReleaseJsonContext.Default.DeployOperationResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.GetReleaseDetailAsync(PackageId.ToString("D"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var detail = result.Value!;

        // Proposal + semantic diff come from the release package.
        Assert.Equal("Promote parcels", detail.Proposal.Title);
        Assert.Equal("staging", detail.Proposal.SourceEnvironmentId);
        Assert.Contains("prod", detail.Proposal.TargetEnvironmentIds);
        Assert.Contains(detail.Proposal.ChangedResources, resource => resource.SemanticResourceId == "field:parcels.zoning");

        // The field change class maps to a field-contract diff badge.
        Assert.Contains(
            detail.Proposal.ChangedResources,
            resource => resource.ChangeClasses.Contains(GitOpsChangeClass.FieldContract));

        // Desired revision is resolved from the manifest source revision.
        Assert.Equal("rev-77", detail.Proposal.DesiredRevision);

        // Environment matrix shows the prod target is behind (drift).
        var prodCell = Assert.Single(detail.EnvironmentMatrix, cell => cell.Environment == "prod");
        Assert.False(prodCell.IsAligned);
        Assert.Equal(77, prodCell.DesiredRevision);
        Assert.Equal(70, prodCell.CurrentRevision);
        Assert.True(detail.HasDrift);

        // Operation lifecycle: same operation id, PR preview, rollback plan promoted to proposal.
        Assert.NotNull(detail.Operation);
        Assert.Equal("op-9", detail.Operation!.OperationId);
        Assert.Equal("https://git.example/pr/9", detail.Operation.PrUrl);
        Assert.Equal(GitOpsRollbackClassification.SnapshotRequired, detail.Proposal.RollbackClassification);
        Assert.NotNull(detail.Operation.RollbackPlan);
        Assert.True(detail.Operation.RollbackPlan!.IsDataAffecting);

        // Data scripts are bound from the package with their rollback coverage.
        Assert.Equal(2, detail.Scripts.Count);
        Assert.Contains(detail.Scripts, s => s.FileName == "001_add_parcels_index.sql" && s.Coverage == GitOpsDataScriptCoverage.Covered);
        Assert.Contains(detail.Scripts, s => s.FileName == "002_drop_zoning_code.sql" && s.Coverage == GitOpsDataScriptCoverage.NoRollback);
        Assert.True(detail.HasDataScriptCoverageGap);

        // X-API-Key admin auth is attached to every request.
        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request =>
            Assert.True(request.Headers.TryGetValues("X-API-Key", out var values) && values.Single() == "admin-key"));
    }

    [Fact]
    public async Task DetailWithOperationBlockersDisablesPullRequestAction()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith($"/release-packages/{PackageId:D}", StringComparison.Ordinal) =>
                JsonResponse(BuildPackage(), MetadataReleaseJsonContext.Default.MetadataReleasePackageResponse),
            var path when path.EndsWith("/gitops-manifest", StringComparison.Ordinal) =>
                JsonResponse(BuildManifest(), MetadataReleaseJsonContext.Default.GitOpsMetadataReleaseManifestResponse),
            var path when path.EndsWith("/operation", StringComparison.Ordinal) =>
                JsonResponse(BuildOperation(blocked: true), MetadataReleaseJsonContext.Default.DeployOperationResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetReleaseDetailAsync(PackageId.ToString("D"));

        var detail = result.Value!;
        // Acceptance criterion: blockers prevent the PR/deploy action.
        Assert.True(detail.Proposal.HasBlockingFindings);
        Assert.False(detail.CanProposePullRequest);
        Assert.Contains("smoke SLO burn exceeded budget", detail.Operation!.Blockers);
    }

    [Fact]
    public async Task DetailWithoutOperationDegradesToMissingOperationStateButStillRendersProposal()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith($"/release-packages/{PackageId:D}", StringComparison.Ordinal) =>
                JsonResponse(BuildPackage(), MetadataReleaseJsonContext.Default.MetadataReleasePackageResponse),
            var path when path.EndsWith("/gitops-manifest", StringComparison.Ordinal) =>
                JsonResponse(BuildManifest(), MetadataReleaseJsonContext.Default.GitOpsMetadataReleaseManifestResponse),
            // No operation has been started yet.
            var path when path.EndsWith("/operation", StringComparison.Ordinal) =>
                new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.GetReleaseDetailAsync(PackageId.ToString("D"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var detail = result.Value!;
        // The proposal + matrix still render; only the operation lifecycle is missing.
        Assert.Equal("Promote parcels", detail.Proposal.Title);
        Assert.Null(detail.Operation);
        Assert.Equal(OperateSectionStatus.Missing, detail.OperationStatus);
        Assert.Contains("No release operation has been started", detail.OperationMessage, StringComparison.Ordinal);
        // With no operation lifecycle, the proposal's own findings still govern the action.
        Assert.True(detail.CanProposePullRequest);
    }

    [Fact]
    public async Task DetailFailsWhenReleasePackageReadIsForbidden()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var result = await client.GetReleaseDetailAsync(PackageId.ToString("D"));

        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task DetailRejectsNonGuidReleaseIdAsMissing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler);

        var result = await client.GetReleaseDetailAsync("not-a-guid");

        Assert.Equal(OperateSectionStatus.Missing, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CoordinatedReleaseBindsLiveCoordinatedOperationEndpoint()
    {
        var handler = new RecordingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/coordinated-releases/" + PackageId.ToString("D") + "/operation", StringComparison.Ordinal)
                ? JsonResponse(BuildCoordinated(), MetadataReleaseJsonContext.Default.CoordinatedReleaseOperationResponse)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler, adminApiKey: "admin-key");

        var result = await client.GetCoordinatedReleaseAsync(PackageId.ToString("D"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        var coordinated = result.Value!;

        // The three artifact kinds map across.
        Assert.Equal(3, coordinated.Artifacts.Count);
        Assert.Contains(coordinated.Artifacts, a => a.Kind == GitOpsCoordinatedArtifactKind.ContainerImage);
        Assert.Contains(coordinated.Artifacts, a => a.Kind == GitOpsCoordinatedArtifactKind.DatabaseSchema);
        Assert.Contains(coordinated.Artifacts, a => a.Kind == GitOpsCoordinatedArtifactKind.Metadata);

        // The ordered step ledger + gate/reversible flags map across.
        Assert.Equal(6, coordinated.Steps.Count);
        var dataGate = Assert.Single(coordinated.Steps, s => s.Step == "MetadataAndSchema");
        Assert.True(dataGate.RequiresApproval);
        Assert.True(dataGate.IsReversible);
        Assert.Equal(GitOpsCoordinatedStepStatus.AwaitingApproval, dataGate.Status);

        // The op is parked at the data gate and rollback is ready.
        Assert.True(coordinated.IsAwaitingApproval);
        Assert.Equal("MetadataAndSchema", coordinated.PendingGate!.Step);
        Assert.True(coordinated.RollbackReady);

        var request = Assert.Single(handler.Requests);
        Assert.True(request.Headers.TryGetValues("X-API-Key", out var keys) && keys.Single() == "admin-key");
    }

    [Fact]
    public async Task CoordinatedReleaseSurfacesMissingOperationHonestly()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.GetCoordinatedReleaseAsync(PackageId.ToString("D"));

        Assert.Equal(OperateSectionStatus.Missing, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("No coordinated platform-upgrade release has been started", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsReturnMissingBindingWhenNoEnvironmentIsConnected()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleGitOpsReleaseClient(new HttpClient(handler), profiles, adminApiKey: "admin-key");

        var list = await client.GetReleaseProposalsAsync();
        var detail = await client.GetReleaseDetailAsync(PackageId.ToString("D"));
        var coordinated = await client.GetCoordinatedReleaseAsync(PackageId.ToString("D"));

        Assert.Equal(OperateSectionStatus.Unavailable, list.Status);
        Assert.Equal(OperateSectionStatus.Unavailable, detail.Status);
        Assert.Equal(OperateSectionStatus.Unavailable, coordinated.Status);
        Assert.Empty(handler.Requests);
    }

    private static CoordinatedReleaseOperationResponse BuildCoordinated() => new()
    {
        OperationId = "coordinated-release-pkg-democ",
        PackageId = PackageId.ToString("D"),
        Status = "AwaitingApproval",
        CurrentStep = "MetadataAndSchema",
        TargetEnvironment = "production",
        CurrentPhase = "Awaiting explicit approval to apply the DB schema change.",
        Artifacts =
        [
            new CoordinatedReleaseArtifactResponse { Kind = "ContainerImage", Title = "Container rollout · prod-ecs", Desired = "honua/server:v21", Current = "honua/server:v20" },
            new CoordinatedReleaseArtifactResponse { Kind = "DatabaseSchema", Title = "DB schema · add-owner-email", Desired = "Add nullable field 'owner_email' to parcels" },
            new CoordinatedReleaseArtifactResponse { Kind = "Metadata", Title = "Metadata · pkg", Desired = "Activate new metadata revision in production" }
        ],
        Steps =
        [
            new CoordinatedReleaseStepResponse { Step = "Preflight", Status = "Succeeded" },
            new CoordinatedReleaseStepResponse { Step = "Backup", Status = "Succeeded" },
            new CoordinatedReleaseStepResponse { Step = "ContainerRollout", Status = "Succeeded", RequiresApproval = true, IsReversible = true },
            new CoordinatedReleaseStepResponse { Step = "MetadataAndSchema", Status = "AwaitingApproval", RequiresApproval = true, IsReversible = true },
            new CoordinatedReleaseStepResponse { Step = "Smoke", Status = "Pending" },
            new CoordinatedReleaseStepResponse { Step = "Promote", Status = "Pending" }
        ],
        ContainerGateApproved = true,
        DataGateApproved = false,
        RollbackReady = true,
        CreatedAt = DateTimeOffset.Parse("2026-05-24T19:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:30:00Z")
    };

    private static HttpConsoleGitOpsReleaseClient CreateClient(HttpMessageHandler handler, string? adminApiKey = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleGitOpsReleaseClient(new HttpClient(handler), profiles, adminApiKey);
    }

    private static MetadataReleasePackageListResponse BuildList() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-05-24T20:00:00Z"),
        Count = 2,
        Limit = 50,
        Offset = 0,
        Items =
        [
            new MetadataReleasePackageSummaryResponse
            {
                PackageId = PackageId,
                PackageKey = "promote-parcels",
                Title = "Promote parcels",
                Summary = "Promote the parcels field contract change to prod.",
                SourceEnvironment = "staging",
                SourceRevision = 77,
                TargetEnvironments = ["prod"],
                EntryCount = 1,
                Status = MetadataReleasePackageStatusWire.Ready,
                CreatedBy = "operator.live",
                CreatedAt = DateTimeOffset.Parse("2026-05-24T19:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:30:00Z")
            },
            new MetadataReleasePackageSummaryResponse
            {
                PackageId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
                PackageKey = "promote-roads",
                // No title -> falls back to the package key.
                SourceEnvironment = "dev",
                SourceRevision = 12,
                TargetEnvironments = ["staging", "prod"],
                EntryCount = 3,
                Status = MetadataReleasePackageStatusWire.Draft,
                CreatedBy = "operator.live",
                CreatedAt = DateTimeOffset.Parse("2026-05-23T10:00:00Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-05-23T10:30:00Z")
            }
        ]
    };

    private static MetadataReleasePackageResponse BuildPackage() => new()
    {
        PackageId = PackageId,
        Metadata = new MetadataObjectMetadataResponse
        {
            Id = PackageId.ToString("D"),
            Name = "promote-parcels",
            Title = "Promote parcels",
            Description = "Promote the parcels field contract change to prod."
        },
        SourceEnvironment = "staging",
        SourceRevision = 77,
        SourceEtag = "etag-77",
        TargetEnvironments = ["prod"],
        Status = MetadataReleasePackageStatusWire.Ready,
        CreatedBy = "operator.live",
        CreatedAt = DateTimeOffset.Parse("2026-05-24T19:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:30:00Z"),
        DataScripts =
        [
            new MetadataReleaseDataScriptResponse
            {
                ScriptId = "001",
                FileName = "001_add_parcels_index.sql",
                Coverage = "covered"
            },
            new MetadataReleaseDataScriptResponse
            {
                ScriptId = "002",
                FileName = "002_drop_zoning_code.sql",
                Coverage = "no-rollback"
            }
        ],
        Entries =
        [
            new MetadataReleaseEntryResponse
            {
                SemanticId = "field:parcels.zoning",
                ArtifactKind = MetadataSemanticArtifactKindWire.Field,
                ResourceType = "field",
                DesiredMetadataRevision = 77,
                ChangeClass = MetadataReleaseChangeClassWire.Metadata,
                TargetStates =
                [
                    new MetadataReleaseTargetStateResponse
                    {
                        Environment = "prod",
                        CurrentMetadataRevision = 70,
                        BindingState = MetadataEnvironmentBindingStateWire.Bound
                    }
                ]
            }
        ]
    };

    private static GitOpsMetadataReleaseManifestResponse BuildManifest() => new()
    {
        ApiVersion = "metadata.honua.io/v2",
        Kind = "MetadataReleasePackage",
        Spec = new GitOpsMetadataReleaseSpecResponse
        {
            PackageId = PackageId,
            Source = new GitOpsMetadataReleaseSourceResponse
            {
                Environment = "staging",
                Revision = 77,
                ETag = "etag-77"
            },
            Targets = [new GitOpsMetadataReleaseTargetResponse { Environment = "prod" }]
        }
    };

    private static DeployOperationResponse BuildOperation(bool blocked) => new()
    {
        OperationId = "op-9",
        Kind = "metadata-release",
        Status = blocked ? "failed" : "running",
        Priority = "normal",
        CurrentPhase = "ci",
        CreatedAt = DateTimeOffset.Parse("2026-05-24T19:35:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-05-24T19:40:00Z"),
        BlockingReasons = blocked ? ["smoke SLO burn exceeded budget"] : [],
        MetadataRelease = new MetadataReleaseContextResponse
        {
            PackageId = PackageId.ToString("D"),
            PrUrl = "https://git.example/pr/9",
            CommitSha = "abc123def456",
            GitOperationId = "git-op-9",
            DesiredRevision = "77",
            TargetEnvironment = "prod",
            CurrentStage = blocked ? "ci-failed" : "ci",
            Blockers = blocked ? ["smoke SLO burn exceeded budget"] : [],
            RollbackPlan = new MetadataRollbackPlanResponse
            {
                Class = "snapshot-required",
                IsDataAffecting = true,
                RequiresExplicitApproval = true,
                Steps = ["restore parcels snapshot"],
                EvidenceRequired = ["backup-id"],
                ApprovalPolicyRef = "policy/rollback-prod"
            }
        }
    };

    private static HttpResponseMessage JsonResponse<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(value, typeInfo);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}

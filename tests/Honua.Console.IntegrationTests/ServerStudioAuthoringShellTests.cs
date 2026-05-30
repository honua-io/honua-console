using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using StudioPackageFamily = Honua.Console.Contracts.StudioPackageFamily;

namespace Honua.Console.IntegrationTests;

public sealed class ServerStudioAuthoringShellTests
{
    [Fact]
    public async Task ClarificationsPersistToServerDraftBeforeSave()
    {
        var client = new RecordingStudioPackageLifecycleClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "map",
            "Make a map");
        var sourceQuestion = session.Clarifications.First(question => question.Id == "source-binding");
        var publishQuestion = session.Clarifications.First(question => question.Id == "publish-intent");

        var withSource = await shell.ApplyClarificationAsync(
            session,
            sourceQuestion.Id,
            "saved-map");
        var clarified = await shell.ApplyClarificationAsync(
            withSource,
            publishQuestion.Id,
            "org-preview");
        var saved = await shell.SaveVersionAsync(clarified);

        Assert.Equal(2, client.UpdateRequests.Count);
        Assert.Equal(1, client.UpdateRequests[0].Generation);
        Assert.Contains(
            client.UpdateRequests[0].Envelope.Bindings,
            binding => binding is { Key: "source-binding", Kind: "content-item", Ref: "Use the current saved map" });
        Assert.Equal(2, client.UpdateRequests[1].Generation);
        Assert.Equal("organization", client.UpdateRequests[1].Envelope.PublicationIntent?.Visibility);
        Assert.Equal(3, clarified.Draft?.Generation);
        Assert.Empty(clarified.Clarifications);
        Assert.Equal(StudioPackageLifecycleState.SavedVersion, saved.ActivePackage.LifecycleState);
        Assert.NotNull(client.SavedVersionEnvelope);
        Assert.Contains(
            client.SavedVersionEnvelope.Bindings,
            binding => binding is { Key: "source-binding", Ref: "Use the current saved map" });
        Assert.Equal("organization", client.SavedVersionEnvelope.PublicationIntent?.Visibility);
    }

    [Fact]
    public async Task ValidateRefreshesDraftGenerationAfterClarificationsAreResolved()
    {
        var client = new RecordingStudioPackageLifecycleClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "map",
            "Make a map");
        var clarified = await ResolveAllClarificationsAsync(shell, session);
        var validated = await shell.ValidateAsync(clarified);

        Assert.Empty(clarified.Clarifications);
        Assert.Equal(3, clarified.Draft?.Generation);
        Assert.Equal(4, validated.Draft?.Generation);
        Assert.Equal(1, client.ValidateRequestCount);
        Assert.Equal(2, client.UpdateRequests.Count);
    }

    [Fact]
    public async Task PreviewPlanRefreshesDraftGenerationAfterClarificationsAreResolved()
    {
        var client = new RecordingStudioPackageLifecycleClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "map",
            "Make a map");
        var clarified = await ResolveAllClarificationsAsync(shell, session);
        var previewed = await shell.PreviewPlanAsync(clarified);

        Assert.Empty(clarified.Clarifications);
        Assert.Equal(3, clarified.Draft?.Generation);
        Assert.Equal(4, previewed.Draft?.Generation);
        Assert.NotNull(previewed.PreviewPlan);
        Assert.Equal(1, client.PreviewPlanRequestCount);
        Assert.Equal(2, client.UpdateRequests.Count);
    }

    [Fact]
    public async Task ClarificationUpdateRetriesOnceAfterDraftGenerationConflict()
    {
        var client = new RecordingStudioPackageLifecycleClient { ConflictOnceOnUpdate = true };
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "map",
            "Make a map");
        var sourceQuestion = session.Clarifications.First(question => question.Id == "source-binding");

        var clarified = await shell.ApplyClarificationAsync(
            session,
            sourceQuestion.Id,
            sourceQuestion.Choices[0].Id);

        Assert.Null(clarified.BindingState);
        Assert.Equal(new long[] { 1, 2 }, client.UpdateRequests.Select(request => request.Generation.GetValueOrDefault()));
        Assert.Equal(3, clarified.Draft?.Generation);
    }

    [Fact]
    public async Task OpenClarificationsBlockValidationPreviewSaveAndPublishBeforeEndpointCalls()
    {
        var client = new RecordingStudioPackageLifecycleClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        var session = await shell.GeneratePackageAsync(
            await shell.CreateInitialSessionAsync(),
            "map",
            "Make a map");
        var publishable = session with
        {
            ActivePackage = session.ActivePackage with { LifecycleState = StudioPackageLifecycleState.SavedVersion },
            Draft = session.Draft! with { CurrentVersionId = Guid.NewGuid().ToString() }
        };

        var validated = await shell.ValidateAsync(session);
        var previewed = await shell.PreviewPlanAsync(session);
        var saved = await shell.SaveVersionAsync(session);
        var published = await shell.PublishAsync(publishable);

        Assert.NotEmpty(session.Clarifications);
        Assert.Equal(0, client.ValidateRequestCount);
        Assert.Equal(0, client.PreviewPlanRequestCount);
        Assert.Equal(0, client.SaveContentVersionRequestCount);
        Assert.Equal(0, client.PublishRequestCount);
        Assert.Contains("clarifications", validated.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clarifications", previewed.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clarifications", saved.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clarifications", published.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(session.Draft, validated.Draft);
        Assert.Equal(session.Draft, previewed.Draft);
        Assert.Equal(session.Draft, saved.Draft);
        Assert.Equal(publishable.Draft, published.Draft);
    }

    private static async Task<StudioAuthoringSession> ResolveAllClarificationsAsync(
        IStudioAuthoringShell shell,
        StudioAuthoringSession session)
    {
        while (session.Clarifications.Count > 0)
        {
            var question = session.Clarifications[0];
            session = await shell.ApplyClarificationAsync(session, question.Id, question.Choices[0].Id);
        }

        return session;
    }

    private sealed class RecordingStudioPackageLifecycleClient : IStudioPackageLifecycleClient
    {
        private StudioPackageDraft? _draft;

        public Uri BaseUri { get; } = new("https://honua.test");

        public bool ConflictOnceOnUpdate { get; init; }

        public List<UpdateStudioPackageDraftRequest> UpdateRequests { get; } = [];

        public StudioPackageEnvelope? SavedVersionEnvelope { get; private set; }

        public int ValidateRequestCount { get; private set; }

        public int PreviewPlanRequestCount { get; private set; }

        public int SaveContentVersionRequestCount { get; private set; }

        public int PublishRequestCount { get; private set; }

        public Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioPackageFamilyCapabilities>.FromData(new StudioPackageFamilyCapabilities
            {
                Durable = true,
                PersistenceMode = StudioPackagePersistenceMode.Durable,
                Families =
                [
                    new StudioPackageFamilyDescriptor
                    {
                        Family = StudioPackageFamily.Map,
                        CurrentSchemaVersion = "1.0",
                        Format = "map.package",
                        SupportLevel = StudioPackageSupportLevel.Supported,
                        SupportedOperations =
                        [
                            StudioPackageOperation.DraftCreate,
                            StudioPackageOperation.DraftRead,
                            StudioPackageOperation.DraftUpdate,
                            StudioPackageOperation.Validate,
                            StudioPackageOperation.PreviewPlan,
                            StudioPackageOperation.ContentVersionCreate,
                            StudioPackageOperation.PublishRequestCreate
                        ],
                        ValidationDepth = "envelope",
                        PreviewSupported = true,
                        PublishSupported = true
                    }
                ]
            }));

        public Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
            CreateStudioPackageDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            var validation = NotValidated();
            _draft = new StudioPackageDraft
            {
                DraftId = Guid.NewGuid(),
                ItemId = request.ItemId ?? Guid.NewGuid(),
                PackageKey = request.PackageKey,
                WorkspaceId = request.WorkspaceId,
                OwnerId = request.OwnerId,
                Family = request.Envelope.Family,
                Envelope = request.Envelope with { Validation = validation },
                Validation = validation,
                Generation = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(_draft));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_draft is not null && _draft.DraftId == draftId
                ? StudioEndpointResult<StudioPackageDraft>.FromData(_draft)
                : NotFound<StudioPackageDraft>("GET /api/v1/studio/package-drafts/{draftId}"));

        public Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
            Guid draftId,
            UpdateStudioPackageDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateRequests.Add(request);
            if (_draft is null || _draft.DraftId != draftId)
            {
                return Task.FromResult(NotFound<StudioPackageDraft>("PUT /api/v1/studio/package-drafts/{draftId}"));
            }

            if (ConflictOnceOnUpdate && UpdateRequests.Count == 1)
            {
                _draft = _draft with { Generation = _draft.Generation + 1 };
                return Task.FromResult(Conflict<StudioPackageDraft>("PUT /api/v1/studio/package-drafts/{draftId}"));
            }

            if (request.Generation != _draft.Generation)
            {
                return Task.FromResult(Conflict<StudioPackageDraft>("PUT /api/v1/studio/package-drafts/{draftId}"));
            }

            var validation = request.Envelope.Validation ?? NotValidated();
            _draft = _draft with
            {
                PackageKey = request.PackageKey,
                WorkspaceId = request.WorkspaceId,
                OwnerId = request.OwnerId,
                Envelope = request.Envelope with { Validation = validation },
                Validation = validation,
                Generation = _draft.Generation + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(_draft));
        }

        public Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default)
        {
            ValidateRequestCount++;
            if (_draft is null || _draft.DraftId != draftId)
            {
                return Task.FromResult(NotFound<StudioValidationSummary>("POST /api/v1/studio/package-drafts/{draftId}/validate"));
            }

            var validation = new StudioValidationSummary
            {
                Status = StudioPackageValidationStatus.Valid,
                GeneratedAt = DateTimeOffset.UtcNow
            };
            _draft = _draft with
            {
                Envelope = _draft.Envelope with { Validation = validation },
                Validation = validation,
                Generation = _draft.Generation + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult(StudioEndpointResult<StudioValidationSummary>.FromData(validation));
        }

        public Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
            Guid draftId,
            CancellationToken cancellationToken = default)
        {
            PreviewPlanRequestCount++;
            if (_draft is null || _draft.DraftId != draftId)
            {
                return Task.FromResult(NotFound<StudioPreviewPlan>("POST /api/v1/studio/package-drafts/{draftId}/preview-plan"));
            }

            var validation = new StudioValidationSummary
            {
                Status = StudioPackageValidationStatus.Valid,
                GeneratedAt = DateTimeOffset.UtcNow
            };
            _draft = _draft with
            {
                Envelope = _draft.Envelope with { Validation = validation },
                Validation = validation,
                Generation = _draft.Generation + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult(StudioEndpointResult<StudioPreviewPlan>.FromData(new StudioPreviewPlan
            {
                DraftId = draftId,
                Family = _draft.Family,
                Synchronous = true,
                RequiresJob = false,
                Steps = ["validate-envelope", "prepare-inline-preview"],
                Validation = validation
            }));
        }

        public Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
            Guid draftId,
            SaveStudioContentVersionRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveContentVersionRequestCount++;
            if (_draft is null || _draft.DraftId != draftId)
            {
                return Task.FromResult(NotFound<StudioContentVersion>("POST /api/v1/studio/package-drafts/{draftId}/content-versions"));
            }

            SavedVersionEnvelope = _draft.Envelope;
            return Task.FromResult(StudioEndpointResult<StudioContentVersion>.FromData(new StudioContentVersion
            {
                ItemId = _draft.ItemId,
                PackageKey = _draft.PackageKey,
                WorkspaceId = _draft.WorkspaceId,
                OwnerId = _draft.OwnerId,
                VersionId = Guid.NewGuid(),
                VersionNumber = 1,
                ContentHash = "sha256:test",
                Envelope = _draft.Envelope,
                Validation = _draft.Validation,
                SourceDraftId = _draft.DraftId,
                CreatedAt = DateTimeOffset.UtcNow
            }));
        }

        public Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioContentVersionListResponse>.FromData(new StudioContentVersionListResponse
            {
                ItemId = itemId,
                Versions = []
            }));

        public Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
            Guid itemId,
            Guid versionId,
            CreateStudioPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            PublishRequestCount++;
            return Task.FromResult(StudioEndpointResult<StudioPublicationRequest>.FromData(new StudioPublicationRequest
            {
                RequestId = Guid.NewGuid(),
                ItemId = itemId,
                VersionId = versionId,
                Status = StudioPublicationRequestStatus.Accepted,
                Validation = NotValidated(),
                CreatedAt = DateTimeOffset.UtcNow
            }));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotFound<StudioPackageDraft>(
                "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen"));

        public Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotFound<StudioContentVersion>(
                "GET /api/v1/studio/content-items/{itemId}/versions/{versionId}"));

        private static StudioValidationSummary NotValidated() => new()
        {
            Status = StudioPackageValidationStatus.NotValidated
        };

        private static StudioEndpointResult<T> NotFound<T>(string contract) =>
            StudioEndpointResult<T>.FromIssue(new StudioEndpointIssue(
                "Missing item",
                contract,
                "The Studio test draft was not found.",
                404));

        private static StudioEndpointResult<T> Conflict<T>(string contract) =>
            StudioEndpointResult<T>.FromIssue(new StudioEndpointIssue(
                "Conflict",
                contract,
                "Stale draft generation; refresh and retry.",
                409));
    }
}

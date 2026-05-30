using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the server-bound app-builder data source
/// (<see cref="HonuaServerStudioAppPackageDataSource"/>). Drives the real source over a recording
/// <see cref="IStudioPackageLifecycleClient"/> to assert the full /studio/app lifecycle against the shipped
/// honua-server contract (#1180/#1183): a new app saves -> validates -> publishes a content version + a
/// publication request; the published version can be reopened into a fresh editable draft whose body is
/// rehydrated; version history is mapped newest-first; rollback repoints the published pointer; and every
/// endpoint issue surfaces as a capability state rather than throwing or fabricating data
/// (Console Patterns Charter section 11).
/// </summary>
public sealed class StudioAppPackageDataSourceServerTests
{
    [Fact]
    public async Task NewApp_SaveValidatePublish_CreatesVersionAndPublicationRequest()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var state = ReadyApp();

        var saved = await source.SaveDraftAsync(state);
        Assert.True(saved.Succeeded);
        Assert.NotNull(saved.State!.DraftId);
        Assert.Equal(1, client.CreateDraftCount);

        var validated = await source.ValidateAsync(saved.State!);
        Assert.True(validated.Succeeded);
        Assert.NotNull(validated.Validation);
        Assert.True(validated.Validation!.IsValid);

        var published = await source.PublishAsync(saved.State!);
        Assert.True(published.Succeeded);
        Assert.Equal(1, client.SaveVersionCount);
        Assert.Equal(1, client.PublishCount);
        // The publish intent carries the explicit, reviewed share/embed policy.
        Assert.Equal("organization", client.LastPublishIntent!.Visibility);
        Assert.True(client.LastPublishIntent.Embed);
        Assert.Equal(published.State!.PublishedVersion, 1);
    }

    [Fact]
    public async Task Publish_BlockedWhenShareEmbedPolicyNotReviewed()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var state = ReadyApp();
        state.ShareEmbedPolicyReviewed = false;

        var result = await source.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.Contains("share/embed", result.Message, StringComparison.OrdinalIgnoreCase);
        // The pre-publish gate must prevent any server write.
        Assert.Equal(0, client.SaveVersionCount);
        Assert.Equal(0, client.PublishCount);
    }

    [Fact]
    public async Task Reopen_PublishedVersion_RehydratesBodyAsNewDraftWithoutPublishedPointer()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        // Author + publish so the recording client holds an immutable version with a real body.
        var saved = await source.SaveDraftAsync(ReadyApp());
        await source.PublishAsync(saved.State!);
        var itemId = client.ItemId;
        var versionId = client.LastVersionId;

        var reopened = await source.ReopenAsync(itemId, versionId);

        Assert.True(reopened.Succeeded);
        var draft = reopened.State!;
        // A reopened draft is editable (has a draft id) and carries no published pointer, so the next save
        // creates a new content version rather than mutating the published one.
        Assert.NotNull(draft.DraftId);
        Assert.Null(draft.PublishedVersion);
        Assert.False(draft.IsPublished);
        // The authored body round-trips back into the editor instead of resetting to a blank scaffold.
        Assert.Equal("Field operations", draft.Title);
        Assert.Equal("content:permits@v3", draft.Pages[0].ContentBinding);
        var action = Assert.Single(draft.Actions);
        Assert.Equal("operator", action.RequiredPermission);
    }

    [Fact]
    public async Task LoadVersionHistory_MapsVersionsNewestFirst_AndFlagsPublished()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var saved = await source.SaveDraftAsync(ReadyApp());
        await source.PublishAsync(saved.State!);
        // Reopen + republish to create a second immutable version.
        var reopened = await source.ReopenAsync(client.ItemId, client.LastVersionId);
        await source.PublishAsync(reopened.State!);

        var history = await source.LoadVersionHistoryAsync(client.ItemId);

        Assert.Null(history.Issue);
        Assert.Equal(2, history.Versions.Count);
        Assert.Equal(2, history.Versions[0].VersionNumber);
        Assert.True(history.Versions[0].IsPublished);
        Assert.False(history.Versions[1].IsPublished);
    }

    [Fact]
    public async Task Rollback_RepointsPublishedPointer_ToTargetVersion()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var saved = await source.SaveDraftAsync(ReadyApp());
        await source.PublishAsync(saved.State!);
        var firstVersion = client.LastVersionId;
        var reopened = await source.ReopenAsync(client.ItemId, firstVersion);
        await source.PublishAsync(reopened.State!);

        var rollback = await source.RollbackAsync(client.ItemId, firstVersion, "Revert regression");

        Assert.True(rollback.Succeeded);
        Assert.Equal(StudioRollbackPointer.Published, client.LastRollback!.Target);
        Assert.Equal(firstVersion, client.LastRollback.TargetVersionId);
        Assert.Equal("Revert regression", client.LastRollback.Reason);
    }

    [Fact]
    public async Task Preview_BeforeSave_DoesNotCallServer_AndReportsActionableMessage()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var result = await source.PreviewAsync(ReadyApp());

        Assert.False(result.Succeeded);
        Assert.Equal(0, client.PreviewCount);
        Assert.Contains("Save", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_OnSavedDraft_ReturnsServerPreviewPlanSteps()
    {
        var client = new RecordingAppLifecycleClient();
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var saved = await source.SaveDraftAsync(ReadyApp());
        var result = await source.PreviewAsync(saved.State!);

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.PreviewCount);
        Assert.Contains("validate-envelope", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndpointIssue_OnSave_SurfacesCapabilityStateInsteadOfThrowing()
    {
        var client = new RecordingAppLifecycleClient { FailCreateWith = 403 };
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var result = await source.SaveDraftAsync(ReadyApp());

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Issue);
        Assert.Equal("App builder", result.Issue!.Surface);
        Assert.Equal("Missing permission", result.Issue.State);
    }

    [Fact]
    public async Task LoadVersionHistory_EndpointIssue_SurfacesCapabilityState()
    {
        var client = new RecordingAppLifecycleClient { FailListVersionsWith = 404 };
        var source = new HonuaServerStudioAppPackageDataSource(client);

        var history = await source.LoadVersionHistoryAsync(Guid.NewGuid());

        Assert.False(history.HasVersions);
        Assert.NotNull(history.Issue);
        Assert.Equal("App builder", history.Issue!.Surface);
        Assert.Equal("Unsupported", history.Issue.State);
    }

    private static StudioAppEditorState ReadyApp()
    {
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "Field operations";
        state.Summary = "Permit inspections";
        state.Pages[0].Title = "Permits";
        state.Pages[0].ContentBinding = "content:permits@v3";
        state.Actions.Add(new StudioAppActionState { Name = "submit", PageRoute = "/", RequiredPermission = "operator" });
        state.Visibility = "organization";
        state.EmbedEnabled = true;
        state.ShareEmbedPolicyReviewed = true;
        return state;
    }

    /// <summary>
    /// A stateful in-test double for the shipped honua-server Studio package lifecycle contract. It mimics
    /// the real server semantics needed for the app lifecycle: drafts hold an envelope body, content
    /// versions are immutable and number monotonically, reopen clones the version body into a new draft
    /// generation with no published pointer, and version listing returns the immutable history.
    /// </summary>
    private sealed class RecordingAppLifecycleClient : IStudioPackageLifecycleClient
    {
        private readonly Dictionary<Guid, StudioPackageDraft> _drafts = new();
        private readonly List<StudioContentVersion> _versions = [];

        public Uri BaseUri { get; } = new("https://honua.test");

        public Guid ItemId { get; private set; } = Guid.NewGuid();

        public Guid LastVersionId { get; private set; }

        public int CreateDraftCount { get; private set; }

        public int SaveVersionCount { get; private set; }

        public int PublishCount { get; private set; }

        public int PreviewCount { get; private set; }

        public int? FailCreateWith { get; init; }

        public int? FailListVersionsWith { get; init; }

        public StudioPublicationIntent? LastPublishIntent { get; private set; }

        public CreateStudioRollbackRequest? LastRollback { get; private set; }

        public Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioPackageFamilyCapabilities>.FromData(
                new StudioPackageFamilyCapabilities()));

        public Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
            CreateStudioPackageDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateDraftCount++;
            if (FailCreateWith is { } status)
            {
                return Task.FromResult(Issue<StudioPackageDraft>(status, "POST /api/v1/studio/package-drafts"));
            }

            var draft = new StudioPackageDraft
            {
                DraftId = Guid.NewGuid(),
                ItemId = ItemId,
                PackageKey = request.PackageKey,
                Family = request.Envelope.Family,
                Envelope = request.Envelope,
                Generation = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _drafts[draft.DraftId] = draft;
            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(draft));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_drafts.TryGetValue(draftId, out var draft)
                ? StudioEndpointResult<StudioPackageDraft>.FromData(draft)
                : Issue<StudioPackageDraft>(404, "GET /api/v1/studio/package-drafts/{draftId}"));

        public Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
            Guid draftId,
            UpdateStudioPackageDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_drafts.TryGetValue(draftId, out var draft))
            {
                return Task.FromResult(Issue<StudioPackageDraft>(404, "PUT /api/v1/studio/package-drafts/{draftId}"));
            }

            draft = draft with
            {
                Envelope = request.Envelope,
                PackageKey = request.PackageKey,
                Generation = draft.Generation + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _drafts[draftId] = draft;
            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(draft));
        }

        public Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
            Guid draftId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioEndpointResult<StudioValidationSummary>.FromData(new StudioValidationSummary
            {
                Status = StudioPackageValidationStatus.Valid,
                GeneratedAt = DateTimeOffset.UtcNow
            }));

        public Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
            Guid draftId,
            CancellationToken cancellationToken = default)
        {
            PreviewCount++;
            return Task.FromResult(StudioEndpointResult<StudioPreviewPlan>.FromData(new StudioPreviewPlan
            {
                DraftId = draftId,
                Family = Honua.Console.Contracts.StudioPackageFamily.App,
                Synchronous = true,
                Steps = ["validate-envelope", "prepare-inline-preview"],
                Validation = new StudioValidationSummary { Status = StudioPackageValidationStatus.Valid }
            }));
        }

        public Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
            Guid draftId,
            SaveStudioContentVersionRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveVersionCount++;
            if (!_drafts.TryGetValue(draftId, out var draft))
            {
                return Task.FromResult(Issue<StudioContentVersion>(404, "POST .../content-versions"));
            }

            var version = new StudioContentVersion
            {
                ItemId = ItemId,
                PackageKey = draft.PackageKey,
                VersionId = Guid.NewGuid(),
                VersionNumber = _versions.Count + 1,
                ContentHash = "sha256:test",
                Envelope = draft.Envelope,
                ChangeNote = request.ChangeNote,
                SourceDraftId = draftId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _versions.Add(version);
            LastVersionId = version.VersionId;
            return Task.FromResult(StudioEndpointResult<StudioContentVersion>.FromData(version));
        }

        public Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
            Guid itemId,
            CancellationToken cancellationToken = default)
        {
            if (FailListVersionsWith is { } status)
            {
                return Task.FromResult(Issue<StudioContentVersionListResponse>(status, "GET .../versions"));
            }

            return Task.FromResult(StudioEndpointResult<StudioContentVersionListResponse>.FromData(
                new StudioContentVersionListResponse { ItemId = itemId, Versions = _versions.ToArray() }));
        }

        public Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default)
        {
            var version = _versions.FirstOrDefault(candidate => candidate.VersionId == versionId);
            return Task.FromResult(version is null
                ? Issue<StudioContentVersion>(404, "GET .../versions/{versionId}")
                : StudioEndpointResult<StudioContentVersion>.FromData(version));
        }

        public Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
            Guid itemId,
            Guid versionId,
            CreateStudioPublicationRequest request,
            CancellationToken cancellationToken = default)
        {
            PublishCount++;
            LastPublishIntent = request.Intent;
            return Task.FromResult(StudioEndpointResult<StudioPublicationRequest>.FromData(new StudioPublicationRequest
            {
                RequestId = Guid.NewGuid(),
                ItemId = itemId,
                VersionId = versionId,
                Intent = request.Intent,
                Status = StudioPublicationRequestStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow
            }));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default)
        {
            var version = _versions.FirstOrDefault(candidate => candidate.VersionId == versionId);
            if (version is null)
            {
                return Task.FromResult(Issue<StudioPackageDraft>(404, "POST .../reopen"));
            }

            // The server clones the immutable version body into a new editable draft generation; it carries
            // a baseVersionId but no published pointer.
            var draft = new StudioPackageDraft
            {
                DraftId = Guid.NewGuid(),
                ItemId = itemId,
                PackageKey = version.PackageKey,
                Family = version.Envelope.Family,
                Envelope = version.Envelope,
                BaseVersionId = version.VersionId,
                Generation = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _drafts[draft.DraftId] = draft;
            return Task.FromResult(StudioEndpointResult<StudioPackageDraft>.FromData(draft));
        }

        public Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
            Guid itemId,
            Guid versionId,
            CancellationToken cancellationToken = default) =>
            ReopenVersionAsync(itemId, versionId, cancellationToken);

        public Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(
            Guid itemId,
            CreateStudioRollbackRequest request,
            CancellationToken cancellationToken = default) =>
            CreateRollbackRequestAsync(itemId, request, cancellationToken);

        public Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(
            Guid itemId,
            CreateStudioRollbackRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRollback = request;
            return Task.FromResult(StudioEndpointResult<StudioRollbackRequest>.FromData(new StudioRollbackRequest
            {
                RequestId = Guid.NewGuid(),
                ItemId = itemId,
                TargetVersionId = request.TargetVersionId,
                Target = request.Target,
                Pointers = new StudioContentItemPointers { ItemId = itemId, PublishedVersionId = request.TargetVersionId },
                Reason = request.Reason,
                CreatedAt = DateTimeOffset.UtcNow
            }));
        }

        private static StudioEndpointResult<T> Issue<T>(int status, string contract)
        {
            var state = status switch
            {
                401 or 403 => "Missing permission",
                404 or 405 or 501 => "Unsupported",
                409 => "Conflict",
                _ => "Unavailable"
            };
            return StudioEndpointResult<T>.FromIssue(new StudioEndpointIssue(state, contract, $"HTTP {status}.", status));
        }
    }
}

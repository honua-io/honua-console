using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the form-builder data sources: the server-backed source maps live shim
/// results and surfaces endpoint issues as capability states (never throwing or fabricating), enforces
/// the pre-publish gate before calling the server, and the unsupported source renders a missing-binding
/// state instead of mock data (Console Patterns Charter section 11).
/// </summary>
public sealed class StudioFormPackageDataSourceTests
{
    [Fact]
    public async Task GetWorkspace_MapsSummaries_NewestFirst()
    {
        var client = new FakeFormPackageClient
        {
            Packages = HonuaAdminEndpointResult<HonuaFormPackageSummary[]>.FromData(
            [
                new HonuaFormPackageSummary { FormId = "older", Title = "Older", UpdatedAt = DateTimeOffset.UnixEpoch },
                new HonuaFormPackageSummary { FormId = "newer", Title = "Newer", UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1) }
            ])
        };
        var source = new HonuaServerStudioFormPackageDataSource(client);

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.CapabilityStates);
        Assert.Equal(["Newer", "Older"], workspace.Packages.Select(package => package.Title));
    }

    [Fact]
    public async Task GetWorkspace_MapsEndpointIssue_ToCapabilityState()
    {
        var client = new FakeFormPackageClient
        {
            Packages = HonuaAdminEndpointResult<HonuaFormPackageSummary[]>.FromIssue(
                new HonuaAdminEndpointIssue("Missing permission", "GET /api/v1/admin/forms/packages", "Forbidden.", 403))
        };
        var source = new HonuaServerStudioFormPackageDataSource(client);

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Form builder", state.Surface);
        Assert.Equal("Missing permission", state.State);
        Assert.Contains("HTTP 403", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_WithNullFormId_ReturnsBlankTemplate_WithoutServerCall()
    {
        var client = new FakeFormPackageClient();
        var source = new HonuaServerStudioFormPackageDataSource(client);

        var load = await source.LoadAsync(null);

        Assert.True(load.HasEditor);
        Assert.Null(load.State!.FormId);
        Assert.False(client.GetCurrentCalled);
    }

    [Fact]
    public async Task Publish_WhenGateUnmet_DoesNotCallServer()
    {
        var client = new FakeFormPackageClient();
        var source = new HonuaServerStudioFormPackageDataSource(client);
        var state = new StudioFormEditorState { FormId = "form-1", Title = "Incomplete" };

        var result = await source.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.False(client.PublishCalled);
        Assert.Contains("before publish", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_WhenAlreadyPublished_DoesNotCallServer()
    {
        var client = new FakeFormPackageClient();
        var source = new HonuaServerStudioFormPackageDataSource(client);

        // A published version is terminal: even with every other publish requirement met (title, field,
        // submit target, reviewed offline policy, valid server validation, no unsaved edits), publish must
        // not reach the server because publish only applies to the open draft — the only action is Reopen.
        var state = new StudioFormEditorState
        {
            FormId = "form-1",
            Version = 3,
            Status = HonuaFormStatuses.Published,
            Title = "Published form",
            ServiceId = "svc",
            OfflinePolicyReviewed = true,
            LastValidation = new StudioFormValidationView(true, [])
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "f1", Label = "Field 1" });
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);

        var result = await source.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.False(client.PublishCalled);
        Assert.Contains("Reopen", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publish_WhenServerReturnsValidationFailure_SurfacesValidationIssues()
    {
        var client = new FakeFormPackageClient
        {
            PublishResult = new HonuaAdminEndpointResult<HonuaFormPackageVersion>(
                new HonuaFormPackageVersion
                {
                    FormId = "form-1",
                    Version = 2,
                    Validation = new HonuaFormPackageValidationResult
                    {
                        IsValid = false,
                        Issues =
                        [
                            new HonuaFormValidationIssue
                            {
                                Code = "target.missing",
                                Severity = "error",
                                FieldId = "asset_id",
                                Message = "Target field asset_id is missing."
                            }
                        ]
                    }
                },
                new HonuaAdminEndpointIssue(
                    "Rejected",
                    "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/publish",
                    "The Honua server rejected the form package document.",
                    400))
        };
        var source = new HonuaServerStudioFormPackageDataSource(client);
        var state = new StudioFormEditorState
        {
            FormId = "form-1",
            Version = 2,
            Status = HonuaFormStatuses.Draft,
            Title = "Ready form",
            ServiceId = "svc",
            OfflinePolicyReviewed = true,
            LastValidation = new StudioFormValidationView(true, [])
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset", TargetField = "asset_id" });
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);

        var result = await source.PublishAsync(state);

        Assert.False(result.Succeeded);
        Assert.True(client.PublishCalled);
        Assert.NotNull(result.Validation);
        Assert.False(result.Validation!.IsValid);
        Assert.Equal("target.missing", Assert.Single(result.Validation.Issues).Code);
        Assert.Same(state, result.State);
        Assert.Equal(result.Validation, state.LastValidation);
        Assert.Equal("Rejected", result.Issue!.State);
    }

    [Fact]
    public async Task Validate_WhenAlreadyPublished_DoesNotCallServer()
    {
        var client = new FakeFormPackageClient();
        var source = new HonuaServerStudioFormPackageDataSource(client);
        var state = new StudioFormEditorState { FormId = "form-1", Version = 3, Status = HonuaFormStatuses.Published };

        var result = await source.ValidateAsync(state);

        Assert.False(result.Succeeded);
        Assert.Equal(0, client.ValidateCallCount);
        Assert.Contains("Reopen", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveDraft_NewForm_CreatesDraft_AndPreservesOfflineReview()
    {
        var client = new FakeFormPackageClient
        {
            CreateResult = HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion
            {
                FormId = "form-9",
                Version = 1,
                Status = HonuaFormPackageStatus.Draft,
                ETag = "etag-1"
            })
        };
        var source = new HonuaServerStudioFormPackageDataSource(client);
        var state = new StudioFormEditorState { Title = "Fresh", OfflinePolicyReviewed = true };

        var result = await source.SaveDraftAsync(state);

        Assert.True(result.Succeeded);
        Assert.True(client.CreateCalled);
        Assert.False(client.UpdateCalled);
        Assert.Equal("form-9", result.State!.FormId);
        Assert.True(result.State.OfflinePolicyReviewed);
        Assert.Null(result.State.LastValidation);
    }

    [Fact]
    public async Task Validate_WhenEditedSinceSave_DoesNotCallServer()
    {
        var client = new FakeFormPackageClient();
        var source = new HonuaServerStudioFormPackageDataSource(client);

        // A draft loaded from the server carries its saved baseline, so it is not dirty and validates.
        var load = await source.LoadAsync("form-1");
        var state = load.State!;
        var clean = await source.ValidateAsync(state);
        Assert.True(clean.Succeeded);
        Assert.Equal(1, client.ValidateCallCount);

        // A local edit after the save must block server validation (it runs against the saved version).
        state.Title = "Edited after save";
        var dirty = await source.ValidateAsync(state);

        Assert.False(dirty.Succeeded);
        Assert.Equal(1, client.ValidateCallCount);
        Assert.Contains("Save your latest edits", dirty.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unsupported_Source_SurfacesMissingBinding_Everywhere()
    {
        var source = new UnsupportedStudioFormPackageDataSource();

        var workspace = await source.GetWorkspaceAsync();
        var load = await source.LoadAsync("form-1");
        var save = await source.SaveDraftAsync(new StudioFormEditorState());

        Assert.Equal("Missing binding", Assert.Single(workspace.CapabilityStates).State);
        Assert.False(load.HasEditor);
        Assert.Equal("Missing binding", Assert.Single(load.CapabilityStates).State);
        Assert.False(save.Succeeded);
        Assert.Equal("Missing binding", save.Issue!.State);
    }

    private sealed class FakeFormPackageClient : IHonuaFormPackageClient
    {
        public Task<HonuaAdminEndpointResult<HonuaFormGenerationProviders>> ListGenerationProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaFormGenerationProviders>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "GET providers", "Not exercised by this fake.")));

        public Task<HonuaAdminEndpointResult<HonuaFormGenerationResult>> GenerateFormAsync(GenerateFormPackageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaFormGenerationResult>.FromIssue(
                new HonuaAdminEndpointIssue("Unsupported", "POST generate", "Not exercised by this fake.")));

        public Uri BaseUri { get; } = new("https://honua.test");

        public HonuaAdminEndpointResult<HonuaFormPackageSummary[]> Packages { get; set; } =
            HonuaAdminEndpointResult<HonuaFormPackageSummary[]>.FromData([]);

        public HonuaAdminEndpointResult<HonuaFormPackageVersion> CreateResult { get; set; } =
            HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion());

        public HonuaAdminEndpointResult<HonuaFormPackageVersion> PublishResult { get; set; } =
            HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion
            {
                FormId = "form-1",
                Version = 1,
                Status = HonuaFormPackageStatus.Published
            });

        public bool GetCurrentCalled { get; private set; }

        public bool CreateCalled { get; private set; }

        public bool UpdateCalled { get; private set; }

        public bool PublishCalled { get; private set; }

        public int ValidateCallCount { get; private set; }

        public Task<HonuaAdminEndpointResult<HonuaFormPackageSummary[]>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages);

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> CreateDraftAsync(HonuaFormPackageDocument document, CancellationToken cancellationToken = default)
        {
            CreateCalled = true;
            return Task.FromResult(CreateResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> GetCurrentAsync(string formId, CancellationToken cancellationToken = default)
        {
            GetCurrentCalled = true;
            return Task.FromResult(HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion { FormId = formId }));
        }

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion[]>> ListVersionsAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaFormPackageVersion[]>.FromData([]));

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> GetVersionAsync(string formId, int packageVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion { FormId = formId, Version = packageVersion }));

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> UpdateDraftAsync(string formId, int packageVersion, string etag, HonuaFormPackageDocument document, CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            return Task.FromResult(HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion { FormId = formId, Version = packageVersion, ETag = etag }));
        }

        public Task<HonuaAdminEndpointResult<HonuaFormPackageValidationResult>> ValidateAsync(string formId, int packageVersion, CancellationToken cancellationToken = default)
        {
            ValidateCallCount++;
            return Task.FromResult(HonuaAdminEndpointResult<HonuaFormPackageValidationResult>.FromData(new HonuaFormPackageValidationResult { IsValid = true }));
        }

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> PublishAsync(string formId, int packageVersion, CancellationToken cancellationToken = default)
        {
            PublishCalled = true;
            return Task.FromResult(PublishResult);
        }

        public Task<HonuaAdminEndpointResult<HonuaFormPackageVersion>> ReopenAsync(string formId, int packageVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaFormPackageVersion>.FromData(new HonuaFormPackageVersion { FormId = formId, Version = packageVersion + 1 }));

        public Task<HonuaAdminEndpointResult<HonuaFormOfflinePolicyResponse>> GetOfflinePolicyAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaFormOfflinePolicyResponse>.FromData(new HonuaFormOfflinePolicyResponse { FormId = formId }));
    }
}

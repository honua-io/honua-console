using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio form-builder data source bound to the real honua-server form package lifecycle
/// (honua-server#1184) through the <see cref="IHonuaFormPackageClient"/> shim. Every read and
/// mutating command speaks the live admin lifecycle (<c>/api/v1/admin/forms/packages</c>) and the
/// runtime offline-policy contract (<c>/api/v1/forms/packages/{formId}/offline-policy</c>); there is
/// no in-memory form data in the merged result (Console Patterns Charter section 11). Endpoint issues
/// (missing permission, unsupported verb, conflict, transport) surface as explicit capability states
/// instead of throwing or fabricating data.
/// </summary>
public sealed class HonuaServerStudioFormPackageDataSource : IStudioFormPackageDataSource
{
    private const string Surface = "Form builder";
    private const string ListContract = "GET /api/v1/admin/forms/packages";
    private const string LoadContract = "GET /api/v1/admin/forms/packages/{formId}";
    private const string SaveContract = "POST/PUT /api/v1/admin/forms/packages";
    private const string ValidateContract = "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/validate";
    private const string PublishContract = "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/publish";
    private const string ReopenContract = "POST /api/v1/admin/forms/packages/{formId}/versions/{version}/reopen";
    private const string OfflineContract = "GET /api/v1/forms/packages/{formId}/offline-policy";

    private readonly IHonuaFormPackageClient _client;

    public HonuaServerStudioFormPackageDataSource(IHonuaFormPackageClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.ListPackagesAsync(cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioFormWorkspace([], [ToCapabilityState(ListContract, issue)]);
        }

        var packages = (result.Data ?? [])
            .Select(StudioFormPackageMapper.ToListItem)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new StudioFormWorkspace(packages, []);
    }

    public async Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default)
    {
        // A brand-new form opens a blank Console-owned authoring scaffold, not server data. Existing
        // packages always load their current version from the live server.
        if (string.IsNullOrWhiteSpace(formId))
        {
            return new StudioFormEditorLoad(StudioFormPackageMapper.CreateTemplate(), []);
        }

        var result = await _client.GetCurrentAsync(formId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioFormEditorLoad(null, [ToCapabilityState(LoadContract, issue)]);
        }

        return new StudioFormEditorLoad(StudioFormPackageMapper.ToEditorState(result.Data!), []);
    }

    public async Task<StudioFormCommandResult> SaveDraftAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsPublished)
        {
            return Failure("This version is published. Reopen it as a draft before editing.");
        }

        var document = StudioFormPackageMapper.ToDocument(state);
        HonuaAdminEndpointResult<HonuaFormPackageVersion> result;
        if (state.IsExistingPackage && !string.IsNullOrWhiteSpace(state.ETag))
        {
            result = await _client
                .UpdateDraftAsync(state.FormId!, state.Version, state.ETag, document, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _client.CreateDraftAsync(document, cancellationToken).ConfigureAwait(false);
        }

        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(SaveContract, issue));
        }

        var saved = StudioFormPackageMapper.ToEditorState(result.Data!);
        // Editing the draft invalidates any earlier server validation, but the operator's offline-policy
        // review is a session decision that should survive a save so the publish gate is not reset.
        saved.LastValidation = null;
        saved.OfflinePolicyReviewed = state.OfflinePolicyReviewed;

        return new StudioFormCommandResult(
            true,
            $"Saved draft version {saved.Version.ToString(CultureInfo.InvariantCulture)}. Validate before publishing.",
            saved);
    }

    public async Task<StudioFormCommandResult> ValidateAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsExistingPackage)
        {
            return Failure("Save the draft before running server validation.");
        }

        var result = await _client
            .ValidateAsync(state.FormId!, state.Version, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(ValidateContract, issue));
        }

        var validation = StudioFormPackageMapper.ToValidationView(result.Data);
        state.LastValidation = validation;

        var message = validation is { IsValid: true }
            ? "Server validation passed."
            : $"Server validation reported {validation?.Issues.Count ?? 0} issue(s).";

        return new StudioFormCommandResult(true, message, state, validation);
    }

    public async Task<StudioFormCommandResult> PublishAsync(
        StudioFormEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // AC#2/AC#3: enforce the explicit offline-policy review and a configured + validated submit
        // target before the Console offers publish to the server.
        var readiness = StudioFormPublishEvaluator.Evaluate(state);
        if (!readiness.CanPublish)
        {
            return Failure(
                $"Resolve {readiness.UnmetRequirements.Count.ToString(CultureInfo.InvariantCulture)} requirement(s) before publish: "
                + string.Join(" ", readiness.UnmetRequirements));
        }

        var result = await _client
            .PublishAsync(state.FormId!, state.Version, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(PublishContract, issue));
        }

        var published = StudioFormPackageMapper.ToEditorState(result.Data!);
        published.OfflinePolicyReviewed = true;
        return new StudioFormCommandResult(
            true,
            $"Published version {published.Version.ToString(CultureInfo.InvariantCulture)}.",
            published);
    }

    public async Task<StudioFormCommandResult> ReopenAsync(
        string formId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        var result = await _client.ReopenAsync(formId, version, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(ReopenContract, issue));
        }

        var draft = StudioFormPackageMapper.ToEditorState(result.Data!);
        return new StudioFormCommandResult(
            true,
            $"Reopened version {version.ToString(CultureInfo.InvariantCulture)} as draft version {draft.Version.ToString(CultureInfo.InvariantCulture)}.",
            draft);
    }

    public async Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formId);

        var result = await _client.GetOfflinePolicyAsync(formId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioFormOfflinePolicyView(false, [], ToCapabilityState(OfflineContract, issue));
        }

        var policy = result.Data!;
        return new StudioFormOfflinePolicyView(policy.Enabled, policy.AvailableTransports);
    }

    private static StudioFormCommandResult Failure(string message, StudioFormCapabilityState? issue = null) =>
        new(false, message, Issue: issue);

    private static StudioFormCapabilityState ToCapabilityState(string contract, HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract ?? contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}

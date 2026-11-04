using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A publication that is not a report is unsupported, so the builder does not render another artifact kind.
public sealed class HonuaServerStudioReportPublicationDataSource : IStudioReportPublicationDataSource
{
    private const string Surface = "Report builder";
    private const string GetContract = "GET /api/v1/console/publications/{publicationId}";
    private const string PublishContract = "POST /api/v1/console/publications";
    private const string RepublishContract = "POST /api/v1/console/publications/{publicationId}/republish";
    private const string RollbackContract = "POST /api/v1/console/publications/{publicationId}/rollback";
    private const string PolicyContract = "PATCH /api/v1/console/publications/{publicationId}/policy";

    private readonly IHonuaContentPublicationClient _client;

    public HonuaServerStudioReportPublicationDataSource(IHonuaContentPublicationClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioReportPublicationLoad> LoadAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        var result = await _client.GetAsync(publicationId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioReportPublicationLoad(null, [ToCapabilityState(GetContract, issue)]);
        }

        var detail = result.Data!;
        // Route is non-null-typed with a new() initializer but deserializes to null on an explicit server
        // JSON null, so coalesce before reading Kind.
        var route = detail.Route ?? new();
        if (!string.Equals(route.Kind, HonuaContentPublicationKinds.Report, StringComparison.Ordinal))
        {
            return new StudioReportPublicationLoad(
                null,
                [
                    new StudioReportCapabilityState(
                        Surface,
                        "Unsupported",
                        GetContract,
                        $"Publication '{publicationId}' is a '{route.Kind}' artifact, not a report. Open it in the matching Studio editor.")
                ]);
        }

        return new StudioReportPublicationLoad(StudioReportPublicationMapper.ToView(detail), []);
    }

    public async Task<StudioReportCommandResult> PublishAsync(
        StudioReportEditorState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var readiness = StudioReportPublishEvaluator.Evaluate(state);
        if (!readiness.CanPublish)
        {
            return new StudioReportCommandResult(
                false,
                $"Resolve before publish: {string.Join(" ", readiness.UnmetRequirements)}");
        }

        var payload = StudioReportDocument.Serialize(state);
        var policy = BuildPolicy(state.Visibility, state.Embeddable);

        // No publication id yet => first publish claims a new server-owned route. An existing publication
        // id => republish a new immutable version and advance the active pointer (version pinning keeps the
        // earlier versions immutable for rollback).
        if (!state.IsPublished)
        {
            var publishResult = await _client.PublishAsync(
                    new HonuaPublishContentRequest
                    {
                        Kind = HonuaContentPublicationKinds.Report,
                        RouteSlug = string.IsNullOrWhiteSpace(state.RouteSlug) ? null : state.RouteSlug,
                        Title = state.Title,
                        ContentPayload = payload,
                        Policy = policy
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return ToCommandResult(PublishContract, publishResult, "Published report");
        }

        var republishResult = await _client.RepublishAsync(
                state.PublicationId!,
                new HonuaRepublishContentRequest
                {
                    Title = state.Title,
                    ContentPayload = payload,
                    ExpectedEtag = string.IsNullOrWhiteSpace(state.ETag) ? null : state.ETag
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ToCommandResult(RepublishContract, republishResult, "Republished report");
    }

    public async Task<StudioReportCommandResult> RollbackAsync(
        string publicationId,
        string targetVersionId,
        string? expectedEtag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersionId);

        var result = await _client.RollbackAsync(
                publicationId,
                new HonuaRollbackContentRequest
                {
                    TargetVersionId = targetVersionId,
                    ExpectedEtag = string.IsNullOrWhiteSpace(expectedEtag) ? null : expectedEtag
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ToCommandResult(RollbackContract, result, $"Rolled back to {targetVersionId}");
    }

    public async Task<StudioReportCommandResult> UpdatePolicyAsync(
        string publicationId,
        string visibility,
        bool embeddable,
        string? expectedEtag = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        var result = await _client.UpdatePolicyAsync(
                publicationId,
                new HonuaUpdatePublicationPolicyRequest
                {
                    Visibility = NormalizeVisibility(visibility),
                    Embed = new HonuaContentEmbedPolicy { AllowEmbedding = embeddable },
                    ExpectedEtag = string.IsNullOrWhiteSpace(expectedEtag) ? null : expectedEtag
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Issue is { } issue)
        {
            return new StudioReportCommandResult(
                false,
                issue.Detail,
                Issue: ToCapabilityState(PolicyContract, issue),
                FieldErrors: issue.FieldErrors);
        }

        // The policy update returns the route only; re-read the detail so the version history panel stays
        // consistent after a visibility/embed change.
        var refreshed = await LoadAsync(publicationId, cancellationToken).ConfigureAwait(false);
        var policy = (result.Data!.Route ?? new()).Policy;
        return new StudioReportCommandResult(
            true,
            $"Updated visibility to {policy?.Visibility ?? HonuaContentPublicationVisibilities.Private} (embed {(policy?.Embed?.AllowEmbedding == true ? "on" : "off")}).",
            refreshed.Publication);
    }

    private const string GenProvidersContract = "GET /api/v1/console/publications/generation/providers";
    private const string GenerateContract = "POST /api/v1/console/publications/generate";

    private static HonuaContentPublicationPolicy BuildPolicy(string visibility, bool embeddable) =>
        new()
        {
            Visibility = NormalizeVisibility(visibility),
            Embed = new HonuaContentEmbedPolicy { AllowEmbedding = embeddable }
        };

    private static string NormalizeVisibility(string? visibility) =>
        string.IsNullOrWhiteSpace(visibility)
            ? HonuaContentPublicationVisibilities.Private
            : visibility.Trim().ToLowerInvariant();

    private StudioReportCommandResult ToCommandResult(
        string contract,
        HonuaAdminEndpointResult<HonuaContentPublicationDetail> result,
        string successPrefix)
    {
        if (result.Issue is { } issue)
        {
            return new StudioReportCommandResult(
                false,
                issue.Detail,
                Issue: ToCapabilityState(contract, issue),
                FieldErrors: issue.FieldErrors);
        }

        var detail = result.Data!;
        var route = detail.Route ?? new();
        if (!string.Equals(route.Kind, HonuaContentPublicationKinds.Report, StringComparison.Ordinal))
        {
            return new StudioReportCommandResult(
                false,
                $"Publication '{route.PublicationId}' is a '{route.Kind}' artifact, not a report.",
                Issue: new StudioReportCapabilityState(Surface, "Unsupported", contract, "Not a report artifact."));
        }

        var view = StudioReportPublicationMapper.ToView(detail);
        return new StudioReportCommandResult(
            true,
            $"{successPrefix} ({view.PublicationId} · r{view.ActiveRevision}).",
            view);
    }

    private static StudioReportCapabilityState ToCapabilityState(string contract, HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract ?? contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}

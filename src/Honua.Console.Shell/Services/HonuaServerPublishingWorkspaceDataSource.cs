using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Publishing workspace data source bound to the real honua-server content publication registry
/// (honua-server#1183) through the <see cref="IHonuaContentPublicationClient"/> shim. The matrix and
/// review surface are projected from live <c>/api/v1/console/publications/{publicationId}</c> reads —
/// there is no in-memory publishing data in the merged result (Console Patterns Charter section 11).
///
/// The registry is keyed by publication id (it exposes no list endpoint), so the workspace matrix is
/// composed from the publication ids configured for the active deployment
/// (<c>Honua:Server:PublicationIds</c>); when none are configured the workspace renders an explicit
/// capability state rather than fabricating rows. Lookup, republish, and rollback drive the
/// author-first / quick-publish flow against the live lifecycle verbs. Endpoint issues (missing
/// permission, not found, conflict, unsupported verb, transport) surface as explicit capability states
/// instead of throwing or fabricating data.
/// </summary>
public sealed class HonuaServerPublishingWorkspaceDataSource : IPublishingWorkspaceDataSource
{
    private const string Surface = "Publishing";
    private const string GetContract = "GET /api/v1/console/publications/{publicationId}";
    private const string RepublishContract = "POST /api/v1/console/publications/{publicationId}/republish";
    private const string RollbackContract = "POST /api/v1/console/publications/{publicationId}/rollback";

    private readonly IHonuaContentPublicationClient _client;
    private readonly IReadOnlyList<string> _publicationIds;

    public HonuaServerPublishingWorkspaceDataSource(
        IHonuaContentPublicationClient client,
        PublishingWorkspaceOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(options);
        _publicationIds = options.PublicationIds;
    }

    public async Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (_publicationIds.Count == 0)
        {
            return new PublishingWorkspace(
                Matrix: [],
                Reviews: [],
                CapabilityStates:
                [
                    new OperateCapabilityState(
                        Surface,
                        "Not configured",
                        "Honua:Server:PublicationIds",
                        "The publication registry (honua-server#1183) is bound, but no publication ids are "
                        + "configured for this deployment. Set Honua:Server:PublicationIds (or "
                        + "HONUA_SERVER_PUBLICATION_IDS) to the comma-separated publication ids the workspace "
                        + "should track.")
                ]);
        }

        var matrix = new List<PublishingMatrixRow>();
        var reviews = new List<PublishingReview>();
        var states = new List<OperateCapabilityState>();

        foreach (var publicationId in _publicationIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _client.GetAsync(publicationId, cancellationToken).ConfigureAwait(false);
            if (result.Issue is { } issue)
            {
                states.Add(ToCapabilityState(GetContract, issue, publicationId));
                continue;
            }

            var detail = result.Data!;
            matrix.Add(PublishingWorkspaceMapper.ToMatrixRow(detail));
            reviews.Add(PublishingWorkspaceMapper.ToReview(detail));
        }

        return new PublishingWorkspace(matrix, reviews, states);
    }

    public async Task<PublishingLookupResult> LookupAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        var result = await _client.GetAsync(publicationId, cancellationToken).ConfigureAwait(false);
        return result.Issue is { } issue
            ? PublishingLookupResult.FromCapabilityState(ToCapabilityState(GetContract, issue, publicationId))
            : ToLookupResult(result.Data!);
    }

    public async Task<PublishingLookupResult> RepublishAsync(
        string publicationId,
        PublishingRepublishCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(command);

        var request = new HonuaRepublishContentRequest
        {
            Title = command.Title,
            ContentHash = command.ContentHash,
            ExpectedEtag = command.ExpectedEtag
        };

        var result = await _client.RepublishAsync(publicationId, request, cancellationToken).ConfigureAwait(false);
        return result.Issue is { } issue
            ? PublishingLookupResult.FromCapabilityState(ToCapabilityState(RepublishContract, issue, publicationId))
            : ToLookupResult(result.Data!);
    }

    public async Task<PublishingLookupResult> RollbackAsync(
        string publicationId,
        PublishingRollbackCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.TargetVersionId) && command.TargetRevision is null)
        {
            return PublishingLookupResult.FromCapabilityState(new OperateCapabilityState(
                Surface,
                "Rejected",
                RollbackContract,
                "A rollback requires a target version id or target revision."));
        }

        var request = new HonuaRollbackContentRequest
        {
            TargetVersionId = command.TargetVersionId,
            TargetRevision = command.TargetRevision,
            ExpectedEtag = command.ExpectedEtag
        };

        var result = await _client.RollbackAsync(publicationId, request, cancellationToken).ConfigureAwait(false);
        return result.Issue is { } issue
            ? PublishingLookupResult.FromCapabilityState(ToCapabilityState(RollbackContract, issue, publicationId))
            : ToLookupResult(result.Data!);
    }

    private static PublishingLookupResult ToLookupResult(HonuaContentPublicationDetail detail) =>
        new(
            PublishingWorkspaceMapper.ToReview(detail),
            PublishingWorkspaceMapper.ToVersions(detail),
            []);

    private static OperateCapabilityState ToCapabilityState(
        string contract,
        HonuaAdminEndpointIssue issue,
        string publicationId)
    {
        var detail = issue.StatusCode is null
            ? issue.Detail
            : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.";

        return new OperateCapabilityState(
            Surface,
            issue.State,
            issue.Contract ?? contract,
            $"Publication '{publicationId}': {detail}");
    }
}

/// <summary>
/// Composition options for the server-bound publishing workspace: the publication ids the matrix
/// tracks for the active deployment. The registry exposes no list endpoint, so the workspace is keyed
/// by the configured ids (comma-separated <c>Honua:Server:PublicationIds</c> /
/// <c>HONUA_SERVER_PUBLICATION_IDS</c>).
/// </summary>
public sealed class PublishingWorkspaceOptions
{
    public PublishingWorkspaceOptions(IReadOnlyList<string> publicationIds)
    {
        ArgumentNullException.ThrowIfNull(publicationIds);
        PublicationIds = publicationIds;
    }

    public IReadOnlyList<string> PublicationIds { get; }

    /// <summary>Parses a comma/semicolon/whitespace-separated publication-id list into trimmed, distinct ids.</summary>
    public static PublishingWorkspaceOptions FromConfiguredList(string? configuredList)
    {
        if (string.IsNullOrWhiteSpace(configuredList))
        {
            return new PublishingWorkspaceOptions([]);
        }

        var ids = configuredList
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new PublishingWorkspaceOptions(ids);
    }
}

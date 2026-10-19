using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Publishing workspace data source bound to the real honua-server content publication registry
/// (honua-server#1183) through the <see cref="IHonuaContentPublicationClient"/> shim. The matrix and
/// review surface are projected from live publication reads — there is no in-memory publishing data
/// in the merged result (Console Patterns Charter section 11).
///
/// The workspace discovers publications from <c>GET /api/v1/console/publications</c> (honua-server#3357).
/// <c>Honua:Server:PublicationIds</c> is an optional filter: when it is empty every listed publication
/// is shown, and when it is set only those ids are kept. Lookup, republish, and rollback still drive
/// the author-first / quick-publish flow against the live lifecycle verbs. Endpoint issues (missing
/// permission, not found, conflict, unsupported verb, transport, incomplete pagination) surface as explicit capability states
/// instead of throwing or fabricating data.
/// </summary>
public sealed class HonuaServerPublishingWorkspaceDataSource : IPublishingWorkspaceDataSource
{
    private const string Surface = "Publishing";
    private const string ListContract = "GET /api/v1/console/publications";
    private const string GetContract = "GET /api/v1/console/publications/{publicationId}";
    private const string RepublishContract = "POST /api/v1/console/publications/{publicationId}/republish";
    private const string RollbackContract = "POST /api/v1/console/publications/{publicationId}/rollback";
    private const int ListPageSize = 100;
    private const int MaxListPages = 100;

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
        var listed = await ListAllAsync(cancellationToken).ConfigureAwait(false);
        if (listed.Issue is { } issue)
        {
            return new PublishingWorkspace(
                Matrix: [],
                Reviews: [],
                CapabilityStates: [ToListCapabilityState(issue)]);
        }

        var details = listed.Items;
        if (_publicationIds.Count > 0)
        {
            var allowed = new HashSet<string>(_publicationIds, StringComparer.Ordinal);
            details = details
                .Where(detail => allowed.Contains(detail.Route?.PublicationId ?? string.Empty))
                .ToArray();
        }

        var matrix = new List<PublishingMatrixRow>(details.Count);
        var reviews = new List<PublishingReview>(details.Count);
        foreach (var detail in details)
        {
            matrix.Add(PublishingWorkspaceMapper.ToMatrixRow(detail));
            reviews.Add(PublishingWorkspaceMapper.ToReview(detail));
        }

        return new PublishingWorkspace(matrix, reviews, []);
    }

    private async Task<(IReadOnlyList<HonuaContentPublicationDetail> Items, HonuaAdminEndpointIssue? Issue)> ListAllAsync(
        CancellationToken cancellationToken)
    {
        var items = new List<HonuaContentPublicationDetail>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < MaxListPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _client.ListAsync(cursor, ListPageSize, lifecycle: null, cancellationToken).ConfigureAwait(false);
            if (result.Issue is { } issue)
            {
                return ([], issue);
            }

            var payload = result.Data;
            if (payload?.Items is { Count: > 0 } pageItems)
            {
                items.AddRange(pageItems);
            }

            var next = payload?.NextCursor;
            if (string.IsNullOrEmpty(next))
            {
                return (items, null);
            }

            if (!seenCursors.Add(next))
            {
                return ([], IncompletePagination("The publication list repeated a cursor."));
            }

            cursor = next;
        }

        return ([], IncompletePagination(
            "The publication list exceeded "
            + MaxListPages.ToString(CultureInfo.InvariantCulture)
            + " pages."));
    }

    private static HonuaAdminEndpointIssue IncompletePagination(string detail) =>
        new("Incomplete pagination", ListContract, detail);

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

    private static OperateCapabilityState ToListCapabilityState(HonuaAdminEndpointIssue issue)
    {
        var detail = issue.StatusCode is null
            ? issue.Detail
            : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.";

        return new OperateCapabilityState(
            Surface,
            issue.State,
            issue.Contract ?? ListContract,
            detail);
    }

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
            $"Publication '{publicationId}': {detail}")
        { StatusCode = issue.StatusCode };
    }
}

/// <summary>
/// Composition options for the server-bound publishing workspace. Publication ids are an optional
/// filter over <c>GET /api/v1/console/publications</c> (comma-separated
/// <c>Honua:Server:PublicationIds</c> / <c>HONUA_SERVER_PUBLICATION_IDS</c>). An empty list shows
/// every publication the registry returns.
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

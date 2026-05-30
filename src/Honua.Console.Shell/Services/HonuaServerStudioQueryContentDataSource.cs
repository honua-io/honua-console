using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio query-builder data source bound to the real honua-server saved-query content/artifacts contract
/// (honua-server#1182, AnalysisContentKind.SavedQuery) through the <see cref="IHonuaAnalysisContentClient"/>
/// shim. Authoring a query creates a server content item/version; preview runs the saved query through the
/// canonical feature-query pipeline and resolves the map/table preview + the downstream binding so the
/// saved query can be reused as input to map/dashboard/report/app/workflow editors (AC#1/AC#3). There is no
/// in-memory query data in the merged result (Console Patterns Charter section 11).
///
/// One capability the issue scopes has NO route in the honua-server#1182 contract and is therefore
/// surfaced as an explicit capability state rather than fabricated:
///   - Saved-query listing: the contract addresses items by id and exposes no list verb, so the workspace
///     cannot enumerate existing queries from live data. New queries and id-addressed loads work; this list
///     binds automatically once honua-server#1182 adds a list route.
/// </summary>
public sealed class HonuaServerStudioQueryContentDataSource : IStudioQueryPackageDataSource
{
    private const string Surface = "Query builder";
    private const string ListContract = "GET /api/v1/analysis/content/items (saved-query list)";
    private const string PreviewContract = "POST /api/v1/analysis/content/items/{itemId}/versions/{contentVersion}/preview";

    private readonly IHonuaAnalysisContentClient _client;

    public HonuaServerStudioQueryContentDataSource(IHonuaAnalysisContentClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        // honua-server#1182 exposes no saved-query list verb. Surface that explicitly instead of mocking a
        // list: operators reach a query by id (deep link / known id) or author a new query.
        var listUnsupported = new StudioQueryCapabilityState(
            Surface,
            "Unsupported",
            ListContract,
            "honua-server does not yet expose a saved-query list endpoint, so existing queries cannot be "
            + "enumerated from live data. Open a known query by id or create a new query. This list binds "
            + "automatically once honua-server#1182 adds a list route.");

        return Task.FromResult(new StudioQueryWorkspace([], [listUnsupported]));
    }

    public async Task<StudioQueryEditorLoad> LoadAsync(string? queryId, CancellationToken cancellationToken = default)
    {
        // A brand-new query opens a blank Console-owned authoring scaffold, not server data. Existing
        // queries always load their latest version from the live server.
        if (string.IsNullOrWhiteSpace(queryId))
        {
            return new StudioQueryEditorLoad(StudioQueryPackageMapper.CreateTemplate(), []);
        }

        var result = await _client
            .GetVersionAsync(queryId, null, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioQueryEditorLoad(null, [ToCapabilityState(issue)]);
        }

        return new StudioQueryEditorLoad(StudioQueryPackageMapper.ToEditorState(result.Data!), []);
    }

    public async Task<StudioQueryCommandResult> SaveAsync(
        StudioQueryEditor query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.LayerId < 0)
        {
            return Failure("Bind a non-negative source layer id before saving the query.");
        }

        var content = StudioQueryPackageMapper.ToSavedQueryContent(query);

        HonuaAdminEndpointResult<HonuaAnalysisContentVersionResponse> result;
        if (query.IsExistingQuery)
        {
            result = await _client
                .CreateVersionAsync(
                    query.QueryId!,
                    new HonuaCreateAnalysisContentVersionRequest
                    {
                        SavedQuery = content,
                        BasedOnVersionId = query.ETag
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            result = await _client
                .CreateItemAsync(
                    new HonuaCreateAnalysisContentItemRequest
                    {
                        Kind = HonuaAnalysisContentKinds.SavedQuery,
                        Name = BuildName(query),
                        Title = string.IsNullOrWhiteSpace(query.Title) ? null : query.Title,
                        SavedQuery = content
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(issue));
        }

        var saved = StudioQueryPackageMapper.ToEditorState(result.Data!);
        // A new immutable version invalidates any prior preview; the operator re-previews the saved query
        // so the map/table preview reflects what was actually saved.
        saved.Preview = null;
        return new StudioQueryCommandResult(
            true,
            $"Saved query version {saved.Version.ToString(CultureInfo.InvariantCulture)}. Preview to map/table or reuse it downstream.",
            saved);
    }

    public async Task<StudioQueryCommandResult> PreviewAsync(
        StudioQueryEditor query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The server preview route addresses a saved item/version, so a query must be saved before it can be
        // previewed against the canonical feature-query pipeline.
        if (!query.IsExistingQuery)
        {
            return Failure("Save the query before previewing it on map/table.");
        }

        var result = await _client
            .PreviewSavedQueryAsync(query.QueryId!, query.Version, query.PreviewLimit, cancellationToken)
            .ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return Failure(issue.Detail, ToCapabilityState(issue));
        }

        query.Preview = StudioQueryPackageMapper.ToPreview(result.Data!);
        var count = query.Preview.TotalCount is { } total
            ? total.ToString("N0", CultureInfo.InvariantCulture)
            : query.Preview.FeatureCount.ToString("N0", CultureInfo.InvariantCulture);
        return new StudioQueryCommandResult(
            true,
            $"Previewed {query.Preview.FeatureCount.ToString("N0", CultureInfo.InvariantCulture)} of {count} feature(s) from the live server.",
            query);
    }

    private static string BuildName(StudioQueryEditor query)
    {
        var basis = string.IsNullOrWhiteSpace(query.Title) ? query.NaturalLanguageQuery : query.Title;
        if (string.IsNullOrWhiteSpace(basis))
        {
            basis = "saved-query";
        }

        var slug = new string(basis
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "saved-query";
        }

        return $"{slug}-{Guid.NewGuid():N}";
    }

    private static StudioQueryCommandResult Failure(string message, StudioQueryCapabilityState? issue = null) =>
        new(false, message, Issue: issue);

    private static StudioQueryCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}

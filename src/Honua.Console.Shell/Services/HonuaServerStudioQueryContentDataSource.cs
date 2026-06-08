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
    private const string GenerateContract = "POST /api/v1/analysis/content/queries/generate";

    private readonly IHonuaAnalysisContentClient _client;
    private readonly IOperateTransitionDataSource? _operate;

    public HonuaServerStudioQueryContentDataSource(
        IHonuaAnalysisContentClient client,
        IOperateTransitionDataSource? operate = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _operate = operate;
    }

    // Catalog grounding: the real published layers, so query generation binds a real serviceId/layerId
    // instead of layerId 0. Best-effort; without the catalog the model falls back to the placeholder.
    private async Task<HonuaQueryGenerationSource[]> LoadAvailableSourcesAsync(CancellationToken cancellationToken)
    {
        if (_operate is null)
        {
            return [];
        }

        try
        {
            var view = await _operate.GetLayersViewAsync(cancellationToken).ConfigureAwait(false);
            return view.Services
                .SelectMany(service => service.Layers.Select(layer => new HonuaQueryGenerationSource
                {
                    ServiceId = service.Name,
                    LayerId = layer.LayerId.ToString(CultureInfo.InvariantCulture),
                    Name = layer.Name,
                }))
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
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

    public async Task<StudioQueryGenerationOutcome> GenerateAsync(
        StudioQueryEditor currentQuery,
        StudioQueryGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentQuery);
        ArgumentNullException.ThrowIfNull(request);

        // A refine turn ships the current saved query so the server edits it; a first turn ships null
        // (fresh). The "has content" probe keys off any authored intent so a blank scaffold requests fresh
        // generation.
        var hasContent = currentQuery.Predicates.Count > 0
            || currentQuery.OutFields.Count > 0
            || currentQuery.Parameters.Count > 0
            || !string.IsNullOrWhiteSpace(currentQuery.ServiceName)
            || !string.IsNullOrWhiteSpace(currentQuery.NaturalLanguageQuery)
            || !string.IsNullOrWhiteSpace(currentQuery.Title);

        var availableSources = await LoadAvailableSourcesAsync(cancellationToken).ConfigureAwait(false);

        var wire = new HonuaGenerateSavedQueryRequest
        {
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Query = hasContent ? StudioQueryPackageMapper.ToSavedQueryContent(currentQuery) : null,
            Conversation = request.Conversation
                .Select(turn => new HonuaAnalysisGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new HonuaAnalysisGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray(),
            AvailableSources = availableSources
        };

        var result = await _client.GenerateQueryAsync(wire, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            // A 404/501 means this server lacks the query generation contract: AI is simply off here
            // (honest "unsupported"), not a missing server binding. Other issues block the surface.
            if (issue.StatusCode is 404 or 501)
            {
                return new StudioQueryGenerationOutcome
                {
                    Status = StudioQueryGenerationStatuses.Unsupported,
                    Rationale = "This server does not offer AI query generation yet."
                };
            }

            return StudioQueryGenerationOutcome.Blocked(ToCapabilityState(issue));
        }

        var outcome = MapGeneration(currentQuery, result.Data!);
        ResolveServiceBinding(outcome.Query, availableSources);
        return outcome;
    }

    // The local model reliably binds the real numeric layerId (grounded in the catalog) but is inconsistent
    // about echoing the serviceId alongside it. The serviceId is deterministic given the layerId, so recover
    // it from the catalog when the model left it blank: match the bound layerId, else fall back to the single
    // available source. This is what lets the live result chart fetch the bound layer's real rows. Never
    // invents a binding — if nothing matches, the query stays unbound (and shows no result).
    private static void ResolveServiceBinding(StudioQueryEditor? query, HonuaQueryGenerationSource[] sources)
    {
        if (query is null || !string.IsNullOrWhiteSpace(query.ServiceName) || sources.Length == 0)
        {
            return;
        }

        var layerId = query.LayerId.ToString(CultureInfo.InvariantCulture);
        var match = sources.FirstOrDefault(source =>
            string.Equals(source.LayerId, layerId, StringComparison.Ordinal));
        match ??= sources.Length == 1 ? sources[0] : null;
        if (match is not null && !string.IsNullOrWhiteSpace(match.ServiceId))
        {
            query.ServiceName = match.ServiceId;
        }
    }

    private static StudioQueryGenerationOutcome MapGeneration(
        StudioQueryEditor currentQuery,
        HonuaSavedQueryGenerationResult result)
    {
        // Each collection declares a non-null initializer, but System.Text.Json overrides it with null when
        // the server emits an explicit JSON null for the key (an omitted key keeps the initializer), so every
        // collection on the deserialized result may be null. Guard each before LINQ — otherwise a perfectly
        // valid 'generated' result (which carries no clarifications/unmapped) throws and freezes the page
        // (mirrors the analysis client's defensive mapping).
        var warnings = new List<string>();
        if (result.Validation is not null)
        {
            warnings.AddRange(result.Validation.Warnings ?? []);
        }

        warnings.AddRange((result.UnmappedRequests ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"No mapping for: {item}"));

        var clarifications = (result.Clarifications ?? [])
            .Select(question => new StudioConversationClarification(
                question.Id,
                string.IsNullOrWhiteSpace(question.Prompt) ? question.Kind : question.Prompt,
                question.Reason ?? string.Empty,
                (question.Choices ?? [])
                    .Select(choice => new StudioConversationChoice(choice.Id, choice.Label, choice.Effect))
                    .ToArray()))
            .ToArray();

        StudioQueryEditor? query = null;
        var generated = string.Equals(result.Status, StudioQueryGenerationStatuses.Generated, StringComparison.Ordinal);
        if (generated && result.Query is { } proposed)
        {
            query = StudioQueryPackageMapper.ApplyGeneratedQuery(currentQuery, proposed);

            if (result.Validation is { IsValid: false })
            {
                warnings.AddRange((result.Validation.Failures ?? []).Select(failure => failure.Message));
            }
        }

        return new StudioQueryGenerationOutcome
        {
            Status = NormalizeStatus(result.Status),
            Query = query,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioQueryGenerationCapability(
                    result.CapabilityState.Name,
                    result.CapabilityState.State,
                    result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model
        };
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        StudioQueryGenerationStatuses.Generated => StudioQueryGenerationStatuses.Generated,
        StudioQueryGenerationStatuses.NeedsClarification => StudioQueryGenerationStatuses.NeedsClarification,
        StudioQueryGenerationStatuses.Unsupported => StudioQueryGenerationStatuses.Unsupported,
        StudioQueryGenerationStatuses.Refused => StudioQueryGenerationStatuses.Refused,
        _ => StudioQueryGenerationStatuses.Error
    };

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

using System.Globalization;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio report-builder data source bound to the real honua-server content publication registry
/// (honua-server#1183) through the <see cref="IHonuaContentPublicationClient"/> shim. The read path
/// speaks the live console lifecycle (<c>/api/v1/console/publications/{publicationId}</c>); there is
/// no in-memory publication data in the merged result (Console Patterns Charter section 11). Endpoint
/// issues (missing permission, not found, unsupported verb, transport) surface as explicit capability
/// states instead of throwing or fabricating data. A publication that is not a report kind is rejected
/// as unsupported so the report builder never renders a map/dashboard/app artifact.
/// </summary>
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
        if (!string.Equals(detail.Route.Kind, HonuaContentPublicationKinds.Report, StringComparison.Ordinal))
        {
            return new StudioReportPublicationLoad(
                null,
                [
                    new StudioReportCapabilityState(
                        Surface,
                        "Unsupported",
                        GetContract,
                        $"Publication '{publicationId}' is a '{detail.Route.Kind}' artifact, not a report. Open it in the matching Studio editor.")
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
        return new StudioReportCommandResult(
            true,
            $"Updated visibility to {result.Data!.Route.Policy.Visibility} (embed {(result.Data.Route.Policy.Embed.AllowEmbedding ? "on" : "off")}).",
            refreshed.Publication);
    }

    private const string GenProvidersContract = "GET /api/v1/console/publications/generation/providers";
    private const string GenerateContract = "POST /api/v1/console/publications/generate";

    public async Task<StudioReportAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.ListReportGenerationProvidersAsync(cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            if (issue.StatusCode is 404 or 501)
            {
                return StudioReportAiCapability.Off;
            }

            return StudioReportAiCapability.Blocked(ToCapabilityState(GenProvidersContract, issue));
        }

        var data = result.Data!;
        return new StudioReportAiCapability
        {
            Enabled = data.Enabled,
            DefaultProvider = data.DefaultProvider,
            Providers = (data.Providers ?? [])
                .Select(provider => new StudioReportAiProvider(
                    provider.Id,
                    string.IsNullOrWhiteSpace(provider.Label) ? provider.Id : provider.Label,
                    provider.Kind,
                    provider.Available,
                    provider.Detail))
                .ToArray()
        };
    }

    public async Task<StudioReportGenerationOutcome> GenerateAsync(
        StudioReportEditorState currentState,
        StudioReportGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(request);

        // A refine turn ships the current report document so the server edits it; a first turn ships null.
        var refine = currentState.Panels.Count > 0;
        var wire = new GenerateReportContentRequest
        {
            Kind = HonuaContentPublicationKinds.Report,
            Prompt = request.Prompt,
            Provider = request.Provider,
            Model = request.Model,
            Document = refine
                ? JsonSerializer.Deserialize<JsonElement>(StudioReportDocument.Serialize(currentState))
                : null,
            Conversation = request.Conversation
                .Select(turn => new HonuaReportGenerationTurn { Role = turn.Role, Content = turn.Content })
                .ToArray(),
            Answers = request.Answers
                .Select(answer => new HonuaReportGenerationAnswer { QuestionId = answer.QuestionId, OptionId = answer.OptionId })
                .ToArray()
        };

        var result = await _client.GenerateReportAsync(wire, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            if (issue.StatusCode is 404 or 501)
            {
                return new StudioReportGenerationOutcome
                {
                    Status = StudioReportGenerationStatuses.Unsupported,
                    Rationale = "This server does not offer AI report generation yet."
                };
            }

            return StudioReportGenerationOutcome.Blocked(ToCapabilityState(GenerateContract, issue));
        }

        return MapGeneration(result.Data!);
    }

    private static StudioReportGenerationOutcome MapGeneration(HonuaReportGenerationResult result)
    {
        // The server omits empty collections (System.Text.Json source-gen serializes them as null), so a
        // perfectly valid 'generated' result commonly arrives with null UnmappedRequests/Clarifications/
        // Choices. Coalesce before LINQ — otherwise a normal generated turn NREs and freezes the page
        // (mirrors the dashboard mapper; this family had regressed by dropping the guards).
        var warnings = (result.UnmappedRequests ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => $"No matching binding/panel for: {item}")
            .ToList();

        var clarifications = (result.Clarifications ?? [])
            .Select(question => new StudioConversationClarification(
                question.Id,
                string.IsNullOrWhiteSpace(question.Prompt) ? question.Kind : question.Prompt,
                question.Reason ?? string.Empty,
                (question.Choices ?? [])
                    .Select(choice => new StudioConversationChoice(choice.Id, choice.Label, choice.Effect))
                    .ToArray()))
            .ToArray();

        StudioReportEditorState? state = null;
        if (string.Equals(result.Status, StudioReportGenerationStatuses.Generated, StringComparison.Ordinal) &&
            result.Document is { } document)
        {
            // A generated document becomes a fresh, unsaved draft the operator reviews and publishes.
            state = new StudioReportEditorState();
            if (!string.IsNullOrWhiteSpace(result.RouteSlug))
            {
                state.RouteSlug = result.RouteSlug!;
            }

            StudioReportDocument.ApplyTo(state, document);
        }

        return new StudioReportGenerationOutcome
        {
            Status = NormalizeStatus(result.Status),
            State = state,
            Rationale = result.Rationale ?? string.Empty,
            Clarifications = clarifications,
            Warnings = warnings,
            CapabilityState = result.CapabilityState is null
                ? null
                : new StudioReportGenerationCapability(result.CapabilityState.Name, result.CapabilityState.State, result.CapabilityState.Reason),
            Provider = result.Provider,
            Model = result.Model
        };
    }

    private static string NormalizeStatus(string? status) => status switch
    {
        StudioReportGenerationStatuses.Generated => StudioReportGenerationStatuses.Generated,
        StudioReportGenerationStatuses.NeedsClarification => StudioReportGenerationStatuses.NeedsClarification,
        StudioReportGenerationStatuses.Unsupported => StudioReportGenerationStatuses.Unsupported,
        StudioReportGenerationStatuses.Refused => StudioReportGenerationStatuses.Refused,
        _ => StudioReportGenerationStatuses.Error
    };

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
        if (!string.Equals(detail.Route.Kind, HonuaContentPublicationKinds.Report, StringComparison.Ordinal))
        {
            return new StudioReportCommandResult(
                false,
                $"Publication '{detail.Route.PublicationId}' is a '{detail.Route.Kind}' artifact, not a report.",
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

using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Server-backed <see cref="IAiPublishDriver"/>. Wires the console's AI publish mode (redesign Phase 3) to the
/// honua-server AI generation surface through the existing <see cref="IWorkflowPackageApiClient"/> shim — the
/// same Bedrock-capable WorkflowGeneration providers the Studio workflow builder uses (honua-server #1737,
/// DefaultProvider=bedrock). The driver:
///
///   1. Probes capability via <c>GET /api/v1/console/workflow-generation/providers</c> — is AI on, and which
///      provider is the default (e.g. "bedrock"). A 404/501 means the server lacks the generation contract: AI
///      is honestly OFF here, not a transport failure.
///   2. Confirms the intent is plannable via <c>POST /api/v1/console/workflow-packages/generate</c> — the
///      server grounds + validates the request with its real LLM (Bedrock) and returns a rationale and any
///      grounding gaps. The console does NOT surface that raw graph/spec; it uses the server's accept/reject
///      and rationale as the trustworthy signal and maps the intent onto the publish flow's protocols + style.
///   3. Maps the result into a single <see cref="AiPublishProposal"/> — resource, protocols, style intent —
///      that the outcome card renders as one approval.
///
/// Nothing applies here; the human approves on the card and apply runs through the manual flow's wired admin
/// publish operation. Every server failure surfaces as a first-class unavailable/blocked outcome — never a
/// crash, never a fabricated proposal (Console Patterns Charter §11).
/// </summary>
public sealed class ServerAiPublishDriver : IAiPublishDriver
{
    private readonly IWorkflowPackageApiClient _api;

    public ServerAiPublishDriver(IWorkflowPackageApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<AiPublishCapability> GetCapabilityAsync(CancellationToken cancellationToken = default)
    {
        var providers = await _api.ListGenerationProvidersAsync(cancellationToken).ConfigureAwait(false);
        if (!providers.IsSuccess || providers.Data is null)
        {
            // 404/501: this server has the console API but not the generation contract — AI is simply off here
            // (honest "unavailable"), not a missing binding. Other issues are a real binding/transport problem.
            if (providers.Issue?.StatusCode is 404 or 501)
            {
                return AiPublishCapability.Off("This honua-server does not offer AI generation yet.");
            }

            return AiPublishCapability.Off(
                providers.Issue?.Detail ?? "The honua-server AI generation surface could not be reached.");
        }

        if (!providers.Data.Enabled)
        {
            return AiPublishCapability.Off(
                "AI generation is configured but switched off on this honua-server.");
        }

        // Enabled, but is any provider actually available (key/endpoint configured)? If not, be honest.
        var available = (providers.Data.Providers ?? [])
            .Any(provider => provider.Available);
        if (!available)
        {
            return AiPublishCapability.Off(
                "AI generation is enabled but no provider is configured (no model endpoint/credentials).");
        }

        return new AiPublishCapability(true, providers.Data.DefaultProvider, Detail: null);
    }

    public async Task<AiPublishOutcome> ProposeAsync(
        string intent,
        IReadOnlyList<AiPublishResourceRef> knownResources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knownResources);
        if (string.IsNullOrWhiteSpace(intent))
        {
            return AiPublishOutcome.NeedsInput(
                "Describe what you want to publish — e.g. \"publish Maui parcels as a feature service and a STAC catalog, styled by zoning\".",
                []);
        }

        // Ground the turn with the real server AI: it confirms it can plan the request (and returns a rationale
        // / grounding gaps) using its configured provider — Bedrock when DefaultProvider=bedrock. We send no
        // graph (a fresh intent) and read the server's status, not its raw graph.
        var wire = new GenerateWorkflowRequest
        {
            Prompt = intent,
            Graph = null,
            Conversation = [],
            Answers = [],
        };

        var result = await _api.GenerateWorkflowAsync(wire, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            if (result.Issue?.StatusCode is 404 or 501)
            {
                return AiPublishOutcome.Unavailable("This honua-server does not offer AI generation yet.");
            }

            return AiPublishOutcome.Blocked(
                result.Issue?.Detail ?? "The honua-server AI generation surface did not respond.");
        }

        return MapToOutcome(intent, result.Data, knownResources);
    }

    public Task RecordDecisionAsync(
        string? feedbackId,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedbackId))
        {
            return Task.CompletedTask;
        }

        // Best-effort; the HTTP client never throws and a missing feedback contract is a no-op.
        return _api.RecordGenerationFeedbackAsync(
            new WorkflowGenerationFeedbackRequest { FeedbackId = feedbackId, Action = action },
            cancellationToken);
    }

    private static AiPublishOutcome MapToOutcome(
        string intent,
        WorkflowGenerationResult result,
        IReadOnlyList<AiPublishResourceRef> knownResources)
    {
        // The server statuses mirror the plan-analysis statuses. We only ever read them — never the raw graph —
        // so the console keeps the outcome+approval framing and never exposes the spec/tool-call plumbing.
        switch (result.Status)
        {
            case "unsupported":
                return AiPublishOutcome.Unavailable(
                    result.Rationale ?? "The AI can't plan this publish request.");

            case "refused":
                return AiPublishOutcome.Blocked(
                    result.Rationale ?? "The AI declined this request.");

            case "error":
                return AiPublishOutcome.Blocked(
                    result.Rationale ?? "The AI generation surface returned an error.");

            case "needs-clarification":
                {
                    var prompts = (result.Clarifications ?? [])
                        .Select(clarification =>
                            string.IsNullOrWhiteSpace(clarification.Prompt) ? clarification.Kind : clarification.Prompt)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .ToArray();
                    return AiPublishOutcome.NeedsInput(
                        result.Rationale ?? "The AI needs a little more detail before it can propose a publish.",
                        prompts);
                }

            default:
                // "generated" (or anything the server treats as success): the server confirmed it can plan the
                // request. Build the proposal from the intent's protocols/style and the server's rationale.
                return BuildProposal(intent, result, knownResources);
        }
    }

    private static AiPublishOutcome BuildProposal(
        string intent,
        WorkflowGenerationResult result,
        IReadOnlyList<AiPublishResourceRef> knownResources)
    {
        var protocols = AiPublishProposalSummary.InferProtocols(intent);
        var styleIntent = AiPublishProposalSummary.InferStyleIntent(intent);
        var requestedName = AiPublishProposalSummary.InferResourceName(intent);

        // Resolve the requested name to a real catalog resource. If we can't, we need the user to pick one —
        // we never fabricate a resource id (Charter §11).
        var resolved = ResolveResource(requestedName, knownResources);
        if (resolved is null)
        {
            var detail = knownResources.Count == 0
                ? "I couldn't find a resource to publish. Add or import data first, then describe the publish."
                : $"Which resource should I publish? I read \"{requestedName ?? intent}\" but couldn't match it.";
            return AiPublishOutcome.NeedsInput(detail, knownResources.Select(r => r.Name).ToArray());
        }

        var resource = resolved.Value;
        var slot = SanitizeSlot(resource.Name);
        var proposal = new AiPublishProposal
        {
            ResourceName = resource.Name,
            Protocols = protocols,
            ServiceSlot = slot,
            StyleIntent = styleIntent,
            Visibility = PublishVisibility.Org,
            Rationale = string.IsNullOrWhiteSpace(result.Rationale)
                ? "The server AI confirmed it can plan this publish from your description."
                : result.Rationale!,
            PlannedPublications = PublishProtocolCatalog.PlanPublications(slot, protocols),
            UnmappedRequests = result.UnmappedRequests ?? [],
            Provider = result.Provider,
            Model = result.Model,
            FeedbackId = result.FeedbackId,
        };

        return AiPublishOutcome.Proposed(proposal);
    }

    private static AiPublishResourceRef? ResolveResource(
        string? requestedName,
        IReadOnlyList<AiPublishResourceRef> knownResources)
    {
        if (knownResources.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            // No name in the intent but exactly one resource exists → unambiguous.
            return knownResources.Count == 1 ? knownResources[0] : null;
        }

        var needle = requestedName.Trim();

        // Exact (case-insensitive) match first, then a contains match either direction.
        var exact = knownResources
            .FirstOrDefault(r => string.Equals(r.Name, needle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(exact.ResourceId))
        {
            return exact;
        }

        var contains = knownResources
            .Where(r =>
                r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                needle.Contains(r.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return contains.Length == 1 ? contains[0] : null;
    }

    private static string SanitizeSlot(string value)
    {
        var sanitized = new string(value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "service" : sanitized;
    }
}

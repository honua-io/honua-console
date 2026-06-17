using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The AI driver for the unified data→publish flow (redesign Phase 3). It turns a plain-language intent into a
/// single <see cref="AiPublishProposal"/> the console renders as an <b>outcome + one approval</b> card. The
/// driver is server-backed: it asks the bound honua-server's AI generation surface (the Bedrock-capable
/// WorkflowGeneration providers — honua-server #1737) whether it can plan the request and for a rationale,
/// then maps that onto the same <see cref="DataToPublishFlowState"/> the manual wizard fills.
///
/// The driver NEVER applies anything — the human approves on the card, and apply runs through the same wired
/// admin publish operation the manual flow uses. When no server is bound, or the server has the generation API
/// but AI is switched off, the driver returns an honest unavailable/blocked outcome (never a crash, never a
/// fabricated proposal — Console Patterns Charter §11).
/// </summary>
public interface IAiPublishDriver
{
    /// <summary>Reports whether the bound server has AI generation enabled and which provider is the default.</summary>
    Task<AiPublishCapability> GetCapabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the user's plain-language intent into an outcome: a proposal to approve/edit/reject, a
    /// needs-input clarification, or an honest unavailable/blocked state. The optional
    /// <paramref name="knownResources"/> lets the driver resolve the intent's resource name to a real catalog
    /// resource (it never fabricates one).
    /// </summary>
    Task<AiPublishOutcome> ProposeAsync(
        string intent,
        IReadOnlyList<AiPublishResourceRef> knownResources,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort training-flywheel signal recorded after the human's decision on a proposal
    /// ("approved" | "edited" | "rejected"). Never throws; a no-op when the server lacks the feedback contract.
    /// </summary>
    Task RecordDecisionAsync(
        string? feedbackId,
        string action,
        CancellationToken cancellationToken = default);
}

/// <summary>A catalog resource the AI driver can resolve an intent's resource name onto (id + display name).</summary>
public readonly record struct AiPublishResourceRef(string ResourceId, string Name);

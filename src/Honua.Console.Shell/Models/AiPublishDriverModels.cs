namespace Honua.Console.Shell.Models;

/// <summary>
/// The AI driver's view of the unified data→publish flow (redesign §3.1 / Phase 3). The AI driver fills the
/// SAME <see cref="DataToPublishFlowState"/> the manual wizard fills, but reaches it through a different
/// surface: the user states intent in plain language, the server's AI generation surface (Bedrock-capable —
/// honua-server #1737) confirms it can plan the request and returns a rationale, and the console maps that
/// into a single <see cref="AiPublishProposal"/> rendered as an <b>outcome + one approval</b> card.
///
/// This is deliberately NOT the noisy plan/spec/tool-call presentation of StudioAiConversation (the founder's
/// "exposes too much plumbing" critique). The card shows the outcome — "I'll publish X as FeatureServer + STAC,
/// styled by zoning — Approve / Edit / Reject" — and tucks the plan internals behind a Details disclosure only.
/// </summary>
public sealed class AiPublishProposal
{
    /// <summary>The resource the AI proposes to publish (display name; resolved to an id on apply).</summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>The metadata-v2 protocols the AI proposes to expose, in <see cref="PublishProtocolCatalog"/> order.</summary>
    public IReadOnlyList<string> Protocols { get; set; } = [];

    /// <summary>The service slot the publications attach to (folder/service), seeded from the resource name.</summary>
    public string ServiceSlot { get; set; } = "service";

    /// <summary>A short, human style intent the AI inferred (e.g. "styled by zoning"); null when none was asked for.</summary>
    public string? StyleIntent { get; set; }

    /// <summary>Go-live visibility the AI proposes (defaults to Org; never public without explicit intent).</summary>
    public PublishVisibility Visibility { get; set; } = PublishVisibility.Org;

    /// <summary>The server AI's one-line rationale for the proposal (shown on the card under the outcome line).</summary>
    public string Rationale { get; set; } = string.Empty;

    /// <summary>
    /// The plan internals that live behind the Details disclosure only: the per-protocol publication plan,
    /// any grounding gaps the server surfaced, and the AI provider/model that produced the proposal. These
    /// are the "plumbing" — never the headline.
    /// </summary>
    public IReadOnlyList<PublishProtocolPlan> PlannedPublications { get; set; } = [];

    /// <summary>Things the intent asked for that have no matching protocol/affordance (surfaced, never dropped).</summary>
    public IReadOnlyList<string> UnmappedRequests { get; set; } = [];

    /// <summary>The AI provider id (e.g. "bedrock") and model that produced this proposal — Details only.</summary>
    public string? Provider { get; set; }

    public string? Model { get; set; }

    /// <summary>Correlates this proposal to later approve/edit/reject feedback (the training flywheel).</summary>
    public string? FeedbackId { get; set; }

    /// <summary>The single-sentence outcome headline the card leads with.</summary>
    public string OutcomeHeadline =>
        AiPublishProposalSummary.Headline(ResourceName, Protocols, StyleIntent);
}

/// <summary>The status of an AI publish turn — the gate the outcome card renders against.</summary>
public enum AiPublishOutcomeStatus
{
    /// <summary>The AI produced a proposal the user can Approve / Edit / Reject.</summary>
    Proposed,

    /// <summary>The AI needs a clarification before it can propose (e.g. which resource).</summary>
    NeedsInput,

    /// <summary>The server has the generation API but AI is switched off / no provider configured (honest "AI unavailable").</summary>
    Unavailable,

    /// <summary>No honua-server is bound, or the AI surface could not be reached — first-class missing-binding/blocked.</summary>
    Blocked,
}

/// <summary>
/// The outcome of one AI publish turn: a status, the proposal (when <see cref="AiPublishOutcomeStatus.Proposed"/>),
/// any clarification prompts (when <see cref="AiPublishOutcomeStatus.NeedsInput"/>), and a human detail line
/// for the Unavailable/Blocked states so the console renders an honest message rather than a crash.
/// </summary>
public sealed class AiPublishOutcome
{
    public required AiPublishOutcomeStatus Status { get; init; }

    public AiPublishProposal? Proposal { get; init; }

    /// <summary>Clarification prompts for <see cref="AiPublishOutcomeStatus.NeedsInput"/> (e.g. "which resource?").</summary>
    public IReadOnlyList<string> Clarifications { get; init; } = [];

    /// <summary>A human message for the Unavailable/Blocked/NeedsInput states (binding detail, "AI is off", etc.).</summary>
    public string? Detail { get; init; }

    public static AiPublishOutcome Unavailable(string detail) =>
        new() { Status = AiPublishOutcomeStatus.Unavailable, Detail = detail };

    public static AiPublishOutcome Blocked(string detail) =>
        new() { Status = AiPublishOutcomeStatus.Blocked, Detail = detail };

    public static AiPublishOutcome NeedsInput(string detail, IReadOnlyList<string> clarifications) =>
        new() { Status = AiPublishOutcomeStatus.NeedsInput, Detail = detail, Clarifications = clarifications };

    public static AiPublishOutcome Proposed(AiPublishProposal proposal) =>
        new() { Status = AiPublishOutcomeStatus.Proposed, Proposal = proposal };
}

/// <summary>
/// Whether the AI publish surface is available against the bound server: the generation providers the server
/// has enabled and the default provider (Bedrock when the server is configured for it). A null capability or
/// <see cref="Enabled"/>=false means the console shows an honest "AI unavailable" state, never a crash.
/// </summary>
public sealed record AiPublishCapability(bool Enabled, string? DefaultProvider, string? Detail)
{
    public static AiPublishCapability Off(string detail) => new(false, null, detail);
}

/// <summary>
/// Builds the human outcome headline and infers the publish intent (protocols + style) from a plain-language
/// prompt. Kept as a pure helper so the mapping is unit-testable without a server and the same words always
/// resolve to the same protocols (Console Patterns Charter: one mapping authority, no fabricated data).
/// </summary>
public static class AiPublishProposalSummary
{
    /// <summary>The single-sentence outcome line the card leads with: "I'll publish <b>X</b> as A + B[, styled by Z]".</summary>
    public static string Headline(string resourceName, IReadOnlyList<string> protocols, string? styleIntent)
    {
        var name = string.IsNullOrWhiteSpace(resourceName) ? "this resource" : resourceName;
        var labels = protocols
            .Select(id => PublishProtocolCatalog.Find(id)?.Label ?? id)
            .ToArray();
        var asClause = labels.Length switch
        {
            0 => "as a service",
            1 => $"as {labels[0]}",
            _ => $"as {string.Join(" + ", labels)}",
        };
        var styleClause = string.IsNullOrWhiteSpace(styleIntent) ? string.Empty : $", styled {styleIntent}";
        return $"I'll publish {name} {asClause}{styleClause}.";
    }

    /// <summary>
    /// Infers the protocol set from intent words, falling back to the default-on set (FeatureServer + MapServer
    /// + STAC) when the intent names no protocol. Recognised words map to <see cref="PublishProtocolCatalog"/>
    /// ids; the returned set is ordered by the catalog so the proposal is stable regardless of phrasing.
    /// </summary>
    public static IReadOnlyList<string> InferProtocols(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var text = prompt.ToLowerInvariant();
        var hits = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (needle, protocol) in ProtocolWords)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                hits.Add(protocol);
            }
        }

        var chosen = hits.Count > 0 ? hits : PublishProtocolCatalog.DefaultEnabled.ToHashSet(StringComparer.Ordinal);

        // Order by the catalog so the preview/headline are stable regardless of prompt word order.
        return PublishProtocolCatalog.All
            .Where(descriptor => chosen.Contains(descriptor.Id))
            .Select(descriptor => descriptor.Id)
            .ToArray();
    }

    /// <summary>
    /// Infers a short style intent clause ("by zoning", "by population") from a "styled by &lt;field&gt;" / "color
    /// by &lt;field&gt;" phrasing, or null when the intent asked for no styling. Returns the clause the headline
    /// appends after "styled" (so the caller writes "styled by zoning").
    /// </summary>
    public static string? InferStyleIntent(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        var text = prompt.ToLowerInvariant();

        foreach (var marker in StyleMarkers)
        {
            var at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var rest = text[(at + marker.Length)..].TrimStart();
            var field = new string(rest.TakeWhile(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());
            if (!string.IsNullOrWhiteSpace(field))
            {
                return $"by {field}";
            }
        }

        return null;
    }

    /// <summary>The resource name an intent asked for, or null. Recognises "publish &lt;name&gt; as", quoted names,
    /// and a trailing "the &lt;name&gt; layer/dataset/parcels" — used only as a seed; the apply step resolves the
    /// real resource id from the bound catalog and never fabricates one.</summary>
    public static string? InferResourceName(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        // A quoted phrase is the strongest signal.
        var firstQuote = prompt.IndexOf('"');
        if (firstQuote >= 0)
        {
            var secondQuote = prompt.IndexOf('"', firstQuote + 1);
            if (secondQuote > firstQuote + 1)
            {
                return prompt[(firstQuote + 1)..secondQuote].Trim();
            }
        }

        // "publish <words> as ..." — take the words between "publish" and the first " as ".
        var lower = prompt.ToLowerInvariant();
        var publishAt = lower.IndexOf("publish ", StringComparison.Ordinal);
        if (publishAt >= 0)
        {
            var afterPublish = publishAt + "publish ".Length;
            var asAt = lower.IndexOf(" as ", afterPublish, StringComparison.Ordinal);
            var slice = asAt > afterPublish
                ? prompt[afterPublish..asAt]
                : prompt[afterPublish..];
            var name = slice.Trim().Trim('.', ',');
            if (!string.IsNullOrWhiteSpace(name) && name.Length <= 80)
            {
                return name;
            }
        }

        return null;
    }

    private static readonly (string Needle, string Protocol)[] ProtocolWords =
    [
        ("featureserver", PublishProtocolCatalog.Protocols.FeatureServer),
        ("feature service", PublishProtocolCatalog.Protocols.FeatureServer),
        ("feature server", PublishProtocolCatalog.Protocols.FeatureServer),
        ("mapserver", PublishProtocolCatalog.Protocols.MapServer),
        ("map service", PublishProtocolCatalog.Protocols.MapServer),
        ("map server", PublishProtocolCatalog.Protocols.MapServer),
        ("imageserver", PublishProtocolCatalog.Protocols.ImageServer),
        ("image service", PublishProtocolCatalog.Protocols.ImageServer),
        ("stac", PublishProtocolCatalog.Protocols.Stac),
        ("ogc", PublishProtocolCatalog.Protocols.OgcFeatures),
        ("ogc api features", PublishProtocolCatalog.Protocols.OgcFeatures),
        ("wms", PublishProtocolCatalog.Protocols.Wms),
        ("wfs", PublishProtocolCatalog.Protocols.Wfs20),
        ("wmts", PublishProtocolCatalog.Protocols.Wmts),
        ("odata", PublishProtocolCatalog.Protocols.OData),
    ];

    private static readonly string[] StyleMarkers =
    [
        "styled by ",
        "style by ",
        "color by ",
        "colour by ",
        "symbolize by ",
        "symbolise by ",
        "categorized by ",
        "categorised by ",
    ];
}

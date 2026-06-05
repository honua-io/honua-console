namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> savedQuery generation (Studio "Query from prompt").
// The wire contract is Honua.Console.Contracts.AnalysisContentShims (analysis/content/queries/generate
// endpoint), mirroring the analysis generation contract. Nothing here fabricates a query: a generation
// outcome is either server-produced (the query editor hydrates from the returned SavedQueryContent via
// StudioQueryPackageMapper) or carries a missing-binding capability state / non-ready status.
// Clarifications reuse StudioConversationClarification so StudioAiConversation renders them with no extra
// mapping. Mirrors StudioAnalysisGeneration.cs.

/// <summary>What the user typed plus the conversational context for one query generate/refine turn.</summary>
public sealed record StudioQueryGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioQueryGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioQueryGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioQueryGenerationTurn(string Role, string Content);

public sealed record StudioQueryGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The result of a query generate/refine turn.</summary>
public sealed record StudioQueryGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioQueryCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioQueryGenerationStatuses.Error;

    /// <summary>The query-editor state with the server-proposed query applied; present iff status == generated.</summary>
    public StudioQueryEditor? Query { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioQueryGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioQueryGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioQueryGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioQueryGenerationOutcome Blocked(StudioQueryCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioQueryGenerationStatuses.Error };
}

public sealed record StudioQueryGenerationCapability(string Name, string State, string? Reason);

public static class StudioQueryGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

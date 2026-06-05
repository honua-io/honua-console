namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> analysis.package generation (Studio "Analysis from prompt").
// The wire contract is Honua.Console.Contracts.AnalysisContentShims (analysis/content/generate endpoint),
// mirroring the workflow generation contract. Nothing here fabricates an analysis: a generation outcome is
// either server-produced (the plan card hydrates from the returned AnalysisPackageContent via
// StudioAnalysisPackageMapper) or carries a missing-binding capability state / non-ready status.
// Clarifications reuse StudioConversationClarification so StudioAiConversation renders them with no extra
// mapping. Mirrors StudioWorkflowGeneration.cs.

/// <summary>What the user typed plus the conversational context for one analysis generate/refine turn.</summary>
public sealed record StudioAnalysisGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioAnalysisGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioAnalysisGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioAnalysisGenerationTurn(string Role, string Content);

public sealed record StudioAnalysisGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The result of an analysis generate/refine turn.</summary>
public sealed record StudioAnalysisGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioAnalysisCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioAnalysisGenerationStatuses.Error;

    /// <summary>The plan-card state with the server-proposed analysis applied; present iff status == generated.</summary>
    public StudioAnalysisPlanEditor? Plan { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioAnalysisGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioAnalysisGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioAnalysisGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioAnalysisGenerationOutcome Blocked(StudioAnalysisCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioAnalysisGenerationStatuses.Error };
}

public sealed record StudioAnalysisGenerationCapability(string Name, string State, string? Reason);

public static class StudioAnalysisGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

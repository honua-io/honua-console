namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> dashboard.document generation (Studio "Dashboard from prompt").
// The wire contract is Honua.Console.Contracts.ContentPublicationShims (the shared content
// publications/generate endpoint, dispatched on kind=dashboard — the same endpoint reports use). Nothing
// here fabricates a dashboard: a generation outcome is either server-produced (the editor preview hydrates
// from the returned honua.dashboard-document.v1 via StudioDashboardDocument.ApplyGeneratedDocument) or
// carries a missing-binding capability state / non-ready status. Clarifications reuse
// StudioConversationClarification so StudioAiConversation renders them with no extra mapping. Mirrors
// StudioAnalysisGeneration.cs / StudioReportGeneration.cs so the surfaces stay consistent.

/// <summary>What the user typed plus the conversational context for one dashboard generate/refine turn.</summary>
public sealed record StudioDashboardGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioDashboardGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioDashboardGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioDashboardGenerationTurn(string Role, string Content);

public sealed record StudioDashboardGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The result of a dashboard generate/refine turn.</summary>
public sealed record StudioDashboardGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioDashboardCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioDashboardGenerationStatuses.Error;

    /// <summary>The editor state with the server-proposed document applied; present iff status == generated.</summary>
    public StudioDashboardEditorState? State { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (unmapped requests + mapper notes), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioDashboardGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioDashboardGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioDashboardGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioDashboardGenerationOutcome Blocked(StudioDashboardCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioDashboardGenerationStatuses.Error };
}

public sealed record StudioDashboardGenerationCapability(string Name, string State, string? Reason);

public static class StudioDashboardGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

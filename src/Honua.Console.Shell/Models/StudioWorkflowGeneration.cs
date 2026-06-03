namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> workflow.package generation (Studio "Workflow from prompt").
// The wire contract is Honua.Console.Contracts.StudioWorkflowShims (workflow-generation endpoints), scoped
// server-side in honua-server/docs/design/ai-workflow-generation.md. Nothing here fabricates a workflow: a
// generation outcome is either server-produced or carries a binding state / non-ready status. Clarifications
// reuse StudioConversationClarification so StudioAiConversation renders them with no extra mapping.

/// <summary>What the user typed plus the conversational context for one generate/refine turn.</summary>
public sealed record StudioWorkflowGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioWorkflowGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioWorkflowGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioWorkflowGenerationTurn(string Role, string Content);

public sealed record StudioWorkflowGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The capability surface for AI generation: whether it is on and which providers are usable.</summary>
public sealed record StudioWorkflowAiCapability
{
    /// <summary>Set when the workflow surface is unbound (no server) - the page shows the shared blocked state.</summary>
    public StudioWorkflowBindingState? BindingState { get; init; }

    /// <summary>False when the server has the workflow API but AI generation is disabled / not configured.</summary>
    public bool Enabled { get; init; }

    public string? DefaultProvider { get; init; }

    public IReadOnlyList<StudioWorkflowAiProvider> Providers { get; init; } = [];

    public static StudioWorkflowAiCapability Blocked(StudioWorkflowBindingState binding) =>
        new() { BindingState = binding, Enabled = false };

    public static StudioWorkflowAiCapability Off { get; } = new() { Enabled = false };
}

public sealed record StudioWorkflowAiProvider(string Id, string Label, string Kind, bool Available, string? Detail);

/// <summary>The result of a generate/refine turn.</summary>
public sealed record StudioWorkflowGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioWorkflowBindingState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioWorkflowGenerationStatuses.Error;

    /// <summary>The updated draft with the server-proposed graph applied; present iff status == generated.</summary>
    public StudioWorkflowPackageDraft? Draft { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioWorkflowGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioWorkflowGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioWorkflowGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioWorkflowGenerationOutcome Blocked(StudioWorkflowBindingState binding) =>
        new() { BindingState = binding, Status = StudioWorkflowGenerationStatuses.Error };
}

public sealed record StudioWorkflowGenerationCapability(string Name, string State, string? Reason);

public static class StudioWorkflowGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

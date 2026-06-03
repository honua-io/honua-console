namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> form.package generation (Studio "Form from prompt").
// The wire contract is Honua.Console.Contracts.FormPackageShims (form-generation endpoints), to be scoped
// server-side (mirrors honua-server/docs/design/ai-workflow-generation.md). Nothing here fabricates a form:
// a generation outcome is either server-produced or carries a capability state / non-ready status.
// Clarifications reuse StudioConversationClarification so StudioAiConversation renders them with no extra
// mapping. This mirrors StudioWorkflowGeneration.cs one-for-one so the two surfaces stay consistent.

/// <summary>What the user typed plus the conversational context for one generate/refine turn.</summary>
public sealed record StudioFormGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioFormGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioFormGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioFormGenerationTurn(string Role, string Content);

public sealed record StudioFormGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The capability surface for AI generation: whether it is on and which providers are usable.</summary>
public sealed record StudioFormAiCapability
{
    /// <summary>Set when the form surface is unbound (no server) - the page shows the shared blocked state.</summary>
    public StudioFormCapabilityState? BindingState { get; init; }

    /// <summary>False when the server has the form API but AI generation is disabled / not configured.</summary>
    public bool Enabled { get; init; }

    public string? DefaultProvider { get; init; }

    public IReadOnlyList<StudioFormAiProvider> Providers { get; init; } = [];

    public static StudioFormAiCapability Blocked(StudioFormCapabilityState binding) =>
        new() { BindingState = binding, Enabled = false };

    public static StudioFormAiCapability Off { get; } = new() { Enabled = false };
}

public sealed record StudioFormAiProvider(string Id, string Label, string Kind, bool Available, string? Detail);

/// <summary>The result of a generate/refine turn.</summary>
public sealed record StudioFormGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioFormCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioFormGenerationStatuses.Error;

    /// <summary>The editor state with the server-proposed package applied; present iff status == generated.</summary>
    public StudioFormEditorState? State { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioFormGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioFormGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioFormGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioFormGenerationOutcome Blocked(StudioFormCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioFormGenerationStatuses.Error };
}

public sealed record StudioFormGenerationCapability(string Name, string State, string? Reason);

public static class StudioFormGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

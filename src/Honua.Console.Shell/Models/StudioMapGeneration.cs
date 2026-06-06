namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> map.package generation (Studio "Map from prompt").
// The wire contract is Honua.Console.Contracts.StudioGenerationShims (map-packages/generate endpoint),
// mirroring the workflow generation contract. Nothing here fabricates a map: a generation outcome is either
// server-produced (the editor hydrates from the returned MapPackage body via StudioMapPackageMapper) or
// carries a missing-binding capability state / non-ready status. Clarifications reuse
// StudioConversationClarification so StudioAiConversation renders them with no extra mapping. Mirrors
// StudioWorkflowGeneration.cs.

/// <summary>What the user typed plus the conversational context for one map generate/refine turn.</summary>
public sealed record StudioMapGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioMapGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioMapGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioMapGenerationTurn(string Role, string Content);

public sealed record StudioMapGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The result of a map generate/refine turn.</summary>
public sealed record StudioMapGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioMapCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioMapGenerationStatuses.Error;

    /// <summary>The editor state with the server-proposed map applied; present iff status == generated.</summary>
    public StudioMapEditorState? State { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioMapGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioMapGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioMapGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioMapGenerationOutcome Blocked(StudioMapCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioMapGenerationStatuses.Error };
}

public sealed record StudioMapGenerationCapability(string Name, string State, string? Reason);

public static class StudioMapGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

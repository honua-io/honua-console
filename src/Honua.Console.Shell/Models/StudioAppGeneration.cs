namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> app.package generation (Studio "App from prompt"). The wire
// contract is Honua.Console.Contracts.StudioAppGenerationShims (app-packages/generate endpoint), mirroring
// the map/dashboard generation contracts. Nothing here fabricates an app: a generation outcome is either
// server-produced (the editor hydrates from the returned studio-app/v1 body via
// StudioAppPackageMapper.ApplyEnvelopeBody — the SAME body the console authors/round-trips) or carries a
// missing-binding capability state / non-ready status. Clarifications reuse StudioConversationClarification
// so StudioAiConversation renders them with no extra mapping. Mirrors StudioMapGeneration.cs /
// StudioDashboardGeneration.cs.

/// <summary>What the user typed plus the conversational context for one app generate/refine turn.</summary>
public sealed record StudioAppGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioAppGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioAppGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioAppGenerationTurn(string Role, string Content);

public sealed record StudioAppGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The result of an app generate/refine turn.</summary>
public sealed record StudioAppGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioAppCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioAppGenerationStatuses.Error;

    /// <summary>The editor state with the server-proposed app applied; present iff status == generated.</summary>
    public StudioAppEditorState? State { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioAppGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioAppGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioAppGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioAppGenerationOutcome Blocked(StudioAppCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioAppGenerationStatuses.Error };
}

public sealed record StudioAppGenerationCapability(string Name, string State, string? Reason);

public static class StudioAppGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

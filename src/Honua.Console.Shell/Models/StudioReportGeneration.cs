namespace Honua.Console.Shell.Models;

// Console-side model for natural-language -> report.document generation (Studio "Report from prompt").
// The wire contract is Honua.Console.Contracts.ContentPublicationShims (report-generation endpoints), to be
// scoped server-side (mirrors honua-server/docs/design/ai-workflow-generation.md). Nothing here fabricates a
// report: a generation outcome is either server-produced or carries a capability state / non-ready status.
// Clarifications reuse StudioConversationClarification so StudioAiConversation renders them with no extra
// mapping. This mirrors StudioWorkflowGeneration.cs / StudioFormGeneration.cs so the surfaces stay consistent.

/// <summary>What the user typed plus the conversational context for one generate/refine turn.</summary>
public sealed record StudioReportGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Provider id to use (from the capability list); null selects the server default.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional per-call model override.</summary>
    public string? Model { get; init; }

    /// <summary>Prior turns (you/Honua) for context.</summary>
    public IReadOnlyList<StudioReportGenerationTurn> Conversation { get; init; } = [];

    /// <summary>Answers to a prior needs-clarification turn (questionId -> chosen optionId).</summary>
    public IReadOnlyList<StudioReportGenerationAnswer> Answers { get; init; } = [];
}

public sealed record StudioReportGenerationTurn(string Role, string Content);

public sealed record StudioReportGenerationAnswer(string QuestionId, string OptionId);

/// <summary>The capability surface for AI generation: whether it is on and which providers are usable.</summary>
public sealed record StudioReportAiCapability
{
    /// <summary>Set when the report surface is unbound (no server) - the page shows the shared blocked state.</summary>
    public StudioReportCapabilityState? BindingState { get; init; }

    /// <summary>False when the server has the report API but AI generation is disabled / not configured.</summary>
    public bool Enabled { get; init; }

    public string? DefaultProvider { get; init; }

    public IReadOnlyList<StudioReportAiProvider> Providers { get; init; } = [];

    public static StudioReportAiCapability Blocked(StudioReportCapabilityState binding) =>
        new() { BindingState = binding, Enabled = false };

    public static StudioReportAiCapability Off { get; } = new() { Enabled = false };
}

public sealed record StudioReportAiProvider(string Id, string Label, string Kind, bool Available, string? Detail);

/// <summary>The result of a generate/refine turn.</summary>
public sealed record StudioReportGenerationOutcome
{
    /// <summary>Set only when the surface is unbound; the page shows the shared blocked state.</summary>
    public StudioReportCapabilityState? BindingState { get; init; }

    /// <summary>"generated" | "needs-clarification" | "unsupported" | "refused" | "error".</summary>
    public string Status { get; init; } = StudioReportGenerationStatuses.Error;

    /// <summary>The editor state with the server-proposed document applied; present iff status == generated.</summary>
    public StudioReportEditorState? State { get; init; }

    /// <summary>The Honua turn body to show (rationale, refusal reason, or error detail).</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Structured questions to render as cards; present iff status == needs-clarification.</summary>
    public IReadOnlyList<StudioConversationClarification> Clarifications { get; init; } = [];

    /// <summary>Non-blocking warnings (validator warnings + unmapped requests), surfaced not dropped.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public StudioReportGenerationCapability? CapabilityState { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public bool IsGenerated => string.Equals(Status, StudioReportGenerationStatuses.Generated, StringComparison.Ordinal);

    public bool NeedsClarification =>
        string.Equals(Status, StudioReportGenerationStatuses.NeedsClarification, StringComparison.Ordinal);

    public static StudioReportGenerationOutcome Blocked(StudioReportCapabilityState binding) =>
        new() { BindingState = binding, Status = StudioReportGenerationStatuses.Error };
}

public sealed record StudioReportGenerationCapability(string Name, string State, string? Reason);

public static class StudioReportGenerationStatuses
{
    public const string Generated = "generated";
    public const string NeedsClarification = "needs-clarification";
    public const string Unsupported = "unsupported";
    public const string Refused = "refused";
    public const string Error = "error";
}

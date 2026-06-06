using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// L0 self-service deflection assistant (honua-console#165). Calls the SAME
/// OpenAI-compatible <c>/v1/chat/completions</c> endpoint honua-support's L1
/// triage consumes (the Honua-GIS qwen model via NIM / vLLM / llama.cpp /
/// Ollama; see honua-gis-llm <c>docs/support/triage-endpoint-contract.md</c>),
/// one inference path, two entry points. The assistant is conversational
/// (concise, reproducible GIS help) rather than schema-locked to the
/// <c>submit_diagnosis</c> object the L1 path forces.
///
/// When no LLM endpoint is configured the surface degrades gracefully: the
/// <see cref="UnsupportedConsoleSupportAssistantClient"/> reports the neutral
/// unsupported state and the page hides the assistant (KB + form remain).
/// </summary>
public interface IConsoleSupportAssistantClient
{
    /// <summary>
    /// True when a live LLM endpoint is configured. The page hides the
    /// assistant box entirely when this is false rather than rendering a dead
    /// input, keeping KB search + the ticket form as the deflection path.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends the running transcript (system framing is prepended internally) to
    /// the chat-completions endpoint and returns the assistant reply.
    /// </summary>
    Task<SupportAssistantResult> AskAsync(
        IReadOnlyList<ChatCompletionMessage> conversation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of an assistant call. Reuses the neutral
/// <see cref="OperateSectionStatus"/> vocabulary (allowed / unsupported /
/// unavailable / forbidden / missing) so the assistant renders through the same
/// empty/error states as the rest of Console.
/// </summary>
public sealed record SupportAssistantResult
{
    public OperateSectionStatus Status { get; init; }

    public string? Reply { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool IsAllowed => Status == OperateSectionStatus.Allowed && !string.IsNullOrWhiteSpace(Reply);

    public static SupportAssistantResult Allowed(string reply) =>
        new() { Status = OperateSectionStatus.Allowed, Reply = reply };

    public static SupportAssistantResult Denied(OperateSectionStatus status, string message) =>
        new() { Status = status, Message = message };
}

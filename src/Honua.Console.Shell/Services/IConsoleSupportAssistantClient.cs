using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// The assistant shares the L1 chat endpoint but stays conversational, and it does not lock the reply to the submit_diagnosis schema.
public interface IConsoleSupportAssistantClient
{
    bool IsConfigured { get; }

    Task<SupportAssistantResult> AskAsync(
        IReadOnlyList<ChatCompletionMessage> conversation,
        CancellationToken cancellationToken = default);
}

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

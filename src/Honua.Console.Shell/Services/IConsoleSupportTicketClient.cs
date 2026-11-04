using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleSupportTicketClient
{
    Task<SupportTicketResult> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default);

    Task<SupportTicketResult> GetTicketAsync(
        string ticketId,
        CancellationToken cancellationToken = default);
}

public sealed record SupportTicketResult
{
    public OperateSectionStatus Status { get; init; }

    public SupportTicketResponse? Value { get; init; }

    public string Message { get; init; } = string.Empty;

    public TerminalFailureReceipt? Receipt { get; init; }

    public bool IsAllowed => Status == OperateSectionStatus.Allowed && Value is not null;

    public static SupportTicketResult Allowed(SupportTicketResponse value) =>
        new() { Status = OperateSectionStatus.Allowed, Value = value };

    public static SupportTicketResult Denied(
        OperateSectionStatus status,
        string message,
        TerminalFailureReceipt? receipt = null) =>
        new() { Status = status, Message = message, Receipt = receipt };
}

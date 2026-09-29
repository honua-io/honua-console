using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Submits and reads in-product support tickets against the honua-support API
/// (<c>POST/GET /api/v1/tickets</c>). Base address and bearer token come from the
/// active <see cref="Models.ConsoleEnvironmentProfile"/> and its account session,
/// mirroring <see cref="IConsoleOperateObservabilityClient"/>. The create call
/// auto-attaches captured context (session, environment, build, route, recent
/// errors) so customers ship context, not log dumps; the read call backs the
/// detail-page polling loop that closes in-product.
/// </summary>
public interface IConsoleSupportTicketClient
{
    Task<SupportTicketResult> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default);

    Task<SupportTicketResult> GetTicketAsync(
        string ticketId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a support ticket call. Reuses the neutral
/// <see cref="OperateSectionStatus"/> vocabulary (allowed / missing / forbidden /
/// unavailable / unsupported) so the support surface renders through the same
/// empty/error states as the rest of Console.
/// </summary>
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

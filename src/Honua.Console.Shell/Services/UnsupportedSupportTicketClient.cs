using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Active when no honua-support base URL is configured. Per the Console
/// missing-binding convention, the support surface renders an explicit
/// unsupported state rather than mocking a ticket round-trip.
/// </summary>
public sealed class UnsupportedSupportTicketClient : IConsoleSupportTicketClient
{
    private const string Message =
        "In-product support is not configured for this Console build. Set the honua-support base URL to enable ticket submission.";

    public Task<SupportTicketResult> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SupportTicketResult.Denied(OperateSectionStatus.Unsupported, Message));

    public Task<SupportTicketResult> GetTicketAsync(
        string ticketId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SupportTicketResult.Denied(OperateSectionStatus.Unsupported, Message));
}

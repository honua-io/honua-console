using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

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

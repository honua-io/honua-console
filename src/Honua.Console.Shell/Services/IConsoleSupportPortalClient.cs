using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleSupportPortalClient
{
    Task<SupportPortalResult<SupportPreflightResponse>> RunPreflightAsync(
        SupportPreflightRequest request,
        CancellationToken cancellationToken = default);

    Task<SupportPortalResult<IReadOnlyList<SupportTicketResponse>>> ListTicketsAsync(
        CancellationToken cancellationToken = default);

    Task<SupportPortalResult<SupportTicketResponse>> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default);

    Task<SupportPortalResult<SupportTicketResponse>> ApproveTicketAsync(
        string ticketId,
        SupportApproveTicketRequest request,
        CancellationToken cancellationToken = default);

    Task<SupportPortalResult<SupportRevokeAccessResponse>> RevokeAccessAsync(
        string ticketId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<SupportPortalResult<SupportSecurityAdvisoriesResponse>> ListAdvisoriesAsync(
        CancellationToken cancellationToken = default);

    Task<SupportPortalResult<SupportFleetInstancesResponse>> ListFleetInstancesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record SupportPortalResult<T>
{
    public OperateSectionStatus Status { get; init; }

    public T? Value { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool IsAllowed => Status == OperateSectionStatus.Allowed && Value is not null;

    public static SupportPortalResult<T> Allowed(T value) =>
        new() { Status = OperateSectionStatus.Allowed, Value = value };

    public static SupportPortalResult<T> Denied(OperateSectionStatus status, string message) =>
        new() { Status = status, Message = message };
}

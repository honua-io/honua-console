using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleSupportPortalClient : IConsoleSupportPortalClient
{
    internal const string SetupGuidance =
        "Support & Security Advisories is not configured for this Console host. " +
        "Set HonuaSupport:BaseUrl (or HONUA_SUPPORT_BASE_URL) and HonuaSupport:ApiKey (or HONUA_SUPPORT_API_KEY) on the host to enable it.";

    public Task<SupportPortalResult<SupportPreflightResponse>> RunPreflightAsync(
        SupportPreflightRequest request,
        CancellationToken cancellationToken = default) =>
        Unsupported<SupportPreflightResponse>();

    public Task<SupportPortalResult<IReadOnlyList<SupportTicketResponse>>> ListTicketsAsync(
        CancellationToken cancellationToken = default) =>
        Unsupported<IReadOnlyList<SupportTicketResponse>>();

    public Task<SupportPortalResult<SupportTicketResponse>> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default) =>
        Unsupported<SupportTicketResponse>();

    public Task<SupportPortalResult<SupportTicketResponse>> ApproveTicketAsync(
        string ticketId,
        SupportApproveTicketRequest request,
        CancellationToken cancellationToken = default) =>
        Unsupported<SupportTicketResponse>();

    public Task<SupportPortalResult<SupportRevokeAccessResponse>> RevokeAccessAsync(
        string ticketId,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        Unsupported<SupportRevokeAccessResponse>();

    public Task<SupportPortalResult<SupportSecurityAdvisoriesResponse>> ListAdvisoriesAsync(
        CancellationToken cancellationToken = default) =>
        Unsupported<SupportSecurityAdvisoriesResponse>();

    public Task<SupportPortalResult<SupportFleetInstancesResponse>> ListFleetInstancesAsync(
        CancellationToken cancellationToken = default) =>
        Unsupported<SupportFleetInstancesResponse>();

    private static Task<SupportPortalResult<T>> Unsupported<T>() =>
        Task.FromResult(SupportPortalResult<T>.Denied(OperateSectionStatus.Unsupported, SetupGuidance));
}

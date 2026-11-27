using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Pages;

public partial class OperateSupportAdvisoriesPage
{
    private readonly CancellationTokenSource _lifetime = new();

    private bool _disposed;
    private bool _loaded;
    private bool _loading;
    private string? _mutatingTicketId;

    private ConsoleSupportContext? _context;
    private string _actionMessage = string.Empty;
    private string _actionNoticeClass = string.Empty;

    private OperateSectionStatus _advisoriesStatus = OperateSectionStatus.Allowed;
    private string _advisoriesMessage = string.Empty;
    private List<SupportSecurityAdvisoryItem> _advisories = [];

    private OperateSectionStatus _ticketsStatus = OperateSectionStatus.Allowed;
    private string _ticketsMessage = string.Empty;
    private List<SupportTicketResponse> _tickets = [];

    private OperateSectionStatus _fleetStatus = OperateSectionStatus.Allowed;
    private string _fleetMessage = string.Empty;
    private List<SupportFleetInstanceItem> _fleetInstances = [];

    private bool IsPortalUnconfigured =>
        _advisoriesStatus == OperateSectionStatus.Unsupported
        && _ticketsStatus == OperateSectionStatus.Unsupported
        && _fleetStatus == OperateSectionStatus.Unsupported;

    private string UnconfiguredMessage =>
        FirstNonBlank(_advisoriesMessage, _ticketsMessage, _fleetMessage);

    protected override async Task OnInitializedAsync()
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        if (_disposed || _loading)
        {
            return;
        }

        _loading = true;
        try
        {
            var token = _lifetime.Token;
            var contextTask = CaptureContextSafeAsync(token);
            var advisoriesTask = PortalClient.ListAdvisoriesAsync(token);
            var ticketsTask = PortalClient.ListTicketsAsync(token);
            var fleetTask = PortalClient.ListFleetInstancesAsync(token);
            await Task.WhenAll(contextTask, advisoriesTask, ticketsTask, fleetTask);
            if (_disposed)
            {
                return;
            }

            _context = contextTask.Result;
            ApplyAdvisories(advisoriesTask.Result);
            ApplyTickets(ticketsTask.Result);
            ApplyFleet(fleetTask.Result);
            _loaded = true;
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task<ConsoleSupportContext?> CaptureContextSafeAsync(CancellationToken token)
    {
        try
        {
            return await ContextBuilder.CaptureAsync("/operate/support", token);
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private void HandleTicketCreated(SupportTicketResponse created)
    {
        _tickets.Insert(0, created);
        _ticketsStatus = OperateSectionStatus.Allowed;
        _actionMessage = $"Created support ticket {created.Id}.";
        _actionNoticeClass = "operate-support-notice-ok";
    }

    private async Task ApproveTicketAsync(SupportTicketResponse ticket)
    {
        if (_disposed || _mutatingTicketId is not null)
        {
            return;
        }

        _mutatingTicketId = ticket.Id;
        try
        {
            var payload = new SupportApproveTicketRequest
            {
                AccessMode = string.IsNullOrWhiteSpace(ticket.AllowedAccessMode) ? "operator-scoped" : ticket.AllowedAccessMode,
                TtlMinutes = ticket.TtlMinutes > 0 ? ticket.TtlMinutes : 60,
                Reason = "Approved by operator from Honua Console"
            };
            var result = await PortalClient.ApproveTicketAsync(ticket.Id, payload, _lifetime.Token);
            if (!_disposed)
            {
                ApplyTicketMutation(result, $"Approved access for ticket {ticket.Id}.");
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        finally
        {
            _mutatingTicketId = null;
        }
    }

    private async Task RevokeTicketAccessAsync(string ticketId)
    {
        if (_disposed || _mutatingTicketId is not null)
        {
            return;
        }

        _mutatingTicketId = ticketId;
        try
        {
            var result = await PortalClient.RevokeAccessAsync(ticketId, "Revoked by operator from Honua Console", _lifetime.Token);
            if (!_disposed)
            {
                ApplyRevokeOutcome(ticketId, result);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        finally
        {
            _mutatingTicketId = null;
        }
    }

    private void ApplyAdvisories(SupportPortalResult<SupportSecurityAdvisoriesResponse> result)
    {
        _advisoriesStatus = result.Status;
        _advisoriesMessage = result.Message;
        _advisories = result.IsAllowed ? result.Value!.Advisories.ToList() : [];
    }

    private void ApplyTickets(SupportPortalResult<IReadOnlyList<SupportTicketResponse>> result)
    {
        _ticketsStatus = result.Status;
        _ticketsMessage = result.Message;
        _tickets = result.IsAllowed ? result.Value!.ToList() : [];
    }

    private void ApplyFleet(SupportPortalResult<SupportFleetInstancesResponse> result)
    {
        _fleetStatus = result.Status;
        _fleetMessage = result.Message;
        _fleetInstances = result.IsAllowed ? result.Value!.Instances.ToList() : [];
    }

    private void ApplyTicketMutation(SupportPortalResult<SupportTicketResponse> result, string successMessage)
    {
        if (!result.IsAllowed)
        {
            _actionMessage = FallbackMessage(result.Status, result.Message);
            _actionNoticeClass = "operate-support-notice-danger";
            return;
        }

        ReplaceTicket(result.Value!);
        _actionMessage = successMessage;
        _actionNoticeClass = "operate-support-notice-ok";
    }

    private void ApplyRevokeOutcome(string ticketId, SupportPortalResult<SupportRevokeAccessResponse> result)
    {
        if (!result.IsAllowed)
        {
            _actionMessage = FallbackMessage(result.Status, result.Message);
            _actionNoticeClass = "operate-support-notice-danger";
            return;
        }

        if (result.Value?.Ticket is { } updated)
        {
            ReplaceTicket(updated);
        }

        _actionMessage = $"Revoked active support access for ticket {ticketId}.";
        _actionNoticeClass = "operate-support-notice-ok";
    }

    private void ReplaceTicket(SupportTicketResponse updated)
    {
        var index = _tickets.FindIndex(item => string.Equals(item.Id, updated.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            _tickets[index] = updated;
        }
    }

    private static string TicketDisplayStatus(SupportTicketResponse ticket) =>
        string.IsNullOrWhiteSpace(ticket.CustomerStatus) ? ticket.Phase : ticket.CustomerStatus;

    private static bool IsTerminalPhase(string? phase) =>
        phase is "resolved" or "cancelled" or "closed";

    private static string SeverityBadgeClass(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" or "sev1" => "operate-support-badge-danger",
        "high" or "sev2" => "operate-support-badge-warn",
        "medium" or "moderate" => "operate-support-badge-info",
        _ => "operate-support-badge-neutral"
    };

    private static string ShortDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return "unreported";
        }

        var trimmed = digest.Trim();
        return trimmed.Length > 19 ? trimmed[..19] : trimmed;
    }

    private static string FallbackMessage(OperateSectionStatus status, string message) =>
        string.IsNullOrWhiteSpace(message) ? OperateSectionPresentation.FallbackMessage(status) : message;

    private static string FirstNonBlank(params string[] candidates) =>
        candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? UnsupportedConsoleSupportPortalClient.SetupGuidance;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

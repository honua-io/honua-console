using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Pages;

public partial class OperateSupportAdvisoriesPage
{
    private readonly CancellationTokenSource _lifetime = new();

    private bool _disposed;
    private bool _loaded;
    private bool _loading;
    private bool _preflightBusy;
    private bool _submitting;
    private string? _mutatingTicketId;

    private string _severity = "sev3";
    private string _service = "honua-server";
    private string _requestedAction = string.Empty;
    private string _allowedAccessMode = "read-only";
    private int _ttlMinutes = 60;
    private string _symptoms = string.Empty;
    private bool _rollbackExpected;

    private ConsoleSupportContext? _context;
    private SupportPreflightResponse? _preflightResult;
    private string _formError = string.Empty;
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

    private string _unconfiguredMessage =>
        FirstNonBlank(_advisoriesMessage, _ticketsMessage, _fleetMessage);

    private bool CanRunPreflight => !string.IsNullOrWhiteSpace(_symptoms);

    private bool CanSubmitTicket =>
        !string.IsNullOrWhiteSpace(_symptoms) && !string.IsNullOrWhiteSpace(_requestedAction);

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

    private async Task RunPreflightAsync()
    {
        if (_disposed || _preflightBusy || !CanRunPreflight)
        {
            return;
        }

        _preflightBusy = true;
        _formError = string.Empty;
        try
        {
            var request = BuildPreflightRequest();
            var result = await PortalClient.RunPreflightAsync(request, _lifetime.Token);
            if (_disposed)
            {
                return;
            }

            if (result.IsAllowed)
            {
                _preflightResult = result.Value;
            }
            else
            {
                _formError = FallbackMessage(result.Status, result.Message);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        finally
        {
            _preflightBusy = false;
        }
    }

    private async Task SubmitTicketAsync()
    {
        if (_disposed || _submitting || !CanSubmitTicket)
        {
            return;
        }

        _submitting = true;
        _formError = string.Empty;
        try
        {
            var request = BuildCreateTicketRequest();
            var result = await PortalClient.CreateTicketAsync(request, _lifetime.Token);
            if (_disposed)
            {
                return;
            }

            ApplyTicketCreation(result);
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        finally
        {
            _submitting = false;
        }
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

    private SupportPreflightRequest BuildPreflightRequest() => new()
    {
        Severity = _severity,
        Environment = NormalizeOrDefault(_context?.EnvironmentKind, "production"),
        Service = NormalizeOrDefault(_service, "honua-server"),
        Symptoms = _symptoms.Trim(),
        Context = _context?.ToContextRequest()
    };

    private CreateSupportTicketRequest BuildCreateTicketRequest() => new()
    {
        Severity = _severity,
        Environment = NormalizeOrDefault(_context?.EnvironmentKind, "production"),
        Service = NormalizeOrDefault(_service, "honua-server"),
        Symptoms = _symptoms.Trim(),
        RequestedAction = _requestedAction.Trim(),
        AllowedAccessMode = _allowedAccessMode,
        TtlMinutes = _ttlMinutes < 5 ? 5 : _ttlMinutes,
        RollbackExpected = _rollbackExpected,
        InstanceUrl = _context?.ServerUri,
        CustomerId = string.IsNullOrWhiteSpace(_context?.TenantId) ? null : _context.TenantId,
        Context = _context?.ToContextRequest()
    };

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

    private void ApplyTicketCreation(SupportPortalResult<SupportTicketResponse> result)
    {
        if (!result.IsAllowed)
        {
            _formError = FallbackMessage(result.Status, result.Message);
            return;
        }

        _tickets.Insert(0, result.Value!);
        _ticketsStatus = OperateSectionStatus.Allowed;
        _requestedAction = string.Empty;
        _symptoms = string.Empty;
        _actionMessage = $"Created support ticket {result.Value!.Id}.";
        _actionNoticeClass = "operate-support-notice-ok";
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

    private static string AdvisoryStatusLabel(SupportSecurityAdvisoryItem advisory) =>
        string.IsNullOrWhiteSpace(advisory.CustomerStatus) ? advisory.VexStatus : advisory.CustomerStatus;

    private static string AdvisoryInstanceSummary(SupportSecurityAdvisoryItem advisory)
    {
        var affected = advisory.Instances.Count(i => string.Equals(i.Status, "affected", StringComparison.OrdinalIgnoreCase));
        var patched = advisory.Instances.Count(i => string.Equals(i.Status, "patched", StringComparison.OrdinalIgnoreCase));
        return $"{affected} affected · {patched} patched";
    }

    private static string SeverityBadgeClass(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" or "sev1" => "operate-support-badge-danger",
        "high" or "sev2" => "operate-support-badge-warn",
        "medium" or "moderate" => "operate-support-badge-info",
        _ => "operate-support-badge-neutral"
    };

    private static string AdvisoryStatusBadgeClass(string? status) => status?.ToLowerInvariant() switch
    {
        "affected" => "operate-support-badge-danger",
        "patched" or "fixed" or "not_affected" => "operate-support-badge-ok",
        "under_investigation" => "operate-support-badge-warn",
        _ => "operate-support-badge-neutral"
    };

    private static string FormatSeverity(string? severity) =>
        string.IsNullOrWhiteSpace(severity) ? "unknown" : severity.ToUpperInvariant();

    private static string ShortDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return "unreported";
        }

        var trimmed = digest.Trim();
        return trimmed.Length > 19 ? trimmed[..19] : trimmed;
    }

    private static string NormalizeOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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

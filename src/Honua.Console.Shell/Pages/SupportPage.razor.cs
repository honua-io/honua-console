using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Pages;

public partial class SupportPage
{
    private bool V4Enabled => Services.GetService<ConsoleV4Options>()?.Enabled == true;

    private const string SupportDescription = "Open a support ticket without copying log dumps. The Console auto-attaches who you are, the connected " +
        "environment, the running Console build, your current route, and the server's recent errors so support gets " +
        "context, not a paste of logs. You can review and edit everything before you submit.";

    private string _severity = "sev3";
    private string _service = "honua-server";
    private string _requestedAction = string.Empty;
    private string _allowedAccessMode = "read-only";
    private int _ttlMinutes = 60;
    private string _scopedKey = string.Empty;
    private string _symptoms = string.Empty;
    private bool _rollbackExpected;

    private ConsoleSupportContext? _context;
    private bool _busy;
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    private string _error = string.Empty;

    private SupportSelfServiceContext _selfService = new();
    private bool _ticketFiled;

    private bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_symptoms) && !string.IsNullOrWhiteSpace(_requestedAction);

    private SupportDeflectionActivity DeflectionActivity => new()
    {
        AssistantTurns = _selfService.AssistantTurns,
        KbItemsViewed = _selfService.ViewedKb.Count
    };

    private void OnSelfServiceContextChanged(SupportSelfServiceContext context)
    {
        if (!_disposed)
        {
            _selfService = context;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var context = await CaptureContextAsync();
            if (!_disposed)
            {
                _context = context;
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
    }

    private Task<ConsoleSupportContext> CaptureContextAsync()
    {
        var route = Nav.ToBaseRelativePath(Nav.Uri);
        return ContextBuilder.CaptureAsync(string.IsNullOrWhiteSpace(route) ? "/" : $"/{route}", _lifetime.Token);
    }

    private async Task SubmitAsync()
    {
        if (_disposed || _busy || !CanSubmit)
        {
            return;
        }

        _busy = true;
        _error = string.Empty;
        var symptoms = SubmissionSymptoms();
        try
        {
            var context = await CaptureContextAsync();
            if (_disposed)
            {
                return;
            }

            _context = context;
            var result = await SupportClient.CreateTicketAsync(CreateRequest(context, symptoms), _lifetime.Token);
            if (!_disposed)
            {
                CompleteSubmission(result);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        finally
        {
            _busy = false;
        }
    }

    private CreateSupportTicketRequest CreateRequest(ConsoleSupportContext capturedContext, string symptoms)
    {
        // The scoped key is a secret carried only on the structured context block
        // (context.scopedKey) for the auto-bundle telemetry pull — never folded
        // into symptoms or rendered back.
        var context = capturedContext.ToContextRequest(_scopedKey);

        return new CreateSupportTicketRequest
        {
            Severity = _severity,
            Environment = string.IsNullOrWhiteSpace(capturedContext.EnvironmentKind) ? "production" : capturedContext.EnvironmentKind,
            Service = string.IsNullOrWhiteSpace(_service) ? "honua-server" : _service.Trim(),
            Symptoms = symptoms,
            RequestedAction = _requestedAction.Trim(),
            AllowedAccessMode = _allowedAccessMode,
            TtlMinutes = _ttlMinutes < 5 ? 5 : _ttlMinutes,
            RollbackExpected = _rollbackExpected,
            InstanceUrl = capturedContext.ServerUri,
            CustomerId = string.IsNullOrWhiteSpace(capturedContext.TenantId) ? null : capturedContext.TenantId,
            Context = context
        };
    }

    private string SubmissionSymptoms()
    {
        var symptoms = _symptoms.Trim();
        var selfServiceContext = _selfService.ToSymptomContext();
        return string.IsNullOrWhiteSpace(selfServiceContext)
            ? symptoms
            : $"{symptoms}\n\n{selfServiceContext}";
    }

    private void CompleteSubmission(SupportTicketResult result)
    {
        if (result.IsAllowed)
        {
            _ticketFiled = true;
            DeflectionLog.ProceededToTicket(DeflectionActivity, result.Value!.Id);
            Nav.NavigateTo(Nav.ToConsoleHref($"/support/tickets/{result.Value.Id}"));
            return;
        }

        _error = string.IsNullOrWhiteSpace(result.Message)
            ? OperateSectionPresentation.FallbackMessage(result.Status)
            : result.Message;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (!_ticketFiled && (_selfService.AssistantTurns > 0 || _selfService.ViewedKb.Count > 0))
        {
            DeflectionLog.Deflected(DeflectionActivity);
        }
    }
}

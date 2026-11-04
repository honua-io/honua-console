using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

public interface IConsoleSupportDeflectionLog
{
    void Deflected(SupportDeflectionActivity activity);

    void ProceededToTicket(SupportDeflectionActivity activity, string? ticketId);
}

public sealed record SupportDeflectionActivity
{
    public int AssistantTurns { get; init; }

    public int KbItemsViewed { get; init; }

    public bool UsedAssistant => AssistantTurns > 0;

    public bool UsedKb => KbItemsViewed > 0;
}

public sealed class ConsoleSupportDeflectionLog : IConsoleSupportDeflectionLog
{
    private readonly ILogger<ConsoleSupportDeflectionLog> _logger;

    public ConsoleSupportDeflectionLog(ILogger<ConsoleSupportDeflectionLog> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Deflected(SupportDeflectionActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _logger.LogInformation(
            "Support L0 session outcome {SupportDeflectionOutcome}: assistantTurns={AssistantTurns} kbItemsViewed={KbItemsViewed} usedAssistant={UsedAssistant} usedKb={UsedKb}",
            "deflected",
            activity.AssistantTurns,
            activity.KbItemsViewed,
            activity.UsedAssistant,
            activity.UsedKb);
    }

    public void ProceededToTicket(SupportDeflectionActivity activity, string? ticketId)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _logger.LogInformation(
            "Support L0 session outcome {SupportDeflectionOutcome}: ticketId={TicketId} assistantTurns={AssistantTurns} kbItemsViewed={KbItemsViewed} usedAssistant={UsedAssistant} usedKb={UsedKb}",
            "ticket",
            ticketId ?? "(pending)",
            activity.AssistantTurns,
            activity.KbItemsViewed,
            activity.UsedAssistant,
            activity.UsedKb);
    }
}

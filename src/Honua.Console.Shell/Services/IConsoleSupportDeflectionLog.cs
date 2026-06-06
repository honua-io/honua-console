using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Records the outcome of an L0 self-service deflection session so the
/// deflection rate (assistant/KB resolved vs. proceeded to a ticket) is
/// measurable (honua-console#165). Uses the repo's existing logging approach —
/// structured <see cref="ILogger"/> events — rather than introducing a new
/// telemetry dependency; a deployment scrapes the structured fields
/// (<c>SupportDeflectionOutcome</c>, KB/assistant interaction counts) from logs.
/// </summary>
public interface IConsoleSupportDeflectionLog
{
    /// <summary>
    /// The session ended deflected: the user did not file a ticket after using
    /// the assistant and/or KB.
    /// </summary>
    void Deflected(SupportDeflectionActivity activity);

    /// <summary>
    /// The user proceeded to the ticket form after L0. <paramref name="ticketId"/>
    /// is the created ticket id when known.
    /// </summary>
    void ProceededToTicket(SupportDeflectionActivity activity, string? ticketId);
}

/// <summary>
/// How much L0 the user engaged with before the outcome, captured so the
/// deflection signal distinguishes "assistant/KB resolved it" from "left
/// without trying".
/// </summary>
public sealed record SupportDeflectionActivity
{
    public int AssistantTurns { get; init; }

    public int KbItemsViewed { get; init; }

    public bool UsedAssistant => AssistantTurns > 0;

    public bool UsedKb => KbItemsViewed > 0;
}

/// <summary>
/// Logs deflection outcomes as structured <see cref="ILogger"/> events. The
/// outcome string and interaction counts are individual log properties so they
/// can be aggregated into a deflection-rate metric downstream.
/// </summary>
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

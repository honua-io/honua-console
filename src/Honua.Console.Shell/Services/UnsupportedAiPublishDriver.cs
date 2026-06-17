using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The runtime default for the AI publish driver when no honua-server base address is configured. Every call
/// returns an honest unavailable/blocked outcome so AI mode renders a first-class "AI unavailable" surface
/// (Console Patterns Charter §11) — never a fabricated proposal and never a crash. Mirrors the other
/// <c>Unsupported*</c> implementations.
/// </summary>
public sealed class UnsupportedAiPublishDriver : IAiPublishDriver
{
    private const string MissingBinding =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so AI mode can reach the honua-server AI "
        + "generation surface. Manual mode works without a server binding for the steps it can drive.";

    public Task<AiPublishCapability> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AiPublishCapability.Off(MissingBinding));

    public Task<AiPublishOutcome> ProposeAsync(
        string intent,
        IReadOnlyList<AiPublishResourceRef> knownResources,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AiPublishOutcome.Blocked(MissingBinding));

    public Task RecordDecisionAsync(
        string? feedbackId,
        string action,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

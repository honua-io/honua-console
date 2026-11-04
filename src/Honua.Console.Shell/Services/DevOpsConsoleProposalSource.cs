using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleDevOpsProposalsClient
{
    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}

// Returns an empty allowed result so the inbox never shows a fabricated devops queue.
public sealed class UnavailableConsoleDevOpsProposalsClient : IConsoleDevOpsProposalsClient
{
    public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
            Array.Empty<ConsoleProposalSummary>()));
}

public sealed class DevOpsConsoleProposalSource : IConsoleProposalSource
{
    private readonly IConsoleDevOpsProposalsClient _devOps;

    public DevOpsConsoleProposalSource(IConsoleDevOpsProposalsClient devOps)
    {
        _devOps = devOps ?? throw new ArgumentNullException(nameof(devOps));
    }

    public ConsoleProposalSource Source => ConsoleProposalSource.DevOps;

    public async Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var listed = await _devOps.ListAsync(status, kind, cancellationToken).ConfigureAwait(false);

        if (!listed.IsAllowed)
        {
            return listed;
        }

        var tagged = (listed.Value ?? [])
            .Select(summary => summary with { Source = ConsoleProposalSource.DevOps })
            .ToArray();

        return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
            tagged,
            listed.PartialResult,
            listed.Message);
    }
}

public sealed record GitOpsProposalBridgeWire
{
    public string? ProposalId { get; init; }
    public string? OperationId { get; init; }

    // The bridge kind is gitops-deploy.
    public string? Kind { get; init; }

    // Maps to the server operation audit RequestedBy field.
    public string? Requester { get; init; }

    // The recording agent identity is the constant honua-devops.
    public string? Agent { get; init; }

    // Lifecycle values match the server workflow operation status.
    public string? ProposalStatus { get; init; }

    public GitOpsProposalPlanWire? Plan { get; init; }

    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
}

public sealed record GitOpsProposalPlanWire
{
    // Formatted as the action, service, environments, and revision.
    public string? DiffSummary { get; init; }

    public bool RequiresApproval { get; init; }

    // Coarse bridge vocabulary is high, elevated, or standard.
    public string? Risk { get; init; }
}

public static class ConsoleDevOpsProposalNormalization
{
    public static ConsoleProposalSummary MapSummary(GitOpsProposalBridgeWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);

        var created = ParseTimestamp(wire.CreatedAt);
        return new ConsoleProposalSummary(
            ProposalId: wire.ProposalId ?? wire.OperationId ?? string.Empty,
            Kind: ConsoleProposalPresentation.MapKind(wire.Kind),
            Status: ConsoleProposalPresentation.MapStatus(wire.ProposalStatus),
            RequestedBy: wire.Requester,
            RequestedByAgent: wire.Agent,
            Summary: wire.Plan?.DiffSummary ?? string.Empty,
            RiskLevel: MapDevOpsRisk(wire.Plan?.Risk),
            CreatedAt: created,
            UpdatedAt: ParseTimestamp(wire.UpdatedAt, fallback: created))
        {
            Source = ConsoleProposalSource.DevOps,
        };
    }

    public static ConsoleProposalRisk MapDevOpsRisk(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "high" => ConsoleProposalRisk.High,
            "elevated" => ConsoleProposalRisk.Medium,
            "standard" => ConsoleProposalRisk.Low,
            // Tolerate the server risk vocabulary too, so a #1692-aggregated record maps cleanly.
            "medium" => ConsoleProposalRisk.Medium,
            "low" => ConsoleProposalRisk.Low,
            _ => ConsoleProposalRisk.Unknown,
        };
    }

    private static DateTimeOffset ParseTimestamp(string? raw, DateTimeOffset? fallback = null) =>
        DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : fallback ?? DateTimeOffset.UnixEpoch;
}

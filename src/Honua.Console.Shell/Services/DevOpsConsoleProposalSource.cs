using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the honua-devops-owned gitops/infra + deliverable proposals for the aggregated approval
/// inbox (issue #193, honua-server #1690 ownership split; devops contract in honua-devops
/// <c>docs/gitops-proposal-contract.md</c> / <c>docs/console-ai-devops-bridge.md</c>). honua-devops
/// owns these proposals via its console bridge (<c>create_gitops_proposal</c> /
/// <c>get_gitops_proposal</c> / <c>get_devops_operation_status</c> /
/// <c>record_gitops_proposal_decision</c>) and projects each as a <c>GitOpsProposalBridge</c> that
/// aligns field-for-field with the server <c>OperationProposal</c> shape, precisely so the console
/// can aggregate both sources with one model.
///
/// IMPORTANT (state of the world, 2026-06): there is NO console-facing HTTP endpoint on
/// honua-devops today. honua-devops is a CLI / MCP-stdio agent host; its bridge projections are
/// returned in-process on <c>OperationResponse.ConsoleBridge</c> (<c>[JsonIgnore]</c>) to MCP/agent
/// callers only, and the sole HTTP listener it exposes (<c>--listen</c>) is the signed honua-support
/// escalation receiver, not a proposals API. The bridge DOES create durable honua-server
/// deploy-control operations (<c>submitImmediately=false</c>), but honua-server #1692's typed
/// <c>OperationProposal</c> projection that would let those surface through the server proposals
/// list (<c>GET /api/v1/admin/proposals</c>) is still open — so there is no wire the console can
/// consume for devops proposals yet.
///
/// Therefore the DEFAULT implementation (<see cref="UnavailableConsoleDevOpsProposalsClient"/>)
/// degrades gracefully to an empty allowed result: the inbox aggregation seam and the
/// normalization (<see cref="ConsoleDevOpsProposalNormalization"/>) are fully in place, so the day
/// honua-devops (or honua-server via the #1692 adapter) exposes a readable list of
/// <c>GitOpsProposalBridge</c> records, an HTTP implementation of this interface drops in with no
/// change to the inbox. See the class-level docs for exactly what devops-side endpoint is needed.
/// </summary>
public interface IConsoleDevOpsProposalsClient
{
    /// <summary>
    /// Lists the honua-devops-owned gitops/deliverable proposals as the shared summary
    /// projection (tagged <see cref="ConsoleProposalSource.DevOps"/>). Optionally filtered by the
    /// bridge's status/kind parameters.
    /// </summary>
    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IConsoleDevOpsProposalsClient"/> for the merged build: honua-devops does
/// not expose a console-facing proposals endpoint yet (see <see cref="IConsoleDevOpsProposalsClient"/>),
/// so this returns an empty allowed result. The devops source is therefore present in the
/// aggregation seam but contributes nothing until the endpoint lands — the inbox stays fully
/// driven by the reachable server source, and no fabricated queue is ever shown (charter §11).
///
/// What is needed to make this source live (documented, not fabricated):
/// honua-devops must expose an authenticated, read-only, console-facing HTTP list endpoint (e.g.
/// <c>GET {devops-base}/api/console-bridge/gitops-proposals</c>) that returns the already-defined
/// <c>GitOpsProposalBridge</c> records (from <c>get_gitops_proposal</c>) as JSON — OR honua-server
/// #1692's <c>OperationProposal</c> projection must include gitops-deploy operations in
/// <c>GET /api/v1/admin/proposals</c> tagged with their devops provenance. When either exists,
/// replace this default with an <see cref="System.Net.Http.IHttpClientFactory"/>-backed client
/// (built through <see cref="HonuaServerClientFactory"/>, never a fail-open singleton) that maps
/// the wire records via <see cref="ConsoleDevOpsProposalNormalization"/>.
/// </summary>
public sealed class UnavailableConsoleDevOpsProposalsClient : IConsoleDevOpsProposalsClient
{
    public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
            Array.Empty<ConsoleProposalSummary>()));
}

/// <summary>
/// The honua-devops proposal source: adapts <see cref="IConsoleDevOpsProposalsClient"/> onto the
/// shared <see cref="IConsoleProposalSource"/> seam, tagging every summary
/// <see cref="ConsoleProposalSource.DevOps"/>. A supplementary source: if it is unavailable the
/// inbox degrades gracefully and still renders the server source (the client returns an empty
/// allowed result by default, so no degradation is even visible until a live client is wired).
/// </summary>
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

/// <summary>
/// The wire shape of a honua-devops console-bridge gitops proposal (<c>GitOpsProposalBridge</c>,
/// honua-devops <c>docs/gitops-proposal-contract.md</c>). This mirrors the canonical
/// <c>OperationProposal</c>-aligned fields the console needs; the full bridge record carries more
/// (evidence, workflow links, suggested actions) that the inbox summary does not require.
/// </summary>
public sealed record GitOpsProposalBridgeWire
{
    public string? ProposalId { get; init; }
    public string? OperationId { get; init; }

    /// <summary>Always <c>gitops-deploy</c> for this bridge.</summary>
    public string? Kind { get; init; }

    /// <summary>The human/owner that requested the proposal (server <c>OperationAuditInfo.RequestedBy</c>).</summary>
    public string? Requester { get; init; }

    /// <summary>The agent identity that recorded the proposal (constant <c>honua-devops</c>).</summary>
    public string? Agent { get; init; }

    /// <summary>The canonical lifecycle value (1:1 with the server <c>WorkflowOperationStatus</c>).</summary>
    public string? ProposalStatus { get; init; }

    public GitOpsProposalPlanWire? Plan { get; init; }

    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
}

/// <summary>The devops proposal plan (<c>ProposalPlan</c>) — the diff/risk/approval posture.</summary>
public sealed record GitOpsProposalPlanWire
{
    /// <summary>Human-readable change summary (<c>{action} {service} -> {envs} @ {revision}</c>).</summary>
    public string? DiffSummary { get; init; }

    public bool RequiresApproval { get; init; }

    /// <summary>Coarse risk: <c>high</c> / <c>elevated</c> / <c>standard</c>.</summary>
    public string? Risk { get; init; }
}

/// <summary>
/// Normalizes a honua-devops <see cref="GitOpsProposalBridgeWire"/> onto the shared
/// <see cref="ConsoleProposalSummary"/> so devops proposals aggregate into the same inbox as
/// server proposals with one model (issue #193). The lifecycle and kind reuse the shared
/// <see cref="ConsoleProposalPresentation"/> mappers; the only devops-specific mapping is the
/// coarse risk vocabulary (<c>standard</c>/<c>elevated</c>/<c>high</c> ⇒ low/medium/high).
/// </summary>
public static class ConsoleDevOpsProposalNormalization
{
    /// <summary>Maps a devops bridge proposal onto the shared summary projection (tagged DevOps).</summary>
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

    /// <summary>
    /// Maps the devops coarse risk vocabulary onto the shared risk level. The bridge uses
    /// <c>standard</c> / <c>elevated</c> / <c>high</c>; an unrecognized value maps to Unknown
    /// (never guessed).
    /// </summary>
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

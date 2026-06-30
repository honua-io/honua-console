using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Test double for <see cref="IConsoleProposalsClient"/> used by the Docker-free approval
/// render regressions (#193). A live page binds to a server through this contract (charter
/// §11); this fake stands in for the honua-server proposals API so the page can be driven
/// without a network. It can be seeded with a list, per-id detail, a denied list status, and
/// a forbidden approve/reject (the RBAC approve-gate path), and records the decisions made.
/// </summary>
public sealed class FakeConsoleProposalsClient : IConsoleProposalsClient
{
    private readonly OperateSectionStatus? _deniedListStatus;
    private readonly string _deniedListMessage;
    private readonly Dictionary<string, ConsoleProposalSummary> _summaries;
    private readonly Dictionary<string, ConsoleProposalDetail> _details;

    public FakeConsoleProposalsClient(
        IReadOnlyList<ConsoleProposalSummary>? proposals = null,
        IEnumerable<ConsoleProposalDetail>? details = null,
        OperateSectionStatus? deniedListStatus = null,
        string deniedListMessage = "",
        bool approveForbidden = false)
    {
        _deniedListStatus = deniedListStatus;
        _deniedListMessage = deniedListMessage;
        _summaries = (proposals ?? []).ToDictionary(p => p.ProposalId, StringComparer.Ordinal);
        _details = (details ?? []).ToDictionary(d => d.ProposalId, StringComparer.Ordinal);
        ApproveForbidden = approveForbidden;
    }

    public bool ApproveForbidden { get; set; }

    public List<string> Approved { get; } = [];

    public List<(string ProposalId, string Reason)> Rejected { get; } = [];

    public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null, string? kind = null, string? requestedBy = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_deniedListStatus is { } denied
            ? OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(denied, _deniedListMessage)
            : OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
                _summaries.Values.OrderBy(p => p.ProposalId, StringComparer.Ordinal).ToArray()));

    public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
        string proposalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_details.TryGetValue(proposalId, out var detail)
            ? OperateSectionResult<ConsoleProposalDetail>.Allowed(detail)
            : OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "Proposal not found."));

    public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
        string proposalId, CancellationToken cancellationToken = default)
    {
        if (ApproveForbidden)
        {
            return Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Forbidden,
                "Approving an operation proposal requires the 'approve' permission."));
        }

        Approved.Add(proposalId);
        var detail = _details[proposalId] with { Status = ConsoleProposalStatus.Submitted };
        _details[proposalId] = detail;
        UpdateSummaryStatus(proposalId, ConsoleProposalStatus.Submitted);
        return Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Allowed(detail));
    }

    public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
        string proposalId, string reason, CancellationToken cancellationToken = default)
    {
        if (ApproveForbidden)
        {
            return Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(
                OperateSectionStatus.Forbidden,
                "Approving an operation proposal requires the 'approve' permission."));
        }

        Rejected.Add((proposalId, reason));
        var detail = _details[proposalId] with { Status = ConsoleProposalStatus.Rejected, ResolutionReason = reason };
        _details[proposalId] = detail;
        UpdateSummaryStatus(proposalId, ConsoleProposalStatus.Rejected);
        return Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Allowed(detail));
    }

    private void UpdateSummaryStatus(string proposalId, ConsoleProposalStatus status)
    {
        if (_summaries.TryGetValue(proposalId, out var summary))
        {
            _summaries[proposalId] = summary with { Status = status };
        }
    }
}

/// <summary>
/// Test double for <see cref="IConsoleProposalRealtimeClient"/>: lets a test raise
/// <see cref="ConsoleProposalEvent"/>s to drive the inbox's live-update path without a real
/// SignalR hub, and records whether the page started/stopped the subscription.
/// </summary>
public sealed class FakeConsoleProposalRealtimeClient : IConsoleProposalRealtimeClient
{
    public event Action<ConsoleProposalEvent>? ProposalChanged;

    public bool IsConnected { get; set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public void Raise(ConsoleProposalEvent evt) => ProposalChanged?.Invoke(evt);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Shared builders for proposal projections in the render regressions.</summary>
public static class FakeProposalFactory
{
    public static ConsoleProposalSummary Summary(
        string id,
        ConsoleProposalKind kind,
        ConsoleProposalStatus status = ConsoleProposalStatus.AwaitingApproval,
        string summary = "summary",
        ConsoleProposalRisk risk = ConsoleProposalRisk.Low) => new(
        ProposalId: id,
        Kind: kind,
        Status: status,
        RequestedBy: "agent.ingest",
        RequestedByAgent: "agent.ingest",
        Summary: summary,
        RiskLevel: risk,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    public static ConsoleProposalDetail Detail(
        string id,
        ConsoleProposalKind kind,
        ConsoleProposalStatus status = ConsoleProposalStatus.AwaitingApproval,
        string summary = "summary",
        ConsoleProposalRisk risk = ConsoleProposalRisk.Low,
        IReadOnlyList<string>? diff = null,
        IReadOnlyList<string>? dryRun = null,
        IReadOnlyList<string>? blockers = null,
        IReadOnlyList<string>? warnings = null) => new(
        ProposalId: id,
        Kind: kind,
        Status: status,
        RequestedBy: "agent.ingest",
        RequestedByAgent: "agent.ingest",
        Summary: summary,
        Diff: diff ?? [],
        DryRun: dryRun ?? [],
        RiskLevel: risk,
        BlockingReasons: blockers ?? [],
        Warnings: warnings ?? [],
        GuardrailTier: "RequiresApproval",
        ResolvedBy: null,
        ResolutionReason: null,
        ExecutionOperationId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        ResolvedAt: null);
}

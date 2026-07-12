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
/// Test double for <see cref="IConsoleProposalRealtimeClient"/> (and the shared capability seam
/// <see cref="IConsoleRealtimeCapabilityClient"/>, console#293): lets a test raise
/// <see cref="ConsoleProposalEvent"/>s to drive the inbox's live-update path, and drive the
/// connection state (connect / degrade / reconnect) to exercise the freshness affordance
/// (console#309), without a real SignalR hub. Records whether the page started/stopped the
/// subscription.
/// </summary>
public sealed class FakeConsoleProposalRealtimeClient : IConsoleProposalRealtimeClient, IConsoleRealtimeCapabilityClient
{
    private ConsoleRealtimeConnectionState _state = ConsoleRealtimeConnectionState.NotConfigured;

    public event Action<ConsoleProposalEvent>? ProposalChanged;

    public event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged;

    /// <summary>Whether <see cref="StartAsync"/> connects (default) or degrades to fallback.</summary>
    public bool ConnectOnStart { get; set; } = true;

    public ConsoleRealtimeConnectionState ConnectionState => _state;

    public bool IsConnected => _state == ConsoleRealtimeConnectionState.Connected;

    public bool IsFallbackEngaged => _state == ConsoleRealtimeConnectionState.FallbackEngaged;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        SetConnectionState(ConnectOnStart
            ? ConsoleRealtimeConnectionState.Connected
            : ConsoleRealtimeConnectionState.FallbackEngaged);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCount++;
        SetConnectionState(ConsoleRealtimeConnectionState.NotConfigured);
        return Task.CompletedTask;
    }

    /// <summary>Drives a connection-state transition, raising <see cref="ConnectionStateChanged"/>.</summary>
    public void SetConnectionState(ConsoleRealtimeConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        ConnectionStateChanged?.Invoke(state);
    }

    public void Raise(ConsoleProposalEvent evt) => ProposalChanged?.Invoke(evt);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A queue read that never completes, to exercise the ~5s bounded-loading budget (console#308):
/// the surface must resolve to its explicit error card rather than spin on "Loading…" forever.
/// </summary>
public sealed class HangingApprovalInboxClient : IConsoleApprovalInboxClient
{
    public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null, string? kind = null, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }
}

/// <summary>
/// A queue read that returns an Unavailable error a fixed number of times, then succeeds — models
/// a backend that recovers so a Retry after an error card can resolve back to the queue (console#308).
/// </summary>
public sealed class RecoveringApprovalInboxClient(int failuresBeforeSuccess, ApprovalInboxSnapshot onRecovery)
    : IConsoleApprovalInboxClient
{
    private int _remainingFailures = failuresBeforeSuccess;

    public int CallCount { get; private set; }

    public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null, string? kind = null, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            return Task.FromResult(OperateSectionResult<ApprovalInboxSnapshot>.Denied(
                OperateSectionStatus.Unavailable, "The honua-server admin API returned 500."));
        }

        return Task.FromResult(OperateSectionResult<ApprovalInboxSnapshot>.Allowed(onRecovery));
    }
}

/// <summary>
/// A queue read whose result a test flips between calls — for asserting the persistent
/// last-successful-refresh marker survives a later failure (console#308).
/// </summary>
public sealed class ScriptedApprovalInboxClient : IConsoleApprovalInboxClient
{
    public OperateSectionResult<ApprovalInboxSnapshot> Result { get; set; } =
        OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty);

    public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null, string? kind = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result);
}

/// <summary>Shared builders for proposal projections in the render regressions.</summary>
public static class FakeProposalFactory
{
    /// <summary>Projects proposals into an inbox snapshot, classified onto GIS-desk ticket types.</summary>
    public static ApprovalInboxSnapshot Snapshot(params ConsoleProposalSummary[] proposals) =>
        new(proposals
            .Select(p => new ApprovalInboxItem(ApprovalTicketPresentation.Classify(p), p))
            .ToArray());

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

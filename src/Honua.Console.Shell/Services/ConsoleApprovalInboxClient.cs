using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Default <see cref="IConsoleApprovalInboxClient"/>: aggregates every proposal source into one
/// GIS-department work queue (issue #193). The console surfaces proposals from BOTH the
/// honua-server proposals API (admin/deploy/metadata/seed, honua-server #1694) and the
/// honua-devops console-bridge gitops/deliverable proposals — honua-server #1690's locked
/// ownership split — behind the shared <see cref="IConsoleProposalSource"/> seam. Each source
/// normalizes its domain onto the shared <see cref="ConsoleProposalSummary"/> (tagged by source);
/// this projection merges them, adds the GIS-desk ticket-type classification, and orders the
/// queue awaiting-approval-first.
///
/// All data stays owned by the originating system; the projection never fabricates or mutates
/// proposals (charter §11). Aggregation is fault-isolating: reachable sources still render when a
/// supplementary source is unavailable (surfaced as a partial result), and the inbox denies only
/// when NO source is reachable — carrying the primary (server) source's denial verbatim so the
/// existing missing/forbidden/unsupported/unavailable surfaces are preserved.
/// </summary>
public sealed class ConsoleApprovalInboxClient : IConsoleApprovalInboxClient
{
    private readonly IReadOnlyList<IConsoleProposalSource> _sources;

    /// <summary>
    /// Aggregates the given proposal sources. The server source is ordered first so it is the
    /// primary: its denial is the inbox's denial when no source is reachable.
    /// </summary>
    public ConsoleApprovalInboxClient(IEnumerable<IConsoleProposalSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources
            .Where(source => source is not null)
            .OrderBy(source => (int)source.Source)
            .ToArray();

        if (_sources.Count == 0)
        {
            throw new ArgumentException("At least one proposal source is required.", nameof(sources));
        }
    }

    /// <summary>
    /// Back-compat convenience: aggregate over just the honua-server proposals API. Equivalent to
    /// a single <see cref="ServerConsoleProposalSource"/>.
    /// </summary>
    public ConsoleApprovalInboxClient(IConsoleProposalsClient proposals)
        : this([new ServerConsoleProposalSource(proposals)])
    {
    }

    public async Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(IConsoleProposalSource Source, OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>> Result)>(_sources.Count);
        foreach (var source in _sources)
        {
            var listed = await source.ListAsync(status, kind, cancellationToken).ConfigureAwait(false);
            results.Add((source, listed));
        }

        var allowed = results.Where(entry => entry.Result.IsAllowed).ToArray();
        if (allowed.Length == 0)
        {
            // No source is reachable: surface the primary (server-first) denial verbatim so the
            // shared missing / forbidden / unsupported / unavailable surfaces are preserved. The
            // inbox never fabricates a queue (charter §11).
            var primary = results[0].Result;
            return OperateSectionResult<ApprovalInboxSnapshot>.Denied(primary.Status, primary.Message, primary.Detail);
        }

        var items = allowed
            .SelectMany(entry => entry.Result.Value ?? [])
            .Select(proposal => new ApprovalInboxItem(ApprovalTicketPresentation.Classify(proposal), proposal))
            // Actionable work first, then most-recently-updated, for a stable work-queue order
            // across both sources.
            .OrderByDescending(item => item.IsAwaitingApproval)
            .ThenByDescending(item => item.Proposal.UpdatedAt)
            .ToArray();

        var denied = results.Where(entry => !entry.Result.IsAllowed).ToArray();
        var partial = denied.Length > 0 || allowed.Any(entry => entry.Result.PartialResult);
        var message = BuildPartialMessage(allowed, denied);

        return OperateSectionResult<ApprovalInboxSnapshot>.Allowed(
            new ApprovalInboxSnapshot(items),
            partial,
            message);
    }

    private static string BuildPartialMessage(
        IReadOnlyList<(IConsoleProposalSource Source, OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>> Result)> allowed,
        IReadOnlyList<(IConsoleProposalSource Source, OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>> Result)> denied)
    {
        if (denied.Count > 0)
        {
            var degraded = string.Join(
                ", ",
                denied.Select(entry => ConsoleProposalPresentation.SourceLabel(entry.Source.Source)));
            return $"Showing proposals from the reachable sources; the {degraded} proposal source is unavailable.";
        }

        // No source failed outright, but an allowed source flagged its own partial read: carry the
        // first non-empty upstream message so the annotation is honest.
        return allowed
            .Where(entry => entry.Result.PartialResult && !string.IsNullOrWhiteSpace(entry.Result.Message))
            .Select(entry => entry.Result.Message)
            .FirstOrDefault() ?? string.Empty;
    }
}

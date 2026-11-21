using System.Text.RegularExpressions;

namespace Honua.Console.Shell.Models;

public enum ConsoleProposalKind
{
    Unknown,

    AdminConfigChange,

    Deploy,

    MetadataRelease,

    Seed,

    DataImport,

    Map,

    Analysis,

    Dashboard,

    App,
}

public enum ConsoleProposalSource
{
    Server,

    DevOps,
}

public enum ConsoleProposalStatus
{
    Unknown,

    Planned,

    AwaitingApproval,

    Submitted,

    Executing,

    Completed,

    Cancelled,

    Indeterminate,

    Reconciling,

    Succeeded,

    Failed,

    Rejected,

    RolledBack,
}

public enum ConsoleProposalRisk
{
    Unknown,

    Low,

    Medium,

    High,
}

public sealed record ConsoleProposalSummary(
    string ProposalId,
    ConsoleProposalKind Kind,
    ConsoleProposalStatus Status,
    string? RequestedBy,
    string? RequestedByAgent,
    string Summary,
    ConsoleProposalRisk RiskLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public ConsoleProposalSource Source { get; init; } = ConsoleProposalSource.Server;

    public string? FindingId { get; init; }

    public string? AutonomyRule { get; init; }

    public string? ActionDiscriminator { get; init; }

    public bool IsAwaitingApproval => Status == ConsoleProposalStatus.AwaitingApproval;

    public bool IsTerminal => Status
        is ConsoleProposalStatus.Succeeded
        or ConsoleProposalStatus.Completed
        or ConsoleProposalStatus.Cancelled
        or ConsoleProposalStatus.Indeterminate
        or ConsoleProposalStatus.Failed
        or ConsoleProposalStatus.Rejected
        or ConsoleProposalStatus.RolledBack;
}

public sealed record ConsoleProposalDetail(
    string ProposalId,
    ConsoleProposalKind Kind,
    ConsoleProposalStatus Status,
    string? RequestedBy,
    string? RequestedByAgent,
    string Summary,
    IReadOnlyList<string> Diff,
    IReadOnlyList<string> DryRun,
    ConsoleProposalRisk RiskLevel,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    string? GuardrailTier,
    string? ResolvedBy,
    string? ResolutionReason,
    string? ExecutionOperationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt)
{
    public ConsoleProposalSource Source { get; init; } = ConsoleProposalSource.Server;

    // Server proposal version and plan hash; zero/null when the server does not report them.
    public long Version { get; init; }

    public string? PlanHash { get; init; }

    public string? OperationId { get; init; }

    public string? Initiation { get; init; }

    public string? ApprovalChannel { get; init; }

    public bool RequiresReproposal { get; init; }

    public string? ApprovalRemediation { get; init; }

    public string? ExecutionPayload { get; init; }

    public string? FindingId { get; init; }

    public string? AutonomyRule { get; init; }

    public string? ActionDiscriminator { get; init; }

    public bool IsAwaitingApproval => Status == ConsoleProposalStatus.AwaitingApproval;

    public bool IsTerminal => Status
        is ConsoleProposalStatus.Succeeded
        or ConsoleProposalStatus.Completed
        or ConsoleProposalStatus.Cancelled
        or ConsoleProposalStatus.Indeterminate
        or ConsoleProposalStatus.Failed
        or ConsoleProposalStatus.Rejected
        or ConsoleProposalStatus.RolledBack;

    public ConsoleProposalSummary ToSummary() => new(
        ProposalId,
        Kind,
        Status,
        RequestedBy,
        RequestedByAgent,
        Summary,
        RiskLevel,
        CreatedAt,
        UpdatedAt)
    {
        Source = Source,
        FindingId = FindingId,
        AutonomyRule = AutonomyRule,
        ActionDiscriminator = ActionDiscriminator,
    };
}

public enum ConsoleProposalEventKind
{
    Pending,

    Resolved,
}

// Carries the inbox fields only, never the execution payload.
public sealed record ConsoleProposalEvent(
    ConsoleProposalEventKind EventKind,
    string ProposalId,
    ConsoleProposalKind Kind,
    ConsoleProposalStatus Status,
    string? RequestedBy,
    ConsoleProposalRisk RiskLevel,
    DateTimeOffset GeneratedAt);

public static class ConsoleProposalPresentation
{
    public static ConsoleProposalKind MapKind(string? raw) => Normalize(raw) switch
    {
        "adminconfigchange" => ConsoleProposalKind.AdminConfigChange,
        "deploy" => ConsoleProposalKind.Deploy,
        "metadatarelease" => ConsoleProposalKind.MetadataRelease,
        "seed" => ConsoleProposalKind.Seed,
        // The data-import GP job (honua-server #1630) may surface under any of these
        // server-emitted kind strings; the console parses the wire value (never prose).
        "dataimport" or "importdataset" or "import" => ConsoleProposalKind.DataImport,
        // Deliverable-request kinds the agent-first console proposes (map / analysis /
        // dashboard / app). These arrive from the honua-devops deliverable lifecycle bridge
        // (and any server-emitted deliverable proposal); the console parses the wire value.
        "map" => ConsoleProposalKind.Map,
        "analysis" => ConsoleProposalKind.Analysis,
        "dashboard" => ConsoleProposalKind.Dashboard,
        "app" or "application" => ConsoleProposalKind.App,
        // The honua-devops gitops proposal bridge emits kind "gitops-deploy"; it is a deploy
        // of a target revision through the control plane, so it maps onto the Deploy kind (the
        // ConsoleProposalSource.DevOps tag carries the gitops/infra provenance).
        "gitopsdeploy" or "gitops" => ConsoleProposalKind.Deploy,
        _ => ConsoleProposalKind.Unknown,
    };

    public static ConsoleProposalStatus MapStatus(string? raw) => Normalize(raw) switch
    {
        "planned" => ConsoleProposalStatus.Planned,
        "awaitingapproval" => ConsoleProposalStatus.AwaitingApproval,
        "submitted" => ConsoleProposalStatus.Submitted,
        "executing" => ConsoleProposalStatus.Executing,
        "completed" => ConsoleProposalStatus.Completed,
        "cancelled" => ConsoleProposalStatus.Cancelled,
        "indeterminate" => ConsoleProposalStatus.Indeterminate,
        "reconciling" => ConsoleProposalStatus.Reconciling,
        "succeeded" => ConsoleProposalStatus.Succeeded,
        "failed" => ConsoleProposalStatus.Failed,
        "rejected" => ConsoleProposalStatus.Rejected,
        "rolledback" => ConsoleProposalStatus.RolledBack,
        _ => ConsoleProposalStatus.Unknown,
    };

    public static ConsoleProposalRisk MapRisk(string? raw) => Normalize(raw) switch
    {
        "low" => ConsoleProposalRisk.Low,
        "medium" => ConsoleProposalRisk.Medium,
        "high" => ConsoleProposalRisk.High,
        _ => ConsoleProposalRisk.Unknown,
    };

    public static string KindLabel(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.AdminConfigChange => "Admin config change",
        ConsoleProposalKind.Deploy => "Deploy",
        ConsoleProposalKind.MetadataRelease => "Metadata release",
        ConsoleProposalKind.Seed => "Seed",
        ConsoleProposalKind.DataImport => "Data import",
        ConsoleProposalKind.Map => "Map",
        ConsoleProposalKind.Analysis => "Analysis",
        ConsoleProposalKind.Dashboard => "Dashboard",
        ConsoleProposalKind.App => "App",
        _ => "Unknown",
    };

    public static string StatusLabel(ConsoleProposalStatus status) => status switch
    {
        ConsoleProposalStatus.Planned => "planned",
        ConsoleProposalStatus.AwaitingApproval => "awaiting approval",
        ConsoleProposalStatus.Submitted => "submitted",
        ConsoleProposalStatus.Executing => "executing",
        ConsoleProposalStatus.Completed => "completed",
        ConsoleProposalStatus.Cancelled => "cancelled",
        ConsoleProposalStatus.Indeterminate => "indeterminate",
        ConsoleProposalStatus.Reconciling => "reconciling",
        ConsoleProposalStatus.Succeeded => "succeeded",
        ConsoleProposalStatus.Failed => "failed",
        ConsoleProposalStatus.Rejected => "rejected",
        ConsoleProposalStatus.RolledBack => "rolled back",
        _ => "unknown",
    };

    public static string StatusClass(ConsoleProposalStatus status) => ToStatus(status).CssClass;

    public static OperateStatus ToStatus(ConsoleProposalStatus status) =>
        new(StatusLabel(status), string.Empty);

    public static string RiskLabel(ConsoleProposalRisk risk) => risk switch
    {
        ConsoleProposalRisk.Low => "low risk",
        ConsoleProposalRisk.Medium => "medium risk",
        ConsoleProposalRisk.High => "high risk",
        _ => "risk unknown",
    };

    public static string RiskClass(ConsoleProposalRisk risk) => risk switch
    {
        ConsoleProposalRisk.High => "console-state-danger",
        ConsoleProposalRisk.Medium => "console-state-warning",
        ConsoleProposalRisk.Low => "console-state-success",
        _ => "console-state-neutral",
    };

    public static bool IsDeliverable(ConsoleProposalKind kind) => kind
        is ConsoleProposalKind.Map
        or ConsoleProposalKind.Analysis
        or ConsoleProposalKind.Dashboard
        or ConsoleProposalKind.App;

    public static string CardKicker(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.DataImport => "Data import approval",
        ConsoleProposalKind.Map => "Map request approval",
        ConsoleProposalKind.Analysis => "Analysis request approval",
        ConsoleProposalKind.Dashboard => "Dashboard request approval",
        ConsoleProposalKind.App => "App request approval",
        _ => "Proposal review",
    };

    public static string? CardVariantClass(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.DataImport => "is-data-import",
        ConsoleProposalKind.Map => "is-deliverable is-deliverable-map",
        ConsoleProposalKind.Analysis => "is-deliverable is-deliverable-analysis",
        ConsoleProposalKind.Dashboard => "is-deliverable is-deliverable-dashboard",
        ConsoleProposalKind.App => "is-deliverable is-deliverable-app",
        _ => null,
    };

    public static string PlanDiffHeading(ConsoleProposalKind kind) => kind switch
    {
        ConsoleProposalKind.DataImport => "Import plan & diff",
        ConsoleProposalKind.Map => "Map plan & diff",
        ConsoleProposalKind.Analysis => "Analysis plan & diff",
        ConsoleProposalKind.Dashboard => "Dashboard plan & diff",
        ConsoleProposalKind.App => "App plan & diff",
        _ => "Plan & diff",
    };

    public static string PreviewHeading(ConsoleProposalKind kind) =>
        IsDeliverable(kind) ? "Preview" : "Dry run";

    public static string SourceLabel(ConsoleProposalSource source) => source switch
    {
        ConsoleProposalSource.DevOps => "DevOps",
        _ => "Server",
    };

    private static readonly Regex CvePattern = new(
        @"\bCVE-\d{4}-\d{4,7}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PinnedDigestPattern = new(
        @"@sha256:[0-9a-fA-F]{64}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsSupportAgentInitiated(ConsoleProposalDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return ContainsSupportAgentMarker(detail.RequestedByAgent)
            || ContainsSupportAgentMarker(detail.RequestedBy)
            || ContainsSupportAgentMarker(detail.Initiation);
    }

    public static bool IsUrgentCvePatch(ConsoleProposalDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return ExtractCveIdentifiers(detail).Count > 0 || ExtractPinnedImageDigests(detail).Count > 0;
    }

    public static IReadOnlyList<string> ExtractCveIdentifiers(ConsoleProposalDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return CollectMatches(detail, CvePattern, value => value.ToUpperInvariant());
    }

    public static IReadOnlyList<string> ExtractPinnedImageDigests(ConsoleProposalDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return CollectMatches(detail, PinnedDigestPattern, value => value.ToLowerInvariant());
    }

    private static bool ContainsSupportAgentMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("honua-support-agent", StringComparison.OrdinalIgnoreCase)
            || value.Contains("support-agent", StringComparison.OrdinalIgnoreCase)
            || value.Contains("supportagent", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> CollectMatches(
        ConsoleProposalDetail detail,
        Regex pattern,
        Func<string, string> normalize)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in EnumerateInspectableText(detail))
        {
            foreach (Match match in pattern.Matches(text))
            {
                var normalized = normalize(match.Value);
                if (seen.Add(normalized))
                {
                    results.Add(normalized);
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateInspectableText(ConsoleProposalDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.Summary)) yield return detail.Summary;
        if (!string.IsNullOrWhiteSpace(detail.ActionDiscriminator)) yield return detail.ActionDiscriminator;
        if (!string.IsNullOrWhiteSpace(detail.ExecutionPayload)) yield return detail.ExecutionPayload;
        foreach (var line in detail.Diff)
        {
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }

        foreach (var warning in detail.Warnings)
        {
            if (!string.IsNullOrWhiteSpace(warning)) yield return warning;
        }
    }

    private static string Normalize(string? raw) => (raw ?? string.Empty)
        .Trim()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();
}

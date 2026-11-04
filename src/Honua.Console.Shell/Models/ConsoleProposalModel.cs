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

    private static string Normalize(string? raw) => (raw ?? string.Empty)
        .Trim()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();
}

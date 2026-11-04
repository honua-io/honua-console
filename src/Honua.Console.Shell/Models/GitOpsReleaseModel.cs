using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public static class GitOpsReleaseRoutes
{
    public const string Releases = "/operate/releases";

    public const string Deploy = "/operate/deploy";

    public static string ReleaseDetail(string releasePackageId) =>
        $"/operate/releases/{Uri.EscapeDataString(releasePackageId)}";
}

public enum GitOpsRollbackClassification
{
    Unknown,
    MetadataOnly,
    ServiceRevision,
    ScriptReversible,
    SnapshotRequired,
    Manual
}

public enum GitOpsChangeClass
{
    Metadata,
    Style,
    FieldContract,
    SchemaContract,
    ServiceConfig,
    Rbac,
    Workflow,
    AppPackage
}

public sealed record GitOpsChangedResource(
    string SemanticResourceId,
    string ResourceType,
    string DisplayName,
    IReadOnlyList<GitOpsChangeClass> ChangeClasses);

public sealed record GitOpsReleaseProposal(
    string ReleasePackageId,
    string Title,
    string Summary,
    string SourceEnvironmentId,
    IReadOnlyList<string> TargetEnvironmentIds,
    string DesiredRevision,
    IReadOnlyList<GitOpsChangedResource> ChangedResources,
    GitOpsRollbackClassification RollbackClassification,
    bool HasBlockingFindings)
{
    public bool CanProposePullRequest => !HasBlockingFindings;
}

public enum GitOpsTargetBindingState
{
    Bound,

    Missing,

    EnvironmentUnavailable,
    // The server did not report a target state for this artifact and environment.

    Unknown
}

public sealed record GitOpsEnvironmentMatrixCell(
    string Environment,
    GitOpsTargetBindingState BindingState,
    long DesiredRevision,
    long? CurrentRevision)
{
    public bool IsAligned =>
        BindingState == GitOpsTargetBindingState.Bound
        && CurrentRevision is { } current
        && current >= DesiredRevision;

    public string DriftLabel => BindingState switch
    {
        GitOpsTargetBindingState.EnvironmentUnavailable => "environment unavailable",
        GitOpsTargetBindingState.Missing => "not yet bound",
        GitOpsTargetBindingState.Bound when IsAligned => "aligned",
        GitOpsTargetBindingState.Bound => "behind",
        _ => "unknown"
    };
}

public enum GitOpsTimelineStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    RolledBack
}

public sealed record GitOpsTimelineStep(
    string Stage,
    GitOpsTimelineStatus Status,
    string? Detail);

public sealed record GitOpsReleaseOperation(
    string OperationId,
    string Status,
    string CurrentStage,
    string? PrUrl,
    string? CommitSha,
    string? GitOperationId,
    string? DeployOperationId,
    string TargetEnvironment,
    string DesiredRevision,
    IReadOnlyList<GitOpsTimelineStep> Timeline,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    GitOpsRollbackPlan? RollbackPlan)
{
    public bool HasBlockers => Blockers.Count > 0;
}

public sealed record GitOpsRollbackPlan(
    GitOpsRollbackClassification Classification,
    bool IsDataAffecting,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> EvidenceRequired,
    string? ApprovalPolicyRef)
{
    public bool IsRollbackReady =>
        Classification != GitOpsRollbackClassification.Unknown
        && (!IsDataAffecting || (Steps.Count > 0 && EvidenceRequired.Count > 0));
}

public enum GitOpsDataScriptCoverage
{
    // The server did not report whether this script has a rollback.

    Unknown,

    Covered,

    NoRollback
}

public sealed record GitOpsDataScript(
    string ScriptId,
    string FileName,
    GitOpsDataScriptCoverage Coverage);

public sealed record GitOpsReleaseDetail(
    GitOpsReleaseProposal Proposal,
    IReadOnlyList<GitOpsEnvironmentMatrixCell> EnvironmentMatrix,
    GitOpsReleaseOperation? Operation,
    OperateSectionStatus OperationStatus,
    string OperationMessage,
    IReadOnlyList<GitOpsDataScript>? DataScripts = null)
{
    public bool CanProposePullRequest =>
        Proposal.CanProposePullRequest && (Operation is null || !Operation.HasBlockers);

    public bool HasDrift => EnvironmentMatrix.Any(cell => !cell.IsAligned);

    public IReadOnlyList<GitOpsDataScript> Scripts => DataScripts ?? [];

    public bool HasDataScriptCoverageGap =>
        Scripts.Any(script => script.Coverage == GitOpsDataScriptCoverage.NoRollback);

    public bool AllDataScriptsCovered =>
        Scripts.Count > 0 && Scripts.All(script => script.Coverage == GitOpsDataScriptCoverage.Covered);
}

public static class GitOpsReleasePresentation
{
    public static string Label(GitOpsChangeClass changeClass) => changeClass switch
    {
        GitOpsChangeClass.Metadata => "metadata",
        GitOpsChangeClass.Style => "style",
        GitOpsChangeClass.FieldContract => "field contract",
        GitOpsChangeClass.SchemaContract => "schema contract",
        GitOpsChangeClass.ServiceConfig => "service config",
        GitOpsChangeClass.Rbac => "RBAC",
        GitOpsChangeClass.Workflow => "workflow",
        GitOpsChangeClass.AppPackage => "app package",
        _ => "change"
    };

    public static string Label(GitOpsRollbackClassification classification) => classification switch
    {
        GitOpsRollbackClassification.MetadataOnly => "metadata-only",
        GitOpsRollbackClassification.ServiceRevision => "service-revision",
        GitOpsRollbackClassification.ScriptReversible => "script-reversible",
        GitOpsRollbackClassification.SnapshotRequired => "snapshot-required",
        GitOpsRollbackClassification.Manual => "manual",
        _ => "unknown"
    };

    public static string Description(GitOpsRollbackClassification classification) => classification switch
    {
        GitOpsRollbackClassification.MetadataOnly =>
            "Rollback can revert metadata/content/service pointers.",
        GitOpsRollbackClassification.ServiceRevision =>
            "Rollback requires a service revision switch.",
        GitOpsRollbackClassification.ScriptReversible =>
            "Rollback requires an attached rollback script.",
        GitOpsRollbackClassification.SnapshotRequired =>
            "Rollback requires a data backup/restore.",
        GitOpsRollbackClassification.Manual =>
            "Rollback cannot be automated safely and needs manual recovery.",
        _ => "Rollback evidence is incomplete; CI must not auto-promote."
    };

    public static string Label(GitOpsDataScriptCoverage coverage) => coverage switch
    {
        GitOpsDataScriptCoverage.Covered => "covered",
        GitOpsDataScriptCoverage.NoRollback => "no rollback",
        _ => "unknown"
    };

    public static string StateClass(GitOpsDataScriptCoverage coverage) => coverage switch
    {
        GitOpsDataScriptCoverage.Covered => "console-state-success",
        GitOpsDataScriptCoverage.NoRollback => "console-state-warning",
        _ => "console-state-neutral"
    };

    public static string Label(GitOpsTimelineStatus status) => status switch
    {
        GitOpsTimelineStatus.Pending => "pending",
        GitOpsTimelineStatus.Running => "running",
        GitOpsTimelineStatus.Succeeded => "succeeded",
        GitOpsTimelineStatus.Failed => "failed",
        GitOpsTimelineStatus.RolledBack => "rolled back",
        _ => "unknown"
    };

    public static string StateClass(GitOpsTimelineStatus status) => status switch
    {
        GitOpsTimelineStatus.Succeeded => "console-state-success",
        GitOpsTimelineStatus.Failed => "console-state-danger",
        GitOpsTimelineStatus.RolledBack => "console-state-warning",
        GitOpsTimelineStatus.Running => "console-state-info",
        _ => "console-state-neutral"
    };

    public static string Label(GitOpsCoordinatedArtifactKind kind) => kind switch
    {
        GitOpsCoordinatedArtifactKind.ContainerImage => "container image",
        GitOpsCoordinatedArtifactKind.DatabaseSchema => "DB / schema change",
        GitOpsCoordinatedArtifactKind.Metadata => "metadata semantic diff",
        _ => "artifact"
    };

    public static string Label(GitOpsCoordinatedStepStatus status) => status switch
    {
        GitOpsCoordinatedStepStatus.Pending => "pending",
        GitOpsCoordinatedStepStatus.AwaitingApproval => "awaiting approval",
        GitOpsCoordinatedStepStatus.Running => "running",
        GitOpsCoordinatedStepStatus.Succeeded => "succeeded",
        GitOpsCoordinatedStepStatus.Failed => "failed",
        GitOpsCoordinatedStepStatus.RolledBack => "rolled back",
        _ => "unknown"
    };

    public static string StateClass(GitOpsCoordinatedStepStatus status) => status switch
    {
        GitOpsCoordinatedStepStatus.Succeeded => "console-state-success",
        GitOpsCoordinatedStepStatus.Failed => "console-state-danger",
        GitOpsCoordinatedStepStatus.RolledBack => "console-state-warning",
        GitOpsCoordinatedStepStatus.Running => "console-state-info",
        GitOpsCoordinatedStepStatus.AwaitingApproval => "console-state-warning",
        _ => "console-state-neutral"
    };
}

public enum GitOpsCoordinatedArtifactKind
{
    Unknown,
    ContainerImage,
    DatabaseSchema,
    Metadata
}

public enum GitOpsCoordinatedStepStatus
{
    Pending,
    AwaitingApproval,
    Running,
    Succeeded,
    Failed,
    RolledBack
}

public sealed record GitOpsCoordinatedArtifact(
    GitOpsCoordinatedArtifactKind Kind,
    string Title,
    string Desired,
    string? Current);

public sealed record GitOpsCoordinatedStep(
    string Step,
    GitOpsCoordinatedStepStatus Status,
    bool RequiresApproval,
    bool IsReversible,
    string? Detail);

public sealed record GitOpsCoordinatedRelease(
    string OperationId,
    string PackageId,
    string Status,
    string CurrentStep,
    string TargetEnvironment,
    string? CurrentPhase,
    IReadOnlyList<GitOpsCoordinatedArtifact> Artifacts,
    IReadOnlyList<GitOpsCoordinatedStep> Steps,
    bool ContainerGateApproved,
    bool DataGateApproved,
    bool RollbackReady,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage)
{
    public bool IsTerminal =>
        Status is "Succeeded" or "Failed" or "RolledBack" or "ManualInterventionRequired";

    public bool IsAwaitingApproval => Status == "AwaitingApproval";

    public GitOpsCoordinatedStep? PendingGate =>
        IsAwaitingApproval
            ? Steps.FirstOrDefault(s => s.Status == GitOpsCoordinatedStepStatus.AwaitingApproval)
            : null;
}

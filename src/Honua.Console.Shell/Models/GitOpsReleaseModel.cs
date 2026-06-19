using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Route constants for the Operate GitOps metadata release visualization surface.
/// The release queue lives under <c>/operate</c> because it is an operator workflow
/// (release proposal, environment matrix, compatibility preflight, Git PR preview,
/// CI/GitOps timeline, and rollback), per the GitOps Metadata Publishing
/// Visualization Design ("Operate is the primary workspace").
/// </summary>
public static class GitOpsReleaseRoutes
{
    public const string Releases = "/operate/releases";

    public static string ReleaseDetail(string releasePackageId) =>
        $"/operate/releases/{Uri.EscapeDataString(releasePackageId)}";
}

/// <summary>
/// Classifies how a release can be rolled back, mirroring the design's rollback
/// classification vocabulary. Surfaced before apply so operators can see rollback
/// readiness without reading raw manifests.
/// </summary>
public enum GitOpsRollbackClassification
{
    Unknown,
    MetadataOnly,
    ServiceRevision,
    ScriptReversible,
    SnapshotRequired,
    Manual
}

/// <summary>
/// Change classes a release applies to a semantic resource, used as badges in the
/// proposal summary grouped resource list.
/// </summary>
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

/// <summary>
/// One semantic resource changed by a release proposal, grouped by resource type
/// in the proposal summary (service, layer, field, map, dashboard, form, app,
/// workflow, GP/ETL).
/// </summary>
public sealed record GitOpsChangedResource(
    string SemanticResourceId,
    string ResourceType,
    string DisplayName,
    IReadOnlyList<GitOpsChangeClass> ChangeClasses);

/// <summary>
/// The MVP "Release Proposal" view (design view #1): what is being promoted,
/// from which source environment to which targets, the desired revision, the
/// changed semantic resources, and the rollback classification. This is a
/// server-owned projection; Console never synthesizes it from a standing mock.
/// </summary>
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
    /// <summary>
    /// Whether a Git PR / deploy action can be offered. A release with unresolved
    /// blocking findings must not be able to create a PR or deploy (acceptance
    /// criterion: blockers prevent PR creation/deploy action).
    /// </summary>
    public bool CanProposePullRequest => !HasBlockingFindings;
}

/// <summary>
/// Binding state of a changed semantic resource in one target environment, used to
/// drive the release environment matrix and drift state (design view "environment
/// matrix"). Mirrors the server <c>MetadataEnvironmentBindingState</c>.
/// </summary>
public enum GitOpsTargetBindingState
{
    /// <summary>The target environment already has a binding for the artifact.</summary>
    Bound,

    /// <summary>The target environment is available but the artifact is absent.</summary>
    Missing,

    /// <summary>The target environment has no active metadata snapshot.</summary>
    EnvironmentUnavailable,

    /// <summary>The server did not report a target state for this artifact/environment.</summary>
    Unknown
}

/// <summary>
/// One cell of the release environment matrix: the desired vs. current revision of a
/// changed artifact in a target environment, plus the drift state. Surfaced so
/// operators see cross-environment drift without reading raw manifests.
/// </summary>
public sealed record GitOpsEnvironmentMatrixCell(
    string Environment,
    GitOpsTargetBindingState BindingState,
    long DesiredRevision,
    long? CurrentRevision)
{
    /// <summary>
    /// Whether the target environment is already aligned with the desired revision.
    /// Drift is anything that is bound but behind, missing, or unavailable.
    /// </summary>
    public bool IsAligned =>
        BindingState == GitOpsTargetBindingState.Bound
        && CurrentRevision is { } current
        && current >= DesiredRevision;

    /// <summary>Neutral drift label for the matrix cell.</summary>
    public string DriftLabel => BindingState switch
    {
        GitOpsTargetBindingState.EnvironmentUnavailable => "environment unavailable",
        GitOpsTargetBindingState.Missing => "not yet bound",
        GitOpsTargetBindingState.Bound when IsAligned => "aligned",
        GitOpsTargetBindingState.Bound => "behind",
        _ => "unknown"
    };
}

/// <summary>
/// CI/GitOps timeline stage status, mapped from the server release-operation
/// lifecycle status (design view "CI/GitOps timeline").
/// </summary>
public enum GitOpsTimelineStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    RolledBack
}

/// <summary>
/// One step in the CI/GitOps timeline: the operation stage, its status, and the
/// operation id (so the Console visualizes the same release operation ids produced
/// by server/devops — acceptance criterion).
/// </summary>
public sealed record GitOpsTimelineStep(
    string Stage,
    GitOpsTimelineStatus Status,
    string? Detail);

/// <summary>
/// The release-operation lifecycle (honua-server#1165): the durable operation id,
/// current stage, Git PR preview, blockers/warnings, and the rollback plan. This is
/// the server-owned release lifecycle projection; Console never synthesizes it.
/// </summary>
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
    /// <summary>
    /// Whether the operation lifecycle reports a blocker. A blocked operation must
    /// not offer a deploy/Git PR action (acceptance criterion).
    /// </summary>
    public bool HasBlockers => Blockers.Count > 0;
}

/// <summary>
/// Rollback plan for a release operation: classification, data-affecting flag,
/// approval requirement, and the rollback steps/evidence. Surfaced before apply so
/// rollback readiness and the rollback window are visible (acceptance criterion).
/// </summary>
public sealed record GitOpsRollbackPlan(
    GitOpsRollbackClassification Classification,
    bool IsDataAffecting,
    bool RequiresExplicitApproval,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> EvidenceRequired,
    string? ApprovalPolicyRef)
{
    /// <summary>
    /// Whether rollback is ready to be relied on before apply: it is classified and
    /// either non-data-affecting or has its required evidence and steps. A data-
    /// affecting rollback with no steps/evidence is NOT ready.
    /// </summary>
    public bool IsRollbackReady =>
        Classification != GitOpsRollbackClassification.Unknown
        && (!IsDataAffecting || (Steps.Count > 0 && EvidenceRequired.Count > 0));
}

/// <summary>
/// Rollback coverage of a data script: whether the release bundle carries a reversible
/// rollback for it (design "Data scripts" badges: <c>covered</c> / <c>no rollback</c>).
/// Mirrors the server data-script coverage projection.
/// </summary>
public enum GitOpsDataScriptCoverage
{
    /// <summary>The server did not report a coverage state for the script.</summary>
    Unknown,

    /// <summary>The script has an attached, verified rollback (reversible).</summary>
    Covered,

    /// <summary>The script has no rollback coverage; it is not auto-reversible.</summary>
    NoRollback
}

/// <summary>
/// One data script that a release applies as part of its bundle, with its rollback
/// coverage (design view "data script coverage"; surface brief "Data Script"). Surfaced
/// per-script so operators can see a coverage gap before apply without reading raw SQL.
/// </summary>
public sealed record GitOpsDataScript(
    string ScriptId,
    string FileName,
    GitOpsDataScriptCoverage Coverage);

/// <summary>
/// The full release detail surface (design views #1-#6): the proposal summary and
/// semantic diff, the environment matrix/drift, the data-script coverage, the
/// CI/GitOps timeline, and the rollback readiness/window. Bound to the real
/// honua-server release package + GitOps manifest (#1163) and release operation
/// lifecycle (#1165).
/// </summary>
public sealed record GitOpsReleaseDetail(
    GitOpsReleaseProposal Proposal,
    IReadOnlyList<GitOpsEnvironmentMatrixCell> EnvironmentMatrix,
    GitOpsReleaseOperation? Operation,
    OperateSectionStatus OperationStatus,
    string OperationMessage,
    IReadOnlyList<GitOpsDataScript>? DataScripts = null)
{
    /// <summary>
    /// Whether a Git PR / deploy action can be offered. Both the proposal-level
    /// blocking findings AND any operation-lifecycle blockers gate the action
    /// (acceptance criterion: blockers prevent PR creation/deploy action).
    /// </summary>
    public bool CanProposePullRequest =>
        Proposal.CanProposePullRequest && (Operation is null || !Operation.HasBlockers);

    /// <summary>Whether the environment matrix reports any drift across targets.</summary>
    public bool HasDrift => EnvironmentMatrix.Any(cell => !cell.IsAligned);

    /// <summary>The release's data scripts (never null for iteration).</summary>
    public IReadOnlyList<GitOpsDataScript> Scripts => DataScripts ?? [];

    /// <summary>
    /// Whether the server reported an explicit data-script coverage gap: at least one
    /// script has no rollback. Drives the compatibility-preflight warning line.
    /// </summary>
    public bool HasDataScriptCoverageGap =>
        Scripts.Any(script => script.Coverage == GitOpsDataScriptCoverage.NoRollback);

    /// <summary>
    /// Whether every data script is explicitly <see cref="GitOpsDataScriptCoverage.Covered"/>.
    /// A script with unknown coverage is NOT counted as covered, so the surface does not
    /// claim "all covered" when rollback coverage is unverified before apply.
    /// </summary>
    public bool AllDataScriptsCovered =>
        Scripts.Count > 0 && Scripts.All(script => script.Coverage == GitOpsDataScriptCoverage.Covered);
}

/// <summary>
/// Presentation helpers for the GitOps release vocabulary so the surface renders
/// neutral, design-aligned labels for change classes and rollback classification.
/// </summary>
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

    /// <summary>
    /// Neutral CSS state class for a data-script coverage badge: covered is success,
    /// a missing rollback is a warning, unknown is neutral.
    /// </summary>
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

    /// <summary>
    /// Neutral CSS state class for a timeline step, reusing the Console status
    /// vocabulary (danger/success/warning/neutral).
    /// </summary>
    public static string StateClass(GitOpsTimelineStatus status) => status switch
    {
        GitOpsTimelineStatus.Succeeded => "console-state-success",
        GitOpsTimelineStatus.Failed => "console-state-danger",
        GitOpsTimelineStatus.RolledBack => "console-state-warning",
        GitOpsTimelineStatus.Running => "console-state-info",
        _ => "console-state-neutral"
    };

    /// <summary>Neutral label for a coordinated-release artifact kind.</summary>
    public static string Label(GitOpsCoordinatedArtifactKind kind) => kind switch
    {
        GitOpsCoordinatedArtifactKind.ContainerImage => "container image",
        GitOpsCoordinatedArtifactKind.DatabaseSchema => "DB / schema change",
        GitOpsCoordinatedArtifactKind.Metadata => "metadata semantic diff",
        _ => "artifact"
    };

    /// <summary>Neutral label for a coordinated-release step status.</summary>
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

    /// <summary>Neutral CSS state class for a coordinated-release step status.</summary>
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

/// <summary>
/// Artifact kind carried by a coordinated platform-upgrade release (Demo C): the container image, the
/// DB/schema change, or the metadata semantic diff, shown side by side in the coordinated proposal.
/// </summary>
public enum GitOpsCoordinatedArtifactKind
{
    Unknown,
    ContainerImage,
    DatabaseSchema,
    Metadata
}

/// <summary>
/// Status of one step in the coordinated ordered timeline. Mirrors the server
/// <c>CoordinatedReleaseStepStatus</c> so the per-step gate/approve and rollback state render.
/// </summary>
public enum GitOpsCoordinatedStepStatus
{
    Pending,
    AwaitingApproval,
    Running,
    Succeeded,
    Failed,
    RolledBack
}

/// <summary>
/// One coordinated-release artifact (container image vs current, DB/schema change, or metadata diff).
/// </summary>
public sealed record GitOpsCoordinatedArtifact(
    GitOpsCoordinatedArtifactKind Kind,
    string Title,
    string Desired,
    string? Current);

/// <summary>
/// One step in the coordinated ordered step timeline, including whether it is an approval gate and
/// whether it is reversible (and thus unwound by the single coordinated rollback).
/// </summary>
public sealed record GitOpsCoordinatedStep(
    string Step,
    GitOpsCoordinatedStepStatus Status,
    bool RequiresApproval,
    bool IsReversible,
    string? Detail);

/// <summary>
/// The coordinated platform-upgrade release (Demo C, honua-server#97): the three artifact kinds, the
/// ordered step timeline with per-step gate/approve status, and the rollback readiness for the whole
/// op. A server-owned projection; the console never synthesizes it from a standing mock.
/// </summary>
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
    /// <summary>Whether the coordinated op is in a terminal state.</summary>
    public bool IsTerminal =>
        Status is "Succeeded" or "Failed" or "RolledBack" or "ManualInterventionRequired";

    /// <summary>Whether the coordinated op is currently parked at an approval gate.</summary>
    public bool IsAwaitingApproval => Status == "AwaitingApproval";

    /// <summary>The step the operator must approve, when parked at a gate.</summary>
    public GitOpsCoordinatedStep? PendingGate =>
        IsAwaitingApproval
            ? Steps.FirstOrDefault(s => s.Status == GitOpsCoordinatedStepStatus.AwaitingApproval)
            : null;
}

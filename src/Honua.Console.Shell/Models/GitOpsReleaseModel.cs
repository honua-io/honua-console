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
}

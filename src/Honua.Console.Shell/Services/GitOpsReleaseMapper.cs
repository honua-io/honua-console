using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Maps the GitOps metadata release wire contracts (release package + GitOps manifest
/// from honua-server#1163, release-operation lifecycle/rollback from honua-server#1165)
/// into the Console release view models. Pure and deterministic so it is fully unit
/// testable without a live server.
/// </summary>
internal static class GitOpsReleaseMapper
{
    public static GitOpsReleaseProposal MapProposal(
        MetadataReleasePackageResponse package,
        GitOpsMetadataReleaseManifestResponse? manifest)
    {
        ArgumentNullException.ThrowIfNull(package);

        var id = package.PackageId.ToString("D");
        var title = FirstNonBlank(
            package.Metadata?.Title,
            package.Metadata?.Name,
            $"Release {id}");
        var summary = FirstNonBlank(
            package.Metadata?.Description,
            $"Promote {package.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} semantic resources from {package.SourceEnvironment}.");

        var changed = package.Entries
            .Select(MapChangedResource)
            .ToArray();

        return new GitOpsReleaseProposal(
            ReleasePackageId: id,
            Title: title,
            Summary: summary,
            SourceEnvironmentId: package.SourceEnvironment,
            TargetEnvironmentIds: package.TargetEnvironments,
            DesiredRevision: ResolveDesiredRevision(package, manifest),
            ChangedResources: changed,
            RollbackClassification: GitOpsRollbackClassification.Unknown,
            HasBlockingFindings: HasBlockingDrift(package));
    }

    /// <summary>
    /// Builds the environment matrix from the per-entry target states. One cell per
    /// (target environment, entry) combination; the worst drift per environment drives
    /// the matrix presentation.
    /// </summary>
    public static IReadOnlyList<GitOpsEnvironmentMatrixCell> MapEnvironmentMatrix(
        MetadataReleasePackageResponse package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var cells = new List<GitOpsEnvironmentMatrixCell>();
        foreach (var environment in package.TargetEnvironments)
        {
            foreach (var entry in package.Entries)
            {
                var state = entry.TargetStates
                    .FirstOrDefault(target => string.Equals(target.Environment, environment, StringComparison.OrdinalIgnoreCase));
                cells.Add(new GitOpsEnvironmentMatrixCell(
                    Environment: environment,
                    BindingState: MapBindingState(state?.BindingState),
                    DesiredRevision: entry.DesiredMetadataRevision,
                    CurrentRevision: state?.CurrentMetadataRevision));
            }
        }

        return cells;
    }

    /// <summary>
    /// Maps the release bundle's data scripts and their rollback coverage (design
    /// "data script coverage"). Returns an empty list when the server projects none;
    /// the surface then renders the established empty/missing state rather than a mock.
    /// </summary>
    public static IReadOnlyList<GitOpsDataScript> MapDataScripts(MetadataReleasePackageResponse package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return package.DataScripts
            .Select(script => new GitOpsDataScript(
                ScriptId: FirstNonBlank(script.ScriptId, script.FileName),
                FileName: FirstNonBlank(script.FileName, script.ScriptId),
                Coverage: MapDataScriptCoverage(script.Coverage)))
            .ToArray();
    }

    public static GitOpsDataScriptCoverage MapDataScriptCoverage(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "covered" or "reversible" => GitOpsDataScriptCoverage.Covered,
            "no-rollback" or "norollback" or "no_rollback" or "uncovered" => GitOpsDataScriptCoverage.NoRollback,
            _ => GitOpsDataScriptCoverage.Unknown
        };

    public static GitOpsReleaseOperation MapOperation(DeployOperationResponse operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var context = operation.MetadataRelease;
        var blockers = (context?.Blockers ?? [])
            .Concat(operation.BlockingReasons)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var warnings = (context?.Warnings ?? [])
            .Concat(operation.Warnings)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GitOpsReleaseOperation(
            OperationId: operation.OperationId,
            Status: operation.Status,
            CurrentStage: FirstNonBlank(context?.CurrentStage, operation.CurrentPhase, operation.Status),
            PrUrl: NullIfBlank(context?.PrUrl),
            CommitSha: NullIfBlank(context?.CommitSha),
            GitOperationId: NullIfBlank(context?.GitOperationId),
            DeployOperationId: NullIfBlank(context?.DeployOperationId),
            TargetEnvironment: context?.TargetEnvironment ?? string.Empty,
            DesiredRevision: context?.DesiredRevision ?? string.Empty,
            Timeline: BuildTimeline(operation, context),
            Blockers: blockers,
            Warnings: warnings,
            RollbackPlan: MapRollbackPlan(context?.RollbackPlan));
    }

    public static GitOpsRollbackPlan? MapRollbackPlan(MetadataRollbackPlanResponse? plan)
    {
        if (plan is null)
        {
            return null;
        }

        return new GitOpsRollbackPlan(
            Classification: MapRollbackClassification(plan.Class),
            IsDataAffecting: plan.IsDataAffecting,
            RequiresExplicitApproval: plan.RequiresExplicitApproval,
            Steps: plan.Steps,
            EvidenceRequired: plan.EvidenceRequired,
            ApprovalPolicyRef: NullIfBlank(plan.ApprovalPolicyRef));
    }

    public static GitOpsRollbackClassification MapRollbackClassification(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "metadata-only" or "metadataonly" or "metadata" => GitOpsRollbackClassification.MetadataOnly,
            "service-revision" or "servicerevision" => GitOpsRollbackClassification.ServiceRevision,
            "script-reversible" or "scriptreversible" => GitOpsRollbackClassification.ScriptReversible,
            "snapshot-required" or "snapshotrequired" => GitOpsRollbackClassification.SnapshotRequired,
            "manual" => GitOpsRollbackClassification.Manual,
            _ => GitOpsRollbackClassification.Unknown
        };

    private static GitOpsChangedResource MapChangedResource(MetadataReleaseEntryResponse entry)
    {
        var resourceType = FirstNonBlank(entry.ResourceType, ArtifactKindLabel(entry.ArtifactKind));
        return new GitOpsChangedResource(
            SemanticResourceId: entry.SemanticId,
            ResourceType: resourceType,
            DisplayName: entry.SemanticId,
            ChangeClasses: [MapChangeClass(entry.ChangeClass, entry.ArtifactKind)]);
    }

    private static GitOpsChangeClass MapChangeClass(
        MetadataReleaseChangeClassWire changeClass,
        MetadataSemanticArtifactKindWire artifactKind) => changeClass switch
        {
            MetadataReleaseChangeClassWire.Policy => GitOpsChangeClass.Rbac,
            MetadataReleaseChangeClassWire.Binding when artifactKind == MetadataSemanticArtifactKindWire.Service =>
                GitOpsChangeClass.ServiceConfig,
            MetadataReleaseChangeClassWire.Content => GitOpsChangeClass.Style,
            MetadataReleaseChangeClassWire.Metadata when artifactKind == MetadataSemanticArtifactKindWire.Field =>
                GitOpsChangeClass.FieldContract,
            _ => GitOpsChangeClass.Metadata
        };

    private static GitOpsTargetBindingState MapBindingState(MetadataEnvironmentBindingStateWire? state) => state switch
    {
        MetadataEnvironmentBindingStateWire.Bound => GitOpsTargetBindingState.Bound,
        MetadataEnvironmentBindingStateWire.Missing => GitOpsTargetBindingState.Missing,
        MetadataEnvironmentBindingStateWire.EnvironmentUnavailable => GitOpsTargetBindingState.EnvironmentUnavailable,
        _ => GitOpsTargetBindingState.Unknown
    };

    /// <summary>
    /// A release with a target environment that is unavailable is a blocking finding:
    /// the proposal cannot be safely promoted there, so the Git PR/deploy action must
    /// be disabled (acceptance criterion).
    /// </summary>
    private static bool HasBlockingDrift(MetadataReleasePackageResponse package) =>
        package.Entries.Any(entry => entry.TargetStates.Any(target =>
            target.BindingState == MetadataEnvironmentBindingStateWire.EnvironmentUnavailable));

    private static IReadOnlyList<GitOpsTimelineStep> BuildTimeline(
        DeployOperationResponse operation,
        MetadataReleaseContextResponse? context)
    {
        var steps = new List<GitOpsTimelineStep>
        {
            new(
                "Git PR",
                context?.PrUrl is { Length: > 0 } ? GitOpsTimelineStatus.Succeeded : GitOpsTimelineStatus.Pending,
                context?.PrUrl)
        };

        var overall = MapTimelineStatus(operation.Status);
        steps.Add(new GitOpsTimelineStep(
            "CI/GitOps",
            overall,
            FirstNonBlank(context?.CurrentStage, operation.CurrentPhase, operation.Status)));

        if (!string.IsNullOrWhiteSpace(context?.DeployOperationId))
        {
            steps.Add(new GitOpsTimelineStep(
                "Deploy",
                overall,
                context!.DeployOperationId));
        }

        return steps;
    }

    private static GitOpsTimelineStatus MapTimelineStatus(string? status) =>
        (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "succeeded" or "completed" or "complete" or "applied" => GitOpsTimelineStatus.Succeeded,
            "failed" or "faulted" or "error" => GitOpsTimelineStatus.Failed,
            "rolledback" or "rolled-back" or "rollbackcompleted" => GitOpsTimelineStatus.RolledBack,
            "submitted" or "reconciling" or "running" or "inprogress" or "in-progress" or "rollbackrequested" =>
                GitOpsTimelineStatus.Running,
            _ => GitOpsTimelineStatus.Pending
        };

    private static string ResolveDesiredRevision(
        MetadataReleasePackageResponse package,
        GitOpsMetadataReleaseManifestResponse? manifest)
    {
        if (manifest?.Spec?.Source is { } source && source.Revision > 0)
        {
            return $"rev-{source.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        var maxEntryRevision = package.Entries
            .Select(entry => entry.DesiredMetadataRevision)
            .DefaultIfEmpty(package.SourceRevision)
            .Max();
        return $"rev-{maxEntryRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string ArtifactKindLabel(MetadataSemanticArtifactKindWire kind) => kind switch
    {
        MetadataSemanticArtifactKindWire.Resource => "resource",
        MetadataSemanticArtifactKindWire.Service => "service",
        MetadataSemanticArtifactKindWire.Publication => "publication",
        MetadataSemanticArtifactKindWire.Field => "field",
        MetadataSemanticArtifactKindWire.Catalog => "catalog",
        MetadataSemanticArtifactKindWire.Policy => "policy",
        MetadataSemanticArtifactKindWire.Role => "role",
        _ => "resource"
    };

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

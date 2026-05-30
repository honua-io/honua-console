using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Native.Core.Tests;

public sealed class GitOpsReleaseModelTests
{
    [Fact]
    public void ReleasesPageDeclaresQueueAndDetailRoutes()
    {
        var routes = typeof(OperateReleasesPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(attribute => attribute.Template)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/operate/releases", routes);
        Assert.Contains("/operate/releases/{SelectedReleaseId}", routes);
    }

    [Fact]
    public void ReleasesQueueRouteIsRecognizedAsAnOperateRoute()
    {
        Assert.True(ConsoleRouteMap.IsOperateRoute(GitOpsReleaseRoutes.Releases));
        Assert.True(ConsoleRouteMap.IsOperateRoute(GitOpsReleaseRoutes.ReleaseDetail("rel-001")));
    }

    [Fact]
    public void ReleaseDetailRouteEscapesThePackageId()
    {
        Assert.Equal("/operate/releases/rel%2F001", GitOpsReleaseRoutes.ReleaseDetail("rel/001"));
    }

    [Fact]
    public void ProposalWithBlockingFindingsCannotProposePullRequest()
    {
        var blocked = BuildProposal(hasBlockingFindings: true);
        var ready = BuildProposal(hasBlockingFindings: false);

        Assert.False(blocked.CanProposePullRequest);
        Assert.True(ready.CanProposePullRequest);
    }

    [Theory]
    [InlineData(GitOpsRollbackClassification.MetadataOnly, "metadata-only")]
    [InlineData(GitOpsRollbackClassification.ServiceRevision, "service-revision")]
    [InlineData(GitOpsRollbackClassification.ScriptReversible, "script-reversible")]
    [InlineData(GitOpsRollbackClassification.SnapshotRequired, "snapshot-required")]
    [InlineData(GitOpsRollbackClassification.Manual, "manual")]
    [InlineData(GitOpsRollbackClassification.Unknown, "unknown")]
    public void RollbackClassificationRendersDesignVocabulary(
        GitOpsRollbackClassification classification,
        string expectedLabel)
    {
        Assert.Equal(expectedLabel, GitOpsReleasePresentation.Label(classification));
        Assert.False(string.IsNullOrWhiteSpace(GitOpsReleasePresentation.Description(classification)));
    }

    [Theory]
    [InlineData(GitOpsChangeClass.FieldContract, "field contract")]
    [InlineData(GitOpsChangeClass.SchemaContract, "schema contract")]
    [InlineData(GitOpsChangeClass.ServiceConfig, "service config")]
    [InlineData(GitOpsChangeClass.AppPackage, "app package")]
    public void ChangeClassRendersDesignVocabulary(GitOpsChangeClass changeClass, string expectedLabel)
    {
        Assert.Equal(expectedLabel, GitOpsReleasePresentation.Label(changeClass));
    }

    private static GitOpsReleaseProposal BuildProposal(bool hasBlockingFindings) => new(
        ReleasePackageId: "rel-001",
        Title: "Promote parcels layer",
        Summary: "Field contract change tested in staging.",
        SourceEnvironmentId: "staging",
        TargetEnvironmentIds: ["prod"],
        DesiredRevision: "rev-42",
        ChangedResources:
        [
            new GitOpsChangedResource(
                "layer:parcels",
                "layer",
                "Parcels",
                [GitOpsChangeClass.FieldContract]),
        ],
        RollbackClassification: GitOpsRollbackClassification.MetadataOnly,
        HasBlockingFindings: hasBlockingFindings);
}

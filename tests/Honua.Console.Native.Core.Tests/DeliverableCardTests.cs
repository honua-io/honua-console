using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pins the deliverable-request card treatment (#193): the map / analysis / dashboard / app
/// proposal kinds parse from their wire strings, classify onto the GIS-desk "Deliverable request"
/// ticket type, and drive the kind-appropriate first-class card (kicker / plan-diff heading /
/// preview heading / CSS variant) in the approval panel — consistent with the existing
/// data-import card. Classification is derived from the server-owned kind, never guessed from prose.
/// </summary>
public sealed class DeliverableCardTests
{
    [Theory]
    [InlineData("map", ConsoleProposalKind.Map)]
    [InlineData("Map", ConsoleProposalKind.Map)]
    [InlineData("analysis", ConsoleProposalKind.Analysis)]
    [InlineData("dashboard", ConsoleProposalKind.Dashboard)]
    [InlineData("app", ConsoleProposalKind.App)]
    [InlineData("application", ConsoleProposalKind.App)]
    [InlineData("gitops-deploy", ConsoleProposalKind.Deploy)]
    public void MapKind_ParsesDeliverableAndGitOpsKinds(string raw, ConsoleProposalKind expected)
    {
        Assert.Equal(expected, ConsoleProposalPresentation.MapKind(raw));
    }

    [Theory]
    [InlineData(ConsoleProposalKind.Map)]
    [InlineData(ConsoleProposalKind.Analysis)]
    [InlineData(ConsoleProposalKind.Dashboard)]
    [InlineData(ConsoleProposalKind.App)]
    public void DeliverableKinds_ClassifyOntoDeliverableTicketType(ConsoleProposalKind kind)
    {
        Assert.Equal(ApprovalTicketType.Deliverable, ApprovalTicketPresentation.Classify(kind));
        Assert.True(ConsoleProposalPresentation.IsDeliverable(kind));
    }

    [Fact]
    public void DataImport_IsNotADeliverable_ButKeepsItsOwnCard()
    {
        Assert.False(ConsoleProposalPresentation.IsDeliverable(ConsoleProposalKind.DataImport));
        Assert.Equal("Data import approval", ConsoleProposalPresentation.CardKicker(ConsoleProposalKind.DataImport));
        Assert.Equal("is-data-import", ConsoleProposalPresentation.CardVariantClass(ConsoleProposalKind.DataImport));
        Assert.Equal("Import plan & diff", ConsoleProposalPresentation.PlanDiffHeading(ConsoleProposalKind.DataImport));
        // A data import is not a deliverable, so its dry-run stays "Dry run" (not "Preview").
        Assert.Equal("Dry run", ConsoleProposalPresentation.PreviewHeading(ConsoleProposalKind.DataImport));
    }

    [Theory]
    [InlineData(ConsoleProposalKind.Map, "Map request approval", "Map plan & diff")]
    [InlineData(ConsoleProposalKind.Analysis, "Analysis request approval", "Analysis plan & diff")]
    [InlineData(ConsoleProposalKind.Dashboard, "Dashboard request approval", "Dashboard plan & diff")]
    [InlineData(ConsoleProposalKind.App, "App request approval", "App plan & diff")]
    public void DeliverableCard_HasKindAppropriateKickerHeadingAndPreview(
        ConsoleProposalKind kind, string expectedKicker, string expectedPlanHeading)
    {
        Assert.Equal(expectedKicker, ConsoleProposalPresentation.CardKicker(kind));
        Assert.Equal(expectedPlanHeading, ConsoleProposalPresentation.PlanDiffHeading(kind));
        // For a deliverable the dry-run IS the rendered preview of the artifact.
        Assert.Equal("Preview", ConsoleProposalPresentation.PreviewHeading(kind));
        Assert.StartsWith("is-deliverable", ConsoleProposalPresentation.CardVariantClass(kind)!);
    }

    [Fact]
    public void NeutralKinds_UseTheNeutralCard()
    {
        Assert.Equal("Proposal review", ConsoleProposalPresentation.CardKicker(ConsoleProposalKind.MetadataRelease));
        Assert.Null(ConsoleProposalPresentation.CardVariantClass(ConsoleProposalKind.MetadataRelease));
        Assert.Equal("Plan & diff", ConsoleProposalPresentation.PlanDiffHeading(ConsoleProposalKind.MetadataRelease));
    }

    [Fact]
    public void KindLabels_CoverTheDeliverableKinds()
    {
        Assert.Equal("Map", ConsoleProposalPresentation.KindLabel(ConsoleProposalKind.Map));
        Assert.Equal("Analysis", ConsoleProposalPresentation.KindLabel(ConsoleProposalKind.Analysis));
        Assert.Equal("Dashboard", ConsoleProposalPresentation.KindLabel(ConsoleProposalKind.Dashboard));
        Assert.Equal("App", ConsoleProposalPresentation.KindLabel(ConsoleProposalKind.App));
    }
}

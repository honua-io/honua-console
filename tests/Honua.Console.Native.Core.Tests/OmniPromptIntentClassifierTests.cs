using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Host-independent coverage for the omni-prompt intent classifier (honua-console#203) — the thin, console-side
/// router that sends one free-text prompt to the Studio (GIS authoring) or DevOps (ops) lane, or to a confirm
/// chip when the signal is weak. Pure and deterministic: the same words always resolve to the same lane.
/// </summary>
public sealed class OmniPromptIntentClassifierTests
{
    private readonly OmniPromptIntentClassifier _classifier = new();

    [Theory]
    [InlineData("publish Maui parcels as a feature service")]
    [InlineData("publish the parcels layer as FeatureServer + STAC, styled by zoning")]
    [InlineData("build a dashboard of land use trends")]
    [InlineData("run a buffer analysis on the hydrants dataset")]
    [InlineData("create an ETL pipeline that ingests the shapefile nightly")]
    public void Classify_StudioAuthoringPrompts_RoutesToStudioWithHighConfidence(string prompt)
    {
        var result = _classifier.Classify(prompt);

        Assert.Equal(OmniPromptIntent.Studio, result.Intent);
        Assert.Equal(OmniPromptConfidence.High, result.Confidence);
    }

    [Theory]
    [InlineData("roll back staging to the last good revision")]
    [InlineData("upgrade the prod server")]
    [InlineData("deploy the new release to production")]
    [InlineData("promote the staging revision to prod")]
    [InlineData("redeploy and reconcile the gitops state")]
    public void Classify_OpsPrompts_RoutesToDevOpsWithHighConfidence(string prompt)
    {
        var result = _classifier.Classify(prompt);

        Assert.Equal(OmniPromptIntent.DevOps, result.Intent);
        Assert.Equal(OmniPromptConfidence.High, result.Confidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("do the thing")]
    [InlineData("help me with this")]
    public void Classify_EmptyOrSignalFreePrompts_ReturnsLowConfidence(string prompt)
    {
        var result = _classifier.Classify(prompt);

        Assert.Equal(OmniPromptConfidence.Low, result.Confidence);
        Assert.False(string.IsNullOrWhiteSpace(result.Rationale));
    }

    [Fact]
    public void Classify_PromptWithBothLaneSignals_ReturnsLowConfidenceForConfirmChip()
    {
        // One Studio marker ("publish") and one DevOps marker ("rollback") → a genuine tie the human disambiguates.
        var result = _classifier.Classify("publish then rollback");

        Assert.Equal(OmniPromptConfidence.Low, result.Confidence);
    }

    [Fact]
    public void Classify_AlwaysReturnsARationale()
    {
        Assert.False(string.IsNullOrWhiteSpace(_classifier.Classify("publish parcels").Rationale));
        Assert.False(string.IsNullOrWhiteSpace(_classifier.Classify("roll back prod").Rationale));
    }
}

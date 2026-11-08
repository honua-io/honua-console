using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ConsolePublicationLayerContextTests
{
    private const string ResourceId = "connection-layer-7";
    private static readonly OperateLayerExposure Exposure = new(
        new("parcels", "Parcels", "FeatureServer", "Running", "server", [], [], []),
        new(7, "Bridges", "LineString", ResourceId, "bridges"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageReadStartedBeforeLayoutLocationChangePublishesForItsOwnRoute(bool completeBeforeLayout)
    {
        var context = new ConsolePublicationLayerContext();
        context.SetPath("/operate/layers/previous-layer");
        var attempt = context.BeginRead(ResourceId);
        if (completeBeforeLayout) context.CompleteRead(attempt, [Exposure]);
        context.SetPath($"/operate/layers/{ResourceId}");
        if (!completeBeforeLayout) context.CompleteRead(attempt, [Exposure]);
        Assert.Equal(new(ResourceId, "parcels", "Bridges"), context.Selection);
    }

    [Fact]
    public void CompleteReadPublishesTheAlreadyResolvedAuthorizedExposure()
    {
        var context = Context();
        context.CompleteRead(context.BeginRead(ResourceId), [Exposure]);
        Assert.Equal(new(ResourceId, "parcels", "Bridges"), context.Selection);
        context.SetPath("/operate/layers/7");
        context.CompleteRead(context.BeginRead("7"), [Exposure]);
        Assert.Equal(new(ResourceId, "parcels", "Bridges"), context.Selection);
    }

    [Fact]
    public void DeniedPageReadClearsPreviouslyPublishedAncestry()
    {
        var context = Context();
        context.CompleteRead(context.BeginRead(ResourceId), [Exposure]);
        context.CompleteRead(context.BeginRead(ResourceId), []);
        Assert.Null(context.Selection);
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("route")]
    [InlineData("newer-read")]
    public void LatePageReadsCannotRestoreStaleAncestry(string change)
    {
        var context = Context();
        var attempt = context.BeginRead(ResourceId);
        if (change == "authority") context.Invalidate();
        else if (change == "route") context.SetPath("/operate/layers/other-layer-9");
        else context.BeginRead(ResourceId);
        context.CompleteRead(attempt, [Exposure]);
        Assert.Null(context.Selection);
    }

    [Fact]
    public void RepeatedIdenticalReadsDoNotTriggerAnotherNavigationRead()
    {
        var context = Context();
        var changes = 0;
        context.Changed += () => changes++;
        context.CompleteRead(context.BeginRead(ResourceId), [Exposure]);
        context.CompleteRead(context.BeginRead(ResourceId), [Exposure]);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void StyleReadUsesItsAuthorizedSlotAndRejectsDeniedOrMismatchedViews()
    {
        var context = Context();
        var view = new OperateLayerStyleOverrideView(ResourceId, [new("7", "parcels", "Bridges")]);
        context.CompleteStyleRead(context.BeginRead(ResourceId), view);
        Assert.Equal(new(ResourceId, "parcels", "Bridges"), context.Selection);
        context.CompleteStyleRead(context.BeginRead(ResourceId), view with
        { BindingState = new("Layer styles", OperateLayerStyleBindingState.Forbidden, "layer styles", "Denied") });
        Assert.Null(context.Selection);
        context.CompleteStyleRead(context.BeginRead(ResourceId), view);
        Assert.NotNull(context.Selection);
        context.CompleteStyleRead(context.BeginRead(ResourceId), view with { ResourceId = "other-layer-7" });
        Assert.Null(context.Selection);
    }

    private static ConsolePublicationLayerContext Context()
    {
        var context = new ConsolePublicationLayerContext();
        context.SetPath($"/operate/layers/{ResourceId}");
        return context;
    }
}

using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// Coverage for the shared status badge/pill (console#293): it renders the
/// <see cref="OperateStatus"/> mapping-table output (label + CSS class), and supports the
/// explicit label/class override path used for an axis the shared vocabulary does not model
/// (proposal risk level).
/// </summary>
public sealed class OperateStatusPillTests : ConsoleComponentTestBase
{
    [Fact]
    public void Status_RendersLabelAndCssClassFromTheSharedVocabulary()
    {
        var cut = Render<OperateStatusPill>(p => p
            .Add(c => c.Status, new OperateStatus("degraded", "One alert is firing.")));

        var pill = cut.Find(".console-status");
        Assert.Contains("console-state-warning", pill.ClassList);
        Assert.Equal("degraded", pill.TextContent.Trim());
        Assert.Equal("One alert is firing.", pill.GetAttribute("title"));
    }

    [Fact]
    public void ManualInterventionRequired_RendersAsDanger()
    {
        var status = DeployOperationPresentation.ToStatus(DeployOperationLifecycle.ManualInterventionRequired);

        var cut = Render<OperateStatusPill>(p => p.Add(c => c.Status, status));

        var pill = cut.Find(".console-status");
        Assert.Contains("console-state-danger", pill.ClassList);
        Assert.Equal("manual intervention required", pill.TextContent.Trim());
    }

    [Fact]
    public void ExplicitLabelAndCssClass_RenderWhenNoStatusIsGiven()
    {
        // The risk-level axis is not part of the shared status vocabulary (it is a severity
        // scale, not a lifecycle state), so it renders through the explicit override path.
        var cut = Render<OperateStatusPill>(p => p
            .Add(c => c.Label, "high risk")
            .Add(c => c.CssClass, "console-state-danger"));

        var pill = cut.Find(".console-status");
        Assert.Contains("console-state-danger", pill.ClassList);
        Assert.Equal("high risk", pill.TextContent.Trim());
    }
}

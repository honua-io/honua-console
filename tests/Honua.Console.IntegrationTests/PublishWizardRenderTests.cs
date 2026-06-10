using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the shared <see cref="PublishWizard"/> shell. Asserts the
/// design-handoff wizard chrome (screens-quick-publish.jsx / screens-publish-flow.jsx): the labelled
/// stepper with the active step marked current, the current step's body slot, ←/→ footer nav, and
/// per-step validity gating disabling the forward control.
/// </summary>
public sealed class PublishWizardRenderTests
{
    private static IReadOnlyList<PublishWizardStep> QuickSteps(bool serviceValid = true) =>
    [
        new PublishWizardStep("Service", BuildBody("service-body", "Service settings"), IsValid: serviceValid, ContinueLabel: "Continue · Layer →"),
        new PublishWizardStep("Layer", BuildBody("layer-body", "Layer settings"), ContinueLabel: "Continue · Review →"),
        new PublishWizardStep("Review", BuildBody("review-body", "Review what gets created")),
    ];

    private static RenderFragment BuildBody(string id, string text) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-test", id);
        builder.AddContent(2, text);
        builder.CloseElement();
    };

    [Fact]
    public void PublishWizard_RendersStepperBodyAndFooterNav()
    {
        using var ctx = new Bunit.BunitContext();

        var cut = ctx.Render<PublishWizard>(parameters => parameters
            .Add(p => p.Steps, QuickSteps())
            .Add(p => p.Current, 0));

        // Labelled stepper with the active step marked current.
        Assert.Contains("publish-stepper", cut.Markup, StringComparison.Ordinal);
        var currentStep = cut.Find(".publish-step-current");
        Assert.Contains("Service", currentStep.TextContent, StringComparison.Ordinal);

        // Current step body slot renders the step's content; later steps are hidden.
        Assert.Contains("Service settings", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Layer settings", cut.Markup, StringComparison.Ordinal);

        // Forward control uses the per-step continue label; back is disabled on the first step.
        var next = cut.Find(".publish-wizard-next");
        Assert.Contains("Continue · Layer →", next.TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find(".publish-wizard-back").HasAttribute("disabled"));
    }

    [Fact]
    public void PublishWizard_ForwardAndBack_RaiseCurrentChanged()
    {
        using var ctx = new Bunit.BunitContext();

        var current = 1;
        var cut = ctx.Render<PublishWizard>(parameters => parameters
            .Add(p => p.Steps, QuickSteps())
            .Add(p => p.Current, current)
            .Add(p => p.CurrentChanged, EventCallback.Factory.Create<int>(this, next => current = next)));

        cut.Find(".publish-wizard-next").Click();
        Assert.Equal(2, current);

        cut.Render(parameters => parameters.Add(p => p.Current, 1));
        cut.Find(".publish-wizard-back").Click();
        Assert.Equal(0, current);
    }

    [Fact]
    public void PublishWizard_InvalidStep_DisablesForwardNav()
    {
        using var ctx = new Bunit.BunitContext();

        var advanced = false;
        var cut = ctx.Render<PublishWizard>(parameters => parameters
            .Add(p => p.Steps, QuickSteps(serviceValid: false))
            .Add(p => p.Current, 0)
            .Add(p => p.CurrentChanged, EventCallback.Factory.Create<int>(this, _ => advanced = true)));

        var next = cut.Find(".publish-wizard-next");
        Assert.True(next.HasAttribute("disabled"));

        // A disabled control must not fire the change; the surface stays on the gated step.
        Assert.False(advanced);
    }

    [Fact]
    public void PublishWizard_LastStep_ShowsFinishAndRaisesOnFinish()
    {
        using var ctx = new Bunit.BunitContext();

        var finished = false;
        var cut = ctx.Render<PublishWizard>(parameters => parameters
            .Add(p => p.Steps, QuickSteps())
            .Add(p => p.Current, 2)
            .Add(p => p.FinishLabel, "Publish + open resource →")
            .Add(p => p.OnFinish, EventCallback.Factory.Create(this, () => finished = true)));

        // The last step swaps the forward control for the finish action.
        Assert.Empty(cut.FindAll(".publish-wizard-next"));
        var finish = cut.Find(".publish-wizard-finish");
        Assert.Contains("Publish + open resource →", finish.TextContent, StringComparison.Ordinal);

        finish.Click();
        Assert.True(finished);
    }
}

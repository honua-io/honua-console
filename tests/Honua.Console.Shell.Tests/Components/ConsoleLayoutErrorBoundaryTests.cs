using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Honua.Console.Shell.Tests.Components;

public sealed class ConsoleLayoutErrorBoundaryTests : ConsoleComponentTestBase
{
    [Fact]
    public void BodyException_ShowsRecoveryUiAndResetsOnRouteChange()
    {
        this.AddAuthorization();
        Services.AddHonuaConsoleShell();

        var shouldThrow = true;
        var layout = Render<ConsoleLayout>(parameters => parameters.Add(
            component => component.Body,
            builder =>
            {
                builder.OpenComponent<ConditionalFaultComponent>(0);
                builder.AddAttribute(1, nameof(ConditionalFaultComponent.ShouldThrow), shouldThrow);
                builder.CloseComponent();
            }));

        Assert.NotNull(layout.Find("[data-console-error-boundary]"));
        Assert.False(Renderer.UnhandledException.IsCompleted);

        shouldThrow = false;
        Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/health");

        layout.WaitForAssertion(() =>
        {
            Assert.Empty(layout.FindAll("[data-console-error-boundary]"));
            Assert.Contains("Healthy child content", layout.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LayerPreviewPane_BlocksJavascriptRouteAndSetsNoopenerOnSafeRoute()
    {
        var unsafeSelection = new LayerPreviewPane.PreviewSelection(
            "layer-1", "Parcels", "featureserver", "FeatureServer", "javascript:alert(1)");
        var unsafePane = Render<LayerPreviewPane>(parameters =>
            parameters.Add(component => component.Selection, unsafeSelection));

        Assert.Empty(unsafePane.FindAll("[data-preview-open-route]"));
        Assert.Equal("javascript:alert(1)", unsafePane.Find("[data-preview-open-route-invalid]").TextContent);

        var safeSelection = unsafeSelection with { Route = "/rest/services/cadastre/FeatureServer/7" };
        var safePane = Render<LayerPreviewPane>(parameters =>
            parameters.Add(component => component.Selection, safeSelection));

        var link = safePane.Find("[data-preview-open-route]");
        Assert.Equal("/rest/services/cadastre/FeatureServer/7", link.GetAttribute("href"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    private sealed class ConditionalFaultComponent : ComponentBase
    {
        [Parameter]
        public bool ShouldThrow { get; set; }

        protected override void OnParametersSet()
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated page crash.");
            }
        }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.AddContent(0, "Healthy child content");
        }
    }
}

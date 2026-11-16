using Bunit;
using Honua.Console.Shell;
using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperatorWorkflowRouteAuthorizationTests
{
    [Theory]
    [InlineData(typeof(StudioAutomationsPage), "/studio/automations")]
    [InlineData(typeof(StudioAutomationEditorPage), "/studio/automations/new")]
    [InlineData(typeof(StudioAutomationEditorPage), "/studio/automations/missing")]
    [InlineData(typeof(StudioAutomationVersionsPage), "/studio/automations/missing/versions")]
    [InlineData(typeof(OperateGeoprocessingPage), "/operate/geoprocessing")]
    [InlineData(typeof(OperateGeoprocessingPage), "/operate/geoprocessing/missing")]
    public void AnonymousNavigationRedirectsBeforeCreatingBackendBoundPage(Type pageType, string route)
    {
        using var context = new BunitContext();
        context.AddAuthorization().SetNotAuthorized();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(route);

        // No backend clients are registered: constructing the page would fail, even before
        // it could display a misleading missing-binding or outage state.
        var view = context.Render<AuthorizeRouteView>(parameters => parameters
            .Add(component => component.RouteData, new RouteData(pageType, new Dictionary<string, object>()))
            .Add(component => component.NotAuthorized, _ => builder =>
            {
                builder.OpenComponent<RedirectToLogin>(0);
                builder.CloseComponent();
            }));

        Assert.Equal($"http://localhost/auth/login?returnTo={Uri.EscapeDataString(route)}", navigation.Uri);
        Assert.DoesNotContain("not bound", view.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Temporarily unavailable", view.Markup, StringComparison.Ordinal);
    }
}

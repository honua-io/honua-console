using System.Net;
using System.Text;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConnectionProviderCapabilityTests
{
    private const string Documentation = "https://github.com/honua-io/honua-server/blob/trunk/docs/ops/nightly-images.md#data-store-variants";

    [Theory]
    [InlineData("[\"postgis\"]", "postgis", "5432")]
    [InlineData("[\"mysql\"]", "mysql", "3306")]
    [InlineData("[\"MYSQL\",\"future-provider\"]", "mysql", "3306")]
    public void ProviderSelectionUsesAdvertisedInventory(string providers, string selected, string port)
    {
        using var context = Context(Manifest(providers));
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Equal(selected,
            Assert.Single(page.FindAll("select")[0].QuerySelectorAll("option")).GetAttribute("value")));
        Assert.Equal(port, page.Find("input[type=number]").GetAttribute("value"));
        Assert.Contains("full server image", page.Markup);
        Assert.Contains("-full", page.Markup);
        Assert.Equal(Documentation, page.Find("[data-provider-documentation]").GetAttribute("href"));
    }

    [Fact]
    public void FullInventoryOffersEverySupportedFormWithoutUpgradeNotice()
    {
        using var context = Context(Manifest("[\"postgis\",\"mysql\",\"oracle\",\"sqlserver\"]"));
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("select")[0].QuerySelectorAll("option").Length));
        Assert.Empty(page.FindAll("[data-provider-upgrade]"));
        Assert.All(page.FindAll("button"), button => Assert.False(button.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"other\",\"dataStores\":{\"installedProviders\":[\"postgis\"]}}")]
    [InlineData("{\"schemaVersion\":\"honua.capability_manifest.v1\",\"dataStores\":{\"installedProviders\":null}}")]
    public void UnknownInventoryDisablesTestingAndCreation(string payload)
    {
        using var context = Context(payload);
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("select")[0].QuerySelectorAll("option")));
        Assert.True(page.FindAll("select")[0].HasAttribute("disabled"));
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.Contains("could not confirm", page.Markup);
        Assert.Empty(page.FindAll("[data-provider-upgrade]"));
    }

    [Fact]
    public void AnonymousManifestDoesNotRestrictProviders()
    {
        using var context = Context("{\"schemaVersion\":\"honua.capability_manifest.v1\"}");
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll("select")[0].QuerySelectorAll("option").Length));
        Assert.Empty(page.FindAll("[data-provider-upgrade]"));
        Assert.All(page.FindAll("button"), button => Assert.False(button.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"future-provider\"]")]
    public void InventoryWithoutCompatibleFormsExplainsDisabledActions(string providers)
    {
        using var context = Context(Manifest(providers));
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Contains("No installed database provider is supported by this form.", page.Markup));
        Assert.Empty(page.FindAll("select")[0].QuerySelectorAll("option"));
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void MissingUpgradeTagDoesNotInventDeploymentInstructions()
    {
        using var context = Context(Manifest("[\"postgis\"]").Replace("\"-full\"", "null", StringComparison.Ordinal));
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("select")[0].QuerySelectorAll("option")));
        Assert.Contains("are not installed on this server", page.Markup);
        Assert.DoesNotContain("image tag with the", page.Markup);
    }

    [Fact]
    public void ForbiddenInventoryDoesNotAssumeDefaultImageSupport()
    {
        using var context = Context(Manifest("[\"postgis\"]"), HttpStatusCode.Forbidden);
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("select")[0].QuerySelectorAll("option")));
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.Contains("Permission required", page.Find(".operate-status-panel").TextContent);
        Assert.Contains("lacks admin permission", page.Find(".operate-status-panel").TextContent);
        Assert.DoesNotContain("Check your server sign-in", page.Markup);
    }

    [Fact]
    public void ServerFailureUsesUnavailablePanelInsteadOfPermissionAdvice()
    {
        using var context = Context(Manifest("[\"postgis\"]"), HttpStatusCode.ServiceUnavailable);
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Contains("Temporarily unavailable", page.Find(".operate-status-panel").TextContent));
        Assert.Contains("HTTP 503", page.Find(".operate-status-panel").TextContent);
        Assert.DoesNotContain("Permission required", page.Markup);
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void UnboundServerShowsBindingInstructionsWithoutSignInAdvice()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        context.Services.AddSingleton<IOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>();
        var page = context.Render<OperateConnectionNewPage>();

        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup);
        Assert.DoesNotContain("Check your server sign-in", page.Markup);
        Assert.All(page.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://person:password@example.test/help")]
    public void UnsafeDocumentationLinkIsNotRendered(string href)
    {
        using var context = Context(Manifest("[\"postgis\"]", href));
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("select")[0].QuerySelectorAll("option")));
        Assert.Empty(page.FindAll("[data-provider-documentation]"));
    }

    [Theory]
    [InlineData(false, "[\"postgis\"]", 1)]
    [InlineData(true, "[\"postgis\"]", 1)]
    [InlineData(false, "[]", 0)]
    [InlineData(true, "[]", 0)]
    public void V4ProviderRestrictionsAndPrivateEditContext(bool enabled, string providers, int count)
    {
        using var context = Context(Manifest(providers));
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        var page = context.Render<OperateConnectionNewPage>();
        page.WaitForAssertion(() => Assert.Equal(count, page.FindAll("select")[0].QuerySelectorAll("option").Length));
        Assert.Equal(enabled ? 2 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        Assert.Equal(count == 0, page.Find("select").HasAttribute("disabled"));
        Assert.Equal(enabled ? "/operate/connections/new" : "/", registry.Current.Route);
        page.Find("input[type=password]").Input("private-test-password");
        Assert.Equal(enabled ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.DoesNotContain("private-test-password", System.Text.Json.JsonSerializer.Serialize(registry.Current));
        if (enabled) CatalogAdminPreview.Capture(count == 0 ? "data-connection-new-unsupported" : "data-connection-new", page.Markup);
    }

    private static string Manifest(string providers, string href = Documentation) =>
        "{\"schemaVersion\":\"honua.capability_manifest.v1\",\"dataStores\":{\"installedProviders\":"
        + providers + ",\"fullImageTagSuffix\":\"-full\",\"documentationHref\":"
        + System.Text.Json.JsonSerializer.Serialize(href) + "}}";

    private static BunitContext Context(string payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var http = new HttpClient(new ManifestHandler(payload, status));
        var client = new HonuaAdminOperateHttpClient(http, new HonuaAdminOperateClientOptions(new Uri("https://server.example/")));
        context.Services.AddSingleton<IConsoleConnectionCreateOperation>(new HonuaServerConsoleConnectionCreateOperation(client));
        context.Services.AddSingleton<IOperateTransitionDataSource, UnsupportedOperateTransitionDataSource>();
        return context;
    }

    private sealed class ManifestHandler(string payload, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/capabilities/manifest", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}

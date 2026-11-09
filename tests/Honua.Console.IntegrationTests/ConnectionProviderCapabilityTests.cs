using System.Net;
using System.Text;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
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
        using var context = Context("{}", HttpStatusCode.Forbidden);
        var page = context.Render<OperateConnectionNewPage>();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("select")[0].QuerySelectorAll("option")));
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

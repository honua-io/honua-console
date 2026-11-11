using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleSystemPagesV4Tests
{
    [Theory]
    [InlineData(false, "environments", "Environments")]
    [InlineData(true, "environments", "Environments")]
    [InlineData(false, "environment-new", "Add environment")]
    [InlineData(true, "environment-new", "Add environment")]
    [InlineData(false, "environment-detail", "Production")]
    [InlineData(true, "environment-detail", "Production")]
    [InlineData(false, "signin", "Sign In")]
    [InlineData(true, "signin", "Sign in")]
    public void SharedHeadingPreservesPageContent(bool enabled, string name, string title)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile { Id = "prod", DisplayName = "Production", ServerBaseUri = new Uri("https://production.example/"), EnvironmentKind = "production" }
        ]));
        context.Services.AddSingleton<IConsoleAccountSessionStore>(new InMemoryConsoleAccountSessionStore());
        context.Services.AddSingleton<IConsoleCapabilityManifest>(ConsoleCapabilityTestManifest.All);
        IRenderedFragment page = name switch
        {
            "environments" => context.Render<EnvironmentProfilesPage>(),
            "environment-new" => context.Render<EnvironmentProfileNewPage>(),
            "environment-detail" => context.Render<EnvironmentProfileDetailPage>(parameters => parameters.Add(p => p.ProfileId, "prod")),
            "signin" => context.Render<AuthSignInPage>(),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        Assert.Equal(title, page.Find("h1").TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        switch (name)
        {
            case "environments":
                Assert.Contains("Selected in this browser session", page.Markup);
                Assert.True(page.Find("button").HasAttribute("disabled"));
                Assert.Equal("/environments/prod", page.Find("a[href$='/environments/prod']").GetAttribute("href"));
                break;
            case "environment-new":
                page.Find("#environment-display-name").Change("New server");
                page.Find("#environment-server-url").Change("http://insecure.example");
                Assert.True(page.Find("button").HasAttribute("disabled"));
                Assert.Contains("Server URL must be an absolute https URL", page.Markup);
                break;
            case "environment-detail":
                Assert.Contains("Not validated", page.Markup);
                Assert.Contains("https://production.example/", page.Markup);
                Assert.Contains("Pinned server fingerprint", page.Markup);
                break;
            case "signin":
                Assert.Contains("Production", page.Markup);
                Assert.Contains("https://production.example/", page.Markup);
                Assert.Equal("Continue", page.Find("button[type='submit']").TextContent);
                Assert.Equal("/", page.Find("a").GetAttribute("href"));
                break;
        }
        CatalogAdminPreview.Capture($"system-{name}-{(enabled ? "v4" : "legacy")}", $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\">{page.Markup}</div>");
    }
}

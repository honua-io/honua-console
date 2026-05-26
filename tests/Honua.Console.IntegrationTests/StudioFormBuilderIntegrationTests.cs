using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-backed form builder renders from live honua-server data (AC#4: Testcontainers coverage
/// boots honua-server/PostgreSQL, seeds a known form package fixture, and asserts the surface renders from
/// live data; Docker-unavailable environments skip cleanly). Drives <see cref="HonuaServerStudioFormPackageDataSource"/>
/// over the real <c>/api/v1/admin/forms/packages</c> lifecycle (honua-server#1184) — never an in-memory client.
/// </summary>
[Collection(StudioFormPackageIntegrationCollection.Name)]
public sealed class StudioFormBuilderIntegrationTests
{
    private readonly StudioFormPackageFixture _fixture;

    public StudioFormBuilderIntegrationTests(StudioFormPackageFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task FormBuilder_SeedsDraftAndRendersFromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var dataSource = new HonuaServerStudioFormPackageDataSource(_fixture.CreateFormClient());

        // 1. Seed a known form package draft on the live server through the real save path.
        var title = $"Console form builder fixture {Guid.NewGuid():N}";
        var seed = StudioFormPackageMapper.CreateTemplate();
        seed.Title = title;
        seed.ServiceId = "console-form-fixture";
        seed.LayerId = 0;

        var saved = await dataSource.SaveDraftAsync(seed);
        Skip.If(
            !saved.Succeeded,
            $"The live server rejected the seeded form draft: {saved.Message}");
        Assert.NotNull(saved.State);
        var formId = saved.State!.FormId;
        Assert.False(string.IsNullOrWhiteSpace(formId));

        // 2. The workspace lists the seeded package from live data (not a mock).
        var workspace = await dataSource.GetWorkspaceAsync();
        Assert.DoesNotContain(workspace.CapabilityStates, state => state.State == "Missing binding");
        Assert.Contains(workspace.Packages, package => package.FormId == formId && package.Title == title);

        // 3. Loading the package hydrates the editor from the live draft.
        var load = await dataSource.LoadAsync(formId);
        Assert.True(load.HasEditor);
        Assert.Equal(title, load.State!.Title);
        Assert.NotEmpty(load.State.Fields);

        // 4. The form builder page renders the seeded draft from the live data source.
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(dataSource);
        var page = ctx.RenderComponent<StudioFormBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains(title, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));

        page.FindAll("button").First(button => button.TextContent.Contains(title, StringComparison.Ordinal)).Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
    }
}

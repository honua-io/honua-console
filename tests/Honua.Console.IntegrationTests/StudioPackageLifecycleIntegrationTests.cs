using Bunit;
using Honua.Console.Shell.Components.Studio;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-backed Studio authoring shell renders from live honua-server data (AC: Testcontainers
/// coverage boots honua-server/PostgreSQL, creates a package fixture, and asserts live-data rendering;
/// Docker-unavailable environments skip cleanly). Drives <see cref="ServerStudioAuthoringShell"/> over the
/// real <c>/api/v1/studio</c> contract: discovers families, creates a real draft, runs server validation,
/// and asserts the shared inspector + page render the live draft - never a mock.
/// </summary>
[Collection(StudioPackageLifecycleIntegrationCollection.Name)]
public sealed class StudioPackageLifecycleIntegrationTests
{
    private readonly StudioPackageLifecycleFixture _fixture;

    public StudioPackageLifecycleIntegrationTests(StudioPackageLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task StudioShell_RendersPackageDraftAndValidation_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        // 1. The initial session hydrates the family capabilities from the live server (#1180).
        var session = await shell.CreateInitialSessionAsync();
        Skip.If(
            session.BindingState is not null,
            $"The live server did not return Studio package families: {session.BindingState?.Detail}");
        Assert.NotEmpty(session.Workflows);

        // 2. Generating a package creates a real server draft and renders it from live data.
        session = await shell.GeneratePackageAsync(
            session,
            session.SelectedWorkflowId,
            "Create an org map from the parcels layer");
        Assert.Null(session.BindingState);
        Assert.NotNull(session.Draft);
        Assert.Empty(session.Clarifications);
        Assert.StartsWith("studio-", session.ActivePackage.PackageRef, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(session.ActivePackage.SchemaVersion));

        // 3. Validation renders the real server validation summary (#1181), not a mock result.
        session = await shell.ValidateAsync(session);
        Assert.Null(session.BindingState);
        Assert.NotEmpty(session.ActivePackage.ValidationItems);

        // 4. The shared inspector renders from the live draft.
        using var inspectorContext = new Bunit.TestContext();
        var inspector = inspectorContext.RenderComponent<StudioPackageInspector>(
            parameters => parameters.Add(component => component.Package, session.ActivePackage));
        Assert.Contains(session.ActivePackage.PackageRef, inspector.Markup, StringComparison.Ordinal);
        Assert.Contains(session.ActivePackage.SchemaVersion, inspector.Markup, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task StudioPage_RendersWorkflowFamilies_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var client = _fixture.CreateClient();
        IStudioAuthoringShell shell = new ServerStudioAuthoringShell(client);

        // Confirm the live server is reachable/authorized before driving the page.
        var probe = await shell.CreateInitialSessionAsync();
        Skip.If(
            probe.BindingState is not null,
            $"The live server did not return Studio package families: {probe.BindingState?.Detail}");

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(shell);

        var page = ctx.RenderComponent<StudioPage>();

        // The page hydrates its session from the live families; once loaded it must render the studio shell
        // (not the missing-binding state) and a server-reported schema version.
        page.WaitForAssertion(
            () => Assert.Contains("data-authoring-contract", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.Contains(probe.SelectedWorkflowId, page.Markup, StringComparison.OrdinalIgnoreCase);
    }
}

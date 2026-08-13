using AngleSharp.Dom;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the server-backed Studio shell. With the async honua-server binding,
/// <see cref="IStudioAuthoringShell.CreateInitialSessionAsync"/> awaits HTTP, so the page renders its
/// loading state before the session is assigned. The lifecycle rail (and every other render path) must
/// never dereference an uninitialized session during that window.
/// </summary>
public sealed class StudioPageRenderTests
{
    [Fact]
    public void StudioPage_DuringAsyncSessionLoad_RendersLoadingStateThenBoundShell()
    {
        var shell = new ControllableStudioAuthoringShell();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioAuthoringShell>(shell);

        // CreateInitialSessionAsync is still pending here: the first render must show the loading view
        // without throwing (the prior bug dereferenced a null _session in the heading lifecycle rail).
        var page = ctx.Render<StudioPage>();
        Assert.Contains("Loading package shell", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-authoring-contract", page.Markup, StringComparison.Ordinal);

        // Once the session resolves to a bound shell, the page renders the studio surface and lifecycle rail.
        var bound = StudioAuthoringSession.Empty with
        {
            Workflows = [new StudioWorkflowOption("map.package", "Map", "map.package", "Generated map", "1.0", SupportLevel: "Supported", PreviewSupported: true, PublishSupported: true)],
            SelectedWorkflowId = "map.package",
        };
        shell.CompleteInitialSession(bound);

        page.WaitForAssertion(
            () => Assert.Contains("data-authoring-contract", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("Loading package shell", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioPage_WhenSessionBindingStateSet_RendersMissingBindingWithoutNullDeref()
    {
        var shell = new ControllableStudioAuthoringShell();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioAuthoringShell>(shell);

        var page = ctx.Render<StudioPage>();

        // Resolve to a missing-binding session (server unreachable / unconfigured): the page must render the
        // shared error surface, not the lifecycle rail or a fabricated package.
        var blocked = StudioAuthoringSession.Empty with
        {
            BindingState = new StudioAuthoringBindingState(
                "Missing binding",
                "honua-server Studio API",
                "Configure Honua:Server:BaseUrl so Studio can bind the server-owned package lifecycle."),
        };
        shell.CompleteInitialSession(blocked);

        page.WaitForAssertion(
            () => Assert.Contains("Studio package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-authoring-contract", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioPage_WithOpenClarifications_DisablesTerminalPackageActions()
    {
        var shell = new ControllableStudioAuthoringShell();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioAuthoringShell>(shell);

        var page = ctx.Render<StudioPage>();
        shell.CompleteInitialSession(CreateSessionWithOpenClarification());

        page.WaitForAssertion(
            () => Assert.Contains("Select the source binding", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Validate").HasAttribute("disabled"));
        Assert.True(FindButton(page, "Preview plan").HasAttribute("disabled"));
        Assert.True(FindButton(page, "Save Version").HasAttribute("disabled"));
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    [Fact]
    public void StudioPage_ClientSideNavFromHomeToProofWithPrompt_SeedsAuthoringShell()
    {
        // Regression for the same-component client-side nav bug: routing from /studio to
        // /studio/proof?prompt=... reuses this StudioPage instance, so OnInitializedAsync does NOT re-run.
        // The seeded prompt must still be applied (now via OnParametersSetAsync) instead of leaving the
        // authoring shell on its default prompt.
        var shell = new ControllableStudioAuthoringShell();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IStudioAuthoringShell>(shell);
        // The /studio home landing renders <StudioHome />, which binds recent projects to the catalog.
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new EmptyCatalogClient());
        ctx.Services.AddSingleton<IConsoleCatalogReadContextResolver>(new AuthenticatedReadContextResolver());

        var nav = ctx.Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        nav.NavigateTo("studio");

        // First render lands on the home landing (no authoring session is read there).
        var page = ctx.Render<StudioPage>();

        // Same-component client-side navigation into the inline authoring shell with a seeded prompt.
        const string seededPrompt = "Map flood risk near hospitals";
        nav.NavigateTo($"studio/proof?prompt={Uri.EscapeDataString(seededPrompt)}");

        var bound = StudioAuthoringSession.Empty with
        {
            Workflows = [new StudioWorkflowOption("map.package", "Map", "map.package", "Generated map", "1.0", SupportLevel: "Supported", PreviewSupported: true, PublishSupported: true)],
            SelectedWorkflowId = "map.package",
        };
        shell.CompleteInitialSession(bound);

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("data-authoring-contract", page.Markup, StringComparison.Ordinal);
                // The seeded prompt is bound into the authoring shell prompt textarea.
                Assert.Equal(seededPrompt, page.Find("#studio-prompt").GetAttribute("value"));
            },
            TimeSpan.FromSeconds(5));
    }

    private static IElement FindButton(IRenderedComponent<StudioPage> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static StudioAuthoringSession CreateSessionWithOpenClarification()
    {
        var workflow = new StudioWorkflowOption(
            "map.package",
            "Map",
            "map.package",
            "Generated map",
            "1.0",
            SupportLevel: "Supported",
            PreviewSupported: true,
            PublishSupported: true);

        return StudioAuthoringSession.Empty with
        {
            Workflows = [workflow],
            SelectedWorkflowId = workflow.Id,
            Clarifications =
            [
                new StudioClarificationQuestion(
                    "source-binding",
                    "Select the source binding",
                    "Studio needs a source before terminal package actions.",
                    [new StudioClarificationChoice("saved-map", "Use the current saved map", "Bind the saved map.")])
            ],
            ActivePackage = StudioAuthoringSession.Empty.ActivePackage with
            {
                PackageRef = "studio-draft:test",
                PackageType = workflow.PackageType,
                Title = "Clarification-gated map",
                LifecycleState = StudioPackageLifecycleState.SavedVersion
            },
            Draft = new StudioDraftHandle(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "clarification-gated-map",
                1,
                Guid.NewGuid().ToString())
        };
    }

    /// <summary>
    /// Test double whose initial session stays pending until <see cref="CompleteInitialSession"/> is called,
    /// letting the test render the page in its async loading window. The remaining operations are not
    /// exercised by these render tests and echo the session back unchanged.
    /// </summary>
    private sealed class ControllableStudioAuthoringShell : IStudioAuthoringShell
    {
        private readonly TaskCompletionSource<StudioAuthoringSession> _initial =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void CompleteInitialSession(StudioAuthoringSession session) => _initial.TrySetResult(session);

        public Task<StudioAuthoringSession> CreateInitialSessionAsync(CancellationToken cancellationToken = default) =>
            _initial.Task;

        public Task<StudioAuthoringSession> SelectWorkflowAsync(StudioAuthoringSession session, string workflowId, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<StudioAuthoringSession> GeneratePackageAsync(StudioAuthoringSession session, string workflowId, string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<StudioAuthoringSession> ApplyClarificationAsync(StudioAuthoringSession session, string questionId, string choiceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<StudioAuthoringSession> ValidateAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<StudioAuthoringSession> PreviewPlanAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<StudioAuthoringSession> SaveVersionAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<StudioAuthoringSession> PublishAsync(StudioAuthoringSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);
    }

    private sealed class AuthenticatedReadContextResolver : IConsoleCatalogReadContextResolver
    {
        public Task<CatalogReadContext> ResolveAsync(string? publicLinkToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogReadContext.Authenticated);
    }

    private sealed class EmptyCatalogClient : IConsoleCatalogClient
    {
        public Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogSearchResult([], new Dictionary<string, int>(StringComparer.Ordinal), request));

        public Task<CatalogItemReadResult> GetCatalogItemAsync(string idOrSlug, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CatalogItemReadResult> GetOpenDataItemAsync(string idOrSlug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> GetMapPackageAsync(string mapId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> GetDraftMapAsync(string sourceItemId, CatalogReadContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MapPackageReadResult> AuthorizeEmbedAsync(string mapId, EmbedRouteOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

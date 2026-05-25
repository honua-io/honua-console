using Bunit;
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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAuthoringShell>(shell);

        // CreateInitialSessionAsync is still pending here: the first render must show the loading view
        // without throwing (the prior bug dereferenced a null _session in the heading lifecycle rail).
        var page = ctx.RenderComponent<StudioPage>();
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
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioAuthoringShell>(shell);

        var page = ctx.RenderComponent<StudioPage>();

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
}

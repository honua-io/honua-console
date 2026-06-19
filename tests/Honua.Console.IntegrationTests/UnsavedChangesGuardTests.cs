using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Shell.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the Wave-0 <see cref="UnsavedChangesGuard"/>. Uses bUnit's fake
/// NavigationManager (which invokes registered location-changing handlers on NavigateTo and records
/// whether the navigation succeeded or was prevented) plus a loose JSInterop so the in-app confirm()
/// and the beforeunload module import resolve. Verifies: dirty + decline => prevented; dirty + confirm
/// => allowed; clean => never prompts; and the location-changing handler is unregistered on dispose.
/// </summary>
public sealed class UnsavedChangesGuardTests
{
    private static Bunit.BunitContext CreateContext()
    {
        var ctx = new Bunit.BunitContext();
        // Loose mode: the beforeunload module import + enable/disable void calls resolve to no-ops.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static BunitNavigationManager Nav(Bunit.BunitContext ctx) =>
        (BunitNavigationManager)ctx.Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void Dirty_AndOperatorDeclines_PreventsNavigation()
    {
        using var ctx = CreateContext();
        // confirm() returns false => operator chose to stay.
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        ctx.Render<UnsavedChangesGuard>(parameters => parameters
            .Add(p => p.IsDirty, true));

        var nav = Nav(ctx);
        nav.NavigateTo("/studio/elsewhere");

        Assert.Equal(NavigationState.Prevented, nav.History.Last().State);
    }

    [Fact]
    public void Dirty_AndOperatorConfirms_AllowsNavigation()
    {
        using var ctx = CreateContext();
        // confirm() returns true => operator chose to leave.
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);

        ctx.Render<UnsavedChangesGuard>(parameters => parameters
            .Add(p => p.IsDirty, true));

        var nav = Nav(ctx);
        nav.NavigateTo("/studio/elsewhere");

        Assert.Equal(NavigationState.Succeeded, nav.History.Last().State);
    }

    [Fact]
    public void Clean_NeverPromptsAndAllowsNavigation()
    {
        using var ctx = CreateContext();
        // No confirm handler configured: if the guard prompted, the strict-by-default invocation would
        // surface. With clean state it must not invoke confirm at all.
        ctx.Render<UnsavedChangesGuard>(parameters => parameters
            .Add(p => p.IsDirty, false));

        var nav = Nav(ctx);
        nav.NavigateTo("/studio/elsewhere");

        Assert.Equal(NavigationState.Succeeded, nav.History.Last().State);
        Assert.DoesNotContain(ctx.JSInterop.Invocations, i => i.Identifier == "confirm");
    }

    [Fact]
    public void DirtyArmsBeforeUnload_CleanDisarms()
    {
        using var ctx = CreateContext();

        var cut = ctx.Render<UnsavedChangesGuard>(parameters => parameters
            .Add(p => p.IsDirty, true));

        // The beforeunload module is imported and enable() invoked when dirty.
        Assert.Contains(ctx.JSInterop.Invocations, i => i.Identifier == "enable");

        cut.Render(parameters => parameters.Add(p => p.IsDirty, false));
        Assert.Contains(ctx.JSInterop.Invocations, i => i.Identifier == "disable");
    }

    [Fact]
    public async Task Dispose_UnregistersLocationChangingHandler()
    {
        using var ctx = CreateContext();
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        var cut = ctx.Render<UnsavedChangesGuard>(parameters => parameters
            .Add(p => p.IsDirty, true));

        await cut.Instance.DisposeAsync();

        // After dispose the handler is gone: a dirty-state navigation now proceeds unimpeded.
        var nav = Nav(ctx);
        nav.NavigateTo("/studio/after-dispose");

        Assert.Equal(NavigationState.Succeeded, nav.History.Last().State);
    }
}

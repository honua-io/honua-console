using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// bUnit / unit tests for the shell notification stack:
///
///   1. <see cref="ConsoleNotificationService"/> — the in-circuit toast queue.
///   2. <see cref="ConsoleNotificationHost"/> — the Razor host that renders toasts from the service.
///   3. <see cref="ConsoleGuardedRunner.RunGuardedAsync"/> — the busy-flag + catch → error-toast wrapper.
///
/// These verify the three layers of the action-feedback system added by the trust-feedback sweep.
/// </summary>
public sealed class ConsoleNotificationTests : ConsoleComponentTestBase
{
    public ConsoleNotificationTests()
    {
        // Register the real service so @inject IConsoleNotificationService resolves.
        Services.AddScoped<IConsoleNotificationService, ConsoleNotificationService>();
    }

    // ── ConsoleNotificationService unit tests ──────────────────────────────────

    [Fact]
    public void Service_starts_with_no_notifications()
    {
        var svc = new ConsoleNotificationService();
        Assert.Empty(svc.Notifications);
    }

    [Fact]
    public void Success_adds_a_success_level_notification()
    {
        var svc = new ConsoleNotificationService();
        svc.Success("Layer published.");

        Assert.Single(svc.Notifications);
        var n = svc.Notifications[0];
        Assert.Equal(ConsoleNotificationLevel.Success, n.Level);
        Assert.Equal("Layer published.", n.Message);
    }

    [Fact]
    public void Error_adds_an_error_level_notification_with_null_auto_dismiss()
    {
        var svc = new ConsoleNotificationService();
        svc.Error("Couldn't save the filter: server down.");

        Assert.Single(svc.Notifications);
        var n = svc.Notifications[0];
        Assert.Equal(ConsoleNotificationLevel.Error, n.Level);
        // Errors must not auto-dismiss (null lifetime keeps them visible until manually dismissed).
        Assert.Null(n.AutoDismissAfter);
    }

    [Fact]
    public void Dismiss_removes_the_identified_toast()
    {
        var svc = new ConsoleNotificationService();
        var id = svc.Error("Something went wrong.");
        Assert.Single(svc.Notifications);

        svc.Dismiss(id);

        Assert.Empty(svc.Notifications);
    }

    [Fact]
    public void Clear_removes_all_toasts()
    {
        var svc = new ConsoleNotificationService();
        svc.Success("A.");
        svc.Error("B.");
        Assert.Equal(2, svc.Notifications.Count);

        svc.Clear();

        Assert.Empty(svc.Notifications);
    }

    [Fact]
    public void Changed_is_raised_when_a_notification_is_added()
    {
        var svc = new ConsoleNotificationService();
        var changeCount = 0;
        svc.Changed += () => changeCount++;

        svc.Info("Hello.");

        Assert.Equal(1, changeCount);
    }

    // ── ConsoleGuardedRunner.RunGuardedAsync unit tests ───────────────────────

    [Fact]
    public async Task RunGuardedAsync_fires_error_toast_on_exception()
    {
        var svc = new ConsoleNotificationService();
        Func<Task> throwing = () => throw new InvalidOperationException("server down");

        await svc.RunGuardedAsync(throwing, failureMessage: "Couldn't save");

        Assert.Single(svc.Notifications);
        var toast = svc.Notifications[0];
        Assert.Equal(ConsoleNotificationLevel.Error, toast.Level);
        Assert.Contains("Couldn't save", toast.Message);
        Assert.Contains("server down", toast.Message);
    }

    [Fact]
    public async Task RunGuardedAsync_fires_success_toast_when_onSuccess_provided_and_no_exception()
    {
        var svc = new ConsoleNotificationService();

        await svc.RunGuardedAsync(
            () => Task.CompletedTask,
            failureMessage: "Couldn't save",
            onSuccess: "Saved.");

        Assert.Single(svc.Notifications);
        var toast = svc.Notifications[0];
        Assert.Equal(ConsoleNotificationLevel.Success, toast.Level);
        Assert.Equal("Saved.", toast.Message);
    }

    [Fact]
    public async Task RunGuardedAsync_does_not_fire_success_toast_when_onSuccess_is_null()
    {
        var svc = new ConsoleNotificationService();

        await svc.RunGuardedAsync(
            () => Task.CompletedTask,
            failureMessage: "Couldn't save",
            onSuccess: null);

        Assert.Empty(svc.Notifications);
    }

    [Fact]
    public async Task RunGuardedAsync_sets_busy_true_then_false_around_the_operation()
    {
        var svc = new ConsoleNotificationService();
        var states = new List<bool>();

        await svc.RunGuardedAsync(
            () => Task.CompletedTask,
            setBusy: busy => states.Add(busy));

        Assert.Equal(new[] { true, false }, states);
    }

    [Fact]
    public async Task RunGuardedAsync_resets_busy_false_even_on_exception()
    {
        var svc = new ConsoleNotificationService();
        var states = new List<bool>();
        Func<Task> throwing = () => throw new InvalidOperationException("oops");

        await svc.RunGuardedAsync(throwing, setBusy: busy => states.Add(busy));

        Assert.Equal(new[] { true, false }, states);
    }

    [Fact]
    public async Task RunGuardedAsync_returns_true_on_success_false_on_failure()
    {
        var svc = new ConsoleNotificationService();

        bool successResult = await svc.RunGuardedAsync(() => Task.CompletedTask);
        bool failureResult = await svc.RunGuardedAsync(
            () => throw new InvalidOperationException("oops"));

        Assert.True(successResult);
        Assert.False(failureResult);
    }

    // ── ConsoleNotificationHost component tests ────────────────────────────────

    [Fact]
    public void Host_renders_no_toasts_on_initial_render()
    {
        var cut = Render<ConsoleNotificationHost>();
        Assert.NotNull(cut.Find("[data-console-toast-host]"));
        Assert.Empty(cut.FindAll("[data-console-toast]"));
    }

    [Fact]
    public void Host_shows_success_toast_with_correct_level_attribute()
    {
        var cut = Render<ConsoleNotificationHost>();
        var svc = Services.GetRequiredService<IConsoleNotificationService>();

        svc.Success("Role deleted.");

        cut.WaitForState(() => cut.FindAll("[data-console-toast]").Count == 1,
            TimeSpan.FromSeconds(2));

        var toast = cut.Find("[data-console-toast]");
        Assert.Equal("success", toast.GetAttribute("data-toast-level"));
        Assert.Contains("Role deleted.", toast.TextContent);
    }

    [Fact]
    public void Host_shows_error_toast_with_correct_level_attribute()
    {
        var cut = Render<ConsoleNotificationHost>();
        var svc = Services.GetRequiredService<IConsoleNotificationService>();

        svc.Error("Couldn't delete role: forbidden.");

        cut.WaitForState(() => cut.FindAll("[data-console-toast]").Count == 1,
            TimeSpan.FromSeconds(2));

        var toast = cut.Find("[data-console-toast]");
        Assert.Equal("error", toast.GetAttribute("data-toast-level"));
        Assert.Contains("Couldn't delete role: forbidden.", toast.TextContent);
    }

    [Fact]
    public void Host_renders_multiple_toasts_independently()
    {
        var cut = Render<ConsoleNotificationHost>();
        var svc = Services.GetRequiredService<IConsoleNotificationService>();

        svc.Success("First.");
        svc.Error("Second.");

        cut.WaitForState(() => cut.FindAll("[data-console-toast]").Count == 2,
            TimeSpan.FromSeconds(2));

        var toasts = cut.FindAll("[data-console-toast]");
        Assert.Equal(2, toasts.Count);
    }

    [Fact]
    public void Host_removes_toast_when_dismiss_is_called()
    {
        var cut = Render<ConsoleNotificationHost>();
        var svc = Services.GetRequiredService<IConsoleNotificationService>();

        var id = svc.Error("Something failed.");
        cut.WaitForState(() => cut.FindAll("[data-console-toast]").Count == 1,
            TimeSpan.FromSeconds(2));

        svc.Dismiss(id);

        cut.WaitForState(() => cut.FindAll("[data-console-toast]").Count == 0,
            TimeSpan.FromSeconds(2));
        Assert.Empty(cut.FindAll("[data-console-toast]"));
    }
}

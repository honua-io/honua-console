using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleNotificationServiceTests
{
    [Fact]
    public void Notify_adds_notification_and_raises_changed()
    {
        var service = new ConsoleNotificationService();
        var changes = 0;
        service.Changed += () => changes++;

        var id = service.Success("Saved.");

        Assert.Equal(1, changes);
        var notification = Assert.Single(service.Notifications);
        Assert.Equal(id, notification.Id);
        Assert.Equal(ConsoleNotificationLevel.Success, notification.Level);
        Assert.Equal("Saved.", notification.Message);
    }

    [Fact]
    public void Errors_and_warnings_do_not_auto_dismiss_by_default()
    {
        var service = new ConsoleNotificationService();

        service.Error("boom");
        service.Warning("careful");

        Assert.All(service.Notifications, n => Assert.Null(n.AutoDismissAfter));
    }

    [Fact]
    public void Info_and_success_carry_a_finite_lifetime_by_default()
    {
        var service = new ConsoleNotificationService();

        service.Info("fyi");
        service.Success("done");

        Assert.All(service.Notifications, n => Assert.NotNull(n.AutoDismissAfter));
    }

    [Fact]
    public void Dismiss_removes_one_notification_and_raises_changed()
    {
        var service = new ConsoleNotificationService();
        var id = service.Info("fyi");
        service.Info("second");

        var changes = 0;
        service.Changed += () => changes++;

        service.Dismiss(id);

        Assert.Equal(1, changes);
        Assert.Single(service.Notifications);
        Assert.DoesNotContain(service.Notifications, n => n.Id == id);
    }

    [Fact]
    public void Dismiss_unknown_id_is_a_noop_without_changed()
    {
        var service = new ConsoleNotificationService();
        service.Info("fyi");

        var changes = 0;
        service.Changed += () => changes++;

        service.Dismiss(Guid.NewGuid());

        Assert.Equal(0, changes);
        Assert.Single(service.Notifications);
    }

    [Fact]
    public void Clear_removes_all_and_raises_changed_once()
    {
        var service = new ConsoleNotificationService();
        service.Info("a");
        service.Info("b");

        var changes = 0;
        service.Changed += () => changes++;

        service.Clear();

        Assert.Equal(1, changes);
        Assert.Empty(service.Notifications);
    }

    [Fact]
    public void Message_is_trimmed()
    {
        var service = new ConsoleNotificationService();

        service.Info("  spaced  ");

        Assert.Equal("spaced", Assert.Single(service.Notifications).Message);
    }
}

public sealed class ConsoleGuardedRunnerTests
{
    [Fact]
    public async Task RunGuardedAsync_toggles_busy_and_returns_true_on_success()
    {
        var service = new ConsoleNotificationService();
        var busyStates = new List<bool>();

        var ok = await service.RunGuardedAsync(
            () => Task.CompletedTask,
            setBusy: busy => busyStates.Add(busy),
            onSuccess: "Done.");

        Assert.True(ok);
        Assert.Equal(new[] { true, false }, busyStates);
        var notification = Assert.Single(service.Notifications);
        Assert.Equal(ConsoleNotificationLevel.Success, notification.Level);
        Assert.Equal("Done.", notification.Message);
    }

    [Fact]
    public async Task RunGuardedAsync_surfaces_failure_toast_and_resets_busy()
    {
        var service = new ConsoleNotificationService();
        var busyStates = new List<bool>();

        var ok = await service.RunGuardedAsync(
            () => throw new InvalidOperationException("server said no"),
            setBusy: busy => busyStates.Add(busy),
            failureMessage: "Couldn't publish");

        Assert.False(ok);
        // Busy is always reset in the finally even though the operation threw.
        Assert.Equal(new[] { true, false }, busyStates);
        var notification = Assert.Single(service.Notifications);
        Assert.Equal(ConsoleNotificationLevel.Error, notification.Level);
        Assert.Contains("Couldn't publish", notification.Message);
        Assert.Contains("server said no", notification.Message);
    }

    [Fact]
    public async Task RunGuardedAsync_does_not_toast_success_when_no_success_text()
    {
        var service = new ConsoleNotificationService();

        var ok = await service.RunGuardedAsync(() => Task.CompletedTask);

        Assert.True(ok);
        Assert.Empty(service.Notifications);
    }
}

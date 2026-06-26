using System.Collections.ObjectModel;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Severity of a console notification, mapped to a status colour + ARIA politeness by the
/// notification host.
/// </summary>
public enum ConsoleNotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// A single transient notification (toast). Created by <see cref="IConsoleNotificationService"/>;
/// rendered by the shell-owned notification host. <see cref="Id"/> is stable so the host can key
/// and dismiss individual toasts.
/// </summary>
public sealed record ConsoleNotification(
    Guid Id,
    ConsoleNotificationLevel Level,
    string Message,
    TimeSpan? AutoDismissAfter);

/// <summary>
/// Shell-owned toast/notification surface. Pages and components raise success/error/info/warning
/// notifications here instead of inventing their own inline status; the single notification host
/// (rendered once in <c>ConsoleLayout</c>) subscribes and renders them with <c>role="alert"</c>,
/// auto-dismiss, and manual dismiss.
///
/// Scoped to the Blazor circuit (one queue per connected user), mirroring the other shell services.
/// </summary>
public interface IConsoleNotificationService
{
    /// <summary>The currently visible notifications, newest last.</summary>
    IReadOnlyList<ConsoleNotification> Notifications { get; }

    /// <summary>Raised whenever the visible set changes so the host can re-render.</summary>
    event Action? Changed;

    /// <summary>Shows a notification and returns its id. A null duration uses the level default.</summary>
    Guid Notify(ConsoleNotificationLevel level, string message, TimeSpan? autoDismissAfter = null);

    /// <summary>Shows an informational notification.</summary>
    Guid Info(string message, TimeSpan? autoDismissAfter = null);

    /// <summary>Shows a success notification.</summary>
    Guid Success(string message, TimeSpan? autoDismissAfter = null);

    /// <summary>Shows a warning notification.</summary>
    Guid Warning(string message, TimeSpan? autoDismissAfter = null);

    /// <summary>Shows an error notification. Errors do not auto-dismiss by default.</summary>
    Guid Error(string message, TimeSpan? autoDismissAfter = null);

    /// <summary>Dismisses a single notification by id (no-op when already gone).</summary>
    void Dismiss(Guid id);

    /// <summary>Dismisses every visible notification.</summary>
    void Clear();
}

/// <summary>
/// Default in-circuit notification queue. Holds the visible toasts and raises <see cref="Changed"/>
/// so the host re-renders. Auto-dismiss is driven by the host (which owns the timer + render loop);
/// the service only carries each toast's requested lifetime so the host knows when to expire it.
/// </summary>
public sealed class ConsoleNotificationService : IConsoleNotificationService
{
    // Sensible defaults: transient confirmations clear themselves; errors stay until dismissed so a
    // failure is never missed.
    private static readonly TimeSpan DefaultTransient = TimeSpan.FromSeconds(6);

    private readonly List<ConsoleNotification> _notifications = [];
    private readonly object _gate = new();

    public IReadOnlyList<ConsoleNotification> Notifications
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<ConsoleNotification>(_notifications.ToArray());
            }
        }
    }

    public event Action? Changed;

    public Guid Notify(ConsoleNotificationLevel level, string message, TimeSpan? autoDismissAfter = null)
    {
        var notification = new ConsoleNotification(
            Guid.NewGuid(),
            level,
            string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim(),
            autoDismissAfter ?? DefaultAutoDismiss(level));

        lock (_gate)
        {
            _notifications.Add(notification);
        }

        Changed?.Invoke();
        return notification.Id;
    }

    public Guid Info(string message, TimeSpan? autoDismissAfter = null) =>
        Notify(ConsoleNotificationLevel.Info, message, autoDismissAfter);

    public Guid Success(string message, TimeSpan? autoDismissAfter = null) =>
        Notify(ConsoleNotificationLevel.Success, message, autoDismissAfter);

    public Guid Warning(string message, TimeSpan? autoDismissAfter = null) =>
        Notify(ConsoleNotificationLevel.Warning, message, autoDismissAfter);

    public Guid Error(string message, TimeSpan? autoDismissAfter = null) =>
        Notify(ConsoleNotificationLevel.Error, message, autoDismissAfter);

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _notifications.RemoveAll(notification => notification.Id == id) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        bool any;
        lock (_gate)
        {
            any = _notifications.Count > 0;
            _notifications.Clear();
        }

        if (any)
        {
            Changed?.Invoke();
        }
    }

    // Errors and warnings persist until dismissed (a null lifetime); info/success self-clear.
    private static TimeSpan? DefaultAutoDismiss(ConsoleNotificationLevel level) => level switch
    {
        ConsoleNotificationLevel.Error => null,
        ConsoleNotificationLevel.Warning => null,
        _ => DefaultTransient,
    };
}

using System.Collections.ObjectModel;

namespace Honua.Console.Shell.Services;

public enum ConsoleNotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record ConsoleNotification(
    Guid Id,
    ConsoleNotificationLevel Level,
    string Message,
    TimeSpan? AutoDismissAfter);

public interface IConsoleNotificationService
{
    IReadOnlyList<ConsoleNotification> Notifications { get; }

    event Action? Changed;

    Guid Notify(ConsoleNotificationLevel level, string message, TimeSpan? autoDismissAfter = null);

    Guid Info(string message, TimeSpan? autoDismissAfter = null);

    Guid Success(string message, TimeSpan? autoDismissAfter = null);

    Guid Warning(string message, TimeSpan? autoDismissAfter = null);

    Guid Error(string message, TimeSpan? autoDismissAfter = null);

    void Dismiss(Guid id);

    void Clear();
}

public sealed class ConsoleNotificationService : IConsoleNotificationService
{
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

using System.Collections.ObjectModel;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ConsoleAgentContextRegistry
{
    private readonly object _gate = new();
    private Registration? _active;
    private ConsoleAgentContextSnapshot _current = new();
    public event Action? Changed;

    public ConsoleAgentContextSnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public Registration Register(ConsoleAgentPageContext context)
    {
        var registration = new Registration(this);
        lock (_gate)
        {
            _current = Snapshot(context);
            _active = registration;
        }
        Changed?.Invoke();
        return registration;
    }

    public void Navigate(string route)
    {
        lock (_gate)
        {
            if (_current.Route == NormalizeRoute(route)) return;
            _current = Snapshot(new ConsoleAgentPageContext { Route = route });
            _active = null;
        }
        Changed?.Invoke();
    }

    public void Invalidate(string route)
    {
        lock (_gate)
        {
            _current = Snapshot(new ConsoleAgentPageContext { Route = route });
            _active = null;
        }
        Changed?.Invoke();
    }

    private bool IsActive(Registration registration)
    {
        lock (_gate) return _active == registration;
    }

    private void Publish(Registration registration, ConsoleAgentPageContext context)
    {
        lock (_gate)
        {
            if (_active != registration) return;
            if (SameContext(context)) return;
            _current = Snapshot(context);
        }
        Changed?.Invoke();
    }

    private bool SameContext(ConsoleAgentPageContext context) =>
        _current.Route == NormalizeRoute(context.Route)
        && _current.Entities.SequenceEqual(context.Entities)
        && _current.Selection.SequenceEqual(context.Selection)
        && _current.Unsaved == context.Unsaved
        && _current.Filters.Count == context.Filters.Count
        && _current.Filters.All(pair => context.Filters.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private void Unregister(Registration registration)
    {
        lock (_gate)
        {
            if (_active != registration) return;
            _current = Snapshot(new ConsoleAgentPageContext { Route = _current.Route });
            _active = null;
        }
        Changed?.Invoke();
    }

    private ConsoleAgentContextSnapshot Snapshot(ConsoleAgentPageContext context)
    {
        var route = NormalizeRoute(context.Route);
        if (context.Unsaved.ChangedFieldCount < 0 || context.Unsaved.PendingOperationCount < 0)
            throw new ArgumentException("Unsaved counts cannot be negative.", nameof(context));
        return new ConsoleAgentContextSnapshot
        {
            Generation = checked(_current.Generation + 1),
            Route = route,
            Entities = Array.AsReadOnly(context.Entities.ToArray()),
            Selection = Array.AsReadOnly(context.Selection.ToArray()),
            Filters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(context.Filters)),
            Unsaved = context.Unsaved
        };
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Split('?', '#')[0];
        if (!normalized.StartsWith('/') || normalized.StartsWith("//", StringComparison.Ordinal) || normalized.Contains('\\'))
            throw new ArgumentException("Context requires a local Console route.", nameof(route));
        return normalized;
    }

    public sealed class Registration(ConsoleAgentContextRegistry registry) : IDisposable
    {
        public bool IsActive => registry.IsActive(this);
        public void Publish(ConsoleAgentPageContext context) => registry.Publish(this, context);
        public void Dispose() => registry.Unregister(this);
    }
}

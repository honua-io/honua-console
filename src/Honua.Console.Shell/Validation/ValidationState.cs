using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public sealed class ValidationState
{
    private IReadOnlyList<ConsoleFieldError> _clientErrors = Array.Empty<ConsoleFieldError>();
    private IReadOnlyList<ConsoleFieldError> _serverErrors = Array.Empty<ConsoleFieldError>();

    public IReadOnlyList<ConsoleFieldError> All => [.. _clientErrors, .. _serverErrors];

    public bool HasErrors => _clientErrors.Count > 0 || _serverErrors.Count > 0;

    public bool HasBlockingErrors => All.Any(error => error.IsBlocking);

    // Gate a re-save on the client channel. Stale server findings would deadlock a draft the operator is fixing.
    public bool HasBlockingClientErrors => _clientErrors.Any(error => error.IsBlocking);

    public void SetClientErrors(IReadOnlyList<ConsoleFieldError>? errors) =>
        _clientErrors = errors ?? Array.Empty<ConsoleFieldError>();

    public void SetServerErrors(IReadOnlyList<ConsoleFieldError>? errors) =>
        _serverErrors = errors ?? Array.Empty<ConsoleFieldError>();

    public void Clear()
    {
        _clientErrors = Array.Empty<ConsoleFieldError>();
        _serverErrors = Array.Empty<ConsoleFieldError>();
    }

    public IReadOnlyList<ConsoleFieldError> Errors(string fieldKey)
    {
        if (string.IsNullOrEmpty(fieldKey))
        {
            return Array.Empty<ConsoleFieldError>();
        }

        return [.. All
            .Where(error => string.Equals(error.FieldKey, fieldKey, StringComparison.Ordinal))
            .OrderByDescending(error => error.Severity)];
    }

    public bool HasErrorsFor(string fieldKey) => Errors(fieldKey).Count > 0;

    public IReadOnlyList<ConsoleFieldError> Summary =>
        [.. All
            .Select((error, index) => (error, index))
            .OrderByDescending(pair => pair.error.Severity)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.error)];
}

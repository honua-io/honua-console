using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Per-editor container that merges client-computed findings (from an <see cref="IFieldValidator{T}"/>)
/// with server-returned findings (from <see cref="ServerFieldErrorMapper"/>), keyed by
/// <see cref="ConsoleFieldError.FieldKey"/>. The two channels are kept separate so re-running the cheap
/// client evaluator on every edit never clobbers the authoritative server findings, and a successful
/// save can clear the server channel without discarding live client findings (and vice-versa). Render
/// surfaces query <see cref="Errors(string)"/> per field; the panel uses <see cref="Summary"/>.
/// </summary>
public sealed class ValidationState
{
    private IReadOnlyList<ConsoleFieldError> _clientErrors = Array.Empty<ConsoleFieldError>();
    private IReadOnlyList<ConsoleFieldError> _serverErrors = Array.Empty<ConsoleFieldError>();

    /// <summary>The merged client + server findings, client first, in insertion order.</summary>
    public IReadOnlyList<ConsoleFieldError> All => [.. _clientErrors, .. _serverErrors];

    /// <summary>True when any finding (client or server) is present.</summary>
    public bool HasErrors => _clientErrors.Count > 0 || _serverErrors.Count > 0;

    /// <summary>
    /// True when any finding blocks the operation (<see cref="ConsoleFieldError.IsBlocking"/>). Info /
    /// warning findings do not flip this.
    /// </summary>
    public bool HasBlockingErrors => All.Any(error => error.IsBlocking);

    /// <summary>Replaces the client-computed channel (e.g. after re-running the editor's validator on edit).</summary>
    public void SetClientErrors(IReadOnlyList<ConsoleFieldError>? errors) =>
        _clientErrors = errors ?? Array.Empty<ConsoleFieldError>();

    /// <summary>Replaces the server-returned channel (e.g. after a 400 validate response is mapped).</summary>
    public void SetServerErrors(IReadOnlyList<ConsoleFieldError>? errors) =>
        _serverErrors = errors ?? Array.Empty<ConsoleFieldError>();

    /// <summary>Clears both channels (e.g. on a successful save/publish).</summary>
    public void Clear()
    {
        _clientErrors = Array.Empty<ConsoleFieldError>();
        _serverErrors = Array.Empty<ConsoleFieldError>();
    }

    /// <summary>
    /// Returns every finding (client + server) for <paramref name="fieldKey"/>, ordered most-severe
    /// first so a single-line inline renderer naturally shows the worst finding. Never returns
    /// <see langword="null"/>.
    /// </summary>
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

    /// <summary>True when <paramref name="fieldKey"/> has at least one finding.</summary>
    public bool HasErrorsFor(string fieldKey) => Errors(fieldKey).Count > 0;

    /// <summary>
    /// A flat, severity-ordered summary of every finding for the validation panel / publish gate. The
    /// most severe findings come first; ties keep insertion order (client before server).
    /// </summary>
    public IReadOnlyList<ConsoleFieldError> Summary =>
        [.. All
            .Select((error, index) => (error, index))
            .OrderByDescending(pair => pair.error.Severity)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.error)];
}

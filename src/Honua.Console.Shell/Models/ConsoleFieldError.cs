namespace Honua.Console.Shell.Models;

// Info and Warning do not block save or publish. Error blocks the field. Blocker blocks the whole save.
public enum ConsoleValidationSeverity
{
    Info,

    Warning,

    Error,

    Blocker,
}

public sealed record ConsoleFieldError(
    string FieldKey,
    string Code,
    ConsoleValidationSeverity Severity,
    string Message,
    string? Path = null)
{
    public bool IsBlocking => Severity is ConsoleValidationSeverity.Error or ConsoleValidationSeverity.Blocker;
}

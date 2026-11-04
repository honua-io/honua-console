namespace Honua.Console.Shell.Models;

// Info and Warning are advisory. Error and Blocker prevent save or publish.
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

namespace Honua.Console.Shell.Models;

public abstract record ConsoleOperationResult<TSelf>
    where TSelf : ConsoleOperationResult<TSelf>, new()
{
    public bool Succeeded { get; init; }

    public string State { get; init; } = "";

    public string? Detail { get; init; }

    public static TSelf MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

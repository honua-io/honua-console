namespace Honua.Console.Shell.Models;

/// <summary>
/// Shared outcome envelope for mutating Console operations ("Set"/"Save"/"Import").
/// Carries the neutral <see cref="State"/> vocabulary token, the <see cref="Succeeded"/>
/// flag, and an optional human-readable <see cref="Detail"/>, plus the standard
/// <see cref="MissingBinding"/> factory used when no honua-server base URL is configured.
/// Each operation declares a self-typed concrete record
/// (<c>public sealed record ConsoleSetXResult : ConsoleOperationResult&lt;ConsoleSetXResult&gt;;</c>)
/// so the concrete type name, call sites, and wire serialization are preserved while the
/// envelope shape and missing-binding factory are inherited rather than re-declared.
/// </summary>
/// <typeparam name="TSelf">The concrete result record deriving from this envelope.</typeparam>
public abstract record ConsoleOperationResult<TSelf>
    where TSelf : ConsoleOperationResult<TSelf>, new()
{
    /// <summary>True when the operation completed successfully.</summary>
    public bool Succeeded { get; init; }

    /// <summary>State vocabulary token (e.g. "Created", "Missing binding", "Rejected", "Unavailable").</summary>
    public string State { get; init; } = "";

    /// <summary>Optional human-readable detail explaining the outcome.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Standard outcome returned when no honua-server base URL is configured and the
    /// operation cannot bind to a live server.
    /// </summary>
    public static TSelf MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

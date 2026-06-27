namespace Honua.Console.Shell.Models;

/// <summary>
/// One shared operation-result vocabulary for Console "Set"/"Save"/"Import" mutations
/// (honua-console#238, AUD-102). Before this, each authoring operation declared its own
/// byte-identical <c>Succeeded</c>/<c>State</c>/<c>Detail</c> record plus a copy-pasted
/// <c>MissingBinding</c> factory. They now derive from this self-typed base so the shape and the
/// missing-binding factory live in exactly one place, while each operation keeps its own concrete
/// type name (so call sites and serialization are unchanged) and adds typed payload members only
/// where the payload genuinely differs.
/// </summary>
/// <typeparam name="TSelf">The concrete result record, so inherited factories return the exact type.</typeparam>
public abstract record ConsoleOperationResult<TSelf>
    where TSelf : ConsoleOperationResult<TSelf>, new()
{
    /// <summary>True when the server applied the mutation.</summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// State vocabulary token (e.g. "Applied", "Saved", "Missing binding", "Rejected", "Unavailable").
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Human-readable detail for the surfaced state.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Standard result for the no-server-binding case: the console performed no network call and the
    /// caller should surface the binding requirement instead of fabricating success.
    /// </summary>
    public static TSelf MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail,
    };
}

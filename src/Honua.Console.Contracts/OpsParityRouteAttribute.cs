namespace Honua.Console.Contracts;

/// <summary>
/// Marks a Console operator route constant with the HTTP method used to match it
/// against the vendored honua-server operations parity map.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class OpsParityRouteAttribute : Attribute
{
    /// <summary>Initializes route-level parity metadata for a Console contract constant.</summary>
    /// <param name="method">The HTTP method used by the Console client.</param>
    public OpsParityRouteAttribute(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        Method = method;
    }

    /// <summary>Gets the HTTP method used by the Console client.</summary>
    public string Method { get; }
}

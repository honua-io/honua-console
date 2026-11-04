namespace Honua.Console.Contracts;

// Method is the verb used to match this constant against the vendored operations parity map.
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class OpsParityRouteAttribute : Attribute
{
    public OpsParityRouteAttribute(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        Method = method;
    }

    public string Method { get; }
}

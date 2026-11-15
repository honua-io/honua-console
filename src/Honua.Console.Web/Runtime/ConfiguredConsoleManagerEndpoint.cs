using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Runtime;

// The mapping belongs to the one configured serving environment. Another serving URI fails closed.
internal sealed class ConfiguredConsoleManagerEndpoint(Uri serving, Uri? management) : IConsoleManagerEndpoint
{
    public Uri? Resolve(Uri servingBaseUri)
    {
        ArgumentNullException.ThrowIfNull(servingBaseUri);
        var source = Directory(servingBaseUri);
        if (management is null) return source;
        return source == Directory(serving) ? management : null;
    }

    private static Uri Directory(Uri value) => new(value.AbsoluteUri.TrimEnd('/') + "/");
}
